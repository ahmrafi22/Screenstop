param(
    [Parameter(Mandatory = $true)]
    [string]$ExePath
)

$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.Drawing
Add-Type -Namespace T -Name U -MemberDefinition @"
[System.Runtime.InteropServices.DllImport("user32.dll", CharSet=CharSet.Unicode)]
public static extern int GetWindowText(System.IntPtr hwnd, System.Text.StringBuilder sb, int max);
[System.Runtime.InteropServices.DllImport("user32.dll")]
public static extern System.IntPtr GetForegroundWindow();
[System.Runtime.InteropServices.DllImport("user32.dll")]
public static extern uint GetWindowThreadProcessId(System.IntPtr hwnd, out uint pid);
[System.Runtime.InteropServices.DllImport("user32.dll")]
public static extern bool EnumWindows(EnumProc cb, System.IntPtr lp);
[System.Runtime.InteropServices.DllImport("user32.dll")]
public static extern bool PostMessage(System.IntPtr hwnd, uint msg, System.IntPtr wp, System.IntPtr lp);
[System.Runtime.InteropServices.DllImport("user32.dll")]
public static extern bool SetCursorPos(int x, int y);
public delegate bool EnumProc(System.IntPtr hwnd, System.IntPtr lp);
"@

$MOUSEEVENTF_MOVE = 0x0001
$MOUSEEVENTF_LEFTDOWN = 0x0002
$MOUSEEVENTF_LEFTUP = 0x0004

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

function Post-Mouse($hwnd, [int]$msg, [uint32]$wParam, [int]$x, [int]$y) {
    $lParam = [System.IntPtr][long](([uint32]$y -shl 16) -bor ([uint32]$x -band 0xFFFF))
    [T.U]::PostMessage($hwnd, $msg, [System.IntPtr]::new($wParam), $lParam) | Out-Null
}

function Test-SessionLocked {
    $fg = [T.U]::GetForegroundWindow()
    $sb = New-Object System.Text.StringBuilder 256
    [T.U]::GetWindowText($fg, $sb, 256) | Out-Null
    return ($sb.ToString() -match "Lock Screen|LockApp|Default Lock")
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

[T.U]::PostMessage($hwndValue, 0x0312, [System.IntPtr]::new(3), [System.IntPtr]::Zero) | Out-Null
Start-Sleep -Seconds 2

$overlay = Find-WindowByTitle $process.Id "ScreendropAreaSelect"
if ($overlay -eq [System.IntPtr]::Zero) {
    Write-Host "FAIL overlay did not appear"
    Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
    exit 1
}

if (Test-SessionLocked) {
    Write-Host "SKIPPED: session is locked; interactive drag requires an unlocked desktop. Run manually: Alt+Shift+3, drag a region."
    Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
    exit 0
}

$x1 = 120; $y1 = 100
$x2 = 520; $y2 = 400

[T.U]::SetCursorPos($x1, $y1) | Out-Null
Post-Mouse $overlay 0x0200 0 $x1 $y1
Start-Sleep -Milliseconds 200
[T.U]::SetCursorPos($x1, $y1) | Out-Null
Post-Mouse $overlay 0x0201 1 $x1 $y1
Start-Sleep -Milliseconds 200
[T.U]::SetCursorPos(260, 250) | Out-Null
Post-Mouse $overlay 0x0200 1 260 250
Start-Sleep -Milliseconds 150
[T.U]::SetCursorPos($x2, $y2) | Out-Null
Post-Mouse $overlay 0x0200 1 $x2 $y2
Start-Sleep -Milliseconds 200
Post-Mouse $overlay 0x0202 0 $x2 $y2
Start-Sleep -Seconds 3

Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue

$shots = @(Get-ChildItem $store -Filter Screendrop_*.png -ErrorAction SilentlyContinue)
if ($shots.Count -eq 0) {
    Write-Host "FAIL no PNG produced after area drag"
    exit 1
}

$expectedW = $x2 - $x1
$expectedH = $y2 - $y1
$failed = $false
foreach ($shot in $shots) {
    $img = [System.Drawing.Image]::FromFile($shot.FullName)
    Write-Host "CAPTURED $($shot.Name): $($img.Width)x$($img.Height) (expected ~$expectedW x $expectedH)"
    if ([math]::Abs($img.Width - $expectedW) -gt 4 -or [math]::Abs($img.Height - $expectedH) -gt 4) {
        Write-Host "FAIL dimensions do not match the dragged rectangle"
        $failed = $true
    }
    $img.Dispose()
}

if ($failed) {
    exit 1
}

Write-Host "AREA CAPTURE E2E PASSED"
exit 0
