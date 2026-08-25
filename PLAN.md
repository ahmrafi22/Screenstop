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

**Decision log (2026-08-24, Phase 1):**
1. *Display capture engine:* GDI `BitBlt` is the primary (currently only) engine for
   fullscreen display capture; WGC arrives in Phase 2 with window capture, where it is
   actually required. Rationale: §6.1 already sanctioned BitBlt as the Win10 fallback — for
   fullscreen stills it is pixel-exact, silent (no WGC yellow border flash), needs no OS
   permission prompt, and matches what the mac `screencapture` CLI produces. The
   `ICapturer`-shaped seam (`GDICapturer` today) keeps the Phase 2 swap trivial.
2. *Test scope widened:* the xUnit project now also hosts Screendrop.Capture integration
   tests (real monitor enumeration + real BitBlt + PNG roundtrip) alongside Core unit
   tests; §7's "Core only" line is superseded. Still no UI tests.
3. *Diagnostics:* `TraceLog` writes `%TEMP%\Screendrop\trace.log` (hotkey registrations,
   WM_HOTKEY receipts, capture results/failures). Foundation for the Phase 7 crash log.
4. *E2E automation:* `scripts/verify-hotkeys.ps1` proves registration ownership lifecycle;
   `scripts/verify-capture-e2e.ps1` posts a synthetic `WM_HOTKEY` to the app's hidden
   window and asserts a real display-sized PNG lands in `%TEMP%\Screendrop`.

**Decision log (2026-08-25, Phase 2):**
1. *WGC deferred for window capture.* `WindowCapturer` ships PrintWindow
   (`PW_RENDERFULLCONTENT`) primary with GDI `BitBlt` fallback (auto-fallback when
   PrintWindow fails or returns a flat frame). Rationale: WGC needs the
   `IGraphicsCaptureItemInterop` COM dance plus a D3D11 GPU→CPU readback path, triggers an
   OS permission prompt, and draws a yellow border on Win10 — none of which adds fidelity
   for stills on this machine, which lacks a locally installed Windows SDK to even build
   the projection reliably. The `WindowCapturer` seam keeps a future WGC engine a drop-in.
   Plan §5 Phase 2 wording "WGC engine lands here" superseded; revisit when GPU-content
   window capture (video/3D viewports) is required, likely with Phase 6 parity work.
