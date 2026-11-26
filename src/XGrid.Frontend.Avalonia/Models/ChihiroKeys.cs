using Avalonia.Input;

namespace XGrid.Frontend.Avalonia.Models;

/// <summary>
/// Names and captures Chihiro cabinet bindings. Bindings are SDL scancodes;
/// 1000 plus SDL's button number means a mouse button, and 0 is unbound.
/// </summary>
public static class ChihiroKeys
{
    /// <summary>Returns a readable name for a binding.</summary>
    /// <param name="binding">SDL scancode, mouse binding or 0.</param>
    /// <returns>The display name.</returns>
    public static string Describe(int binding)
    {
        if (binding == 0)
        {
            return "Unbound";
        }

        if (binding is >= 1001 and <= 1008)
        {
            return binding switch
            {
                1001 => "Mouse left",
                1002 => "Mouse middle",
                1003 => "Mouse right",
                1004 => "Mouse side 1",
                1005 => "Mouse side 2",
                _ => $"Mouse {binding - 1000}",
            };
        }

        if (binding is >= 4 and <= 29)
        {
            return ((char)('A' + binding - 4)).ToString();
        }

        if (binding is >= 30 and <= 38)
        {
            return ((char)('1' + binding - 30)).ToString();
        }

        if (binding is >= 58 and <= 69)
        {
            return "F" + (binding - 57);
        }

        if (binding is >= 89 and <= 98)
        {
            return "Keypad " + (binding - 88) % 10;
        }

        return binding switch
        {
            39 => "0",
            40 => "Enter",
            41 => "Escape",
            42 => "Backspace",
            43 => "Tab",
            44 => "Space",
            45 => "-",
            46 => "=",
            47 => "[",
            48 => "]",
            49 => "\\",
            51 => ";",
            52 => "'",
            53 => "`",
            54 => ",",
            55 => ".",
            56 => "/",
            57 => "Caps Lock",
            70 => "Print Screen",
            71 => "Scroll Lock",
            72 => "Pause",
            73 => "Insert",
            74 => "Home",
            75 => "Page Up",
            76 => "Delete",
            77 => "End",
            78 => "Page Down",
            79 => "Right",
            80 => "Left",
            81 => "Down",
            82 => "Up",
            83 => "Num Lock",
            84 => "Keypad /",
            85 => "Keypad *",
            86 => "Keypad -",
            87 => "Keypad +",
            88 => "Keypad Enter",
            99 => "Keypad .",
            100 => "Non-US \\",
            101 => "Menu",
            103 => "Keypad =",
            224 => "Left Ctrl",
            225 => "Left Shift",
            226 => "Left Alt",
            227 => "Left Win",
            228 => "Right Ctrl",
            229 => "Right Shift",
            230 => "Right Alt",
            231 => "Right Win",
            _ => "Code " + binding,
        };
    }

    /// <summary>Converts a physical key to its SDL scancode.</summary>
    /// <param name="key">Captured physical key.</param>
    /// <returns>The scancode, or 0 when the key has no binding.</returns>
    public static int ToScancode(PhysicalKey key)
    {
        if (key is >= PhysicalKey.A and <= PhysicalKey.Z)
        {
            return 4 + ((int)key - (int)PhysicalKey.A);
        }

        if (key is >= PhysicalKey.Digit1 and <= PhysicalKey.Digit9)
        {
            return 30 + ((int)key - (int)PhysicalKey.Digit1);
        }

        if (key is >= PhysicalKey.F1 and <= PhysicalKey.F12)
        {
            return 58 + ((int)key - (int)PhysicalKey.F1);
        }

        if (key is >= PhysicalKey.NumPad1 and <= PhysicalKey.NumPad9)
        {
            return 89 + ((int)key - (int)PhysicalKey.NumPad1);
        }

        return key switch
        {
            PhysicalKey.Digit0 => 39,
            PhysicalKey.Enter => 40,
            PhysicalKey.Escape => 41,
            PhysicalKey.Backspace => 42,
            PhysicalKey.Tab => 43,
            PhysicalKey.Space => 44,
            PhysicalKey.Minus => 45,
            PhysicalKey.Equal => 46,
            PhysicalKey.BracketLeft => 47,
            PhysicalKey.BracketRight => 48,
            PhysicalKey.Backslash => 49,
            PhysicalKey.Semicolon => 51,
            PhysicalKey.Quote => 52,
            PhysicalKey.Backquote => 53,
            PhysicalKey.Comma => 54,
            PhysicalKey.Period => 55,
            PhysicalKey.Slash => 56,
            PhysicalKey.CapsLock => 57,
            PhysicalKey.PrintScreen => 70,
            PhysicalKey.ScrollLock => 71,
            PhysicalKey.Pause => 72,
            PhysicalKey.Insert => 73,
            PhysicalKey.Home => 74,
            PhysicalKey.PageUp => 75,
            PhysicalKey.Delete => 76,
            PhysicalKey.End => 77,
            PhysicalKey.PageDown => 78,
            PhysicalKey.ArrowRight => 79,
            PhysicalKey.ArrowLeft => 80,
            PhysicalKey.ArrowDown => 81,
            PhysicalKey.ArrowUp => 82,
            PhysicalKey.NumLock => 83,
            PhysicalKey.NumPadDivide => 84,
            PhysicalKey.NumPadMultiply => 85,
            PhysicalKey.NumPadSubtract => 86,
            PhysicalKey.NumPadAdd => 87,
            PhysicalKey.NumPadEnter => 88,
            PhysicalKey.NumPad0 => 98,
            PhysicalKey.NumPadDecimal => 99,
            PhysicalKey.IntlBackslash => 100,
            PhysicalKey.ContextMenu => 101,
            PhysicalKey.NumPadEqual => 103,
            PhysicalKey.ControlLeft => 224,
            PhysicalKey.ShiftLeft => 225,
            PhysicalKey.AltLeft => 226,
            PhysicalKey.MetaLeft => 227,
            PhysicalKey.ControlRight => 228,
            PhysicalKey.ShiftRight => 229,
            PhysicalKey.AltRight => 230,
            PhysicalKey.MetaRight => 231,
            _ => 0,
        };
    }
}
