# Builds the Screenstop installer.
#
# Prereq: Inno Setup 6 (winget install JRSoftware.InnoSetup).
# Output: installer\Output\Screenstop-Setup-<version>.exe

param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

# 1. Publish the app (framework-dependent; the .NET 8 Desktop Runtime is a
#    prerequisite, same as running the app from source).
#    `dotnet publish -o` does not clean its target, so a previous build's output
#    survives - and the installer globs dist\*, so a stale assembly (e.g. one
#    left over from before the rename) would ship next to the new ones. Wipe
#    the directory first.
if (Test-Path "$PSScriptRoot\dist") {
    Remove-Item -Recurse -Force "$PSScriptRoot\dist"
}

Write-Host "Publishing Screenstop ($Configuration, win-x64)..." -ForegroundColor Cyan
dotnet publish "$root\src\Screenstop.App" -c $Configuration -r win-x64 `
    --self-contained false -o "$PSScriptRoot\dist"
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

# Fail loudly rather than shipping a mixed set of binaries.
$stale = Get-ChildItem "$PSScriptRoot\dist" -Filter "Screendrop.*" -ErrorAction SilentlyContinue
if ($stale) { throw "stale pre-rename assemblies in dist: $($stale.Name -join ', ')" }

# 2. Locate ISCC (per-user winget install first, then Program Files).
$iscc = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $iscc) {
    throw "ISCC.exe not found. Install Inno Setup 6: winget install JRSoftware.InnoSetup"
}

# 3. Compile the installer into a staging dir OUTSIDE the repo, then copy it
#    back. The repo lives under OneDrive, and OneDrive/antivirus sync can lock
#    the freshly created Setup.exe during the in-place icon resource update
#    (EndUpdateResource error 110). Building outside the synced folder avoids it.
$staging = Join-Path ([System.IO.Path]::GetTempPath()) ("Screenstop-installer-" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force -Path $staging | Out-Null
try {
    Write-Host "Compiling installer with $iscc..." -ForegroundColor Cyan
    & $iscc "/O$staging" "$PSScriptRoot\Screenstop.iss"
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Retrying after 3s..." -ForegroundColor Yellow
        Start-Sleep -Seconds 3
        & $iscc "/O$staging" "$PSScriptRoot\Screenstop.iss"
        if ($LASTEXITCODE -ne 0) { throw "ISCC failed" }
    }

    New-Item -ItemType Directory -Force -Path "$PSScriptRoot\Output" | Out-Null
    Get-ChildItem "$staging\*.exe" | ForEach-Object { Copy-Item $_.FullName "$PSScriptRoot\Output\" -Force }
} finally {
    Remove-Item -Recurse -Force $staging -ErrorAction SilentlyContinue
}

Write-Host ""
Write-Host "Installer ready:" -ForegroundColor Green
Get-ChildItem "$PSScriptRoot\Output\*.exe" | ForEach-Object { Write-Host "  $($_.FullName) ($([math]::Round($_.Length/1MB, 1)) MB)" }
