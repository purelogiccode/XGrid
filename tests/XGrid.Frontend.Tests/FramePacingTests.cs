using XGrid.Frontend.Avalonia.Services;
using Xunit;

namespace XGrid.Frontend.Tests;

/// <summary>
/// Pins the picture pacing. The core publishes frames far faster than a screen
/// refreshes, and the shell used to present them on the 100 ms UI tick, so a
/// 60 fps title showed ten pictures a second.
/// </summary>
public sealed class FramePacingTests
{
    private const long Frequency = 10_000_000; // 100 ns ticks.

    [Fact]
    public void ShouldPresent_PresentsTheFirstFrame()
    {
        Assert.True(FramePacing.ShouldPresent(Frequency, 0, Frequency));
    }

    [Fact]
    public void ShouldPresent_HoldsUntilThePresentIntervalElapsed()
    {
        const long last = 5_000;

        Assert.False(FramePacing.ShouldPresent(last + Milliseconds(9), last, Frequency));
        Assert.True(FramePacing.ShouldPresent(last + Milliseconds(10), last, Frequency));
        Assert.True(FramePacing.ShouldPresent(last + Milliseconds(25), last, Frequency));
    }

    [Fact]
    public void ShouldPresent_DoesNotStallOnAClockGoingBackwards()
    {
        Assert.True(FramePacing.ShouldPresent(1_000, 5_000, Frequency));
    }

    [Fact]
    public void Constants_KeepThePictureAtScreenRates()
    {
        // The regression this pins: a 100 ms presentation tick is ten pictures
        // a second however fast the guest renders.
        Assert.True(FramePacing.PollIntervalMilliseconds <= 16);
        Assert.InRange(FramePacing.PresentIntervalMilliseconds, 10, 33);
    }

    private static long Milliseconds(int milliseconds)
    {
        return Frequency * milliseconds / 1000;
    }
}
