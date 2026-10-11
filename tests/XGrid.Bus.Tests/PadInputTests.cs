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
}
