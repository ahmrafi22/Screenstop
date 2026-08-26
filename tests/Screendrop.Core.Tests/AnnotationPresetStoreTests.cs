using System.IO;
using Screendrop.Core.Annotations;
using Xunit;

namespace Screendrop.Core.Tests;

public class AnnotationPresetStoreTests : IDisposable
{
    private readonly string _directory;
    private readonly AnnotationPresetStore _store;

    public AnnotationPresetStoreTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"sd-presets-{Guid.NewGuid():N}");
        _store = new AnnotationPresetStore(_directory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void Load_without_file_returns_builtins_in_mac_order()
    {
        var presets = _store.Load();

        Assert.Equal(AnnotationPresetStore.CurrentSettingsName, presets[0].Name);
        Assert.Equal(AnnotationPresetStore.DefaultName, presets[^1].Name);
        Assert.Equal(2, presets.Count);
    }

    [Fact]
    public void Saved_presets_round_trip_between_the_builtins()
    {
        var saved = new List<AnnotationPreset>
        {
            new() { Name = "Frosted Lake", Tool = AnnotationTool.Blur, ColorIndex = 6, StrokeWidth = 0.004, Density = 0.4 },
        };
        _store.Save(saved);

        var loaded = _store.Load();

        Assert.Equal(3, loaded.Count);
        Assert.Equal(AnnotationPresetStore.CurrentSettingsName, loaded[0].Name);
        Assert.Equal("Frosted Lake", loaded[1].Name);
        Assert.Equal(AnnotationTool.Blur, loaded[1].Tool);
        Assert.Equal(0.4, loaded[1].Density, 6);
        Assert.Equal(AnnotationPresetStore.DefaultName, loaded[2].Name);
    }

    [Fact]
    public void Corrupt_file_falls_back_to_builtins()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "annotation-presets.json"), "{ not json ");

        var presets = _store.Load();

        Assert.Equal(2, presets.Count);
        Assert.Equal(AnnotationPresetStore.CurrentSettingsName, presets[0].Name);
        Assert.Equal(AnnotationPresetStore.DefaultName, presets[1].Name);
    }

    [Fact]
    public void Default_preset_targets_red_in_mac_palette_order()
    {
        var presets = _store.Load();
        var defaultPreset = presets.Single(p => p.Name == AnnotationPresetStore.DefaultName);

        Assert.InRange(defaultPreset.ColorIndex, 0, AnnotationColor.Palette.Count - 1);
        Assert.Equal(AnnotationColor.Red, AnnotationColor.Palette[defaultPreset.ColorIndex]);
    }

    [Fact]
    public void Save_sanitizes_duplicate_names_and_out_of_range_style_values()
    {
        _store.Save(new List<AnnotationPreset>
        {
            new() { Name = "  Review  ", Tool = (AnnotationTool)999, ColorIndex = 999, StrokeWidth = 9, Density = 9 },
            new() { Name = "review", Tool = AnnotationTool.Text, ColorIndex = 2, StrokeWidth = 0.004, Density = 0.4 },
            new() { Name = AnnotationPresetStore.DefaultName, Tool = AnnotationTool.Blur },
        });

        var preset = Assert.Single(_store.Load(), p => p.Name == "Review");

        Assert.Equal(AnnotationTool.Rectangle, preset.Tool);
        Assert.Equal(AnnotationColor.Palette.Count - 1, preset.ColorIndex);
        Assert.Equal(0.02, preset.StrokeWidth, 6);
        Assert.Equal(1, preset.Density, 6);
    }
}
