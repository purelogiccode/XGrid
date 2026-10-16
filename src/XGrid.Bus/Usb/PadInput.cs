using System;
using System.Globalization;
using System.Runtime.InteropServices;

namespace XGrid.Bus.Usb;

/// <summary>Parsed <c>--pad</c> CLI input state for driving an <see cref="XidGamepad"/>.</summary>
[StructLayout(LayoutKind.Auto)]
public readonly struct PadInput
{
    /// <summary>Creates a pad state from its parts.</summary>
    public PadInput(ushort buttons, byte[] analog, short[] sticks)
    {
        Buttons = buttons;
        Analog = analog;
        Sticks = sticks;
    }

    /// <summary>Gets the digital button bitmask.</summary>
    public ushort Buttons { get; }

    /// <summary>Gets the 8 analog button bytes (X Y A B white black triggers).</summary>
    public byte[] Analog { get; }

    /// <summary>Gets the 4 stick axes (LX LY RX RY).</summary>
    public short[] Sticks { get; }

    /// <summary>Gets the neutral (no input) pad state.</summary>
    public static PadInput Neutral { get; } = new PadInput(0, new byte[8], new short[4]);

    /// <summary>
    /// Parses a button set: known button names joined with '+' (case-insensitive,
    /// underscores/dashes/spaces ignored) or a hex digital mask.
    /// </summary>
    /// <remarks>
    /// Button facts follow xemu <c>hw/xbox/xid.h</c>: analog indices A0 B1 X2 Y3
    /// BLACK4 WHITE5 LT6 RT7; digital bits UP 0x0001 DOWN 0x0002 LEFT 0x0004
    /// RIGHT 0x0008 START 0x0010 BACK 0x0020 LTHUMB 0x0040 RTHUMB 0x0080.
    /// Known names take precedence over hex, so <c>a</c> means analog-A rather
    /// than mask 0x000A; analog names press fully (0xFF). The words
    /// <c>none</c>/<c>neutral</c>/<c>release</c> contribute nothing, for
    /// explicit release events.
    /// </remarks>
    public static bool TryParseButtonSet(string? text, out ushort buttons, out byte[] analog)
    {
        buttons = 0;
        analog = new byte[8];
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        string[] parts = text.Split(['+', '|'], StringSplitOptions.RemoveEmptyEntries);
        foreach (string part in parts)
        {
            string key = NormalizeButtonName(part);
            switch (key)
            {
                case "up":
                case "dpadup":
                    buttons |= 0x0001;
                    break;
                case "down":
                case "dpaddown":
                    buttons |= 0x0002;
                    break;
                case "left":
                case "dpadleft":
                    buttons |= 0x0004;
                    break;
                case "right":
                case "dpadright":
                    buttons |= 0x0008;
                    break;
                case "start":
                    buttons |= 0x0010;
                    break;
                case "back":
                case "select":
                    buttons |= 0x0020;
                    break;
                case "lthumb":
                case "leftthumb":
                case "l3":
                    buttons |= 0x0040;
                    break;
                case "rthumb":
                case "rightthumb":
                case "r3":
                    buttons |= 0x0080;
                    break;
                case "a":
                    analog[0] = 0xFF;
                    break;
                case "b":
                    analog[1] = 0xFF;
                    break;
                case "x":
                    analog[2] = 0xFF;
                    break;
                case "y":
                    analog[3] = 0xFF;
                    break;
                case "black":
                    analog[4] = 0xFF;
                    break;
                case "white":
                    analog[5] = 0xFF;
                    break;
                case "lt":
                case "lefttrigger":
                case "ltrigger":
                    analog[6] = 0xFF;
                    break;
                case "rt":
                case "righttrigger":
                case "rtrigger":
                    analog[7] = 0xFF;
                    break;
                case "none":
                case "neutral":
                case "release":
                    break;
                default:
                    if (!ushort.TryParse(part.Trim(), NumberStyles.HexNumber, CultureInfo.InvariantCulture,
                            out ushort mask))
                    {
                        return false;
                    }

                    buttons |= mask;
                    break;
            }
        }

        return true;
    }

    private static string NormalizeButtonName(string text)
    {
        return text.Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();
    }

    /// <summary>Parses CLI pad options; null or empty means neutral.</summary>
    /// <remarks>
    /// The buttons part accepts the <see cref="TryParseButtonSet"/> name form
    /// (e.g. <c>down</c>, <c>a+down</c>) as well as a hex digital mask; named
    /// analog levels seed the base which an explicit analog hex string then
    /// replaces.
    /// </remarks>
    public static bool TryParse(string? buttonsHex, string? analogHex, string? sticksCsv, out PadInput state)
    {
        state = default;
        if (!TryParseButtonSet(buttonsHex, out ushort buttons, out byte[] analog))
        {
            return false;
        }

        if (!string.IsNullOrEmpty(analogHex))
        {
            if (analogHex.Length != 16)
            {
                return false;
            }

            for (int i = 0; i < 8; i++)
            {
                if (!byte.TryParse(
                        analogHex.AsSpan(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture,
                        out analog[i]))
                {
                    return false;
                }
            }
        }

        short[] sticks = new short[4];
        if (!string.IsNullOrEmpty(sticksCsv))
        {
            string[] parts = sticksCsv.Split(',');
            if (parts.Length != 4)
            {
                return false;
            }

            for (int i = 0; i < 4; i++)
            {
                if (!short.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out sticks[i]))
                {
                    return false;
                }
            }
        }

        state = new PadInput(buttons, analog, sticks);
        return true;
    }
}
