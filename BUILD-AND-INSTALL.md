# Building and installing Screenstop (Windows)

Everything below is run from the repository root — the folder containing
`Screenstop.sln`.

---

## Prerequisites

| Tool | Version | Needed for | Install with |
| --- | --- | --- | --- |
| .NET SDK | 8.0 or newer | building, running, packaging | `winget install Microsoft.DotNet.SDK.8` |
| .NET Desktop Runtime | 8.0 (WindowsDesktop) | running the installed app | ships with the SDK; the installer does **not** bundle it |
| Inno Setup | 6 | compiling `Screenstop-Setup-<version>.exe` | `winget install JRSoftware.InnoSetup` |

Minimum target OS is **Windows 10 19041** (the project targets
`net8.0-windows10.0.19041.0`).

---

## 1. Build and test from source

The repo has a helper script that restores, builds, and runs the tests in one
step:

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

Pass `-Configuration Release` to build optimized binaries:

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1 -Configuration Release
```

Or drive the steps individually:

```powershell
dotnet restore Screenstop.sln
dotnet build   Screenstop.sln -c Debug
dotnet test    tests\Screenstop.Core.Tests -c Debug
```

### Run it without installing

```powershell
dotnet run --project src\Screenstop.App -c Debug
```

The binary lands at
`src\Screenstop.App\bin\Debug\net8.0-windows10.0.19041.0\Screenstop.exe`.

> Screenstop is a single-instance app. If a copy is already running (including
> an installed one), launching another instance silently exits and surfaces the
> existing Settings window instead. Quit the running copy first.

---

## 2. Build the installer

```powershell
powershell -ExecutionPolicy Bypass -File .\installer\build-installer.ps1
```

This does two things:

1. `dotnet publish` the app for `win-x64` (framework-dependent) into
   `installer\dist`.
2. Compile `installer\Screenstop.iss` with Inno Setup into
   `installer\Output\Screenstop-Setup-<version>.exe`.

Use `-Configuration Debug` to package an unoptimized build.

The script compiles into a temp directory first and only then copies the
result back into `installer\Output`. That is deliberate: the repo lives under
OneDrive, and OneDrive/antivirus can lock a freshly written `Setup.exe` while
Inno Setup is still updating its icon resources.

---

## 3. Install

### Interactively

Double-click `installer\Output\Screenstop-Setup-<version>.exe` and follow the
wizard. The installer offers an optional desktop shortcut (unchecked by
default) and launches Screenstop when it finishes.

### Silently

```powershell
.\installer\Output\Screenstop-Setup-<version>.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-
```

`/SP-` suppresses the "This will install…" confirmation. No desktop shortcut is
created in silent mode. A running Screenstop is closed automatically
(`CloseApplications=yes` in the `.iss`).

### What the installer does

- **Per-user install — no UAC.** `PrivilegesRequired=lowest`, so it writes to
  `%LOCALAPPDATA%\Programs\Screenstop` and only ever touches `HKCU`.
- Registers an uninstaller under **Settings → Apps → Installed apps**.
- Creates Start Menu entries for Screenstop and Uninstall Screenstop.
- **Never deletes your settings.** `%APPDATA%\Screenstop` (preferences, crash
  reports) survives uninstall and reinstall. Only the staging folder
  `%TEMP%\Screenstop` is removed.
- On uninstall it also removes the per-user launch-at-login `Run` key.

---

## 4. Uninstall

Settings → Apps → Installed apps → **Screenstop** → Uninstall, or run the
uninstaller directly:

```powershell
& "$env:LOCALAPPDATA\Programs\Screenstop\unins000.exe"
```

---

## 5. Bumping the version

The version is declared in **two** places and they must match, or the installer
will be named one version and install another:

| File | Setting |
| --- | --- |
| `src\Screenstop.App\Screenstop.App.csproj` | `<Version>` and `<FileVersion>` |
| `installer\Screenstop.iss` | `#define MyAppVersion` |

To go from 1.1.5 to 1.1.6, change all three values:

```xml
<Version>1.1.6</Version>
<FileVersion>1.1.6.0</FileVersion>
```

```pascal
#define MyAppVersion "1.1.6"
```

Then rebuild the installer. Old `Screenstop-Setup-*.exe` files are left in
`installer\Output`; delete the ones you no longer need.

---

## Repository layout

```
Screenstop.sln
build.ps1                       restore + build + test
BUILD-AND-INSTALL.md            this file
installer/
  build-installer.ps1           publish + compile the Setup.exe
  Screenstop.iss                Inno Setup script (version lives here)
  dist/                         publish output, consumed by the .iss
  Output/                       finished Setup-<version>.exe files
src/
  Screenstop.App/               WPF app (UI, tray, capture flow)
  Screenstop.Core/              settings, geometry, annotation + background models
  Screenstop.Capture/           GDI/Win32 screen capture
  Screenstop.Rendering/         SkiaSharp annotation, background and blur rendering
tests/
  Screenstop.Core.Tests/        xUnit tests
```

The app has no MVVM framework and no code-behind XAML: all UI is constructed in
C#, with the shared visual system (the `Sd.*` resource keys) defined in
`src\Screenstop.App\App.xaml`.
