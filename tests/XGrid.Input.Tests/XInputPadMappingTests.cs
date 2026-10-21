using System;
using Xunit;

namespace XGrid.Input.Tests;

/// <summary>
/// Pins the XInput-to-Xbox-report translation. The mapping is pure so it runs
/// without a controller: the P/Invoke reader only produces the raw fields.
/// </summary>
public sealed class XInputPadMappingTests
{
    private const ushort None = 0;

    [Fact]
    public void NeutralInput_MapsToNeutralReport()
    {
        HostPad pad = XInputPadMapping.Map(None, 0, 0, 0, 0, 0, 0);

        Assert.True(pad.IsNeutral);
        Assert.Equal(0, pad.Buttons);
        Assert.All(pad.Analog, b => Assert.Equal(0, b));
        Assert.All(pad.Sticks, s => Assert.Equal(0, s));
    }

    [Theory]
    [InlineData(XInputPadMapping.DpadUp)]
    [InlineData(XInputPadMapping.DpadDown)]
    [InlineData(XInputPadMapping.DpadLeft)]
    [InlineData(XInputPadMapping.DpadRight)]
    [InlineData(XInputPadMapping.Start)]
    [InlineData(XInputPadMapping.Back)]
    [InlineData(XInputPadMapping.LeftThumb)]
    [InlineData(XInputPadMapping.RightThumb)]
    public void DigitalBits_PassThroughUnchanged(ushort flag)
    {
        HostPad pad = XInputPadMapping.Map(flag, 0, 0, 0, 0, 0, 0);

        Assert.Equal(flag, pad.Buttons);
        Assert.DoesNotContain(true, Array.ConvertAll(pad.Analog, b => b != 0));
    }

    [Theory]
    [InlineData(XInputPadMapping.ButtonA, 0)]
    [InlineData(XInputPadMapping.ButtonB, 1)]
    [InlineData(XInputPadMapping.ButtonX, 2)]
    [InlineData(XInputPadMapping.ButtonY, 3)]
    [InlineData(XInputPadMapping.LeftShoulder, 4)]
    [InlineData(XInputPadMapping.RightShoulder, 5)]
    public void FaceAndShoulderButtons_MapToAnalogBytes(ushort flag, int index)
    {
        HostPad pad = XInputPadMapping.Map(flag, 0, 0, 0, 0, 0, 0);

        Assert.Equal(0, pad.Buttons);
        for (int i = 0; i < pad.Analog.Length; i++)
        {
            Assert.Equal(i == index ? (byte)0xFF : (byte)0, pad.Analog[i]);
        }
    }

    [Fact]
    public void XInputOnlyFlags_DoNotLeakIntoTheDigitalMask()
    {
        // 0xFF00 is entirely XInput-only: no part of it belongs in the report's
        // digital byte, which the real pad defines only up to 0x0080.
        HostPad pad = XInputPadMapping.Map(0xFF00, 0, 0, 0, 0, 0, 0);

        Assert.Equal(0, pad.Buttons);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(29, 0)]
    [InlineData(30, 30)]
    [InlineData(128, 128)]
    [InlineData(255, 255)]
    public void Triggers_AreThresholdedAtXInputsRecommendedValue(byte raw, byte expected)
    {
        HostPad pad = XInputPadMapping.Map(None, raw, raw, 0, 0, 0, 0);

        Assert.Equal(expected, pad.Analog[6]);
        Assert.Equal(expected, pad.Analog[7]);
    }

    [Fact]
    public void Sticks_InsideDeadZone_AreZeroedOnBothAxes()
    {
        // 5000 per axis is below the left dead zone per axis, and radially
        // 5000^2 * 2 = 50e6 < 7849^2 = 61.6e6, so both axes must be zero.
        HostPad pad = XInputPadMapping.Map(None, 0, 0, 5000, 5000, -5000, -5000);

        Assert.Equal(0, pad.Sticks[0]);
        Assert.Equal(0, pad.Sticks[1]);
        Assert.Equal(0, pad.Sticks[2]);
        Assert.Equal(0, pad.Sticks[3]);
    }

