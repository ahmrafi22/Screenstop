using Microsoft.Win32;
using Screenstop.Core.Settings;

namespace Screenstop.App.Infrastructure;

/// <summary>
/// Launch-at-login via the per-user Run key (HKCU — no elevation needed,
/// per PLAN §1). The value points at the running executable, so a portable
/// copy registers itself and an installed copy registers the install path.
/// </summary>
internal static class LaunchAtLogin
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return key?.GetValue(LaunchAtLoginCommand.RunValueName) is string value
                && !string.IsNullOrWhiteSpace(value);
        }
        catch (Exception ex)
        {
            TraceLog.Write($"launch-at-login read failed: {ex.Message}");
            return false;
        }
    }

    public static void SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
            if (key is null)
            {
                TraceLog.Write("launch-at-login: could not open Run key");
                return;
            }

            if (enabled)
            {
                var exePath = Environment.ProcessPath;
                if (string.IsNullOrWhiteSpace(exePath))
                {
                    TraceLog.Write("launch-at-login: no process path");
                    return;
                }

                key.SetValue(LaunchAtLoginCommand.RunValueName, LaunchAtLoginCommand.Build(exePath));
                TraceLog.Write($"launch-at-login enabled: {exePath}");
            }
            else
            {
                key.DeleteValue(LaunchAtLoginCommand.RunValueName, throwOnMissingValue: false);
                TraceLog.Write("launch-at-login disabled");
            }
        }
        catch (Exception ex)
        {
            TraceLog.Write($"launch-at-login write failed: {ex.Message}");
        }
    }
}
