using System.Text.Json;

namespace Screenstop.Core.Background;

/// Actions placeable on the floating preview card (mac `OverlayCardAction`
/// parity, minus cloud/pin which are out of Windows v1 scope).
public enum CardAction
{
    Copy,
    Compress,
    Save,
    Annotate,
    View,
    Delete,
    Close,
}

public static class CardActionExtensions
{
    public static string Title(this CardAction action) => action switch
    {
        CardAction.Copy => "Copy",
        CardAction.Compress => "Compress",
        CardAction.Save => "Save",
        CardAction.Annotate => "Annotate",
        CardAction.View => "View",
        CardAction.Delete => "Delete",
        CardAction.Close => "Dismiss",
        _ => action.ToString(),
    };

    public static string Help(this CardAction action) => action switch
    {
        CardAction.Copy => "Copy to clipboard",
        CardAction.Compress => "Copy a compressed JPG",
        CardAction.Save => "Save to disk",
        CardAction.Annotate => "Annotate screenshot",
        CardAction.View => "Open in Explorer",
        CardAction.Delete => "Delete screenshot",
        CardAction.Close => "Dismiss preview",
        _ => action.ToString(),
    };

    /// Segoe Fluent Icons glyph for the corner badge.
    public static string Glyph(this CardAction action) => action switch
    {
        CardAction.Copy => "\uE8C8",      // Copy
        CardAction.Compress => "\uE73F",   // Resize mouse (compress)
        CardAction.Save => "\uE74E",       // Save
        CardAction.Annotate => "\uE70F",   // Edit
        CardAction.View => "\uE890",       // View
        CardAction.Delete => "\uE74D",     // Delete
        CardAction.Close => "\uE711",      // Cancel
        _ => "\uE711",
    };
}

/// Corner + center placement of preview card actions (mac
/// `OverlayCardLayout` parity). Center holds at most three pills.
public sealed class OverlayCardLayout : IEquatable<OverlayCardLayout>
{
    public const int MaxCenterActions = 3;

    public CardAction? TopLeading { get; set; } = CardAction.Delete;

    public CardAction? TopTrailing { get; set; } = CardAction.Close;

    public CardAction? BottomLeading { get; set; } = CardAction.Annotate;

    public CardAction? BottomTrailing { get; set; } = CardAction.View;

    public List<CardAction> Center { get; set; } = [CardAction.Copy, CardAction.Save];

    public List<CardAction> Hidden { get; set; } = [CardAction.Compress];

    public static OverlayCardLayout Default => new();

    public bool Equals(OverlayCardLayout? other) =>
        other is not null
        && TopLeading == other.TopLeading
        && TopTrailing == other.TopTrailing
        && BottomLeading == other.BottomLeading
        && BottomTrailing == other.BottomTrailing
        && Center.SequenceEqual(other.Center)
        && Hidden.SequenceEqual(other.Hidden);

    public override int GetHashCode() => HashCode.Combine(
        TopLeading, TopTrailing, BottomLeading, BottomTrailing, Center.Count, Hidden.Count);

    public OverlayCardLayout Clone() => new()
    {
        TopLeading = TopLeading,
        TopTrailing = TopTrailing,
        BottomLeading = BottomLeading,
        BottomTrailing = BottomTrailing,
        Center = Center.ToList(),
        Hidden = Hidden.ToList(),
    };
}

/// Persists the card layout as JSON (mac `OverlayCardLayoutStore` parity).
public sealed class OverlayCardLayoutStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    private readonly string _filePath;

    public OverlayCardLayout Layout { get; private set; } = OverlayCardLayout.Default;

    public event Action? Changed;

    public OverlayCardLayoutStore(string? directory = null)
    {
        string root = directory
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Screenstop");
        _filePath = Path.Combine(root, "overlay-card-layout.json");
        Load();
    }

    public void Save(OverlayCardLayout layout)
    {
        Layout = Sanitize(layout);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            string tempPath = _filePath + $".tmp.{Environment.ProcessId}";
            File.WriteAllText(tempPath, JsonSerializer.Serialize(Layout, JsonOptions));
            File.Move(tempPath, _filePath, overwrite: true);
            Changed?.Invoke();
        }
        catch (IOException)
        {
        }
    }

    public void Reset() => Save(OverlayCardLayout.Default);

    private void Load()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                return;
            }

            var layout = JsonSerializer.Deserialize<OverlayCardLayout>(File.ReadAllText(_filePath), JsonOptions);
            if (layout is not null)
            {
                Layout = Sanitize(layout);
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            Layout = OverlayCardLayout.Default;
        }
    }

    /// Every action appears exactly once across corners, center, and tray;
    /// duplicates and unknown placements fall back to the default layout.
    private static OverlayCardLayout Sanitize(OverlayCardLayout layout)
    {
        var valid = new HashSet<CardAction>(Enum.GetValues<CardAction>());
        var seen = new HashSet<CardAction>();

        CardAction? Take(CardAction? action)
        {
            if (action is null || !valid.Contains(action.Value) || !seen.Add(action.Value))
            {
                return null;
            }

            return action;
        }

        var sanitized = new OverlayCardLayout
        {
            TopLeading = Take(layout.TopLeading),
            TopTrailing = Take(layout.TopTrailing),
            BottomLeading = Take(layout.BottomLeading),
            BottomTrailing = Take(layout.BottomTrailing),
            Center = new List<CardAction>(),
            Hidden = new List<CardAction>(),
        };

        foreach (var action in layout.Center)
        {
            if (sanitized.Center.Count >= OverlayCardLayout.MaxCenterActions)
            {
                break;
            }

            var taken = Take(action);
            if (taken is not null)
            {
                sanitized.Center.Add(taken.Value);
            }
        }

        foreach (var action in layout.Hidden)
        {
            var taken = Take(action);
            if (taken is not null)
            {
                sanitized.Hidden.Add(taken.Value);
            }
        }

        foreach (var action in valid)
        {
            if (!seen.Contains(action))
            {
                sanitized.Hidden.Add(action);
            }
        }

        return sanitized;
    }
}
