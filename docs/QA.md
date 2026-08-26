# QA Checklist

Manual verification per phase. Check items off as verified.

## Phase 7 — Settings, polish, packaging ✅ (automated) / manual items open

Automated (all green, 138/138 xUnit):

- [x] Crash report: file name encodes timestamp, layout contains all fields,
      fatal flag, trailing-whitespace trim, distinct names per millisecond (5 tests)
- [x] Launch-at-login command: quoting rules, no double-quote, whitespace trim,
      empty-path rejection (6 tests)
- [x] Hotkey combos: parse/format/canonicalization, F1–F24, named keys, bare-key
      rejection, malformed-input rejection, settings normalization incl. duplicate
      resolution (30 tests)
- [x] Installer compiles clean with ISCC 6.7.3 → `Screenstop-Setup-1.0.0.exe`
      (9.4 MB) produced and verified
- [x] Build clean, zero warnings

Manual (needs an unlocked desktop):

- [ ] Tray menu → Settings… opens the window; a second click focuses the open one
- [ ] General: launch-at-login toggle writes/removes
      `HKCU\...\Run\Screenstop` (check with regedit); survives a sign-out/in
- [ ] Screenshots: toggles + quality slider + save folder + name pattern persist
      after Save and change capture behavior immediately
- [ ] Hotkeys: record a new combo → Save → old combo stops working, new one
      captures; conflict with another app → balloon names it
- [ ] Hotkeys: Backspace resets a box to its default; Escape cancels recording
- [ ] About: version shown; trace-log / crash-report buttons open Explorer
- [ ] Crash log: force a crash (or trust the handlers) → report appears in
      `%APPDATA%\Screenstop\crashes` with version + OS + stack
- [ ] Installer: clean install (no UAC prompt), app launches from the wizard,
      tray icon appears; uninstall removes the app but keeps
      `%APPDATA%\Screenstop` (settings + crash reports) and the Run key cleanup
- [ ] Reinstall over an existing install: settings survive, no duplicate entries

## Phase 6 — Export renderer + integration ✅ (automated) / manual items open

Automated (all green, 97/97 xUnit):

- [x] Preview/export parity: same document rendered at 240px vs 960px agrees
      within per-tool tolerance (vector tools, freehand, pixelate, blur,
      numbered circle, text) — `AnnotationParityTests`
- [x] Sidecar round-trip preserves annotations; corrupt sidecar tolerated
- [x] Saving edits never modifies the source image bytes (non-destructive)
- [x] `LoadComposited` applies sidecar edits (pixel-checked) and returns the
      plain decode when there are none
- [x] Empty sidecar (no annotations) treated as unannotated
- [x] Clipboard integration test resilient to clipboard contention
      (retry + skip; DIB layout covered by the pure unit test)

Manual (needs an unlocked desktop):

- [ ] Edit a card → annotate → Save → card thumbnail shows the annotations
- [ ] Re-open Edit on the same card → previous annotations are still there
- [ ] Export… writes a flattened PNG; opening it in another app shows the
      annotations baked in
- [ ] Save/Copy from the panel export the annotated image (not the original)
- [ ] Discard an annotated card → both the staged PNG and its `.screenstop`
      sidecar are deleted

## Phase 5 — Annotation editor ✅ (automated) / manual items open

Automated (all green):

- [x] Annotation model: normalized coords, undo/redo (incl. batch gestures),
      hit-testing, marker renumbering, clone independence (21 tests)
- [x] Renderer: every tool marks the expected pixels and leaves the rest
      untouched (11 tests)
- [x] Build clean, zero warnings

Manual (needs an unlocked desktop):

- [ ] Every tool draws: rect, ellipse, arrow, freehand, text, numbered
      circle, pixelate, blur
- [ ] Select tool: click selects (dashed box + handles), drag moves (clamped
      to the image), corner handle resizes, arrow endpoints drag
- [ ] Ctrl+Z / Ctrl+Y undo/redo; a whole drag is one undo step
- [ ] Delete removes the selection; numbered markers renumber
- [ ] Text: click places the inline box; Enter commits, Esc cancels,
      Shift+Enter adds a line
- [ ] Color swatches change the active color and recolor the selection
- [ ] Large captures: canvas fits the image, annotations stay crisp

## Audit — Phases 0–4 vs. mac parity (2026-08-26)

Automated (all green, 52/52 xUnit):

- [x] Discard/eviction delete the staged temp PNG; saved exports untouched
- [x] Save/Copy keep the card on failure (retry), dismiss only on success
- [x] Panel Save uses the capture's timestamp in the filename
- [x] Panel placed on the capture's monitor (origin threaded through the pipeline)
- [x] Area crop + pipeline run off the UI thread
- [x] GDI readback writes directly into the SKBitmap buffer (no double copy)
- [x] Flat-frame detection samples 8 scanlines with early exit
- [x] Toast thumbnail aspect-fit inside the 32×32 icon canvas
- [x] Card thumbnails decode via SKCodec scaled target (PNG falls back safely)
- [x] `PreviewStack.Evicted` coverage (eviction raises, remove does not)

Manual (needs an unlocked desktop):

- [ ] Discard a card → no orphaned `.png` left in `%TEMP%\Screenstop`
- [ ] 7 captures → oldest card's staging file is deleted on eviction
- [ ] Capture on a secondary monitor → panel appears on that monitor
- [ ] Toast thumbnail shows the capture without distortion
- [ ] UWP apps (Calculator, Photos) remain absent from the window picker
      (documented limitation — PrintWindow cannot composite them)

## Phase 4 — Preview panel ✅ (automated) / manual items open

Automated (all green):

- [x] `PreviewStack` eviction tests (max-N keeps newest, drops oldest)
- [x] `PlacementResolver` centered + clamped placement tests
- [x] `DisplayAffinity` sets `WDA_EXCLUDEFROMCAPTURE` (0x11) — read back and asserted
- [x] Panel E2E: capture(s) → `ScreenstopPreviewPanel` appears, affinity held, app alive
- [x] Region-crop test is stability-guarded (skips strict compare on the animating lock screen)
- [x] Regression: 50/50 xUnit + all 6 E2E scripts green

Manual:

- [ ] After a capture the panel appears bottom-center of the active display
- [ ] Hover a card → Save/Copy/Edit/Discard buttons appear; they behave (Save writes to
      export dir, Copy puts image on clipboard, Edit opens the annotation editor, Discard
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
- [ ] Tray context menu → "Quit Screenstop" removes the icon cleanly (visual check)
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

- [ ] Defaults: capture → PNG staged in `%TEMP%\Screenstop`, toast shows size
- [ ] Toggle AutoSave → PNG appears in `Pictures\Screenstop`
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
