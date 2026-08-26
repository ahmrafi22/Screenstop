using Screendrop.Core.Settings;
using Xunit;

namespace Screendrop.Core.Tests;

public sealed class HotkeyComboTests
{
    [Theory]
    [InlineData("Alt+Shift+1", true, true, false, false, 0x31)]
    [InlineData("alt+shift+1", true, true, false, false, 0x31)]
    [InlineData("Shift+Alt+1", true, true, false, false, 0x31)]
    [InlineData("Ctrl+F4", false, false, true, false, 0x73)]
    [InlineData("Ctrl+Win+Z", false, false, true, true, 0x5A)]
    public void Parse_accepts_valid_combos(
        string text, bool alt, bool shift, bool ctrl, bool win, int vk)
    {
        Assert.True(HotkeyCombo.TryParse(text, out var combo));
        Assert.Equal(alt, combo.Alt);
        Assert.Equal(shift, combo.Shift);
        Assert.Equal(ctrl, combo.Control);
        Assert.Equal(win, combo.Windows);
        Assert.Equal(vk, combo.VirtualKey);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Alt+Shift")]        // no key
    [InlineData("Alt")]              // modifier only
    [InlineData("Alt+Shift+1+2")]    // two keys
    [InlineData("Alt+F25")]          // F-key out of range
    [InlineData("Alt+NotAKey")]      // unknown token
    public void Parse_rejects_invalid_combos(string text)
    {
        Assert.False(HotkeyCombo.TryParse(text, out _));
    }

    [Fact]
    public void Format_is_canonical_regardless_of_input_order()
    {
        Assert.True(HotkeyCombo.TryParse("Shift+Alt+1", out var combo));

        Assert.Equal("Alt+Shift+1", combo.Format());
    }

    [Fact]
    public void Format_round_trips_all_defaults()
    {
        foreach (var text in new[]
        {
            HotkeyCombo.DefaultFullscreen,
            HotkeyCombo.DefaultWindow,
            HotkeyCombo.DefaultArea,
        })
        {
            Assert.True(HotkeyCombo.TryParse(text, out var combo));
            Assert.Equal(text, combo.Format());
        }
    }

    [Theory]
    [InlineData("Alt+1", true)]
    [InlineData("1", false)]          // bare key — would hijack typing
    [InlineData("F9", false)]
    [InlineData("Ctrl+F9", true)]
    public void HasModifier_flags_bare_keys(string text, bool expected)
    {
        Assert.True(HotkeyCombo.TryParse(text, out var combo));
        Assert.Equal(expected, combo.HasModifier);
    }

    [Theory]
    [InlineData(0x31, "1")]
    [InlineData(0x5A, "Z")]
    [InlineData(0x70, "F1")]
    [InlineData(0x87, "F24")]
    [InlineData(0x20, "Space")]
    [InlineData(0x0D, "Enter")]
    [InlineData(0x1B, "Esc")]
    [InlineData(0x25, "Left")]
    public void Key_name_covers_common_keys(int vk, string expected)
    {
        Assert.Equal(expected, HotkeyCombo.KeyName(vk));
    }

    [Fact]
    public void Settings_normalize_restores_invalid_hotkeys_to_defaults()
    {
        var settings = new ScreendropSettings
        {
            FullscreenHotkey = "garbage",
            WindowHotkey = "",
            AreaHotkey = "Ctrl+Alt+A",
        };

        settings.Normalize();

        Assert.Equal(HotkeyCombo.DefaultFullscreen, settings.FullscreenHotkey);
        Assert.Equal(HotkeyCombo.DefaultWindow, settings.WindowHotkey);
        Assert.Equal("Ctrl+Alt+A", settings.AreaHotkey);
    }

    [Fact]
    public void Settings_normalize_rejects_bare_key_hotkeys()
    {
        var settings = new ScreendropSettings { FullscreenHotkey = "5" };

        settings.Normalize();

        Assert.Equal(HotkeyCombo.DefaultFullscreen, settings.FullscreenHotkey);
    }

    [Fact]
    public void Settings_normalize_resolves_duplicate_hotkeys()
    {
        var settings = new ScreendropSettings
        {
            FullscreenHotkey = "Ctrl+Alt+F",
            WindowHotkey = "Ctrl+Alt+F",   // duplicate of fullscreen
            AreaHotkey = "Ctrl+Alt+F",     // duplicate again
        };

        settings.Normalize();

        Assert.Equal("Ctrl+Alt+F", settings.FullscreenHotkey);
        Assert.Equal(HotkeyCombo.DefaultWindow, settings.WindowHotkey);
        Assert.Equal(HotkeyCombo.DefaultArea, settings.AreaHotkey);
    }

    [Fact]
    public void Settings_normalize_canonicalizes_hotkey_format()
    {
        var settings = new ScreendropSettings { FullscreenHotkey = "shift+alt+9" };

        settings.Normalize();

        Assert.Equal("Alt+Shift+9", settings.FullscreenHotkey);
    }
}
