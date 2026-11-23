using System;
using System.Collections.Generic;

namespace XGrid.Input;

/// <summary>
/// Builds the emulated pad report from logical action values, so keyboard and
/// gamepad bindings share one translation into the XID report shape.
/// </summary>
public static class PadStateBuilder
{
    /// <summary>Builds a report from action values and optional stick axes.</summary>
    /// <param name="values">Action values: 0-255 (128 or more is pressed for a digital action).</param>
    /// <param name="sticks">Four stick axes (LX, LY, RX, RY); empty means centered.</param>
    public static HostPad Build(IReadOnlyDictionary<PadAction, byte> values, ReadOnlySpan<short> sticks = default)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (sticks.Length is not (0 or HostPad.StickCount))
        {
            throw new ArgumentException($"Expected {HostPad.StickCount} stick axes.", nameof(sticks));
        }

        var analog = new byte[HostPad.AnalogCount];
        ushort buttons = 0;
        foreach (KeyValuePair<PadAction, byte> pair in values)
        {
            Apply(pair.Key, pair.Value, ref buttons, analog);
        }

        var axes = new short[HostPad.StickCount];
        if (!sticks.IsEmpty)
        {
            sticks.CopyTo(axes);
        }

        return new HostPad(buttons, analog, axes);
    }

    /// <summary>Applies one action value to a report being assembled.</summary>
    /// <param name="action">Pad action.</param>
    /// <param name="value">0-255; a digital action is pressed at 128 or more.</param>
    /// <param name="buttons">Digital button mask to update.</param>
    /// <param name="analog">Eight analog bytes to update.</param>
    public static void Apply(PadAction action, byte value, ref ushort buttons, byte[] analog)
    {
        ArgumentNullException.ThrowIfNull(analog);
        if (analog.Length != HostPad.AnalogCount)
        {
            throw new ArgumentException($"Expected {HostPad.AnalogCount} analog bytes.", nameof(analog));
        }

        if (PadActions.IsDigital(action))
        {
            if (value >= 128)
            {
                buttons |= PadActions.DigitalMask(action);
            }

            return;
        }

        int index = PadActions.AnalogIndex(action);
        if (index >= 0)
        {
            analog[index] = value;
        }
    }
}
