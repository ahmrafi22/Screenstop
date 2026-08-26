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

# 3. Compile the installer. Retry once: antivirus occasionally holds the
#    output file during the icon resource update (transient error 110).
Write-Host "Compiling installer with $iscc..." -ForegroundColor Cyan
& $iscc "$PSScriptRoot\Screendrop.iss"
if ($LASTEXITCODE -ne 0) {
    Write-Host "Retrying after 3s (antivirus lock)..." -ForegroundColor Yellow
    Start-Sleep -Seconds 3
    & $iscc "$PSScriptRoot\Screendrop.iss"
    if ($LASTEXITCODE -ne 0) { throw "ISCC failed" }
}

Write-Host ""
Write-Host "Installer ready:" -ForegroundColor Green
Get-ChildItem "$PSScriptRoot\Output\*.exe" | ForEach-Object { Write-Host "  $($_.FullName) ($([math]::Round($_.Length/1MB, 1)) MB)" }
