using Screendrop.Core.Settings;
using Xunit;

namespace Screendrop.Core.Tests;

public sealed class LaunchAtLoginCommandTests
{
    [Fact]
    public void Build_quotes_a_plain_path()
    {
        var command = LaunchAtLoginCommand.Build(@"C:\Program Files\Screendrop\Screendrop.exe");

        Assert.Equal("\"C:\\Program Files\\Screendrop\\Screendrop.exe\"", command);
    }

    [Fact]
    public void Build_quotes_a_path_without_spaces_too()
    {
        // Always quote: the Run key is parsed by the shell, and consistent
        // quoting avoids every edge case around spaces added later (updates
        // that move the install dir, renamed folders).
        var command = LaunchAtLoginCommand.Build(@"C:\Tools\Screendrop.exe");

        Assert.Equal("\"C:\\Tools\\Screendrop.exe\"", command);
    }

    [Fact]
    public void Build_does_not_double_quote_an_already_quoted_path()
    {
        var command = LaunchAtLoginCommand.Build("\"C:\\Tools\\Screendrop.exe\"");

        Assert.Equal("\"C:\\Tools\\Screendrop.exe\"", command);
    }

    [Fact]
    public void Build_trims_surrounding_whitespace()
    {
        var command = LaunchAtLoginCommand.Build("  C:\\Tools\\Screendrop.exe  ");

        Assert.Equal("\"C:\\Tools\\Screendrop.exe\"", command);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Build_rejects_empty_paths(string path)
    {
        Assert.Throws<ArgumentException>(() => LaunchAtLoginCommand.Build(path));
    }
}
