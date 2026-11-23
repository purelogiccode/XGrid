using System;
using System.Collections.Generic;

namespace XGrid.Input;

/// <summary>
/// Translates an XInput controller state into the emulated Xbox pad report.
/// </summary>
/// <remarks>
/// Pure and platform-free so the translation is unit-testable without a
/// controller: the P/Invoke reader (<see cref="XInputGamepadReader"/>) only
/// produces the raw fields.
///
/// The mapping is close to the identity because XInput is the descendant of
/// the same pad: the low byte of the XInput button mask uses the same bit
/// assignments as the Xbox digital mask (d-pad, START, BACK, thumb clicks).
/// The four face buttons and the two shoulder buttons move to the analog
/// report, which is where the real pad carries their pressure levels.
/// </remarks>
public static class XInputPadMapping
{
    // Digital bits shared with the Xbox report's low byte.
    /// <summary>D-pad up digital bit.</summary>
    public const ushort DpadUp = 0x0001;

    /// <summary>D-pad down digital bit.</summary>
    public const ushort DpadDown = 0x0002;

    /// <summary>D-pad left digital bit.</summary>
    public const ushort DpadLeft = 0x0004;

    /// <summary>D-pad right digital bit.</summary>
    public const ushort DpadRight = 0x0008;

    /// <summary>START digital bit.</summary>
    public const ushort Start = 0x0010;

    /// <summary>BACK digital bit.</summary>
    public const ushort Back = 0x0020;

    /// <summary>Left thumb-click digital bit.</summary>
    public const ushort LeftThumb = 0x0040;

    /// <summary>Right thumb-click digital bit.</summary>
    public const ushort RightThumb = 0x0080;

    /// <summary>Mask covering every digital bit the Xbox report carries.</summary>
    private const ushort DigitalMask = 0x00FF;

    // XInput-only flags that map onto an analog byte.
    /// <summary>Left shoulder (black) flag.</summary>
    public const ushort LeftShoulder = 0x0100;

    /// <summary>Right shoulder (white) flag.</summary>
    public const ushort RightShoulder = 0x0200;

    /// <summary>A button flag.</summary>
    public const ushort ButtonA = 0x1000;

    /// <summary>B button flag.</summary>
    public const ushort ButtonB = 0x2000;

    /// <summary>X button flag.</summary>
    public const ushort ButtonX = 0x4000;

    /// <summary>Y button flag.</summary>
    public const ushort ButtonY = 0x8000;

    /// <summary>Translates raw XInput fields into a pad report with default options.</summary>
    /// <param name="xinputButtons">The XInput <c>wButtons</c> mask.</param>
    /// <param name="leftTrigger">Left trigger, 0-255.</param>
    /// <param name="rightTrigger">Right trigger, 0-255.</param>
    /// <param name="leftX">Left stick X, -32768 to 32767.</param>
    /// <param name="leftY">Left stick Y, -32768 to 32767, positive up.</param>
    /// <param name="rightX">Right stick X, -32768 to 32767.</param>
    /// <param name="rightY">Right stick Y, -32768 to 32767, positive up.</param>
    /// <returns>The equivalent Xbox pad report.</returns>
    public static HostPad Map(
        ushort xinputButtons,
        byte leftTrigger,
        byte rightTrigger,
        short leftX,
        short leftY,
        short rightX,
        short rightY)
    {
        return Map(xinputButtons, leftTrigger, rightTrigger, leftX, leftY, rightX, rightY, null);
    }

