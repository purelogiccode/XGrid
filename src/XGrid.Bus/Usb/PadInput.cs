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

    /// <summary>Parses CLI pad options; null or empty means neutral.</summary>
    public static bool TryParse(string? buttonsHex, string? analogHex, string? sticksCsv, out PadInput state)
    {
        state = default;
        ushort buttons = 0;
        if (!string.IsNullOrEmpty(buttonsHex))
        {
            if (!ushort.TryParse(buttonsHex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out buttons))
            {
                return false;
            }
        }

        byte[] analog = new byte[8];
        if (!string.IsNullOrEmpty(analogHex))
        {
            if (analogHex.Length != 16)
            {
                return false;
            }

            for (int i = 0; i < 8; i++)
            {
                if (!byte.TryParse(
                    analogHex.AsSpan(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out analog[i]))
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
