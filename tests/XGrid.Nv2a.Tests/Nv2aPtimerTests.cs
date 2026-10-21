using Xunit;

namespace XGrid.Nv2a.Tests;

/// <summary>
/// Pins the 0.8.137 PTIMER model: a 61-bit register time with a writable
/// offset, a 27-bit-masked alarm compare that re-arms on every low-word wrap,
/// the alarm's own INTR_0 bit and the PMC aggregate mirror.
/// </summary>
/// <remarks>
/// Reference: xemu 0.8.137 <c>hw/xbox/nv2a/ptimer.c</c> (register layout,
/// <c>get_next_alarm_time</c>, the read-latch in <c>ptimer_read</c>, the
/// TIME_0/TIME_1 offset writes) and <c>nv2a.c</c> <c>nv2a_update_irq</c>
/// (<c>NV_PMC_INTR_0_PTIMER</c> = bit 20 gated on pending &amp; enabled).
/// </remarks>
public sealed class Nv2aPtimerTests
{
    private const uint Base = Nv2AMmio.BaseAddress;
    private const uint Ptimer = 0x009000u;

    private const uint Intr0 = Ptimer + 0x100u;
    private const uint IntrEn0 = Ptimer + 0x140u;
    private const uint Numerator = Ptimer + 0x200u;
    private const uint Denominator = Ptimer + 0x210u;
    private const uint Time0 = Ptimer + 0x400u;
    private const uint Time1 = Ptimer + 0x410u;
    private const uint Alarm0 = Ptimer + 0x420u;

    private const uint PmcIntr = 0x000100u;
    private const uint PmcIntrEn = 0x000140u;

    private const uint PtimerPmcBit = 1u << 20;

    private static Nv2AMmio Create() => new();

    private static ulong RegTime(Nv2AMmio nv2A)
    {
        ulong high = nv2A.Read(Base + Time1, 4);
        ulong low = nv2A.Read(Base + Time0, 4);
        return (high << 32) | low;
    }

    [Fact]
    public void ResetUsesTheReferenceNumeratorAndDenominator()
    {
        Nv2AMmio nv2A = Create();

        Assert.Equal(1u, nv2A.Read(Base + Numerator, 4));
        Assert.Equal(1u, nv2A.Read(Base + Denominator, 4));
        Assert.Equal(0u, nv2A.Read(Base + IntrEn0, 4));

        // The alarm time resets to zero, so its first compare against a zero
        // clock is already due: reading INTR_0 latches the pending bit (the
        // reference's ptimer_read does exactly this) and re-arms one wrap out.
        Assert.Equal(1u, nv2A.Read(Base + Intr0, 4));
        nv2A.Write(Base + Intr0, 4, 1u);
        Assert.Equal(0u, nv2A.Read(Base + Intr0, 4));
    }

    [Fact]
    public void RegisterTimeIsTheCoreClockShiftedByFive()
    {
        Nv2AMmio nv2A = Create();

        nv2A.Advance(0x1234);

        // reg = core ticks << 5, split into the 29-bit high and 32-bit low words.
        ulong expected = 0x1234ul << 5;
        Assert.Equal(expected, RegTime(nv2A));
    }

    [Fact]
    public void DenominatorScalesTheRegisterClock()
    {
        Nv2AMmio nv2A = Create();
        nv2A.Write(Base + Denominator, 4, 2u);

        nv2A.Advance(100);

        // absolute = ticks * denominator / numerator = 200.
        Assert.Equal(200ul << 5, RegTime(nv2A));
    }

    [Fact]
    public void Time0WriteRepositionsTheLowWord()
    {
        Nv2AMmio nv2A = Create();

        nv2A.Write(Base + Time0, 4, 0x1000u);
        Assert.Equal(0x1000u, nv2A.Read(Base + Time0, 4));

        // One internal tick (32 register units) later the low word advances.
        nv2A.Advance(1);
        Assert.Equal(0x1020u, nv2A.Read(Base + Time0, 4));
    }

