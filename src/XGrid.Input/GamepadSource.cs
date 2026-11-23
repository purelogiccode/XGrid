using System;
using System.Collections.Generic;

namespace XGrid.Input;

/// <summary>A physical XInput control that can drive a <see cref="PadAction"/>.</summary>
public enum GamepadSource
{
    /// <summary>A button.</summary>
    A,

    /// <summary>B button.</summary>
    B,

    /// <summary>X button.</summary>
    X,

    /// <summary>Y button.</summary>
    Y,

    /// <summary>Left shoulder (LB).</summary>
    LeftShoulder,

    /// <summary>Right shoulder (RB).</summary>
    RightShoulder,

    /// <summary>Back.</summary>
    Back,

    /// <summary>Start.</summary>
    Start,

    /// <summary>Left thumb click.</summary>
    LeftThumb,

    /// <summary>Right thumb click.</summary>
    RightThumb,

    /// <summary>D-pad up.</summary>
    DpadUp,

    /// <summary>D-pad down.</summary>
    DpadDown,

    /// <summary>D-pad left.</summary>
    DpadLeft,

    /// <summary>D-pad right.</summary>
    DpadRight,

    /// <summary>Left trigger axis.</summary>
    LeftTrigger,

    /// <summary>Right trigger axis.</summary>
    RightTrigger,
}

/// <summary>Static metadata for <see cref="GamepadSource"/>.</summary>
public static class GamepadSources
{
    /// <summary>Every source, in the order the settings UI lists them.</summary>
    public static IReadOnlyList<GamepadSource> All { get; } = Enum.GetValues<GamepadSource>();

    /// <summary>Returns a short display label for the source.</summary>
    /// <param name="source">Gamepad source.</param>
    public static string DisplayName(GamepadSource source)
    {
        return source switch
        {
            GamepadSource.LeftShoulder => "Left shoulder (LB)",
            GamepadSource.RightShoulder => "Right shoulder (RB)",
            GamepadSource.LeftThumb => "Left thumb click",
            GamepadSource.RightThumb => "Right thumb click",
            GamepadSource.DpadUp => "D-pad up",
            GamepadSource.DpadDown => "D-pad down",
            GamepadSource.DpadLeft => "D-pad left",
            GamepadSource.DpadRight => "D-pad right",
            GamepadSource.LeftTrigger => "Left trigger",
            GamepadSource.RightTrigger => "Right trigger",
            _ => source.ToString(),
        };
    }

    /// <summary>Parses a source name written by <see cref="Enum.ToString()"/>.</summary>
    /// <param name="name">Source name.</param>
    /// <param name="source">Parsed source.</param>
    public static bool TryParse(string? name, out GamepadSource source)
    {
        source = default;
        return !string.IsNullOrWhiteSpace(name)
            && Enum.TryParse(name, ignoreCase: true, out source)
            && Enum.IsDefined(source);
    }
}
