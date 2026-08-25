using System.IO;
using System.Text.Json;

namespace Screendrop.Core.Settings;

public static class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string DefaultDirectoryPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Screendrop");

    public static string DefaultFilePath =>
        Path.Combine(DefaultDirectoryPath, "settings.json");

    public static string DefaultExportDirectoryPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            "Screendrop");

    public static ScreendropSettings Load(string? filePath = null)
    {
        var path = filePath ?? DefaultFilePath;

        if (File.Exists(path))
        {
            try
            {
                var settings = JsonSerializer.Deserialize<ScreendropSettings>(File.ReadAllText(path));
                if (settings is not null)
                {
                    settings.Normalize();
                    return settings;
                }
            }
            catch (JsonException)
            {
            }
            catch (IOException)
            {
            }
        }

        var defaults = new ScreendropSettings();
        defaults.Normalize();
        return defaults;
    }

    public static void Save(ScreendropSettings settings, string? filePath = null)
    {
        var path = filePath ?? DefaultFilePath;
        settings.Normalize();

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var tempPath = path + $".tmp.{Environment.ProcessId}";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(settings, JsonOptions));

        if (File.Exists(path))
        {
            File.Delete(path);
        }

        File.Move(tempPath, path);
    }
}
