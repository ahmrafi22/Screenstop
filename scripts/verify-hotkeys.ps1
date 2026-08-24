param(
    [Parameter(Mandatory = $true)]
    [string]$ExePath
)

$ErrorActionPreference = "Stop"

Add-Type -Namespace ScreenstopTest -Name User32 -MemberDefinition @"
[System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
public static extern bool RegisterHotKey(System.IntPtr hWnd, int id, uint fsModifiers, uint vk);
[System.Runtime.InteropServices.DllImport("user32.dll")]
public static extern bool UnregisterHotKey(System.IntPtr hWnd, int id);
"@

$combos = @(
    @{ Id = 1; Vk = 0x31; Name = "Alt+Shift+1" },
    @{ Id = 2; Vk = 0x32; Name = "Alt+Shift+2" },
    @{ Id = 3; Vk = 0x33; Name = "Alt+Shift+3" }
)

function Test-Registration {
    $result = @{}
    foreach ($combo in $combos) {
        $ok = [ScreenstopTest.User32]::RegisterHotKey([IntPtr]::Zero, $combo.Id, 0x5, $combo.Vk)
        $result[$combo.Name] = $ok
        if ($ok) {
            [ScreenstopTest.User32]::UnregisterHotKey([IntPtr]::Zero, $combo.Id) | Out-Null
        }
    }
    return $result
}

if (-not (Test-Path $ExePath)) {
    Write-Error "Executable not found at $ExePath"
    exit 1
}

$before = Test-Registration

$process = Start-Process -FilePath $ExePath -PassThru
Start-Sleep -Seconds 3
$during = Test-Registration

Stop-Process -Id $process.Id -Force
Wait-Process -Id $process.Id -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2
$after = Test-Registration

$failed = $false
foreach ($combo in $combos) {
    $name = $combo.Name

    if (-not $before[$name]) {
        Write-Host "FAIL $name is already held by another process BEFORE app launch"
        $failed = $true
    }

    if ($during[$name]) {
        Write-Host "FAIL $name was still free while the app was running (app does not hold it)"
        $failed = $true
    }

    if (-not $after[$name]) {
        Write-Host "FAIL $name was not released after the app exited"
        $failed = $true
    }
}

if ($failed) {
    Write-Host "HOTKEY VERIFICATION FAILED"
    exit 1
}

Write-Host "HOTKEY VERIFICATION PASSED: all 3 combos held while running, released on exit"
exit 0
