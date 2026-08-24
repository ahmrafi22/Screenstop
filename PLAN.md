# Screendrop for Windows — Port Plan

Sibling project to the macOS app (`../Screendrop`). This document is the single source of truth
for scope, stack, structure, milestones, and risks. Update it as decisions change; do not let
code drift from it silently.

**References:**
- macOS architecture (verified, supersedes the PNG): `../Screendrop/docs/architecture.md`
- Key fact for this port: mac stills use the `screencapture` CLI for all modes; ScreenCaptureKit is recording-only.
- The mac app has 4 logged optimization proposals (single after-capture fan-out, background upload queue, composition root, model/flow split). Windows adopts the first and fourth **by design from day one** (see §4 mapping notes) rather than porting the scattered structure and refactoring later.

---

## 1. Goal & Scope

Port Screendrop's **screenshot + annotation core** to native Windows. The macOS app has grown a
large recording/teleprompter/cloud suite (~44k lines total); that suite is **out of scope** for v1.

### Parity matrix

| macOS feature | Windows v1 |
|---|---|
| Tray-only app (no dock/main window) | In scope — tray icon, no taskbar window |
| Global hotkeys: fullscreen / window / area | In scope — `RegisterHotKey`, user-configurable |
| Fullscreen capture (ScreenCaptureKit) | In scope — Windows.Graphics.Capture (WGC), GDI fallback |
| Window capture (`screencapture` CLI) | In scope — WGC on hwnd, hover-highlight picker |
| Area capture (rubber band) | In scope — per-monitor overlay windows |
| Floating preview panel + screenshot stack | In scope — borderless topmost WPF window |
| Panel invisible in its own captures (`PreviewWindowCaptureExclusion`) | In scope — `SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)` |
| Annotation editor (rect, ellipse, arrow, freehand, text, numbered circles, pixelate, blur) | In scope |
| Normalized [0,1] annotation coordinate model | Ported verbatim into Core |
| Full-res export renderer (CG) | In scope — SkiaSharp compositor |
| Auto-save / auto-copy / auto-compress prefs | In scope |
| Screenshot history store + naming pattern | In scope |
| Settings window | In scope |
| Launch at login | In scope — registry `Run` key |
| Recording Studio (timeline, pointer/keystroke capture, export) | Deferred — own future project |
| Camera overlay recording | Deferred |
| Transcription / Speech engine | Deferred |
| Teleprompter | Deferred |
| Cloud upload + sidecar uploader | Deferred |
| Wallpaper export | Out of scope v1 |
| App Intents / Shortcuts | Out of scope (no direct equivalent; revisit with voice access later) |
| Sparkle updater | Deferred — winget manifest first, self-update later |

---

## 2. Tech Stack (locked)

| Layer | Choice |
|---|---|
| Language / runtime | C# 12, .NET 8 LTS |
| UI framework | WPF (tray lifecycle, borderless overlays, editor shell) |
| MVVM helpers | CommunityToolkit.Mvvm (`ObservableObject` for the ported models) |
| Rendering / canvas | SkiaSharp (`SKElement` host inside WPF) |
| Screen capture | Windows.Graphics.Capture via CsWinRT; GDI `BitBlt` fallback |
| Global hotkeys | Win32 `RegisterHotKey` + `HwndSource.AddHook` |
| Tray icon | H.NotifyIcon.Wpf |
| Clipboard PNG | `CF_DIB` + `PNG` registered format |
| Settings | JSON in `%APPDATA%\Screendrop\settings.json` |
| Tests | xUnit (Core geometry + document logic only) |
| Packaging | Inno Setup EXE (default); MSIX optional later |

NuGet dependencies (all first-party-ecosystem or ubiquitous): `SkiaSharp`,
`SkiaSharp.Views.WPF`, `H.NotifyIcon.Wpf`, `CommunityToolkit.Mvvm`,
`System.Text.Json` (in-box). Nothing else without editing this plan.

**Decision log (2026-08-24, Phase 0):** `Microsoft.Windows.CsWinRT` removed from
`Screendrop.Capture`. Targeting `net8.0-windows10.0.19041.0` pulls the identical CsWinRT
projections automatically via `Microsoft.Windows.SDK.NET.Ref`; the standalone package's
build targets additionally require a locally installed Windows SDK and fail on machines
with only the .NET SDK. Same APIs, fewer moving parts.

### Why not the alternatives (decided)
- **WinUI 3**: immature story for borderless overlay windows and tray apps.
- **Tauri/Electron**: Screendrop is mostly custom native windows (overlays, click-through,
  capture exclusion) — exactly what webviews handle worst.
- **Go**: no role; not proposed.

---

## 3. Solution Layout

