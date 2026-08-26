using System.IO;
using System.Text.Json;

namespace Screenstop.Core.Annotations;

/// <summary>
/// A user-saved annotation style preset (mac inspector parity): a name plus
/// the default tool and style values to apply when picked.
/// </summary>
public sealed class AnnotationPreset
{
    public string Name { get; set; } = string.Empty;

    /// Tool selected when the preset is applied.
    public AnnotationTool Tool { get; set; } = AnnotationTool.Rectangle;

    /// Default color index into AnnotationColor.Palette (-1 = keep current).
    public int ColorIndex { get; set; } = -1;

    /// Default stroke width, normalized (negative = keep current).
    public double StrokeWidth { get; set; } = -1;

    /// Default redaction strength for pixelate/blur (negative = keep current).
    public double Density { get; set; } = 0.23;
}

/// <summary>
/// Persists annotation presets to %APPDATA%\Screenstop\annotation-presets.json.
/// Pure storage + defaults; the editor applies them to the canvas state.
/// </summary>
public sealed class AnnotationPresetStore
{
    public const string CurrentSettingsName = "Current Settings";
    public const string DefaultName = "Default Preset";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _path;

    public AnnotationPresetStore(string? directory = null)
    {
        directory ??= Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Screenstop");
        _path = Path.Combine(directory, "annotation-presets.json");
    }

    /// The built-ins always present: Current Settings (captures live state)
    /// first, then saved presets, then the factory default.
    public List<AnnotationPreset> Load()
    {
        var saved = new List<AnnotationPreset>();
        try
        {
            if (File.Exists(_path))
            {
                var presets = JsonSerializer.Deserialize<List<AnnotationPreset>>(File.ReadAllText(_path), JsonOptions);
                if (presets is not null)
                {
                    saved.AddRange(presets.Where(p => p is not null && !string.IsNullOrWhiteSpace(p.Name)));
                }
            }
        }
        catch (JsonException)
        {
            // Corrupt file: fall back to built-ins rather than failing the editor.
        }
        catch (IOException)
        {
        }

        return BuiltIns(saved);
    }

    public void Save(List<AnnotationPreset> presets)
    {
        string? directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(_path, JsonSerializer.Serialize(presets, JsonOptions));
    }

    private static List<AnnotationPreset> BuiltIns(List<AnnotationPreset> saved)
    {
        var result = new List<AnnotationPreset>
        {
            new() { Name = CurrentSettingsName },
        };
        result.AddRange(saved);
        result.Add(new AnnotationPreset
        {
            Name = DefaultName,
            Tool = AnnotationTool.Rectangle,
            ColorIndex = 5, // Red in the mac-ordered palette
            Density = 0.23,
        });
        return result;
    }
}
