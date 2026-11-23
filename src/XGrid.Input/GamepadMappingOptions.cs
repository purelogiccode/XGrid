using System.Collections.Generic;

namespace XGrid.Input;

/// <summary>
/// User-tunable gamepad translation: action-to-source bindings, stick dead
/// zones, trigger threshold, Y inversion and stick swapping.
/// </summary>
public sealed class GamepadMappingOptions
{
    /// <summary>Gets the default dead zone for the left stick, as a percentage.</summary>
    public const int DefaultLeftDeadZonePercent = 24;

    /// <summary>Gets the default dead zone for the right stick, as a percentage.</summary>
    public const int DefaultRightDeadZonePercent = 27;

    /// <summary>Gets the default trigger press threshold, as a percentage.</summary>
    public const int DefaultTriggerThresholdPercent = 12;

    /// <summary>Gets or sets the left-stick radial dead zone (0-100%).</summary>
    public int LeftDeadZonePercent { get; init; } = DefaultLeftDeadZonePercent;

    /// <summary>Gets or sets the right-stick radial dead zone (0-100%).</summary>
    public int RightDeadZonePercent { get; init; } = DefaultRightDeadZonePercent;

    /// <summary>Gets or sets the trigger press threshold (0-100%).</summary>
    public int TriggerThresholdPercent { get; init; } = DefaultTriggerThresholdPercent;

    /// <summary>Gets or sets a value indicating whether the left stick Y axis is inverted.</summary>
    public bool InvertLeftY { get; init; }

    /// <summary>Gets or sets a value indicating whether the right stick Y axis is inverted.</summary>
    public bool InvertRightY { get; init; }

    /// <summary>Gets or sets a value indicating whether the left and right sticks are swapped.</summary>
    public bool SwapSticks { get; init; }

    /// <summary>Gets the action-to-source bindings; a missing action uses <see cref="DefaultBindings"/>.</summary>
    public IReadOnlyDictionary<PadAction, GamepadSource> Bindings { get; init; } = DefaultBindings;

    /// <summary>Gets the default bindings: the identity layout with the shoulders as black/white.</summary>
    public static IReadOnlyDictionary<PadAction, GamepadSource> DefaultBindings { get; } =
        new Dictionary<PadAction, GamepadSource>
        {
            [PadAction.A] = GamepadSource.A,
            [PadAction.B] = GamepadSource.B,
            [PadAction.X] = GamepadSource.X,
            [PadAction.Y] = GamepadSource.Y,
            [PadAction.DpadUp] = GamepadSource.DpadUp,
            [PadAction.DpadDown] = GamepadSource.DpadDown,
            [PadAction.DpadLeft] = GamepadSource.DpadLeft,
            [PadAction.DpadRight] = GamepadSource.DpadRight,
            [PadAction.Start] = GamepadSource.Start,
            [PadAction.Back] = GamepadSource.Back,
            [PadAction.White] = GamepadSource.RightShoulder,
            [PadAction.Black] = GamepadSource.LeftShoulder,
            [PadAction.LeftThumb] = GamepadSource.LeftThumb,
            [PadAction.RightThumb] = GamepadSource.RightThumb,
            [PadAction.LeftTrigger] = GamepadSource.LeftTrigger,
            [PadAction.RightTrigger] = GamepadSource.RightTrigger,
        };

    /// <summary>Gets the default options.</summary>
    public static GamepadMappingOptions Default { get; } = new();
}
