using System.Text.Json;

namespace Screenstop.Core.Background;

/// A named, reusable snapshot of the background, layout, camera, focus blur,
/// screenshot border, and watermark settings (mac
/// `AnnotationBackgroundPreset` parity).
public sealed class BackgroundPreset
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    public BackgroundSettings Background { get; set; } = new();

    public bool Matches(BackgroundSettings candidate) =>
        Background.PresetComparable().Equals(candidate.PresetComparable());

    public bool HasMissingWallpaper =>
        Background.Style.Kind == BackgroundStyleKind.Wallpaper
        && !string.IsNullOrEmpty(Background.Style.WallpaperPath)
        && !File.Exists(Background.Style.WallpaperPath);
}

/// Persists the background preset library as JSON in the app-data directory
/// (mac `AnnotationBackgroundPresetStore` parity, minus UserDefaults).
public sealed class BackgroundPresetStore
{
    public const int MaximumNameLength = 48;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    private sealed class Library
    {
        public int Version { get; set; } = 1;

        public List<BackgroundPreset> Presets { get; set; } = new();

        public Guid? DefaultPresetId { get; set; }
    }

    private readonly string _filePath;

    public List<BackgroundPreset> Presets { get; private set; } = new();

    public Guid? DefaultPresetId { get; private set; }

    public event Action? Changed;

    public BackgroundPresetStore(string? directory = null)
    {
        string root = directory
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Screenstop");
        _filePath = Path.Combine(root, "background-presets.json");
        Load();
    }

    public BackgroundPreset? DefaultPreset =>
        Presets.FirstOrDefault(p => p.Id == DefaultPresetId && !p.HasMissingWallpaper);

    public BackgroundPreset? ById(Guid? id) =>
        id is null ? null : Presets.FirstOrDefault(p => p.Id == id);

    public BackgroundPreset? ByName(string rawName)
    {
        string candidate = NormalizedName(rawName);
        if (candidate.Length == 0)
        {
            return null;
        }

        return Presets.FirstOrDefault(p => NamesMatch(p.Name, candidate));
    }

    /// Creates a new preset. Duplicate names are rejected so saving from the
    /// naming popover can never overwrite a user's existing recipe.
    public BackgroundPreset? SavePreset(string rawName, BackgroundSettings settings)
    {
        string name = NormalizedName(rawName);
        if (name.Length == 0 || ByName(name) is not null)
        {
            return null;
        }

        if (settings.Style.Kind == BackgroundStyleKind.Wallpaper
            && !string.IsNullOrEmpty(settings.Style.WallpaperPath)
            && !File.Exists(settings.Style.WallpaperPath))
        {
            return null;
        }

        var preset = new BackgroundPreset { Name = name, Background = settings.Clone() };
        Presets.Add(preset);
        Persist();
        return preset;
    }

    public void SetDefaultPreset(Guid? id)
    {
        if (id is not null && !Presets.Any(p => p.Id == id && !p.HasMissingWallpaper))
        {
            return;
        }

        DefaultPresetId = id;
        Persist();
    }

    public BackgroundPreset? DeletePreset(Guid id)
    {
        var preset = Presets.FirstOrDefault(p => p.Id == id);
        if (preset is null)
        {
            return null;
        }

        Presets.Remove(preset);
        if (DefaultPresetId == id)
        {
            DefaultPresetId = null;
        }

        Persist();
        return preset;
    }

    public static string NormalizedName(string rawName)
    {
        string collapsed = string.Join(
            " ",
            rawName.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return collapsed.Length <= MaximumNameLength
            ? collapsed
            : collapsed[..MaximumNameLength];
    }

    private static bool NamesMatch(string lhs, string rhs) =>
        string.Equals(lhs, rhs, StringComparison.OrdinalIgnoreCase);

    private void Load()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                return;
            }

            var library = JsonSerializer.Deserialize<Library>(File.ReadAllText(_filePath), JsonOptions);
            if (library is null || library.Version != 1)
            {
                return;
            }

            Presets = Sanitize(library.Presets);
            DefaultPresetId = Presets.Any(p => p.Id == library.DefaultPresetId)
                ? library.DefaultPresetId
                : null;
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            Presets = new List<BackgroundPreset>();
            DefaultPresetId = null;
        }
    }

    private void Persist()
    {
        try
        {
            var library = new Library
            {
                Version = 1,
                Presets = Presets,
                DefaultPresetId = DefaultPresetId,
            };

            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            string tempPath = _filePath + $".tmp.{Environment.ProcessId}";
            File.WriteAllText(tempPath, JsonSerializer.Serialize(library, JsonOptions));
            File.Move(tempPath, _filePath, overwrite: true);
            Changed?.Invoke();
        }
        catch (IOException)
        {
        }
    }

    private static List<BackgroundPreset> Sanitize(List<BackgroundPreset> loaded)
    {
        var seenIds = new HashSet<Guid>();
        var seenNames = new List<string>();
        var result = new List<BackgroundPreset>(loaded.Count);

        foreach (var preset in loaded)
        {
            if (preset is null)
            {
                continue;
            }

            var id = seenIds.Add(preset.Id) ? preset.Id : Guid.NewGuid();
            seenIds.Add(id);

            string normalized = NormalizedName(preset.Name);
            string name = UniqueSanitizedName(
                normalized.Length == 0 ? "Untitled Preset" : normalized,
                seenNames);
            seenNames.Add(name);

            preset.Id = id;
            preset.Name = name;
            result.Add(preset);
        }

        return result;
    }

    private static string UniqueSanitizedName(string baseName, List<string> existingNames)
    {
        if (!existingNames.Any(existing => NamesMatch(existing, baseName)))
        {
            return baseName;
        }

        for (int suffix = 2; ; suffix++)
        {
            string suffixText = $" {suffix}";
            int availableCharacters = Math.Max(1, MaximumNameLength - suffixText.Length);
            string candidate = baseName[..Math.Min(baseName.Length, availableCharacters)] + suffixText;
            if (!existingNames.Any(existing => NamesMatch(existing, candidate)))
            {
                return candidate;
            }
        }
    }
}