    [Fact]
    public void Time1WriteSetsTheHighWord()
    {
        Nv2AMmio nv2A = Create();

        nv2A.Write(Base + Time1, 4, 5u);
        nv2A.Write(Base + Time0, 4, 0x20u);

        Assert.Equal(5u, nv2A.Read(Base + Time1, 4));
        Assert.Equal(0x20u, nv2A.Read(Base + Time0, 4));
    }

    [Fact]
    public void Time1IsMaskedToTwentyNineBits()
    {
        Nv2AMmio nv2A = Create();

        nv2A.Write(Base + Time1, 4, 0xFFFFFFFFu);

        Assert.Equal(0x1FFFFFFFu, nv2A.Read(Base + Time1, 4));
    }

    [Fact]
    public void AlarmRaisesIntr0AndThePmcAggregateWhenReached()
    {
        Nv2AMmio nv2A = Create();
        nv2A.Write(Base + Time0, 4, 0u);
        nv2A.Write(Base + Alarm0, 4, 0x400u);
        nv2A.Write(Base + IntrEn0, 4, 1u);

        // 31 internal ticks = 0x3E0 < 0x400: still armed.
        nv2A.Advance(31);
        Assert.Equal(0u, nv2A.Read(Base + Intr0, 4));

        // Crossing the programmed time latches the pending bit.
        nv2A.Advance(1);
        Assert.Equal(1u, nv2A.Read(Base + Intr0, 4));

        // ... and the PMC aggregate reports PTIMER only while enabled.
        nv2A.Write(Base + PmcIntrEn, 4, 1u);
        Assert.Equal(PtimerPmcBit, nv2A.Read(Base + PmcIntr, 4));

        // Acknowledging INTR_0 clears both levels.
        nv2A.Write(Base + Intr0, 4, 1u);
        Assert.Equal(0u, nv2A.Read(Base + Intr0, 4));
        Assert.Equal(0u, nv2A.Read(Base + PmcIntr, 4));
    }

    [Fact]
    public void AlarmStaysSilentWhileTheInterruptIsDisabled()
    {
        Nv2AMmio nv2A = Create();
        nv2A.Write(Base + Time0, 4, 0u);
        nv2A.Write(Base + Alarm0, 4, 0x100u);

        nv2A.Advance(16);
        nv2A.Write(Base + PmcIntrEn, 4, 1u);

        // Disabled: no raise at the crossing, so no PMC level either.
        Assert.Equal(0u, nv2A.Read(Base + IntrEn0, 4));
        Assert.Equal(0u, nv2A.Read(Base + PmcIntr, 4));

        // Enabling with the time already reached raises it immediately
        // (schedule_qemu_timer's already-due path).
        nv2A.Write(Base + IntrEn0, 4, 1u);
        Assert.Equal(1u, nv2A.Read(Base + Intr0, 4));
        Assert.Equal(PtimerPmcBit, nv2A.Read(Base + PmcIntr, 4));
    }

    [Fact]
    public void IntrReadLatchesAnAlreadyReachedAlarmEvenWhenDisabled()
    {
        // ptimer_read latches unconditionally; only the PMC aggregate is gated.
        Nv2AMmio nv2A = Create();
        nv2A.Write(Base + Time0, 4, 0u);
        nv2A.Write(Base + IntrEn0, 4, 1u);
        nv2A.Write(Base + Alarm0, 4, 0x100u);
        nv2A.Write(Base + IntrEn0, 4, 0u);
        nv2A.Advance(8);

        nv2A.Write(Base + PmcIntrEn, 4, 1u);
        Assert.Equal(0u, nv2A.Read(Base + PmcIntr, 4));

        Assert.Equal(1u, nv2A.Read(Base + Intr0, 4));

        // Pending but disabled: the aggregate still does not report it.
        Assert.Equal(0u, nv2A.Read(Base + PmcIntr, 4));
        nv2A.Write(Base + IntrEn0, 4, 1u);
        Assert.Equal(PtimerPmcBit, nv2A.Read(Base + PmcIntr, 4));
    }

