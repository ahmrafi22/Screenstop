using System.IO;
using System.Text.Json;

namespace Screendrop.Core.Annotations;

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
/// Persists annotation presets to %APPDATA%\Screendrop\annotation-presets.json.
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
            "Screendrop");
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

        return BuiltIns(Sanitize(saved));
    }

    public void Save(List<AnnotationPreset> presets)
    {
        string? directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var saved = Sanitize(presets);
        string temporaryPath = _path + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(saved, JsonOptions));
        File.Move(temporaryPath, _path, overwrite: true);
    }

    private static List<AnnotationPreset> Sanitize(IEnumerable<AnnotationPreset> presets)
    {
        var result = new List<AnnotationPreset>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in presets)
        {
            string name = source.Name?.Trim() ?? string.Empty;
            if (name.Length > 48) name = name[..48];
            if (string.IsNullOrWhiteSpace(name) || name is CurrentSettingsName or DefaultName || !names.Add(name)) continue;

            result.Add(new AnnotationPreset
            {
                Name = name,
                Tool = Enum.IsDefined(source.Tool) ? source.Tool : AnnotationTool.Rectangle,
                ColorIndex = Math.Clamp(source.ColorIndex, -1, AnnotationColor.Palette.Count - 1),
                StrokeWidth = source.StrokeWidth < 0 ? -1 : Math.Clamp(source.StrokeWidth, 0.002, 0.02),
                Density = source.Density < 0 ? -1 : Math.Clamp(source.Density, 0.02, 1),
            });
        }
        return result;
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
            ColorIndex = 1, // Red in the mac-ordered palette
            Density = 0.23,
        });
        return result;
    }
}
