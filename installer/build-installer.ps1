# Builds the Screendrop installer.
#
# Prereq: Inno Setup 6 (winget install JRSoftware.InnoSetup).
# Output: installer\Output\Screendrop-Setup-<version>.exe

param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

# 1. Publish the app (framework-dependent; the .NET 8 Desktop Runtime is a
#    prerequisite, same as running the app from source).
Write-Host "Publishing Screendrop ($Configuration, win-x64)..." -ForegroundColor Cyan
dotnet publish "$root\src\Screendrop.App" -c $Configuration -r win-x64 `
    --self-contained false -o "$PSScriptRoot\dist"
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

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
$staging = Join-Path ([System.IO.Path]::GetTempPath()) ("Screendrop-installer-" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force -Path $staging | Out-Null
try {
    Write-Host "Compiling installer with $iscc..." -ForegroundColor Cyan
    & $iscc "/O$staging" "$PSScriptRoot\Screendrop.iss"
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Retrying after 3s..." -ForegroundColor Yellow
        Start-Sleep -Seconds 3
        & $iscc "/O$staging" "$PSScriptRoot\Screendrop.iss"
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
