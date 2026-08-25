# QA Checklist

Manual verification per phase (see PLAN.md §5 and §7). Check items off as verified.

## Phase 4 — Preview panel ✅ (automated) / manual items open

Automated (all green):

- [x] `PreviewStack` eviction tests (max-N keeps newest, drops oldest)
- [x] `PlacementResolver` centered + clamped placement tests
- [x] `DisplayAffinity` sets `WDA_EXCLUDEFROMCAPTURE` (0x11) — read back and asserted
- [x] Panel E2E: capture(s) → `ScreendropPreviewPanel` appears, affinity held, app alive
- [x] Region-crop test is stability-guarded (skips strict compare on the animating lock screen)
- [x] Regression: 50/50 xUnit + all 6 E2E scripts green

Manual:

- [ ] After a capture the panel appears bottom-center of the active display
- [ ] Hover a card → Save/Copy/Edit/Discard buttons appear; they behave (Save writes to
      export dir, Copy puts image on clipboard, Edit shows the Phase-5 notice, Discard
      removes the card and deletes the file)
- [ ] Drag the panel anywhere (across monitors) and it stays put
- [ ] The panel never appears in subsequent screenshots (visual check on unlocked desktop)
- [ ] Panel survives sleep/resume and shows the stack correctly
- [ ] 7+ captures: only the newest 6 cards remain

## Audit — Phases 2–3 re-review (2026-08-25)

- [x] Region capture never returns garbage for off-screen edges (PatBlt black fill)
- [x] Minimized windows are skipped by the picker (IsIconic)
- [x] Hotkey during an open area/picker overlay is ignored (busy guard), no second overlay
- [x] Settings save is atomic (rename, no missing-file window)
- [x] Reserved device names (`CON`, `COM1`, …) get a `_` prefix
- [x] Regression: 39/39 xUnit + all 5 E2E scripts green

## Phase 0 — Scaffold ✅

- [x] `dotnet build` green from clean clone (`.\build.ps1`)
- [x] xUnit smoke test passes (`dotnet test`)
- [x] Tray icon appears on launch (verified live during E2E runs)
- [ ] Tray context menu → "Quit Screendrop" removes the icon cleanly (visual check)
- [x] Launching a second instance exits silently (exit code 0)
- [x] No taskbar button / main window appears (EnumWindows audit: tray + hidden pump only)

## Phase 3 — After-capture pipeline ✅ (automated) / manual items open

Automated (all green):

- [x] Settings roundtrip + safe defaults + quality clamp + corrupt-file fallback (xUnit)
- [x] FileNaming tokens/sanitization/uniqueness (xUnit)
- [x] DIB builder header + bottom-up pixel layout (xUnit)
- [x] JPEG encode quality ladder + clamp + PNG roundtrip (xUnit)
- [x] Pipeline E2E: AutoSave+AutoCompress+pattern → `Shot_{date}_fullscreen.jpg` in
      configured folder, staged PNG in temp (scripts/verify-pipeline.ps1)
- [x] Regression: fullscreen/hotkeys/area/picker E2E all green with pipeline in place
- [ ] Live clipboard test (self-skips while session locked; runs on unlocked desktop)

Manual:

- [ ] Defaults: capture → PNG staged in `%TEMP%\Screendrop`, toast shows size
- [ ] Toggle AutoSave → PNG appears in `Pictures\Screendrop`
- [ ] Toggle AutoCompress → saved files become `.jpg` at the configured quality,
      toast shows `↓N%`
- [ ] Custom `FileNamePattern` → exported filenames follow tokens; collisions get
      ` 1`, ` 2` suffixes
- [ ] Toggle AutoCopy → paste into an editor yields the image (PNG/DIB fidelity)
- [ ] Toast shows a thumbnail of the capture

## Phase 2 — Area + window selection ✅ (automated) / manual items open

Automated (all green):

- [x] `PixelRect` geometry unit tests (contains/intersects/intersect/from-min-max)
- [x] Region capture pixel-exact vs full-capture crop (tolerance ≤5% for live pixels)
- [x] `MonitorGeometry` DPI scale sanity + DIP→physical roundtrip
- [x] Area overlay smoke: `Alt+Shift+3` shows `ScreendropAreaSelect` window; Esc closes it;
      trace records cancellation; app stays alive
- [x] Picker overlay smoke: `Alt+Shift+2` shows `ScreendropWindowPicker`; Esc closes it;
      trace records cancellation; app stays alive
- [x] `WindowCapturer`: live test window captured via PrintWindow with correct dims and
      non-flat content (integration test)
- [x] Regression: fullscreen capture + hotkey lifecycle still green
- [ ] Interactive drag E2E (`scripts/verify-area-capture.ps1`) — SKIPPED while session
      locked; run on unlocked desktop

Manual:

- [ ] `Alt+Shift+3` → drag on the focused display → PNG in `%TEMP%\Screendrop` matches the
      dragged rectangle exactly (pixel-perfect)
- [ ] Overlay dims the whole monitor; selection rect shows live `x, y · w × h px` HUD
- [ ] Esc cancels silently; Enter confirms current drag; tiny drags (<2px) cancel
- [ ] `Alt+Shift+2` → hover highlights the window under the cursor with a blue ring +
      title tag; click captures it; occluded/overlapping windows pick the topmost one
- [ ] Picker excludes Screendrop's own windows, desktop, taskbar, and tool windows
- [ ] Mixed-DPI: overlay covers the focused monitor exactly; crop matches physical pixels

## Phase 1 — Monitor capture + hotkeys ✅ (automated) / manual items open

Automated (`dotnet test`, `scripts/*.ps1`) — all green:

- [x] Enumeration: ≥1 monitor, primary rooted at origin, non-empty physical bounds
- [x] GDI capture: bitmap size == monitor physical pixels, content not flat color
- [x] PNG encode/decode roundtrip preserves dimensions
- [x] Hotkeys Alt+Shift+1/2/3 held by running app; released cleanly on exit
- [x] Synthetic WM_HOTKEY → coordinator → capture → PNG saved (1920x1080 observed)

Manual:

- [ ] Press real Alt+Shift+1: balloon notification appears, PNG in `%TEMP%\Screendrop`
      shows the display that had keyboard focus
- [ ] Multi-monitor mixed-DPI setup: captured PNG resolution matches the focused
      display's physical pixels exactly
- [ ] Register one combo from another tool first → launch app → conflict balloon names it
- [ ] Alt+Shift+2 / Alt+Shift+3 show "arrives with the area/window phase" balloons
- [ ] Trace log grows at `%TEMP%\Screendrop\trace.log` without errors
