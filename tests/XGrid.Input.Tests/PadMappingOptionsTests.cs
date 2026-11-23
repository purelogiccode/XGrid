using System.Collections.Generic;
using Xunit;

namespace XGrid.Input.Tests;

/// <summary>
/// Pins the user-configurable part of the gamepad translation: action
/// rebinding, dead zones, trigger threshold, Y inversion and stick swapping.
/// </summary>
public sealed class PadMappingOptionsTests
{
    [Fact]
    public void DefaultBindings_KeepTheIdentityLayoutAndShouldersAsBlackWhite()
    {
        Assert.Equal(GamepadSource.A, GamepadMappingOptions.DefaultBindings[PadAction.A]);
        Assert.Equal(GamepadSource.RightShoulder, GamepadMappingOptions.DefaultBindings[PadAction.White]);
        Assert.Equal(GamepadSource.LeftShoulder, GamepadMappingOptions.DefaultBindings[PadAction.Black]);
        Assert.Equal(GamepadSource.LeftTrigger, GamepadMappingOptions.DefaultBindings[PadAction.LeftTrigger]);
    }

    [Fact]
    public void CustomBinding_CanDriveAFaceButtonFromATrigger()
    {
        var options = new GamepadMappingOptions
        {
            Bindings = new Dictionary<PadAction, GamepadSource>
            {
                [PadAction.A] = GamepadSource.RightTrigger,
            },
        };

        HostPad pad = XInputPadMapping.Map(0, 0, 200, 0, 0, 0, 0, options);

        Assert.Equal(200, pad.Analog[0]);
    }

    [Fact]
    public void CustomBinding_CanDriveADigitalButtonFromAFaceButton()
    {
        var options = new GamepadMappingOptions
        {
            Bindings = new Dictionary<PadAction, GamepadSource>
            {
                [PadAction.DpadUp] = GamepadSource.A,
            },
        };

        HostPad pad = XInputPadMapping.Map(XInputPadMapping.ButtonA, 0, 0, 0, 0, 0, 0, options);

        Assert.Equal(XInputPadMapping.DpadUp, pad.Buttons);
    }

    [Theory]
    [InlineData(100, 0)]
    [InlineData(127, 127)]
    [InlineData(128, 128)]
    [InlineData(255, 255)]
    public void TriggerThresholdPercent_IsApplied(byte raw, byte expected)
    {
        var options = new GamepadMappingOptions { TriggerThresholdPercent = 50 };

        HostPad pad = XInputPadMapping.Map(0, raw, raw, 0, 0, 0, 0, options);

        Assert.Equal(expected, pad.Analog[6]);
        Assert.Equal(expected, pad.Analog[7]);
    }

    [Fact]
    public void InvertLeftY_FlipsOnlyTheLeftStick()
    {
        var options = new GamepadMappingOptions { InvertLeftY = true };

        HostPad pad = XInputPadMapping.Map(0, 0, 0, (short)10000, (short)20000, (short)30000, (short)32000, options);

        Assert.Equal(10000, pad.Sticks[0]);
        Assert.Equal(-20000, pad.Sticks[1]);
        Assert.Equal(30000, pad.Sticks[2]);
        Assert.Equal(32000, pad.Sticks[3]);
    }

    [Fact]
    public void SwapSticks_SwapsBothAxes()
    {
        var options = new GamepadMappingOptions { SwapSticks = true };

        HostPad pad = XInputPadMapping.Map(0, 0, 0, (short)10000, (short)20000, (short)30000, (short)32000, options);

        Assert.Equal(30000, pad.Sticks[0]);
        Assert.Equal(32000, pad.Sticks[1]);
        Assert.Equal(10000, pad.Sticks[2]);
        Assert.Equal(20000, pad.Sticks[3]);
    }

    [Fact]
    public void ZeroDeadZone_PassesSmallAxes()
    {
        var options = new GamepadMappingOptions { LeftDeadZonePercent = 0, RightDeadZonePercent = 0 };

        HostPad pad = XInputPadMapping.Map(0, 0, 0, (short)1, (short)1, (short)1, (short)1, options);

        Assert.Equal(1, pad.Sticks[0]);
        Assert.Equal(1, pad.Sticks[1]);
        Assert.Equal(1, pad.Sticks[2]);
        Assert.Equal(1, pad.Sticks[3]);
    }

    [Fact]
    public void PadStateBuilder_MapsDigitalAndAnalogActions()
    {
        var values = new Dictionary<PadAction, byte>
        {
            [PadAction.A] = 200,
            [PadAction.B] = 100,
            [PadAction.DpadLeft] = 255,
            [PadAction.Start] = 0,
        };

        HostPad pad = PadStateBuilder.Build(values, [(short)100, (short)-100, (short)0, (short)0]);

        Assert.Equal(200, pad.Analog[0]);
        Assert.Equal(100, pad.Analog[1]);
        Assert.Equal(XInputPadMapping.DpadLeft, pad.Buttons);
        Assert.Equal(0, pad.Buttons & XInputPadMapping.Start);
        Assert.Equal(100, pad.Sticks[0]);
        Assert.Equal(-100, pad.Sticks[1]);
    }
}

