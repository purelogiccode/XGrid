using System;
using System.Collections.Generic;
using XGrid.Frontend.Avalonia.Services;
using XGrid.Input;

namespace XGrid.Frontend.Avalonia.Models;

/// <summary>
/// Player-1 input configuration: keyboard bindings, gamepad bindings and the
/// stick/trigger options the shell applies before publishing a pad report.
/// </summary>
/// <remarks>
/// Action and source keys are the enum names from <see cref="PadAction"/> and
/// <see cref="GamepadSource"/>. Keyboard values are comma-separated Avalonia
/// key names, so one action can keep an alternate key (the default A keeps both
/// A and Space).
/// </remarks>
public sealed class InputSettings
{
    /// <summary>Gets or sets the keyboard bindings (action name to key list).</summary>
    public Dictionary<string, string> Keyboard { get; set; } = InputDefaults.Keyboard();

    /// <summary>Gets or sets the gamepad bindings (action name to source name).</summary>
    public Dictionary<string, string> Gamepad { get; set; } = InputDefaults.Gamepad();

    /// <summary>Gets or sets the left-stick radial dead zone (0-100%).</summary>
    public int LeftDeadZonePercent { get; set; } = GamepadMappingOptions.DefaultLeftDeadZonePercent;

    /// <summary>Gets or sets the right-stick radial dead zone (0-100%).</summary>
    public int RightDeadZonePercent { get; set; } = GamepadMappingOptions.DefaultRightDeadZonePercent;

    /// <summary>Gets or sets the trigger press threshold (0-100%).</summary>
    public int TriggerThresholdPercent { get; set; } = GamepadMappingOptions.DefaultTriggerThresholdPercent;

    /// <summary>Gets or sets a value indicating whether the left stick Y axis is inverted.</summary>
    public bool InvertLeftY { get; set; }

    /// <summary>Gets or sets a value indicating whether the right stick Y axis is inverted.</summary>
    public bool InvertRightY { get; set; }

    /// <summary>Gets or sets a value indicating whether the sticks are swapped.</summary>
    public bool SwapSticks { get; set; }

    /// <summary>Creates an independent copy of these settings.</summary>
    public InputSettings Clone()
    {
        return new InputSettings
        {
            Keyboard = new Dictionary<string, string>(Keyboard, StringComparer.OrdinalIgnoreCase),
            Gamepad = new Dictionary<string, string>(Gamepad, StringComparer.OrdinalIgnoreCase),
            LeftDeadZonePercent = LeftDeadZonePercent,
            RightDeadZonePercent = RightDeadZonePercent,
            TriggerThresholdPercent = TriggerThresholdPercent,
            InvertLeftY = InvertLeftY,
            InvertRightY = InvertRightY,
            SwapSticks = SwapSticks,
        };
    }

    /// <summary>Repairs values loaded from disk and fills missing actions with defaults.</summary>
    public void Normalize()
    {
        Keyboard ??= InputDefaults.Keyboard();
        Gamepad ??= InputDefaults.Gamepad();
        var keyboard = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (PadAction action in PadActions.All)
        {
            string name = action.ToString();
            keyboard[name] = Keyboard.TryGetValue(name, out string? keys)
                ? KeyboardKeys.NormalizeList(keys)
                : InputDefaults.DefaultKeyboard(action);
        }

        Keyboard = keyboard;

        var gamepad = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (PadAction action in PadActions.All)
        {
            string name = action.ToString();
            string fallback = InputDefaults.DefaultGamepad(action);
            gamepad[name] = Gamepad.TryGetValue(name, out string? source)
                            && GamepadSources.TryParse(source, out _)
                ? source
                : fallback;
        }

        Gamepad = gamepad;
        LeftDeadZonePercent = Math.Clamp(LeftDeadZonePercent, 0, 100);
        RightDeadZonePercent = Math.Clamp(RightDeadZonePercent, 0, 100);
        TriggerThresholdPercent = Math.Clamp(TriggerThresholdPercent, 0, 100);
    }
}

/// <summary>The shipped player-1 input defaults.</summary>
public static class InputDefaults
{
    /// <summary>Returns a fresh default keyboard map.</summary>
    public static Dictionary<string, string> Keyboard()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (PadAction action in PadActions.All)
        {
            map[action.ToString()] = DefaultKeyboard(action);
        }

        return map;
    }

    /// <summary>Returns a fresh default gamepad map.</summary>
    public static Dictionary<string, string> Gamepad()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (PadAction action in PadActions.All)
        {
            map[action.ToString()] = DefaultGamepad(action);
        }

        return map;
    }

    /// <summary>Returns the default keyboard keys for one action.</summary>
    /// <param name="action">Pad action.</param>
    public static string DefaultKeyboard(PadAction action)
    {
        return action switch
        {
            PadAction.A => "A,Space",
            PadAction.B => "S",
            PadAction.X => "D",
            PadAction.Y => "F",
            PadAction.DpadUp => "Up",
            PadAction.DpadDown => "Down",
            PadAction.DpadLeft => "Left",
            PadAction.DpadRight => "Right",
            PadAction.Start => "Enter",
            PadAction.Back => "Back",
            PadAction.White => "Q",
            PadAction.Black => "E",
            PadAction.LeftTrigger => "Z",
            PadAction.RightTrigger => "C",
            _ => string.Empty,
        };
    }

    /// <summary>Returns the default gamepad source for one action.</summary>
    /// <param name="action">Pad action.</param>
    public static string DefaultGamepad(PadAction action)
    {
        return GamepadMappingOptions.DefaultBindings[action].ToString();
    }
}
