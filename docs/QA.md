# QA Checklist

Manual verification per phase. Check items off as verified.

## Phase 0 — Scaffold ✅

- [x] `dotnet build` green from clean clone (`.\build.ps1`)
- [x] xUnit smoke test passes (`dotnet test`)
- [x] Tray icon appears on launch (verified live during E2E runs)
- [ ] Tray context menu → "Quit Screenstop" removes the icon cleanly (visual check)
- [x] Launching a second instance exits silently (exit code 0)
- [x] No taskbar button / main window appears (EnumWindows audit: tray + hidden pump only)

## Phase 2 — Area + window selection 🔄

Automated (all green):

- [x] `PixelRect` geometry unit tests (contains/intersects/intersect/from-min-max)
- [x] Region capture pixel-exact vs full-capture crop (tolerance ≤5% for live pixels)
- [x] `MonitorGeometry` DPI scale sanity + DIP→physical roundtrip
- [x] Area overlay smoke: `Alt+Shift+3` shows `ScreenstopAreaSelect` window; Esc closes it;
      trace records cancellation; app stays alive
- [x] Picker overlay smoke: `Alt+Shift+2` shows `ScreenstopWindowPicker`; Esc closes it;
      trace records cancellation; app stays alive
- [x] `WindowCapturer`: live test window captured via PrintWindow with correct dims and
      non-flat content (integration test)
- [x] Regression: fullscreen capture + hotkey lifecycle still green
- [ ] Interactive drag E2E (`scripts/verify-area-capture.ps1`) — SKIPPED while session
      locked; run on unlocked desktop

Manual:

- [ ] `Alt+Shift+3` → drag on the focused display → PNG in `%TEMP%\Screenstop` matches the
      dragged rectangle exactly (pixel-perfect)
- [ ] Overlay dims the whole monitor; selection rect shows live `x, y · w × h px` HUD
- [ ] Esc cancels silently; Enter confirms current drag; tiny drags (<2px) cancel
- [ ] `Alt+Shift+2` → hover highlights the window under the cursor with a blue ring +
      title tag; click captures it; occluded/overlapping windows pick the topmost one
- [ ] Picker excludes Screenstop's own windows, desktop, taskbar, and tool windows
- [ ] Mixed-DPI: overlay covers the focused monitor exactly; crop matches physical pixels

## Phase 1 — Monitor capture + hotkeys ✅ (automated) / manual items open

Automated (`dotnet test`, `scripts/*.ps1`) — all green:

- [x] Enumeration: ≥1 monitor, primary rooted at origin, non-empty physical bounds
- [x] GDI capture: bitmap size == monitor physical pixels, content not flat color
- [x] PNG encode/decode roundtrip preserves dimensions
- [x] Hotkeys Alt+Shift+1/2/3 held by running app; released cleanly on exit
- [x] Synthetic WM_HOTKEY → coordinator → capture → PNG saved (1920x1080 observed)

Manual:

- [ ] Press real Alt+Shift+1: balloon notification appears, PNG in `%TEMP%\Screenstop`
      shows the display that had keyboard focus
- [ ] Multi-monitor mixed-DPI setup: captured PNG resolution matches the focused
      display's physical pixels exactly
- [ ] Register one combo from another tool first → launch app → conflict balloon names it
- [ ] Alt+Shift+2 / Alt+Shift+3 show "arrives with the area/window phase" balloons
- [ ] Trace log grows at `%TEMP%\Screenstop\trace.log` without errors
