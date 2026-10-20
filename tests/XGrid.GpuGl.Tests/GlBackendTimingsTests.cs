using System;
using System.Diagnostics;
using System.Text;
using Xunit;

namespace XGrid.GpuGl.Tests;

/// <summary>GlBackendTimings accumulation/format/reset goldens (pure C#).</summary>
public sealed class GlBackendTimingsTests
{
    [Fact]
    public void NewTimings_AreZero()
    {
        var timings = new GlBackendTimings();
        Assert.Equal(0, timings.DrawMs);
        Assert.Equal(0, timings.FlushMs);
        Assert.Equal(0, timings.ClearMs);
        Assert.Equal(0, timings.ReadbackMs);
        Assert.Equal(0, timings.Readbacks);
    }

    [Fact]
    public void AddDrawTicks_AccumulatesMilliseconds()
    {
        var timings = new GlBackendTimings();
        long ticks = Stopwatch.Frequency / 1000; // one millisecond
        timings.AddDrawTicks(ticks);
        timings.AddDrawTicks(ticks * 2);
        Assert.Equal(3.0, timings.DrawMs, 6);
        Assert.Equal(0, timings.ClearMs);
        Assert.Equal(0, timings.Readbacks);
    }

    [Fact]
    public void AddDraw_AccumulatesMilliseconds()
    {
        var timings = new GlBackendTimings();
        timings.AddDraw(TimeSpan.FromMilliseconds(2.5));
        timings.AddDraw(TimeSpan.FromMilliseconds(1.5));
        Assert.Equal(4.0, timings.DrawMs);
        Assert.Equal(0, timings.ClearMs);
        Assert.Equal(0, timings.Readbacks);
    }

    [Fact]
    public void AddReadback_AccumulatesAndCounts()
    {
        var timings = new GlBackendTimings();
        timings.AddReadback(TimeSpan.FromMilliseconds(10));
        timings.AddReadback(TimeSpan.FromMilliseconds(20));
        Assert.Equal(30.0, timings.ReadbackMs);
        Assert.Equal(2, timings.Readbacks);
    }

    [Fact]
    public void Reset_ZeroesAllAccumulators()
    {
        var timings = new GlBackendTimings();
        timings.AddDraw(TimeSpan.FromMilliseconds(3));
        timings.AddClear(TimeSpan.FromMilliseconds(4));
        timings.AddReadback(TimeSpan.FromMilliseconds(5));
        timings.Reset();
        Assert.Equal(0, timings.DrawMs);
        Assert.Equal(0, timings.ClearMs);
        Assert.Equal(0, timings.ReadbackMs);
        Assert.Equal(0, timings.Readbacks);
    }

    [Fact]
    public void AppendTo_EmitsTimedFragment()
    {
        var timings = new GlBackendTimings();
        timings.AddDraw(TimeSpan.FromMilliseconds(1.26));
        timings.AddClear(TimeSpan.FromMilliseconds(2.5));
        timings.AddReadback(TimeSpan.FromMilliseconds(7));
        var builder = new StringBuilder();
        timings.AppendTo(builder);
        Assert.Equal(
            " timed[drawMs=1.3 flushMs=0.0 clearMs=2.5 readbackMs=7.0 readbacks=1]",
            builder.ToString());
    }

    [Fact]
    public void AddBatchFlushTicks_CountsAsDrawSubmissionAndFlushSubset()
    {
        // A batched draw reaches GL in the flush, so it must stay inside the
        // draw-submission total (drawMs stays comparable across batched and
        // unbatched runs) while flushMs isolates the instanced-draw cost.
        var timings = new GlBackendTimings();
        long ticks = Stopwatch.Frequency / 1000; // one millisecond
        timings.AddDrawTicks(ticks * 3);
        timings.AddBatchFlushTicks(ticks * 2);
        Assert.Equal(5.0, timings.DrawMs, 6);
        Assert.Equal(2.0, timings.FlushMs, 6);
        timings.Reset();
        Assert.Equal(0, timings.DrawMs);
        Assert.Equal(0, timings.FlushMs);
    }

    [Fact]
    public void AppendTo_NullBuilder_Throws()
    {
        var timings = new GlBackendTimings();
        Assert.Throws<ArgumentNullException>(() => timings.AppendTo(null!));
    }
}