2. *Overlay never baked into shots:* area capture pre-captures the focused display BEFORE
   showing the selection overlay, then `ExtractSubset`s on confirm. This sidesteps the
   `WDA_EXCLUDEFROMCAPTURE` requirement for area mode entirely (still needed for the
   Preview panel in Phase 4). Window capture is overlay-free by construction (PrintWindow
   reads the window's own DWM content).
3. *Test-time interaction limits:* the automation host is a lock-screen session, so
   WPF-overlay mouse input cannot be synthesized end-to-end. Overlay smoke tests verify
   show/Esc-close/telemetry/app-alive; drag/click E2E (`verify-area-capture.ps1`) skips
   with a clear message on locked sessions and runs manually on an unlocked desktop.
4. *Session-lock resilience:* all Phase 2 capture paths (BitBlt, PrintWindow, region crop)
   verified working while the session is locked.

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

- ~~.NET 8 SDK — not installed~~ **Done (2026-08-24):** installed 8.0.424 via winget.
- Windows 10 21H2+ or Win11 (WGC requirement).

---

## 9. Phase Checklist

Live status board. Update as phases complete; keep §5 acceptance text authoritative.

### Phase 0 — Scaffold ✅ (2026-08-24)
- [x] Solution + 4 projects + test project, NuGet refs, `PerMonitorV2` manifest
- [x] Single-instance mutex; second launch exits silently with code 0
- [x] Tray icon with Quit; no taskbar window
- [x] `build.ps1`, `.gitignore`, `docs/QA.md`
- [x] `dotnet build` green from clean clone

### Phase 1 — Monitor capture + hotkeys ✅ (2026-08-24)
- [x] Monitor enumeration in physical pixels (`MonitorEnumerator`)
- [x] Focused-display resolution via foreground window → nearest monitor
- [x] Display capture → temp PNG (`GDICapturer` → `%TEMP%\Screendrop\*.png`)
- [x] Hotkeys `Alt+Shift+1/2/3` on hidden message pump (`HotkeyService`)
- [x] Conflict toast on failed registration (tray balloon)
- [x] Tests: geometry unit tests + capture integration tests (7/7 green)
- [x] Automated E2E: hotkey ownership held while running / released on exit
- [x] Automated E2E: synthetic `WM_HOTKEY` produces real display-sized PNG
- [ ] Manual: real keypress on multi-monitor mixed-DPI setup (single-monitor machine here)

### Phase 2 — Area + window selection ✅ (WGC deferred by decision; manual QA open)
- [x] Region crop pixel-exact vs drawn rect (`GDICapturer.CaptureRegion`, 2A)
- [x] Core geometry helpers: `Contains`, `Intersects`, `Intersect`, `FromMinMax` (2A)
- [x] Rubber-band overlay per monitor (dimmed backdrop, crosshair, size HUD, Esc cancel) (2B)
- [x] Pre-capture + extract flow: full display captured BEFORE overlay so the overlay never appears in the shot (2B)
- [x] DPI-aware overlay placement + DIP↔physical conversion (`MonitorGeometry`, 2B)
- [x] `Alt+Shift+3` wired to area capture (2B)
- [x] Window picker with hover highlight + live title tag (smallest-area hit-test over enumerated windows, own/tool/desktop windows excluded) (2C)
- [x] `Alt+Shift+2` wired to pick → capture (2C)
- [x] Window capture engine: `WindowCapturer` (PrintWindow `PW_RENDERFULLCONTENT` primary, BitBlt fallback on blank frame) (2D)
- [ ] WGC engine — **deferred by decision** (see §9 decision log); BitBlt/PrintWindow satisfy v1 stills

### Phase 3 — After-capture pipeline 🔄 (in progress — see progress log)
- [x] `SettingsStore` (Core): versioned JSON at `%APPDATA%\Screendrop\settings.json`, safe defaults, quality clamp, atomic save (3a)
- [x] `FileNaming` (Core): token expansion `{timestamp}/{date}/{time}/{type}`, filename sanitization, unique-name resolution (mac `"name 1.png"` style) (3b)
- [ ] Save-to-temp PNG wired into single fan-out point (`AfterCapturePipeline.Run`)
- [x] `AfterCapturePipeline.Run` — single fan-out: always stage PNG to `%TEMP%\Screendrop`, then AutoSave (PNG/JPEG per AutoCompress) + AutoCopy, returns toast summary (3e)
- [x] All three capture paths (fullscreen/window/area) route through the pipeline (3e)
- [x] `ClipboardService` (Capture): writes both `CF_DIB` and registered `PNG` formats, bottom-up DIB builder (3c)
- [x] Auto-copy wired: `AutoCopy` toggle → `ClipboardService.SetImage` in pipeline (3e)
- [x] `JpegCompressor` (Rendering): SkiaSharp JPEG encode with quality clamp, PNG helper (3d)
- [x] Auto-compress wired: `AutoCompress` + `CompressionQuality` → JPEG saves with `↓N%` summary (3e)
- [x] Naming-pattern files: `FileNamePattern` tokens + unique resolution used for AutoSave (3b/3e)
- [x] Toast with thumbnail: `TrayController.Notify(..., thumbnailPath)` builds a ≤128px HICON from the capture, passes it to `ShowNotification` with delayed `DestroyIcon` (3f)

### Phase 4 — Preview panel ⬜
- [ ] Borderless topmost panel, stack up to N shots
- [ ] Hover action row (save/copy/edit/discard)
- [ ] Placement resolver (bottom-center of active screen)
- [ ] `SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)` exclusion
- [ ] Drag-follow across monitors; survives sleep/resume

### Phase 5 — Annotation editor ⬜
- [ ] Document model + geometry ports unit-tested (normalized coords)
- [ ] SkiaSharp canvas with undo/redo
- [ ] Tools in order: rect → ellipse → freehand → arrow (+heads) → text adorner →
      numbered circles → pixelate → blur (progressive)
- [ ] Zoom/pan stable at 4K images

### Phase 6 — Export renderer + integration ⬜
- [ ] Full-res compositing incl. pixelate & blur parity
- [ ] Editor output wired into after-capture pipeline and history

### Phase 7 — Settings, polish, packaging ⬜
- [ ] Prefs tabs parity (General/Screenshots/Hotkeys/About)
- [ ] Launch-at-login registry Run key
- [ ] Crash log
- [ ] Inno Setup installer + icon set
- [ ] Clean install/uninstall/reinstall QA

### Build progress log
- **2026-08-24 P0**: scaffold, tray, mutex, manifest, build.ps1. `1264bdf`
- **2026-08-24 P1**: monitor enumeration, GDI display capture, hotkeys 1/2/3, conflict toast, trace log, E2E scripts. `49e4ac0`
- **2026-08-24 P2a**: `PixelRect` geometry (`Contains`/`Intersects`/`Intersect`/`FromMinMax`), `GDICapturer.CaptureRegion`, pixel-exact region test vs full-capture crop. 11/11 tests green.
- **2026-08-24 P2b**: area selection overlay (`ScreendropAreaSelect` window: full-monitor dim, rubber-band rect, size HUD, crosshair, Esc/Enter), DPI-aware placement via `MonitorGeometry`, `Alt+Shift+3` → pre-capture + `ExtractSubset` (overlay never baked into shot). 12/12 tests green; overlay smoke E2E green; interactive-drag E2E skips on locked sessions.
- **2026-08-25 P2c**: window picker (`ScreendropWindowPicker` overlay spanning the virtual screen, crosshair, hover highlight ring + title tag; candidate set = visible titled non-tool top-level windows, own-process + Progman/WorkerW/tray excluded; smallest-area hit-test for topmost), `Alt+Shift+2` → pick → interim `CaptureRegion`. 12/12 tests + all four E2E scripts green.
- **2026-08-25 P2d**: `WindowCapturer` (PrintWindow `PW_RENDERFULLCONTENT` → flat-frame/blank auto-fallback to `BitBlt`), wired into `Alt+Shift+2`; integration test creates a live STATIC window and asserts captured dims + non-flat content. 13/13 tests green; all E2E scripts green. **Phase 2 complete** (WGC deferred — see §9 decision log).
