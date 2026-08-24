# QA Checklist

Manual verification per phase (see PLAN.md §5 and §7). Check items off as verified.

## Phase 0 — Scaffold

- [ ] `dotnet build` green from clean clone (`.\build.ps1`)
- [ ] xUnit smoke test passes (`dotnet test`)
- [ ] Tray icon appears on launch
- [ ] Tray context menu → "Quit Screendrop" removes the icon cleanly
- [ ] Launching a second instance exits silently (exit code 0)
- [ ] No taskbar button / main window appears
