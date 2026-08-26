namespace Screenstop.Core.Diagnostics;

/// <summary>
/// A single crash report. Formatting is pure (no IO) so the report layout
/// is unit-testable; the app layer owns file writing.
/// </summary>
public sealed record CrashReport(
    DateTimeOffset Timestamp,
    string AppVersion,
    string OsDescription,
    string ExceptionText,
    bool IsTerminating)
{
    public string FileName => BuildFileName(Timestamp);

    public static string BuildFileName(DateTimeOffset timestamp) =>
        $"crash-{timestamp:yyyyMMdd-HHmmss-fff}.txt";

    public string Format()
    {
        return string.Join(
            Environment.NewLine,
            "Screenstop crash report",
            "=======================",
            $"time:      {Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz}",
            $"version:   {AppVersion}",
            $"os:        {OsDescription}",
            $"fatal:     {(IsTerminating ? "yes" : "no")}",
            string.Empty,
            "exception:",
            ExceptionText.TrimEnd(),
            string.Empty);
    }
}
