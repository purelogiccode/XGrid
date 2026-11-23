using XGrid.Bus.Usb;
using Xunit;

namespace XGrid.Bus.Tests;

public sealed class PadScriptTests
{
    [Fact]
    public void NullOrEmpty_YieldsZeroEvents()
    {
        Assert.True(PadScript.TryParse(null, out PadScript? script));
        Assert.NotNull(script);
        Assert.Equal(0, script.Count);

        Assert.True(PadScript.TryParse("  ", out script));
        Assert.NotNull(script);
        Assert.Equal(0, script.Count);
    }

    [Fact]
    public void SingleMomentary_ParsesAnalogA()
    {
        Assert.True(PadScript.TryParse("a@100", out PadScript? script));
        Assert.NotNull(script);

        PadScriptEvent only = Assert.Single(script.Events);
        Assert.Equal((ulong)100, only.Step);
        Assert.False(only.Hold);
        Assert.Equal(0, only.Input.Buttons);
        Assert.Equal(0xFF, only.Input.Analog[0]);
        Assert.Equal(new short[4], only.Input.Sticks);
    }

    [Fact]
    public void MultipleEvents_SortByStep()
    {
        Assert.True(PadScript.TryParse("b@200,a@100", out PadScript? script));
        Assert.NotNull(script);
        Assert.Equal(2, script.Count);
        Assert.Equal((ulong)100, script.Events[0].Step);
        Assert.Equal((ulong)200, script.Events[1].Step);
        Assert.Equal(0xFF, script.Events[0].Input.Analog[0]);
        Assert.Equal(0xFF, script.Events[1].Input.Analog[1]);
    }

    [Fact]
    public void SameStep_PreservesSpecOrder()
    {
        Assert.True(PadScript.TryParse("a@100,down@100", out PadScript? script));
        Assert.NotNull(script);
        Assert.Equal(0xFF, script.Events[0].Input.Analog[0]);
        Assert.Equal(0x0002, script.Events[1].Input.Buttons);
    }

    [Fact]
    public void HoldSuffix_Latches()
    {
        Assert.True(PadScript.TryParse("down@100+hold", out PadScript? script));
        Assert.NotNull(script);

        PadScriptEvent only = Assert.Single(script.Events);
        Assert.True(only.Hold);
        Assert.Equal(0x0002, only.Input.Buttons);

        Assert.True(PadScript.TryParse("a@50+HOLD", out script));
        Assert.NotNull(script);
        Assert.True(Assert.Single(script.Events).Hold);
    }

    [Fact]
    public void HexButtonSet_ParsesAsDigitalMask()
    {
        Assert.True(PadScript.TryParse("0002@100", out PadScript? script));
        Assert.NotNull(script);
        Assert.Equal(0x0002, Assert.Single(script.Events).Input.Buttons);
    }

    [Fact]
    public void ReleaseWord_ParsesAsNeutral()
    {
        Assert.True(PadScript.TryParse("none@300", out PadScript? script));
        Assert.NotNull(script);

        PadScriptEvent only = Assert.Single(script.Events);
        Assert.Equal((ulong)300, only.Step);
        Assert.Equal(0, only.Input.Buttons);
        Assert.Equal(new byte[8], only.Input.Analog);
    }

    [Fact]
    public void MalformedSpecs_Fail()
    {
        Assert.False(PadScript.TryParse("a", out _));
        Assert.False(PadScript.TryParse("a@", out _));
        Assert.False(PadScript.TryParse("@100", out _));
        Assert.False(PadScript.TryParse("a@up", out _));
        Assert.False(PadScript.TryParse("a@1@2", out _));
        Assert.False(PadScript.TryParse("a@-1", out _));
        Assert.False(PadScript.TryParse("zzz@1", out _));
        Assert.False(PadScript.TryParse("a@1,,down@2", out _));
        Assert.False(PadScript.TryParse("a@1, ,down@2", out _));
    }