    [Fact]
    public void Sticks_OutsideDeadZone_PassThroughExactly()
    {
        HostPad pad = XInputPadMapping.Map(None, 0, 0, 20000, -30000, 32767, -32768);

        Assert.Equal(20000, pad.Sticks[0]);
        Assert.Equal(-30000, pad.Sticks[1]);
        Assert.Equal(32767, pad.Sticks[2]);
        Assert.Equal(-32768, pad.Sticks[3]);
    }

    [Fact]
    public void DeadZone_IsRadialNotPerAxis()
    {
        // Each axis alone is inside the per-axis dead zone (6000 < 7849), but the
        // radial magnitude is outside it: a diagonal push must survive.
        HostPad pad = XInputPadMapping.Map(None, 0, 0, 6000, 6000, 0, 0);

        Assert.Equal(6000, pad.Sticks[0]);
        Assert.Equal(6000, pad.Sticks[1]);
    }

    [Fact]
    public void RightStick_UsesTheLargerRightDeadZone()
    {
        // 8000 clears the left dead zone (7849) but not the right one (8689).
        HostPad left = XInputPadMapping.Map(None, 0, 0, 8000, 0, 0, 0);
        HostPad right = XInputPadMapping.Map(None, 0, 0, 0, 0, 8000, 0);

        Assert.Equal(8000, left.Sticks[0]);
        Assert.Equal(0, right.Sticks[2]);
    }

    [Fact]
    public void FullState_MapsEveryFieldAtOnce()
    {
        HostPad pad = XInputPadMapping.Map(
            (ushort)(XInputPadMapping.DpadRight | XInputPadMapping.Start | XInputPadMapping.ButtonA),
            0,
            200,
            16000,
            16000,
            0,
            0);

        Assert.Equal(XInputPadMapping.DpadRight | XInputPadMapping.Start, pad.Buttons);
        Assert.Equal(0xFF, pad.Analog[0]);
        Assert.Equal(0, pad.Analog[6]);
        Assert.Equal(200, pad.Analog[7]);
        Assert.Equal(16000, pad.Sticks[0]);
        Assert.Equal(16000, pad.Sticks[1]);
    }

    [Fact]
    public void HostPad_RejectsWrongShapedReports()
    {
        Assert.Throws<ArgumentNullException>(() => new HostPad(0, null!, new short[4]));
        Assert.Throws<ArgumentNullException>(() => new HostPad(0, new byte[8], null!));
        Assert.Throws<ArgumentException>(() => new HostPad(0, new byte[7], new short[4]));
        Assert.Throws<ArgumentException>(() => new HostPad(0, new byte[8], new short[3]));
    }

    [Fact]
    public void HostPad_IsNeutralDetectsEveryField()
    {
        Assert.True(HostPad.Neutral.IsNeutral);
        Assert.False(new HostPad(1, new byte[8], new short[4]).IsNeutral);
        Assert.False(new HostPad(0, [0, 0, 0, 1, 0, 0, 0, 0], new short[4]).IsNeutral);
        Assert.False(new HostPad(0, new byte[8], new short[] { 0, 0, 0, -1 }).IsNeutral);
    }

    [Fact]
    public void Reader_DegradesGracefullyWhenNoControllerIsPresent()
    {
        // The reader must never throw for a missing platform runtime or an
        // absent controller: the UI polls it unconditionally at 60 Hz.
        using var reader = new XInputGamepadReader();

        bool connected = reader.TryRead(out HostPad pad);
        if (!connected)
        {
            Assert.True(pad.IsNeutral);
            Assert.Equal(-1, reader.UserIndex);
        }
        else
        {
            Assert.InRange(reader.UserIndex, 0, 3);
        }
    }
}
