using System;

namespace XGrid.Input;

/// <summary>
/// Host-controller state expressed in the emulated Xbox pad's report format:
/// the digital button mask plus the eight analog bytes and four stick axes.
/// </summary>
/// <remarks>
/// Field order and semantics follow the XID input report that
/// <c>XGrid.Bus.Usb.XidGamepad</c> produces: analog indices A0 B1 X2 Y3
/// BLACK4 WHITE5 LT6 RT7; digital bits UP 0x0001 DOWN 0x0002 LEFT 0x0004
/// RIGHT 0x0008 START 0x0010 BACK 0x0020 LTHUMB 0x0040 RTHUMB 0x0080; sticks
/// are signed 16-bit LX, LY, RX, RY with positive Y up.
/// </remarks>
public readonly struct HostPad
{
    /// <summary>Creates a pad state from its report fields.</summary>
    public HostPad(ushort buttons, byte[] analog, short[] sticks)
    {
        ArgumentNullException.ThrowIfNull(analog);
        ArgumentNullException.ThrowIfNull(sticks);
        if (analog.Length != AnalogCount)
        {
            throw new ArgumentException($"Expected {AnalogCount} analog bytes.", nameof(analog));
        }

        if (sticks.Length != StickCount)
        {
            throw new ArgumentException($"Expected {StickCount} stick axes.", nameof(sticks));
        }

        Buttons = buttons;
        Analog = analog;
        Sticks = sticks;
    }

    /// <summary>Number of analog button bytes in a pad report.</summary>
    public const int AnalogCount = 8;

    /// <summary>Number of stick axes in a pad report.</summary>
    public const int StickCount = 4;

    /// <summary>Gets the digital button bitmask.</summary>
    public ushort Buttons { get; }

    /// <summary>Gets the eight analog button bytes (X Y A B white black triggers).</summary>
    public byte[] Analog { get; }

    /// <summary>Gets the four stick axes (LX LY RX RY).</summary>
    public short[] Sticks { get; }

    /// <summary>Gets the neutral (no input) pad state.</summary>
    public static HostPad Neutral { get; } = new(0, new byte[AnalogCount], new short[StickCount]);

    /// <summary>Gets whether every button, trigger and axis is at rest.</summary>
    public bool IsNeutral
    {
        get
        {
            if (Buttons != 0)
            {
                return false;
            }

            foreach (byte b in Analog)
            {
                if (b != 0)
                {
                    return false;
                }
            }

            foreach (short s in Sticks)
            {
                if (s != 0)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
