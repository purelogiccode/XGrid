using System;
using XGrid.Core.Emulation;
using Xunit;

namespace XGrid.Core.Tests;

public sealed class EmulatedClockTests
{
    [Fact]
    public void Advance_AccumulatesCycles()
    {
        var clock = new EmulatedClock(TimeMode.Deterministic);

        clock.Advance(10);
        clock.Advance(32);

        Assert.Equal(42UL, clock.Cycles);
    }

    [Fact]
    public void Reset_ReturnsToZero()
    {
        var clock = new EmulatedClock(TimeMode.Deterministic);
        clock.Advance(100);

        clock.Reset();

        Assert.Equal(0UL, clock.Cycles);
    }

    [Fact]
    public void Advance_Overflow_Throws()
    {
        var clock = new EmulatedClock(TimeMode.Deterministic);
        clock.Advance(ulong.MaxValue);

        Assert.Throws<OverflowException>(() => clock.Advance(1));
    }

    [Fact]
    public void Mode_IsPreserved()
    {
        var clock = new EmulatedClock(TimeMode.HostTime);

        Assert.Equal(TimeMode.HostTime, clock.Mode);
    }
}
