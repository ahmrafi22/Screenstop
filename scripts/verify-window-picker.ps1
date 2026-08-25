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

if ($null -eq $hwndValue) {
    Write-Host "FAIL could not read hotkey hwnd from trace"
    Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
    exit 1
}

[T.U]::PostMessage($hwndValue, 0x0312, [System.IntPtr]::new(2), [System.IntPtr]::Zero) | Out-Null
Start-Sleep -Seconds 2

$overlay = Find-WindowByTitle $process.Id "ScreendropWindowPicker"
if ($overlay -eq [System.IntPtr]::Zero) {
    Write-Host "FAIL window picker overlay did not appear"
    Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
    exit 1
}
Write-Host "picker overlay appeared: $overlay"

[T.U]::PostMessage($overlay, 0x0100, [System.IntPtr]::new(0x1B), [System.IntPtr]::Zero) | Out-Null
Start-Sleep -Seconds 2

$stillThere = Find-WindowByTitle $process.Id "ScreendropWindowPicker"
if ($stillThere -ne [System.IntPtr]::Zero) {
    Write-Host "FAIL picker overlay did not close on Escape"
    Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
    exit 1
}
Write-Host "picker overlay closed on Escape"

$cancelled = (Get-Content $trace | Select-String "window pick cancelled").Count
if ($cancelled -lt 1) {
    Write-Host "FAIL trace does not record cancellation"
    Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
    exit 1
}
Write-Host "trace records cancellation"

if ($process.HasExited) {
    Write-Host "FAIL app died during picker"
    exit 1
}
Write-Host "app still alive"

Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
Write-Host "WINDOW PICKER SMOKE PASSED"
exit 0