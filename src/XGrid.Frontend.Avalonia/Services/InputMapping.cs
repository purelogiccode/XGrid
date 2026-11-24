using System.Collections.Generic;
using Avalonia.Input;
using XGrid.Frontend.Avalonia.Models;
using XGrid.Input;

namespace XGrid.Frontend.Avalonia.Services;

/// <summary>Projects the persisted input settings onto the pure input mapping.</summary>
public static class InputMapping
{
    /// <summary>Builds the gamepad options for a launch/poll.</summary>
    /// <param name="settings">Input settings.</param>
    public static GamepadMappingOptions Create(InputSettings settings)
    {
        var bindings = new Dictionary<PadAction, GamepadSource>();
        foreach (PadAction action in PadActions.All)
        {
            if (settings.Gamepad.TryGetValue(action.ToString(), out string? source)
                && GamepadSources.TryParse(source, out GamepadSource parsed))
            {
                bindings[action] = parsed;
            }
            else
            {
                bindings[action] = GamepadMappingOptions.DefaultBindings[action];
            }
        }

        return new GamepadMappingOptions
        {
            LeftDeadZonePercent = settings.LeftDeadZonePercent,
            RightDeadZonePercent = settings.RightDeadZonePercent,
            TriggerThresholdPercent = settings.TriggerThresholdPercent,
            InvertLeftY = settings.InvertLeftY,
            InvertRightY = settings.InvertRightY,
            SwapSticks = settings.SwapSticks,
            Bindings = bindings,
        };
    }

    /// <summary>Maps a keyboard key to the first action bound to it.</summary>
    /// <param name="settings">Input settings.</param>
    /// <param name="key">Pressed key.</param>
    /// <param name="action">Mapped action.</param>
    public static bool TryMapKey(InputSettings settings, Key key, out PadAction action)
    {
        if (KeyboardKeys.IsReserved(key))
        {
            action = default;
            return false;
        }

        foreach (PadAction candidate in PadActions.All)
        {
            if (!settings.Keyboard.TryGetValue(candidate.ToString(), out string? keys))
            {
                continue;
            }

            foreach (Key bound in KeyboardKeys.ParseList(keys))
            {
                if (bound == key)
                {
                    action = candidate;
                    return true;
                }
            }
        }

        action = default;
        return false;
    }

    /// <summary>Overlays the pressed keyboard keys on a gamepad report.</summary>
    /// <param name="gamepad">Gamepad report.</param>
    /// <param name="settings">Input settings.</param>
    /// <param name="pressed">Pressed keys.</param>
    public static HostPad MergeKeyboard(HostPad gamepad, InputSettings settings, IEnumerable<Key> pressed)
    {
        ushort buttons = gamepad.Buttons;
        var analog = (byte[])gamepad.Analog.Clone();
        foreach (Key key in pressed)
        {
            if (TryMapKey(settings, key, out PadAction action))
            {
                PadStateBuilder.Apply(action, 255, ref buttons, analog);
            }
        }

        return new HostPad(buttons, analog, (short[])gamepad.Sticks.Clone());
    }
}
