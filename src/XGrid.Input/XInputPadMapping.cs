namespace XGrid.Input;

/// <summary>
/// Translates an XInput controller state into the emulated Xbox pad report.
/// </summary>
/// <remarks>
/// Pure and platform-free so the translation is unit-testable without a
/// controller: the P/Invoke reader (<see cref="XInputGamepadReader"/>) only
/// produces the raw fields.
///
/// The mapping is close to the identity because XInput is the descendants of
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
    public const ushort DigitalMask = 0x00FF;

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

    /// <summary>XInput's recommended left-stick dead zone.</summary>
    public const short LeftDeadZone = 7849;

    /// <summary>XInput's recommended right-stick dead zone.</summary>
    public const short RightDeadZone = 8689;

    /// <summary>XInput's recommended trigger press threshold.</summary>
    public const byte TriggerThreshold = 30;

    /// <summary>Translates raw XInput fields into a pad report.</summary>
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
        var analog = new byte[HostPad.AnalogCount];
        analog[0] = Pressed(xinputButtons, ButtonA);
        analog[1] = Pressed(xinputButtons, ButtonB);
        analog[2] = Pressed(xinputButtons, ButtonX);
        analog[3] = Pressed(xinputButtons, ButtonY);
        analog[4] = Pressed(xinputButtons, LeftShoulder);
        analog[5] = Pressed(xinputButtons, RightShoulder);
        analog[6] = leftTrigger >= TriggerThreshold ? leftTrigger : (byte)0;
        analog[7] = rightTrigger >= TriggerThreshold ? rightTrigger : (byte)0;

        var sticks = new short[HostPad.StickCount];
        sticks[0] = ApplyDeadZone(leftX, leftY, LeftDeadZone);
        sticks[1] = ApplyDeadZone(leftY, leftX, LeftDeadZone);
        sticks[2] = ApplyDeadZone(rightX, rightY, RightDeadZone);
        sticks[3] = ApplyDeadZone(rightY, rightX, RightDeadZone);

        return new HostPad((ushort)(xinputButtons & DigitalMask), analog, sticks);
    }

    private static byte Pressed(ushort buttons, ushort flag)
    {
        return (buttons & flag) != 0 ? (byte)0xFF : (byte)0;
    }

    /// <summary>
    /// Zeroes one axis when the stick is inside its radial dead zone.
    /// </summary>
    /// <remarks>
    /// The test is radial, matching the pad's behaviour: a stick held at 70% of
    /// the dead zone radius on both axes is inside it even though each axis
    /// alone is below the threshold.
    /// </remarks>
    private static short ApplyDeadZone(short axis, short otherAxis, short deadZone)
    {
        long magnitudeSquared = ((long)axis * axis) + ((long)otherAxis * otherAxis);
        return magnitudeSquared < (long)deadZone * deadZone ? (short)0 : axis;
    }
}
