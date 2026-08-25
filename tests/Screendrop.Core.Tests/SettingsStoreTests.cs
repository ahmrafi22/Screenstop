using Screendrop.Core.Settings;
using Xunit;

namespace Screendrop.Core.Tests;

public class SettingsStoreTests : IDisposable
{
    private readonly string _dir;
    private readonly string _file;

    public SettingsStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"screendrop-settings-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
        _file = Path.Combine(_dir, "settings.json");
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    [Fact]
    public void Missing_file_yields_safe_defaults()
    {
        var settings = SettingsStore.Load(_file);

        Assert.False(settings.AutoSave);
        Assert.False(settings.AutoCopy);
        Assert.False(settings.AutoCompress);
        Assert.Equal(ScreendropSettings.DefaultQuality, settings.CompressionQuality);
        Assert.Equal(ScreendropSettings.DefaultFileNamePattern, settings.FileNamePattern);
        Assert.Equal(1, settings.Version);
    }

    [Fact]
    public void Save_and_load_roundtrip_preserves_values()
    {
        var original = new ScreendropSettings
        {
            AutoSave = true,
            AutoCopy = true,
            AutoCompress = true,
            CompressionQuality = 0.6,
            ExportDirectoryPath = @"C:\shots",
            FileNamePattern = "pic_{timestamp}",
        };

        SettingsStore.Save(original, _file);
        var loaded = SettingsStore.Load(_file);

        Assert.True(loaded.AutoSave);
        Assert.True(loaded.AutoCopy);
        Assert.True(loaded.AutoCompress);
        Assert.Equal(0.6, loaded.CompressionQuality);
        Assert.Equal(@"C:\shots", loaded.ExportDirectoryPath);
        Assert.Equal("pic_{timestamp}", loaded.FileNamePattern);
    }

    [Fact]
    public void Load_clamps_quality_to_valid_range()
    {
        File.WriteAllText(_file, """{"CompressionQuality": 5.0}""");
        Assert.Equal(1.0, SettingsStore.Load(_file).CompressionQuality);

        File.WriteAllText(_file, """{"CompressionQuality": -2.0}""");
        Assert.Equal(0.1, SettingsStore.Load(_file).CompressionQuality);
    }

    [Fact]
    public void Load_falls_back_to_defaults_on_corrupt_json()
    {
        File.WriteAllText(_file, "{ not valid json !!!");
        var settings = SettingsStore.Load(_file);

        Assert.Equal(ScreendropSettings.DefaultFileNamePattern, settings.FileNamePattern);
        Assert.Equal(1, settings.Version);
    }

    [Fact]
    public void Empty_pattern_falls_back_to_default()
    {
        File.WriteAllText(_file, """{"FileNamePattern": ""}""");
        Assert.Equal(ScreendropSettings.DefaultFileNamePattern, SettingsStore.Load(_file).FileNamePattern);
    }

    [Fact]
    public void Save_is_atomic_and_writes_valid_json()
    {
        var settings = new ScreendropSettings { AutoCopy = true, CompressionQuality = 0.4 };
        SettingsStore.Save(settings, _file);

        Assert.True(File.Exists(_file));
        Assert.False(File.Exists(_file + $".tmp.{Environment.ProcessId}"));

        var reloaded = SettingsStore.Load(_file);
        Assert.True(reloaded.AutoCopy);
        Assert.Equal(0.4, reloaded.CompressionQuality);
    }

    [Fact]
    public void Save_overwrites_existing_file_without_leaving_temp()
    {
        var first = new ScreendropSettings { AutoSave = true, AutoCopy = true };
        SettingsStore.Save(first, _file);

        var second = new ScreendropSettings { AutoSave = false, AutoCompress = true, CompressionQuality = 0.5 };
        SettingsStore.Save(second, _file);

        var loaded = SettingsStore.Load(_file);
        Assert.False(loaded.AutoSave);
        Assert.False(loaded.AutoCopy);
        Assert.True(loaded.AutoCompress);
        Assert.Equal(0.5, loaded.CompressionQuality);

        Assert.False(Directory.EnumerateFiles(_dir, "*.tmp.*").Any());
    }
}