    [Fact]
    public void Apply_MomentaryPressAutoReleases()
    {
        Assert.True(PadScript.TryParse("a@100", out PadScript? script));
        Assert.NotNull(script);

        Assert.False(script.ApplyIfDue(50, out _));
        Assert.True(script.ApplyIfDue(100, out PadInput pressed));
        Assert.Equal(0xFF, pressed.Analog[0]);

        Assert.False(script.ApplyIfDue(500, out _));
        Assert.True(script.ApplyIfDue(100 + PadScript.DefaultPressSteps, out pressed));
        Assert.Equal(0, pressed.Buttons);
        Assert.Equal(new byte[8], pressed.Analog);
        Assert.False(script.ApplyIfDue(100 + PadScript.DefaultPressSteps + 1, out _));
    }

    [Fact]
    public void Apply_HoldLatchesWithoutRelease()
    {
        Assert.True(PadScript.TryParse("down@100+hold", out PadScript? script));
        Assert.NotNull(script);

        Assert.True(script.ApplyIfDue(100, out PadInput pressed));
        Assert.Equal(0x0002, pressed.Buttons);
        Assert.False(script.ApplyIfDue(100 + (5 * PadScript.DefaultPressSteps), out _));
        Assert.False(script.TryGetNextBoundary(100 + (5 * PadScript.DefaultPressSteps), out _));
    }

    [Fact]
    public void Apply_ExplicitReleaseEndsHold()
    {
        Assert.True(PadScript.TryParse("down@100+hold,none@200", out PadScript? script));
        Assert.NotNull(script);

        Assert.True(script.ApplyIfDue(100, out PadInput pressed));
        Assert.Equal(0x0002, pressed.Buttons);
        Assert.True(script.ApplyIfDue(200, out pressed));
        Assert.Equal(0, pressed.Buttons);
        Assert.Equal(new byte[8], pressed.Analog);
    }

    [Fact]
    public void Apply_OverlappingPressCancelsPendingRelease()
    {
        Assert.True(PadScript.TryParse("a@100,b@150", out PadScript? script));
        Assert.NotNull(script);

        Assert.True(script.ApplyIfDue(100, out PadInput pressed));
        Assert.Equal(0xFF, pressed.Analog[0]);
        Assert.True(script.ApplyIfDue(150, out pressed));
        Assert.Equal(0xFF, pressed.Analog[1]);

        // A's auto-release at 100+K is superseded by B's press; nothing due yet.
        Assert.False(script.ApplyIfDue(100 + PadScript.DefaultPressSteps, out _));
        Assert.True(script.ApplyIfDue(150 + PadScript.DefaultPressSteps, out pressed));
        Assert.Equal(new byte[8], pressed.Analog);
    }

    [Fact]
    public void NextBoundary_CoversPressesAndReleases()
    {
        Assert.True(PadScript.TryParse("a@100,down@5000000+hold", out PadScript? script));
        Assert.NotNull(script);

        Assert.True(script.TryGetNextBoundary(0, out ulong boundary));
        Assert.Equal((ulong)100, boundary);

        Assert.True(script.ApplyIfDue(100, out _));
        Assert.True(script.TryGetNextBoundary(100, out boundary));
        Assert.Equal(100 + PadScript.DefaultPressSteps, boundary);

        Assert.True(script.ApplyIfDue(100 + PadScript.DefaultPressSteps, out _));
        Assert.True(script.TryGetNextBoundary(100 + PadScript.DefaultPressSteps, out boundary));
        Assert.Equal((ulong)5000000, boundary);

        Assert.True(script.ApplyIfDue(5000000, out _));
        Assert.False(script.TryGetNextBoundary(5000000, out _));
    }

    [Fact]
    public void Reset_RestartsApplication()
    {
        Assert.True(PadScript.TryParse("a@100", out PadScript? script));
        Assert.NotNull(script);

        Assert.True(script.ApplyIfDue(100, out _));
        script.Reset();
        Assert.True(script.ApplyIfDue(100, out PadInput pressed));
        Assert.Equal(0xFF, pressed.Analog[0]);
    }

    [Fact]
    public void DefaultPressSteps_IsOneMillion()
    {
        Assert.Equal((ulong)1_000_000, PadScript.DefaultPressSteps);
    }
}
