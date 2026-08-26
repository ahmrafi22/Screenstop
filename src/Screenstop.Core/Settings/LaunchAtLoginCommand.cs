namespace Screenstop.Core.Settings;

/// <summary>
/// Builds the command line stored in the registry Run key for launch-at-login.
/// Pure string logic so quoting rules are unit-testable without touching the registry.
/// </summary>
public static class LaunchAtLoginCommand
{
    public const string RunValueName = "Screenstop";

    /// <summary>
    /// The Run-key value: the quoted executable path. Quoting is mandatory —
    /// install paths under Program Files contain spaces, and an unquoted path
    /// with spaces is a classic untrusted-service-search vector.
    /// </summary>
    public static string Build(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            throw new ArgumentException("Executable path is required.", nameof(executablePath));
        }

        var trimmed = executablePath.Trim();
        return trimmed.StartsWith('"') ? trimmed : $"\"{trimmed}\"";
    }
}
