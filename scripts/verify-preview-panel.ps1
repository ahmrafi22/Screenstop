param(
    [Parameter(Mandatory = $true)]
    [string]$ExePath
)

$ErrorActionPreference = "Stop"

Add-Type -Namespace T -Name U -MemberDefinition @"
[System.Runtime.InteropServices.DllImport("user32.dll", CharSet=CharSet.Unicode)]
public static extern int GetWindowText(System.IntPtr hwnd, System.Text.StringBuilder sb, int max);
[System.Runtime.InteropServices.DllImport("user32.dll")]
public static extern uint GetWindowThreadProcessId(System.IntPtr hwnd, out uint pid);
[System.Runtime.InteropServices.DllImport("user32.dll")]
public static extern bool EnumWindows(EnumProc cb, System.IntPtr lp);
[System.Runtime.InteropServices.DllImport("user32.dll")]
public static extern bool PostMessage(System.IntPtr hwnd, uint msg, System.IntPtr wp, System.IntPtr lp);
[System.Runtime.InteropServices.DllImport("user32.dll")]
public static extern bool GetWindowDisplayAffinity(System.IntPtr hwnd, out uint affinity);
public delegate bool EnumProc(System.IntPtr hwnd, System.IntPtr lp);
"@

function Find-WindowByTitle([int]$processId, [string]$title) {
    $cb = [T.U+EnumProc]{
        param($h, $lp)
        $sb = New-Object System.Text.StringBuilder 256
        [T.U]::GetWindowText($h, $sb, 256) | Out-Null
        $owner = [uint32]0
        [T.U]::GetWindowThreadProcessId($h, [ref]$owner) | Out-Null
        if ($owner -eq $script:procId -and $sb.ToString() -eq $script:wantedTitle) {
            $script:foundHwnd = $h
            return $false
        }
        return $true
    }
    $script:procId = [uint32]$processId
    $script:wantedTitle = $title
    $script:foundHwnd = [System.IntPtr]::Zero
    [T.U]::EnumWindows($cb, [System.IntPtr]::Zero) | Out-Null
    return $script:foundHwnd
}

$store = Join-Path $env:TEMP "Screendrop"
if (Test-Path $store) { Remove-Item $store -Recurse -Force }

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
Start-Sleep -Seconds 3
[T.U]::PostMessage($hwndValue, 0x0312, [System.IntPtr]::new(1), [System.IntPtr]::Zero) | Out-Null
Start-Sleep -Seconds 3

$panel = Find-WindowByTitle $process.Id "ScreendropPreviewPanel"
if ($panel -eq [System.IntPtr]::Zero) {
    Write-Host "FAIL preview panel did not appear after capture"
    Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
    exit 1
}
Write-Host "preview panel appeared after two captures: $panel"

$affinity = [uint32]0
[T.U]::GetWindowDisplayAffinity($panel, [ref]$affinity) | Out-Null
Write-Host "panel display affinity: 0x$('{0:X}' -f $affinity)"
if ($affinity -ne 0x11) {
    Write-Host "FAIL panel is not excluded from capture (expected WDA_EXCLUDEFROMCAPTURE 0x11)"
    Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
    exit 1
}

$staged = @(Get-ChildItem $store -Filter Screendrop_*.png -ErrorAction SilentlyContinue)
Write-Host "staged captures: $($staged.Count)"
if ($staged.Count -lt 2) {
    Write-Host "FAIL expected at least two staged captures"
    Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
    exit 1
}

if ($process.HasExited) {
    Write-Host "FAIL app died after panel appeared"
    exit 1
}
Write-Host "app still alive"

Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
Write-Host "PREVIEW PANEL SMOKE PASSED"
exit 0