    [Fact]
    public void AlarmRearmsOnTheNextLowWordWrap()
    {
        Nv2AMmio nv2A = Create();
        nv2A.Write(Base + Time1, 4, 0u);
        nv2A.Write(Base + Time0, 4, 0x40u);

        // 0x20 <= the current low word, so the alarm targets the next 2^32 step.
        nv2A.Write(Base + Alarm0, 4, 0x20u);
        Assert.Equal(0x20u, nv2A.Read(Base + Alarm0, 4));

        nv2A.Write(Base + IntrEn0, 4, 1u);

        // The clock sits at 0x40, so 2^27 - 2 internal ticks is still inside
        // this low word (0x100000000) and one more tick hits the alarm.
        nv2A.Advance((1ul << 27) - 2);
        Assert.Equal(0u, nv2A.Read(Base + Intr0, 4));

        nv2A.Advance(1);
        Assert.Equal(1u, nv2A.Read(Base + Intr0, 4));
    }

    [Fact]
    public void AlarmReArmsAfterEachFiring()
    {
        Nv2AMmio nv2A = Create();
        nv2A.Write(Base + Time0, 4, 0u);
        nv2A.Write(Base + IntrEn0, 4, 1u);
        nv2A.Write(Base + Alarm0, 4, 0x100u);

        nv2A.Advance(8);
        Assert.Equal(1u, nv2A.Read(Base + Intr0, 4));
        nv2A.Write(Base + Intr0, 4, 1u);

        // The re-armed alarm is one low-word wrap away, so it stays silent.
        nv2A.Advance(8);
        Assert.Equal(0u, nv2A.Read(Base + Intr0, 4));
    }

    [Fact]
    public void AlarmWriteAfterReachingTheSameTimeRollsToTheNextWrap()
    {
        Nv2AMmio nv2A = Create();
        nv2A.Write(Base + Time0, 4, 0x100u);

        // The requested low word equals the current one: the reference re-arms
        // one 2^32 step later instead of firing immediately.
        nv2A.Write(Base + Alarm0, 4, 0x100u);
        nv2A.Write(Base + IntrEn0, 4, 1u);
        nv2A.Advance(8);

        Assert.Equal(0u, nv2A.Read(Base + Intr0, 4));
    }

    [Fact]
    public void NumeratorScalesTheRegisterClockDown()
    {
        Nv2AMmio nv2A = Create();
        nv2A.Write(Base + Numerator, 4, 4u);

        nv2A.Advance(64);

        // absolute = ticks * 1 / 4 = 16 internal ticks -> 16 << 5.
        Assert.Equal(16ul << 5, RegTime(nv2A));
    }

    [Fact]
    public void PtimerStateRoundTripsThroughASnapshot()
    {
        Nv2AMmio nv2A = Create();
        nv2A.Write(Base + Time0, 4, 0x2000u);
        nv2A.Write(Base + IntrEn0, 4, 1u);
        nv2A.Write(Base + Alarm0, 4, 0x8000u);
        nv2A.Advance(64);
        nv2A.Write(Base + Numerator, 4, 3u);
        nv2A.Write(Base + Denominator, 4, 5u);

        using var buffer = new System.IO.MemoryStream();
        using (var writer = new System.IO.BinaryWriter(buffer, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            nv2A.SaveState(writer);
        }

        buffer.Position = 0;
        var restored = new Nv2AMmio();
        using (var reader = new System.IO.BinaryReader(buffer, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            restored.LoadState(reader);
        }

        Assert.Equal(nv2A.Read(Base + IntrEn0, 4), restored.Read(Base + IntrEn0, 4));
        Assert.Equal(nv2A.Read(Base + Time0, 4), restored.Read(Base + Time0, 4));
        Assert.Equal(nv2A.Read(Base + Time1, 4), restored.Read(Base + Time1, 4));
        Assert.Equal(nv2A.Read(Base + Alarm0, 4), restored.Read(Base + Alarm0, 4));
        Assert.Equal(nv2A.Read(Base + Numerator, 4), restored.Read(Base + Numerator, 4));
        Assert.Equal(nv2A.Read(Base + Denominator, 4), restored.Read(Base + Denominator, 4));

        // The restored alarm still fires at the same register time.
        nv2A.Advance(0x4000);
        restored.Advance(0x4000);
        Assert.Equal(nv2A.Read(Base + Intr0, 4), restored.Read(Base + Intr0, 4));
    }
}
