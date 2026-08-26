using Screendrop.Core.Diagnostics;
using Xunit;

namespace Screendrop.Core.Tests;

public sealed class CrashReportTests
{
    private static readonly DateTimeOffset FixedTime =
        new(2026, 8, 26, 14, 30, 5, 123, TimeSpan.FromHours(6));

    [Fact]
    public void File_name_encodes_timestamp()
    {
        var report = MakeReport();

        Assert.Equal("crash-20260826-143005-123.txt", report.FileName);
    }

    [Fact]
    public void Format_contains_all_fields()
    {
        var report = MakeReport("System.InvalidOperationException: boom");

        var text = report.Format();

        Assert.Contains("Screendrop crash report", text);
        Assert.Contains("time:      2026-08-26 14:30:05.123 +06:00", text);
        Assert.Contains("version:   1.2.3.4", text);
        Assert.Contains("os:        Windows 11", text);
        Assert.Contains("fatal:     yes", text);
        Assert.Contains("System.InvalidOperationException: boom", text);
    }

    [Fact]
    public void Format_marks_non_fatal_reports()
    {
        var report = MakeReport(isTerminating: false);

        Assert.Contains("fatal:     no", report.Format());
    }

    [Fact]
    public void Format_trims_trailing_whitespace_from_exception_text()
    {
        var report = MakeReport("boom\n\n\n");

        var text = report.Format();

        Assert.DoesNotContain("boom\n\n\n", text);
        Assert.Contains("boom", text);
    }

    [Fact]
    public void Distinct_timestamps_produce_distinct_file_names()
    {
        var first = CrashReport.BuildFileName(FixedTime);
        var second = CrashReport.BuildFileName(FixedTime.AddMilliseconds(1));

        Assert.NotEqual(first, second);
    }

    private static CrashReport MakeReport(
        string exceptionText = "System.Exception: test",
        bool isTerminating = true) =>
        new(FixedTime, "1.2.3.4", "Windows 11", exceptionText, isTerminating);
}
