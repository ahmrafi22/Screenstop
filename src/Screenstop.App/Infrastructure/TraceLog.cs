using System.IO;

namespace Screenstop.App.Infrastructure;

internal static class TraceLog
{
    private const string FileName = "trace.log";

    private static readonly string FilePath =
        Path.Combine(Path.GetTempPath(), "Screenstop", FileName);

    private static readonly object Gate = new();

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.AppendAllText(FilePath, $"{DateTimeOffset.Now:HH:mm:ss.fff} {message}{Environment.NewLine}");
            }
        }
        catch
        {
        }
    }
}