    /// <summary>Translates raw XInput fields into a pad report with user bindings.</summary>
    /// <param name="xinputButtons">The XInput <c>wButtons</c> mask.</param>
    /// <param name="leftTrigger">Left trigger, 0-255.</param>
    /// <param name="rightTrigger">Right trigger, 0-255.</param>
    /// <param name="leftX">Left stick X, -32768 to 32767.</param>
    /// <param name="leftY">Left stick Y, -32768 to 32767, positive up.</param>
    /// <param name="rightX">Right stick X, -32768 to 32767.</param>
    /// <param name="rightY">Right stick Y, -32768 to 32767, positive up.</param>
    /// <param name="options">Bindings and axis options, or null for defaults.</param>
    /// <returns>The equivalent Xbox pad report.</returns>
    public static HostPad Map(
        ushort xinputButtons,
        byte leftTrigger,
        byte rightTrigger,
        short leftX,
        short leftY,
        short rightX,
        short rightY,
        GamepadMappingOptions? options)
    {
        options ??= GamepadMappingOptions.Default;
        byte triggerThreshold = (byte)Math.Clamp(
            (options.TriggerThresholdPercent * 255) / 100,
            0,
            255);

        var values = new Dictionary<PadAction, byte>(PadActions.All.Count);
        foreach (PadAction action in PadActions.All)
        {
            GamepadSource source = options.Bindings.TryGetValue(action, out GamepadSource bound)
                ? bound
                : GamepadMappingOptions.DefaultBindings[action];
            values[action] = ReadSource(source, xinputButtons, leftTrigger, rightTrigger, triggerThreshold);
        }

        short lx = leftX;
        short ly = leftY;
        short rx = rightX;
        short ry = rightY;
        if (options.InvertLeftY)
        {
            ly = Negate(ly);
        }

        if (options.InvertRightY)
        {
            ry = Negate(ry);
        }

        if (options.SwapSticks)
        {
            (lx, ly, rx, ry) = (rx, ry, lx, ly);
        }

        int leftDeadZone = DeadZoneRadius(options.LeftDeadZonePercent);
        int rightDeadZone = DeadZoneRadius(options.RightDeadZonePercent);
        short[] sticks =
        [
            ApplyDeadZone(lx, ly, leftDeadZone),
            ApplyDeadZone(ly, lx, leftDeadZone),
            ApplyDeadZone(rx, ry, rightDeadZone),
            ApplyDeadZone(ry, rx, rightDeadZone),
        ];

        return PadStateBuilder.Build(values, sticks);
    }

    private static byte ReadSource(
        GamepadSource source,
        ushort buttons,
        byte leftTrigger,
        byte rightTrigger,
        byte triggerThreshold)
    {
        return source switch
        {
            GamepadSource.LeftTrigger => leftTrigger >= triggerThreshold ? leftTrigger : (byte)0,
            GamepadSource.RightTrigger => rightTrigger >= triggerThreshold ? rightTrigger : (byte)0,
            _ => (buttons & Flag(source)) != 0 ? (byte)0xFF : (byte)0,
        };
    }

    private static ushort Flag(GamepadSource source)
    {
        return source switch
        {
            GamepadSource.A => ButtonA,
            GamepadSource.B => ButtonB,
            GamepadSource.X => ButtonX,
            GamepadSource.Y => ButtonY,
            GamepadSource.LeftShoulder => LeftShoulder,
            GamepadSource.RightShoulder => RightShoulder,
            GamepadSource.Back => Back,
            GamepadSource.Start => Start,
            GamepadSource.LeftThumb => LeftThumb,
            GamepadSource.RightThumb => RightThumb,
            GamepadSource.DpadUp => DpadUp,
            GamepadSource.DpadDown => DpadDown,
            GamepadSource.DpadLeft => DpadLeft,
            GamepadSource.DpadRight => DpadRight,
            _ => 0,
        };
    }

    private static int DeadZoneRadius(int percent)
    {
        return (int)Math.Round(short.MaxValue * Math.Clamp(percent, 0, 100) / 100.0);
    }

    private static short Negate(short value)
    {
        return value == short.MinValue ? short.MaxValue : (short)-value;
    }

    /// <summary>
    /// Zeroes one axis when the stick is inside its radial dead zone.
    /// </summary>
    /// <remarks>
    /// The test is radial, matching the pad's behaviour: a stick held at 70% of
    /// the dead zone radius on both axes is inside it even though each axis
    /// alone is below the threshold.
    /// </remarks>
    private static short ApplyDeadZone(short axis, short otherAxis, int deadZone)
    {
        long magnitudeSquared = ((long)axis * axis) + ((long)otherAxis * otherAxis);
        return magnitudeSquared < (long)deadZone * deadZone ? (short)0 : axis;
    }
}
