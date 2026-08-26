using System.Text.Json;
using System.Text.Json.Serialization;

namespace Screenstop.Core.Annotations;

/// Persisted edit state for a screenshot, stored next to the image as
/// `<image>.screenstop` (mac parity: non-destructive, re-openable edits).
/// All geometry is normalized [0,1] relative to the base image.
public sealed class AnnotationDocument
{
    public const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public int Version { get; set; } = CurrentVersion;

    public List<Annotation> Annotations { get; set; } = new();

    public AnnotationDocument Clone() => new()
    {
        Version = Version,
        Annotations = Annotations.Select(a => a.Clone()).ToList(),
    };

    public static string SidecarPathFor(string imagePath) => imagePath + ".screenstop";

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public static AnnotationDocument? FromJson(string json)
    {
        try
        {
            var document = JsonSerializer.Deserialize<AnnotationDocument>(json, JsonOptions);
            document?.Annotations.RemoveAll(a => a is null);
            return document;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static AnnotationDocument? Load(string imagePath)
    {
        string sidecar = SidecarPathFor(imagePath);
        if (!File.Exists(sidecar))
        {
            return null;
        }

        try
        {
            return FromJson(File.ReadAllText(sidecar));
        }
        catch (IOException)
        {
            return null;
        }
    }

    public void Save(string imagePath)
    {
        string sidecar = SidecarPathFor(imagePath);
        string tempPath = sidecar + $".tmp.{Environment.ProcessId}";
        File.WriteAllText(tempPath, ToJson());
        File.Move(tempPath, sidecar, overwrite: true);
    }
}
