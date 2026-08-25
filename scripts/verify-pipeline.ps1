param(
    [Parameter(Mandatory = $true)]
    [string]$ExePath
)

$ErrorActionPreference = "Stop"

Add-Type -Namespace T -Name U -MemberDefinition @"
[System.Runtime.InteropServices.DllImport("user32.dll")]
public static extern bool PostMessage(System.IntPtr hwnd, uint msg, System.IntPtr wp, System.IntPtr lp);
"@

$appData = Join-Path $env:APPDATA "Screenstop"
$settingsFile = Join-Path $appData "settings.json"
$store = Join-Path $env:TEMP "Screenstop"
$exportDir = Join-Path $env:TEMP "screenstop-export-qa"

Remove-Item $exportDir -Recurse -Force -ErrorAction SilentlyContinue
if (Test-Path $store) { Remove-Item $store -Recurse -Force }

$settings = @{
    AutoSave = $true
    AutoCompress = $true
    CompressionQuality = 0.6
    ExportDirectoryPath = $exportDir
    FileNamePattern = "Shot_{date}_{type}"
} | ConvertTo-Json
New-Item -ItemType Directory -Force -Path $appData | Out-Null
Set-Content -Path $settingsFile -Value $settings -Encoding UTF8

$process = Start-Process -FilePath $ExePath -PassThru
Start-Sleep -Seconds 3

if ($process.HasExited) {
    Write-Host "FAIL app exited immediately"
    exit 1
}

$trace = Join-Path $store "trace.log"
$hwndValue = $null
foreach ($line in (Get-Content $trace)) {
    if ($line -match "hotkey window handle=(\d+)") {
        $hwndValue = [System.IntPtr]::new([long]$Matches[1])
        break
    }
}

[T.U]::PostMessage($hwndValue, 0x0312, [System.IntPtr]::new(1), [System.IntPtr]::Zero) | Out-Null
Start-Sleep -Seconds 4
Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue

$staged = @(Get-ChildItem $store -Filter Screenstop_*.png -ErrorAction SilentlyContinue)
if ($staged.Count -eq 0) {
    Write-Host "FAIL no staged PNG in temp store"
    exit 1
}
Write-Host "staged: $($staged[0].Name) ($([math]::Round($staged[0].Length/1KB)) KB)"

$exports = @(Get-ChildItem $exportDir -Filter *.jpg -ErrorAction SilentlyContinue)
if ($exports.Count -eq 0) {
    Write-Host "FAIL no compressed JPEG saved to export directory"
    exit 1
}

$nameOk = $false
foreach ($export in $exports) {
    Write-Host "exported: $($export.Name) ($([math]::Round($export.Length/1KB)) KB)"
    if ($export.Name -match "^Shot_\d{4}-\d{2}-\d{2}_fullscreen( \d+)?\.jpg$") {
        $nameOk = $true
    }
}

if (-not $nameOk) {
    Write-Host "FAIL exported filename does not match pattern 'Shot_{date}_{type}.jpg'"
    exit 1
}

Remove-Item $settingsFile -Force -ErrorAction SilentlyContinue
Remove-Item $exportDir -Recurse -Force -ErrorAction SilentlyContinue

Write-Host "PIPELINE E2E PASSED"
exit 0
