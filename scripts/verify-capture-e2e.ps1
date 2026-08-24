param(
    [Parameter(Mandatory = $true)]
    [string]$ExePath
)

$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.Drawing
Add-Type -Namespace T -Name U -MemberDefinition @"
[System.Runtime.InteropServices.DllImport("user32.dll")]
public static extern bool PostMessage(System.IntPtr hwnd, uint msg, System.IntPtr wp, System.IntPtr lp);
"@

$store = Join-Path $env:TEMP "Screendrop"
if (Test-Path $store) { Remove-Item $store -Recurse -Force }

$process = Start-Process -FilePath $ExePath -PassThru
Start-Sleep -Seconds 3

if ($process.HasExited) {
    Write-Host "FAIL app exited immediately"
    exit 1
}

$trace = Join-Path $store "trace.log"
if (-not (Test-Path $trace)) {
    Write-Host "FAIL trace log missing"
    Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
    exit 1
}

$hwndValue = $null
foreach ($line in (Get-Content $trace)) {
    if ($line -match "hotkey window handle=(\d+)") {
        $hwndValue = [System.IntPtr]::new([long]$Matches[1])
        break
    }
}

if ($null -eq $hwndValue) {
    Write-Host "FAIL could not read hotkey window handle from trace"
    Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
    exit 1
}

Write-Host "posting WM_HOTKEY(Fullscreen) to hwnd $hwndValue"
[T.U]::PostMessage($hwndValue, 0x0312, [System.IntPtr]::new(1), [System.IntPtr]::Zero) | Out-Null

Start-Sleep -Seconds 4
Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue

$shots = @(Get-ChildItem $store -Filter Screendrop_*.png -ErrorAction SilentlyContinue)
if ($shots.Count -eq 0) {
    Write-Host "FAIL no PNG produced in $store"
    exit 1
}

$failed = $false
foreach ($shot in $shots) {
    $img = [System.Drawing.Image]::FromFile($shot.FullName)
    Write-Host "CAPTURED $($shot.Name): $($img.Width)x$($img.Height) $([math]::Round($shot.Length/1KB)) KB"
    if ($img.Width -le 0 -or $img.Height -le 0) {
        $failed = $true
    }
    $img.Dispose()
}

if ($failed) {
    Write-Host "FAIL invalid capture dimensions"
    exit 1
}

Write-Host "E2E CAPTURE PASSED"
exit 0
