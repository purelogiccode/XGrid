using System;
using System.Collections.Generic;

namespace XGrid.Input;

/// <summary>One logical Xbox pad control a binding can drive.</summary>
public enum PadAction
{
    /// <summary>A face button.</summary>
    A,

    /// <summary>B face button.</summary>
    B,

    /// <summary>X face button.</summary>
    X,

    /// <summary>Y face button.</summary>
    Y,

    /// <summary>D-pad up.</summary>
    DpadUp,

    /// <summary>D-pad down.</summary>
    DpadDown,

    /// <summary>D-pad left.</summary>
    DpadLeft,

    /// <summary>D-pad right.</summary>
    DpadRight,

    /// <summary>Start.</summary>
    Start,

    /// <summary>Back.</summary>
    Back,

    /// <summary>White face button.</summary>
    White,

    /// <summary>Black face button.</summary>
    Black,

    /// <summary>Left thumb click.</summary>
    LeftThumb,

    /// <summary>Right thumb click.</summary>
    RightThumb,

    /// <summary>Left trigger.</summary>
    LeftTrigger,

    /// <summary>Right trigger.</summary>
    RightTrigger,
}

/// <summary>Static metadata and report mapping for <see cref="PadAction"/>.</summary>
public static class PadActions
{
    /// <summary>Every action, in the order the settings UI lists them.</summary>
    public static IReadOnlyList<PadAction> All { get; } = Enum.GetValues<PadAction>();

    /// <summary>Returns true when the action is a digital button (a bit in <c>wButtons</c>).</summary>
    /// <param name="action">Pad action.</param>
    public static bool IsDigital(PadAction action)
    {
        return action is PadAction.DpadUp or PadAction.DpadDown or PadAction.DpadLeft or PadAction.DpadRight
            or PadAction.Start or PadAction.Back or PadAction.LeftThumb or PadAction.RightThumb;
    }

    /// <summary>Returns the analog byte index for an analog action, or -1 for a digital one.</summary>
    /// <param name="action">Pad action.</param>
    public static int AnalogIndex(PadAction action)
    {
        return action switch
        {
            PadAction.A => 0,
            PadAction.B => 1,
            PadAction.X => 2,
            PadAction.Y => 3,
            PadAction.Black => 4,
            PadAction.White => 5,
            PadAction.LeftTrigger => 6,
            PadAction.RightTrigger => 7,
            _ => -1,
        };
    }

    /// <summary>Returns the digital bit for a digital action, or 0 for an analog one.</summary>
    /// <param name="action">Pad action.</param>
    public static ushort DigitalMask(PadAction action)
    {
        return action switch
        {
            PadAction.DpadUp => 0x0001,
            PadAction.DpadDown => 0x0002,
            PadAction.DpadLeft => 0x0004,
            PadAction.DpadRight => 0x0008,
            PadAction.Start => 0x0010,
            PadAction.Back => 0x0020,
            PadAction.LeftThumb => 0x0040,
            PadAction.RightThumb => 0x0080,
            _ => 0,
        };
    }

    /// <summary>Returns a short display label for the action.</summary>
    /// <param name="action">Pad action.</param>
    public static string DisplayName(PadAction action)
    {
        return action switch
        {
            PadAction.DpadUp => "D-pad up",
            PadAction.DpadDown => "D-pad down",
            PadAction.DpadLeft => "D-pad left",
            PadAction.DpadRight => "D-pad right",
            PadAction.LeftThumb => "Left thumb",
            PadAction.RightThumb => "Right thumb",
            PadAction.LeftTrigger => "Left trigger",
            PadAction.RightTrigger => "Right trigger",
            PadAction.White => "White",
            PadAction.Black => "Black",
            _ => action.ToString(),
        };
    }

    /// <summary>Parses an action name written by <see cref="Enum.ToString()"/>.</summary>
    /// <param name="name">Action name.</param>
    /// <param name="action">Parsed action.</param>
    public static bool TryParse(string? name, out PadAction action)
    {
        action = default;
        return !string.IsNullOrWhiteSpace(name)
            && Enum.TryParse(name, ignoreCase: true, out action)
            && Enum.IsDefined(action);
    }
}
