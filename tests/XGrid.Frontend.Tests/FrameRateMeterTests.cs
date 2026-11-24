using Xunit;
using FrameRateMeter = XGrid.Frontend.Avalonia.Services.FrameRateMeter;

namespace XGrid.Frontend.Tests;

/// <summary>Pins the one-second frame-rate window used by the FPS overlay.</summary>
public sealed class FrameRateMeterTests
{
    [Fact]
    public void TryUpdate_DoesNotReportBeforeTheWindowCloses()
    {
        var meter = new FrameRateMeter();

        Assert.False(meter.TryUpdate(1000, out _, out _));
        meter.AddFrame();
        Assert.False(meter.TryUpdate(1500, out _, out _));
    }

    [Fact]
    public void TryUpdate_ReportsFramesAndFrameTimeForTheWindow()
    {
        var meter = new FrameRateMeter();
        Assert.False(meter.TryUpdate(0, out _, out _));

        for (int index = 0; index < 60; index++)
        {
            meter.AddFrame();
        }

        Assert.True(meter.TryUpdate(FrameRateMeter.WindowMilliseconds, out double fps, out double frameTimeMs));
        Assert.Equal(60, fps, 3);
        Assert.Equal(1000.0 / 60.0, frameTimeMs, 3);
    }

    [Fact]
    public void TryUpdate_StartsANewWindowAfterAReport()
    {
        var meter = new FrameRateMeter();
        meter.TryUpdate(0, out _, out _);
        meter.AddFrame();

        Assert.True(meter.TryUpdate(1000, out _, out _));
        Assert.False(meter.TryUpdate(1500, out _, out _));

        meter.AddFrame();
        Assert.True(meter.TryUpdate(2000, out double fps, out _));
        Assert.Equal(1, fps, 3);
    }

    [Fact]
    public void Reset_ClearsThePendingWindow()
    {
        var meter = new FrameRateMeter();
        meter.TryUpdate(0, out _, out _);
        meter.AddFrame();

        meter.Reset();

        Assert.False(meter.TryUpdate(2000, out _, out _));
        Assert.True(meter.TryUpdate(3000, out double fps, out _));
        Assert.Equal(0, fps, 3);
    }
}
