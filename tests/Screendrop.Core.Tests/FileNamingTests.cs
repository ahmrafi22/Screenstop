using Screendrop.Core.History;
using Xunit;

namespace Screendrop.Core.Tests;

public class FileNamingTests
{
    private static readonly DateTimeOffset FixedTime = new(2026, 8, 25, 10, 30, 15, TimeSpan.Zero);

    [Fact]
    public void Default_pattern_matches_mac_format()
    {
        var name = FileNaming.BuildFileName("Screendrop_{timestamp}", FixedTime, "fullscreen", "png");

        Assert.Equal("Screendrop_2026-08-25-10-30-15.png", name);
    }

    [Fact]
    public void Date_time_and_type_tokens_expand()
    {
        var name = FileNaming.BuildFileName("{date}_{time}_{type}", FixedTime, "area", "jpg");

        Assert.Equal("2026-08-25_10-30-15_area.jpg", name);
    }

    [Fact]
    public void Unknown_tokens_are_removed()
    {
        var name = FileNaming.Expand("{timestamp}{bogus}", FixedTime, "window");

        Assert.Equal("2026-08-25-10-30-15", name);
    }

    [Fact]
    public void Invalid_filename_chars_are_sanitized()
    {
        var name = FileNaming.BuildFileName("a:b/c?d*e|f", FixedTime, "fullscreen", "png");

        Assert.DoesNotContain(':', name);
        Assert.DoesNotContain('/', name);
        Assert.DoesNotContain('?', name);
        Assert.DoesNotContain('*', name);
        Assert.DoesNotContain('|', name);
        Assert.EndsWith(".png", name);
    }

    [Theory]
    [InlineData("CON")]
    [InlineData("PRN")]
    [InlineData("AUX")]
    [InlineData("NUL")]
    [InlineData("COM1")]
    [InlineData("LPT9")]
    public void Reserved_device_names_are_prefix_guarded(string pattern)
    {
        var name = FileNaming.BuildFileName(pattern, FixedTime, "fullscreen", "png");

        Assert.StartsWith("_", name);
        Assert.EndsWith(".png", name);
    }

    [Fact]
    public void ResolveUnique_returns_original_when_free()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"screendrop-naming-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);

        try
        {
            var resolved = FileNaming.ResolveUnique(dir, "shot.png");
            Assert.Equal(Path.Combine(dir, "shot.png"), resolved);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ResolveUnique_numbers_existing_files()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"screendrop-naming-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "shot.png"), new byte[] { 1 });
        File.WriteAllBytes(Path.Combine(dir, "shot 1.png"), new byte[] { 1 });

        try
        {
            var resolved = FileNaming.ResolveUnique(dir, "shot.png");
            Assert.Equal(Path.Combine(dir, "shot 2.png"), resolved);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
