using System.Text;
using System.Text.RegularExpressions;

namespace Screendrop.Core.History;

public static class FileNaming
{
    private static readonly char[] InvalidChars = Path.GetInvalidFileNameChars();
    private static readonly Regex TokenPattern = new(@"\{[^}]+\}", RegexOptions.Compiled);
    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    public static string BuildFileName(
        string pattern,
        DateTimeOffset time,
        string captureType,
        string extension)
    {
        var name = Sanitize(Expand(pattern, time, captureType));
        return EnsureNotReserved(name) + "." + extension.TrimStart('.');
    }

    public static string Expand(string pattern, DateTimeOffset time, string captureType)
    {
        var result = new StringBuilder(pattern);

        result.Replace("{timestamp}", time.ToString("yyyy-MM-dd-HH-mm-ss"));
        result.Replace("{date}", time.ToString("yyyy-MM-dd"));
        result.Replace("{time}", time.ToString("HH-mm-ss"));
        result.Replace("{type}", Sanitize(captureType));

        return TokenPattern.Replace(result.ToString(), string.Empty);
    }

    public static string Sanitize(string name)
    {
        var builder = new StringBuilder(name.Length);

        foreach (char c in name)
        {
            builder.Append(Array.IndexOf(InvalidChars, c) >= 0 || char.IsControl(c) ? '-' : c);
        }

        var result = builder.ToString().Trim();
        if (string.IsNullOrWhiteSpace(result) || result == "." || result == "..")
        {
            result = "Screendrop";
        }

        return result;
    }

    private static string EnsureNotReserved(string name)
    {
        int dot = name.IndexOf('.');
        string baseName = dot >= 0 ? name[..dot] : name;
        return ReservedNames.Contains(baseName) ? "_" + name : name;
    }

    public static string ResolveUnique(string directory, string fileName)
    {
        string original = Path.Combine(directory, fileName);
        if (!File.Exists(original))
        {
            return original;
        }

        string baseName = Path.GetFileNameWithoutExtension(fileName);
        string extension = Path.GetExtension(fileName);

        for (int index = 1; index <= 10_000; index++)
        {
            string candidate = Path.Combine(directory, $"{baseName} {index}{extension}");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }

        return Path.Combine(directory, $"{baseName} {Guid.NewGuid():N}{extension}");
    }
}