```
Screendrop-Windows/
├─ PLAN.md                        ← this file
├─ Screendrop.sln
├─ build.ps1                      ← one-shot restore+build+test script
├─ docs/                          ← QA checklist, decisions log (later)
├─ src/
│  ├─ Screendrop.App/             ← WPF executable (composition root)
│  │  ├─ App.xaml(.cs)            single-instance mutex, tray lifecycle, DI
│  │  ├─ Tray/                    tray icon + menu (capture, history, settings, quit)
│  │  ├─ Hotkeys/                 HotkeyService (RegisterHotKey), shortcut recorder
│  │  ├─ Capture/                 CaptureCoordinator, monitor/window enumeration
│  │  ├─ AreaSelect/              per-monitor rubber-band overlay windows
│  │  ├─ Preview/                 preview panel presenter + stack view + placement
│  │  ├─ Annotation/              editor window, canvas host, tool state machine, inspector
│  │  ├─ Settings/                prefs window (General/Screenshots/Hotkeys/About)
│  │  └─ Infrastructure/          dispatcher helpers, toast, crash log, startup registration
│  ├─ Screendrop.Core/            ← net8.0 class library, ZERO UI/framework deps
│  │  ├─ Geometry/                ports of Vec.swift, Mat.swift, Geometry2d, MathUtils…
│  │  ├─ Annotations/             AnnotationDocument, tools enum, shapes, normalized coords
│  │  └─ History/                 history store, file naming patterns
│  ├─ Screendrop.Capture/         ← net8.0; CsWinRT WGC wrapper, D3D→CPU readback, GDI fallback
│  └─ Screendrop.Rendering/       ← net8.0; SkiaSharp full-res compositor, pixelate/blur passes
└─ tests/
   └─ Screendrop.Core.Tests/      ← xUnit for geometry + annotation model
```

Dependency rule: `App → {Core, Capture, Rendering}`; `Capture → Core`; `Rendering → Core`.
Core must never reference WPF, CsWinRT, or SkiaSharp — it stays pure logic so it compiles fast
and is unit-testable (mirrors the mac model layer discipline).

---

## 4. macOS → Windows File Mapping (key files)

| macOS source | Windows target | Notes |
|---|---|---|
| `ScreendropApp.swift` | `App.xaml.cs` + `Tray/TrayController.cs` | `.accessory` policy → tray-only |
| `HotkeyManager.swift` (Carbon) | `Hotkeys/HotkeyService.cs` | `WM_HOTKEY` pump on hidden window |
| `HotkeyShortcutRecorder.swift` | `Settings/HotkeyRecorderControl.cs` | key-capture box in prefs |
| `CaptureCoordinator.swift` | `Capture/CaptureCoordinator.cs` | same orchestration shape |
| `ScreenshotManager.swift` | `Screendrop.Capture/WgcCapturer.cs` | mac uses the `screencapture` CLI for **all** still modes (shadow fidelity — see `../Screendrop/docs/architecture.md`); Windows has no CLI equivalent, so WGC is the only option. Frame pool → D3D texture → CPU map → SKBitmap; BitBlt fallback where the capture border can't be suppressed |
| `screencapture -R` area mode | `AreaSelect/AreaSelectionOverlay.cs` | per-monitor windows, Esc cancels, dim backdrop |
| `ScreenshotPreviewStack.swift` (@Observable) | `Core/…` + VM w/ CommunityToolkit.Mvvm | observable collection, max-N stack |
| `PreviewPanelPresenter.swift` | `Preview/PreviewPanelPresenter.cs` | ShowActivated=false, Topmost, no chrome |
| `PreviewWindowPlacement.swift` | `Preview/PreviewPlacement.cs` | bottom-center-of-active-screen resolver |
| `PreviewWindowCaptureExclusion.swift` | `Preview/DisplayAffinityGuard.cs` | `SetWindowDisplayAffinity` |
| `AnnotationDocument.swift` | `Core/Annotations/AnnotationDocument.cs` | normalized coords preserved |
| `Shape.swift` family, arrows, PathBuilder, InkPath | `Core/Geometry/*` | near-mechanical port |
| `AnnotationCanvas.swift` | `Annotation/AnnotationCanvasView.cs` | SkiaSharp paint loop, WPF input |
| `AnnoTextEditorOverlay.swift` | WPF `TextBox` adorner over canvas | text stays editable during edit |
| `AnnotationRenderer.swift` (CG) | `Rendering/SkiaRenderer.cs` | normalize→px only here + live view |
| `ScreendropPreferences.swift` (UserDefaults) | `Core/…/SettingsStore.cs` (JSON) | versioned schema, safe defaults |
| `ScreenshotHistoryStore.swift` | `Core/History/HistoryStore.cs` | JSON index + file refs |
| `ScreenshotFileNaming.swift` | `Core/History/FileNaming.cs` | pattern tokens identical |
| `AfterCaptureActions.swift` | `Infrastructure/AfterCapturePipeline.cs` | mac evaluates the after-capture matrix in 3 scattered call sites (flagged as OPT in `../Screendrop/docs/architecture.md`) — **Windows adopts the single fan-out point from day one**: one `AfterCapturePipeline.Run(capture, type)` invoked by the coordinator, covering overlay/copy/save/upload/annotate/pin |
| `MenuBarView.swift` | tray context menu | |

