using XGrid.Bus.Usb;
using Xunit;

namespace XGrid.Bus.Tests;

public sealed class PadInputTests
{
    [Fact]
    public void NullOptions_ParseAsNeutral()
    {
        Assert.True(PadInput.TryParse(null, null, null, out PadInput pad));

        Assert.Equal(0, pad.Buttons);
        Assert.Equal(new byte[8], pad.Analog);
        Assert.Equal(new short[4], pad.Sticks);
    }

    [Fact]
    public void ValidOptions_ParseAllParts()
    {
        Assert.True(PadInput.TryParse("10", "0000FF0000000000", "100,-200,0,0", out PadInput pad));

        Assert.Equal(0x10, pad.Buttons);
        Assert.Equal(0xFF, pad.Analog[2]);
        Assert.Equal(100, pad.Sticks[0]);
        Assert.Equal(-200, pad.Sticks[1]);
    }

    [Fact]
    public void MalformedOptions_Fail()
    {
        Assert.False(PadInput.TryParse("ZZ", null, null, out _));
        Assert.False(PadInput.TryParse(null, "00FF", null, out _));
        Assert.False(PadInput.TryParse(null, "00ZZ000000000000", null, out _));
        Assert.False(PadInput.TryParse(null, null, "1,2,3", out _));
        Assert.False(PadInput.TryParse(null, null, "1,2,3,99999", out _));
    }

    [Fact]
    public void ButtonSet_NullOrEmpty_IsNeutral()
    {
        Assert.True(PadInput.TryParseButtonSet(null, out ushort buttons, out byte[] analog));
        Assert.Equal(0, buttons);
        Assert.Equal(new byte[8], analog);

        Assert.True(PadInput.TryParseButtonSet("  ", out buttons, out analog));
        Assert.Equal(0, buttons);
        Assert.Equal(new byte[8], analog);
    }

    [Fact]
    public void ButtonSet_HexMask_IsBackCompatible()
    {
        Assert.True(PadInput.TryParseButtonSet("0002", out ushort buttons, out byte[] analog));
        Assert.Equal(0x0002, buttons);
        Assert.Equal(new byte[8], analog);

        Assert.True(PadInput.TryParseButtonSet("10", out buttons, out _));
        Assert.Equal(0x0010, buttons);
    }

    [Fact]
    public void ButtonSet_DigitalNames_MapToXidBits()
    {
        Assert.True(PadInput.TryParseButtonSet("down", out ushort buttons, out _));
        Assert.Equal(0x0002, buttons);

        Assert.True(PadInput.TryParseButtonSet("UP", out buttons, out _));
        Assert.Equal(0x0001, buttons);

        Assert.True(PadInput.TryParseButtonSet("dpad_left", out buttons, out _));
        Assert.Equal(0x0004, buttons);

        Assert.True(PadInput.TryParseButtonSet("start+back", out buttons, out _));
        Assert.Equal(0x0030, buttons);

        Assert.True(PadInput.TryParseButtonSet("lthumb|rthumb", out buttons, out _));
        Assert.Equal(0x00C0, buttons);
    }

    [Fact]
    public void ButtonSet_AnalogNames_PressFully()
    {
        Assert.True(PadInput.TryParseButtonSet("a", out ushort buttons, out byte[] analog));
        Assert.Equal(0, buttons);
        Assert.Equal(new byte[] { 0xFF, 0, 0, 0, 0, 0, 0, 0 }, analog);

        Assert.True(PadInput.TryParseButtonSet("a+b+x+y+white+black+lt+rt", out _, out analog));
        Assert.Equal(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF }, analog);
    }

    [Fact]
    public void ButtonSet_MixedCombo_SetsBoth()
    {
        Assert.True(PadInput.TryParseButtonSet("a+down", out ushort buttons, out byte[] analog));
        Assert.Equal(0x0002, buttons);
        Assert.Equal(0xFF, analog[0]);
        Assert.Equal(0, analog[1]);
    }

    [Fact]
    public void ButtonSet_ReleaseWords_AreNeutral()
    {
        foreach (string word in new string[] { "none", "neutral", "release", "0" })
        {
            Assert.True(PadInput.TryParseButtonSet(word, out ushort buttons, out byte[] analog));
            Assert.Equal(0, buttons);
            Assert.Equal(new byte[8], analog);
        }
    }

    [Fact]
    public void ButtonSet_UnknownName_Fails()
    {
        Assert.False(PadInput.TryParseButtonSet("nope", out _, out _));
        Assert.False(PadInput.TryParseButtonSet("a+nope", out _, out _));
    }

    [Fact]
    public void TryParse_AcceptsButtonNames()
    {
        Assert.True(PadInput.TryParse("down", null, null, out PadInput pad));
        Assert.Equal(0x0002, pad.Buttons);
        Assert.Equal(new byte[8], pad.Analog);

        Assert.True(PadInput.TryParse("a", null, null, out pad));
        Assert.Equal(0xFF, pad.Analog[0]);
    }
}
