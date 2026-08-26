using System.Text;

namespace Screendrop.Core.Settings;

/// <summary>
/// A global hotkey combination, stored as a canonical string in settings
/// (e.g. "Alt+Shift+1"). Parsing/formatting is pure so the rules are
/// unit-testable; the app layer maps it to RegisterHotKey modifiers.
/// </summary>
public sealed record HotkeyCombo(bool Alt, bool Shift, bool Control, bool Windows, int VirtualKey)
{
    public const string DefaultFullscreen = "Alt+Shift+1";
    public const string DefaultWindow = "Alt+Shift+2";
    public const string DefaultArea = "Alt+Shift+3";

    /// <summary>Canonical "Mod+Mod+Key" form, modifiers in fixed order.</summary>
    public string Format()
    {
        var builder = new StringBuilder();
        if (Control) builder.Append("Ctrl+");
        if (Alt) builder.Append("Alt+");
        if (Shift) builder.Append("Shift+");
        if (Windows) builder.Append("Win+");
        builder.Append(KeyName(VirtualKey));
        return builder.ToString();
    }

    public override string ToString() => Format();

    /// <summary>
    /// Parses "Alt+Shift+1", "Ctrl+F4", etc. Returns false on unknown keys,
    /// missing key, or empty input. Modifier order in the input is free.
    /// </summary>
    public static bool TryParse(string? text, out HotkeyCombo combo)
    {
        combo = new HotkeyCombo(false, false, false, false, 0);

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var parts = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return false;
        }

        bool alt = false, shift = false, control = false, windows = false;
        int virtualKey = 0;
        bool hasKey = false;

        foreach (var part in parts)
        {
            switch (part.ToLowerInvariant())
            {
                case "alt": alt = true; break;
                case "shift": shift = true; break;
                case "ctrl":
                case "control": control = true; break;
                case "win":
                case "windows": windows = true; break;
                default:
                    if (hasKey)
                    {
                        return false; // two key tokens — malformed
                    }

                    if (!TryParseKey(part, out virtualKey))
                    {
                        return false;
                    }

                    hasKey = true;
                    break;
            }
        }

        if (!hasKey)
        {
            return false;
        }

        combo = new HotkeyCombo(alt, shift, control, windows, virtualKey);
        return true;
    }

    /// <summary>
    /// A combo is usable as a global hotkey only with at least one modifier —
    /// a bare key would hijack normal typing system-wide.
    /// </summary>
    public bool HasModifier => Alt || Shift || Control || Windows;

    public static string KeyName(int virtualKey)
    {
        return virtualKey switch
        {
            >= 0x30 and <= 0x39 => ((char)virtualKey).ToString(),          // '0'-'9'
            >= 0x41 and <= 0x5A => ((char)virtualKey).ToString(),          // 'A'-'Z'
            >= 0x70 and <= 0x87 => $"F{virtualKey - 0x70 + 1}",            // F1-F24
            0x20 => "Space",
            0x0D => "Enter",
            0x09 => "Tab",
            0x1B => "Esc",
            0x08 => "Backspace",
            0x2D => "Insert",
            0x2E => "Delete",
            0x24 => "Home",
            0x23 => "End",
            0x21 => "PageUp",
            0x22 => "PageDown",
            0x25 => "Left",
            0x26 => "Up",
            0x27 => "Right",
            0x28 => "Down",
            0xBA => ";", 0xBB => "=", 0xBC => ",", 0xBD => "-",
            0xBE => ".", 0xBF => "/", 0xC0 => "`",
            0xDB => "[", 0xDC => "\\", 0xDD => "]", 0xDE => "'",
            _ => $"VK{virtualKey:X2}",
        };
    }

    private static bool TryParseKey(string token, out int virtualKey)
    {
        virtualKey = 0;

        if (token.Length == 1)
        {
            char c = char.ToUpperInvariant(token[0]);
            if (c is >= '0' and <= '9')
            {
                virtualKey = c; // '0'-'9' == 0x30-0x39
                return true;
            }

            if (c is >= 'A' and <= 'Z')
            {
                virtualKey = c; // 'A'-'Z' == 0x41-0x5A
                return true;
            }
        }

        if (token.Length > 1
            && (token[0] == 'F' || token[0] == 'f')
            && int.TryParse(token.AsSpan(1), out int fNumber)
            && fNumber is >= 1 and <= 24)
        {
            virtualKey = 0x70 + fNumber - 1;
            return true;
        }

        switch (token.ToLowerInvariant())
        {
            case "space": virtualKey = 0x20; return true;
            case "enter":
            case "return": virtualKey = 0x0D; return true;
            case "tab": virtualKey = 0x09; return true;
            case "esc":
            case "escape": virtualKey = 0x1B; return true;
            case "backspace": virtualKey = 0x08; return true;
            case "insert": virtualKey = 0x2D; return true;
            case "delete":
            case "del": virtualKey = 0x2E; return true;
            case "home": virtualKey = 0x24; return true;
            case "end": virtualKey = 0x23; return true;
            case "pageup": virtualKey = 0x21; return true;
            case "pagedown": virtualKey = 0x22; return true;
            case "left": virtualKey = 0x25; return true;
            case "up": virtualKey = 0x26; return true;
            case "right": virtualKey = 0x27; return true;
            case "down": virtualKey = 0x28; return true;
            case ";": virtualKey = 0xBA; return true;
            case "=": virtualKey = 0xBB; return true;
            case ",": virtualKey = 0xBC; return true;
            case "-": virtualKey = 0xBD; return true;
            case ".": virtualKey = 0xBE; return true;
            case "/": virtualKey = 0xBF; return true;
            case "`": virtualKey = 0xC0; return true;
            case "[": virtualKey = 0xDB; return true;
            case "\\": virtualKey = 0xDC; return true;
            case "]": virtualKey = 0xDD; return true;
            case "'": virtualKey = 0xDE; return true;
        }

        return false;
    }
}