---

## 5. Phases & Milestones

Each phase ends with the acceptance criteria met and a green `build.ps1`. Commit atomically
per milestone (repo convention carries over from mac project).

### Phase 0 — Scaffold (start here)
- Solution + 4 projects + test project, NuGet refs, `PerMonitorV2` DPI manifest,
  single-instance mutex, tray icon with Quit, `build.ps1`.
- **Accept:** `dotnet build` green from clean clone; tray icon appears/disappears cleanly;
  second launch exits silently.

### Phase 1 — Monitor capture + hotkeys
- Monitor enumeration with DPI awareness; WGC capture of a display → temp PNG;
  hotkey defaults `Alt+Shift+1/2/3`; failure toast if hotkey is taken.
- **Accept:** hotkey produces correct-resolution PNG of the focused display on multi-monitor
  setups, including mixed-DPI.

### Phase 2 — Area + window selection
- Rubber-band overlay per monitor (dimmed backdrop, crosshair, size HUD, Esc cancel);
  alt-mode window picker with hover highlight (WindowFromPoint → hwnd → WGC item).
- **Accept:** area crop pixel-exact vs drawn rect; window capture excludes panel itself.

### Phase 3 — After-capture pipeline
- Save-to-temp PNG, auto-copy (DIB+PNG), auto-compress JPEG (quality setting),
  naming-pattern files, toast with thumbnail.
- **Accept:** each preference toggle changes behavior as labeled; filenames match pattern.

### Phase 4 — Preview panel
- Borderless topmost panel, stack up to N shots, hover action row (save/copy/edit/discard),
  placement resolver, display-affinity exclusion, drag-follow across monitors.
- **Accept:** panel never appears in subsequent captures; survives sleep/resume.

### Phase 5 — Annotation editor
- Document model + geometry ports (unit-tested), SkiaSharp canvas, undo/redo,
  tools in order: rect → ellipse → freehand → arrow (+heads) → text (adorner) →
  numbered circles → pixelate → blur (progressive).
- **Accept:** every tool draws/selects/moves/undoes; zoom/pan stable at 4K images.

### Phase 6 — Export renderer + integration
- Full-res compositing incl. preview/export parity for pixelate & blur;
  wire editor output into after-capture pipeline and history.
- **Accept:** exported image matches canvas within visual tolerance; Core tests pass.

### Phase 7 — Settings, polish, packaging
- Prefs tabs parity, launch-at-login, crash log, Inno Setup installer, icon set.
- **Accept:** clean install/uninstall/reinstall on Win10 21H2+ and Win11; app survives reboot.

---

## 6. Key Technical Decisions & Risks

1. **Capture API borders.** `GraphicsCaptureSession.IsBorderRequired` (border-free capture)
   needs Win11 + permission flow. On Win10, fullscreen falls back to GDI `BitBlt`;
   window capture keeps the yellow border there (documented limitation).
2. **WGC threading.** Requires a `DispatcherQueue` on the capturing thread; frame arrival →
   GPU copy → staging texture → CPU map → `SKBitmap`. Keep off UI thread except final bitmap handoff.
3. **Mixed-DPI multi-monitor.** PerMonitorV2 manifest mandatory; all placement math in
   physical pixels; never rely on system-DPI assumptions (the #1 way ports like this break).
4. **Clipboard fidelity.** Write both `CF_DIB` and registered `PNG` format; some targets
   (Slack/Discord) prefer PNG and lose transparency otherwise.
5. **Blur/pixelate performance.** Pixelate precomputes one downscaled sample; progressive
   blur = stacked gaussian passes with varying radii, cached per region while dragging.
6. **Hotkey conflicts.** `RegisterHotKey` failures surface in Settings with the conflicting
   registration listed; never silently drop.
7. **Unsigned binaries.** SmartScreen warnings until a code-signing cert is chosen
   (same situation as early mac builds; decide before wide distribution).

## 7. Verification

- `build.ps1` = restore → build → xUnit tests (Core only).
- Per-phase manual QA checklist lives in `docs/QA.md` (created in Phase 0 skeleton).
- Parity spot-checks: run identical scene captures on mac + Windows exports side by side.

## 8. Prerequisites (this machine)

- .NET 8 SDK — **not currently installed** (`dotnet` not found). Install via
  `winget install Microsoft.DotNet.SDK.8` before Phase 0.
- Windows 10 21H2+ or Win11 (WGC requirement).
