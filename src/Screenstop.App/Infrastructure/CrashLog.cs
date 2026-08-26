using System.IO;
using Screenstop.Core.Diagnostics;
using Screenstop.Core.Settings;

namespace Screenstop.App.Infrastructure;

/// <summary>
/// Persists crash reports to %APPDATA%\Screenstop\crashes (unlike the trace
/// log in %TEMP%, crash reports survive disk cleanup so recurring crashes
/// can be diagnosed after the fact).
/// </summary>
internal static class CrashLog
{
    public static string DirectoryPath =>
        Path.Combine(SettingsStore.DefaultDirectoryPath, "crashes");

    /// <summary>Writes the report; returns the file path, or null on failure.</summary>
    public static string? Write(CrashReport report)
    {
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            var path = Path.Combine(DirectoryPath, report.FileName);
            File.WriteAllText(path, report.Format());
            TraceLog.Write($"crash report written: {path}");
            return path;
        }
        catch (Exception ex)
        {
            TraceLog.Write($"crash report write failed: {ex.Message}");
            return null;
        }
    }
}
