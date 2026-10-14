using System.Collections.Generic;
using XGrid.Memory;
using Xunit;

namespace XGrid.Cpu.Tests;

/// <summary>
/// Idle-skip V2 tests (§119): the device read-effect/deadline model. A
/// classified MMIO poll may stay in a skippable body and the skip lands
/// exactly at (never past) its value transition; stable registers span whole
/// skips; unknown registers and model loss keep the conservative V1 rejection;
/// MMIO writes and IRQ boundaries remain hard stops; the measurement
/// attribution counters classify rejected/armable steps per loop head.
/// </summary>
public sealed class IdleSkipMmioTests
{
    private const uint RamPollAddress = 0x2000;
    private const uint MmioAddress = 0x3000;
    private const uint LoopHead = 0x1000;

    // mov eax,[0x2000]; or eax,[0x3000]; test eax,eax; jz 0x1000; hlt
    // (RAM poll plus a classified device-register read in one body)
    private static readonly byte[] DualPollLoop =
    {
        0xA1, 0x00, 0x20, 0x00, 0x00, 0x0B, 0x05, 0x00, 0x30, 0x00, 0x00, 0x85, 0xC0, 0x74, 0xF1, 0xF4,
    };

    // mov [0x3000],eax; mov eax,[0x2000]; test eax,eax; jz 0x1000; hlt
    // (device-register WRITE in body: immediate hard stop even when the model
    // would classify the register as stable)
    private static readonly byte[] MmioWriteLoop =
    {
        0xA3, 0x00, 0x30, 0x00, 0x00, 0xA1, 0x00, 0x20, 0x00, 0x00, 0x85, 0xC0, 0x74, 0xF2, 0xF4,
    };

    // in al,0x40; test al,0x80; jnz 0x1000; hlt  (PIO poll: attribution target)
    private static readonly byte[] PioPollLoop = { 0xE4, 0x40, 0xA8, 0x80, 0x75, 0xFA, 0xF4 };

    // dec ecx; jnz 0x1000; hlt  (register countdown: static-rule attribution)
    private static readonly byte[] CountdownLoop = { 0x49, 0x75, 0xFD, 0xF4 };

    [Fact]
    public void MmioPoll_ScheduledChange_SkipLandsExactlyOnDeadline()
    {
        (XboxCpu cpu, FakeMmioHost host) = CreateIdleCpu(MmioPollLoopBytes());
        host.ScheduleRegisterChange(MmioAddress, 1000, value: 1);

        cpu.Execute(1_000_000);

        // Zero overshoot: the skip lands on the change step, the interpreter
        // re-reads the new value and exits the poll.
        Assert.Equal(1000UL, host.Advanced);
        Assert.Equal(1000UL, cpu.IdleSkipSkippedSteps);
        Assert.Equal(1UL, cpu.IdleSkipStopReadDeadline);
        Assert.Equal(0UL, cpu.IdleSkipStopModelLost);
        Assert.True(cpu.Context.Halted);
        Assert.Equal(0x100Au, cpu.Context.Eip); // past the hlt at 0x1009
    }

    [Fact]
    public void MmioPoll_StableRegister_SpansChunksUntilRamPollExits()
    {
        (XboxCpu cpu, FakeMmioHost host) = CreateIdleCpu(DualPollLoop);
        host.Stable.Add(MmioAddress);
        host.FireRamPollAtAdvanced = 9000;
        int chunk = cpu.IdleSkipChunkSteps;

        cpu.Execute(1_000_000);

        // The stable device read never clamps a chunk; the skip advances in
        // whole chunks until the RAM poll flips and the watch stop fires.
        Assert.Equal(0UL, cpu.IdleSkipStopReadDeadline);
        Assert.Equal(1UL, cpu.IdleSkipStopWatch);
        Assert.Equal((ulong)(3 * chunk), host.Advanced);
        Assert.True(host.Advanced > 9000 && host.Advanced <= 9000 + (ulong)chunk);
        Assert.True(cpu.Context.Halted);
    }

    [Fact]
    public void MmioPoll_UnknownRegister_IsRejectedConservatively()
    {
        (XboxCpu cpu, FakeMmioHost host) = CreateIdleCpu(MmioPollLoopBytes());

        cpu.Execute(50_000);

        // No model entry for 0x3000: the V1 rejection applies unchanged.
        Assert.Equal(0UL, host.Advanced);
        Assert.Equal(0UL, cpu.IdleSkipTriggers);
        Assert.True(cpu.IdleSkipAccessAborts >= 1);
        Assert.True(cpu.IdleSkipRejectedStepsMmioRead > 0);
    }

    [Fact]
    public void MmioPoll_ModelLostMidSkip_StopsConservatively()
    {
        (XboxCpu cpu, FakeMmioHost host) = CreateIdleCpu(MmioPollLoopBytes());
        host.ScheduleRegisterChange(MmioAddress, 100_000, value: 1);
        host.BreakModelAfterAdvance = 5000;

        cpu.Execute(50_000);

        // The body armed while the register was classified; once the model
        // stops classifying it mid-skip, the skipper stops on the next clamp
        // and never advances again.
        Assert.Equal(8192UL, host.Advanced);
        Assert.Equal(8192UL, cpu.IdleSkipSkippedSteps);
        Assert.Equal(1UL, cpu.IdleSkipStopModelLost);
        Assert.Equal(0UL, cpu.IdleSkipStopReadDeadline);
    }

    [Fact]
    public void MmioWriteInBody_IsHardStopEvenWhenRegisterIsClassified()
    {
        (XboxCpu cpu, FakeMmioHost host) = CreateIdleCpu(MmioWriteLoop);
        host.Stable.Add(MmioAddress);
        host.FireRamPollAtAdvanced = 100_000;

        cpu.Execute(50_000);

        // Any memory write (RAM or device) remains a V1 hard stop.
        Assert.Equal(0UL, host.Advanced);
        Assert.Equal(0UL, cpu.IdleSkipTriggers);
        Assert.True(cpu.IdleSkipAccessAborts >= 1);
        Assert.True(cpu.IdleSkipRejectedStepsMemWrite > 0);
    }

    [Fact]
    public void MmioBody_IrqBoundaryIsPreserved()
    {
        (XboxCpu cpu, FakeMmioHost host) = CreateIdleCpu(MmioPollLoopBytes());
        host.ScheduleRegisterChange(MmioAddress, 1000, value: 1);
        host.ScheduleIrqAt(500);

        cpu.Execute(100_000);

        // The chunk is clamped by the register deadline (1000), crossing the
        // IRQ event; on the next stop-condition check the IRQ (checked first)
        // wins over the read deadline, and no further time is advanced.
        Assert.Equal(1000UL, host.Advanced);
        Assert.True(cpu.IdleSkipStopIrq >= 1);
        Assert.Equal(0UL, cpu.IdleSkipStopReadDeadline);

        // No interrupt source is wired in this harness, so the interpreter
        // observes the (already-changed) register and exits the poll.
        Assert.True(cpu.Context.Halted);
    }

    [Fact]
    public void Attribution_MmioReject_RecordsHeadStepsAndAddress()
    {
        (XboxCpu cpu, FakeMmioHost host) = CreateIdleCpu(MmioPollLoopBytes());
        cpu.IdleSkipMeasureOnly = true;

        cpu.Execute(50_000);

        Assert.True(cpu.IdleSkipRejectedStepsMmioRead > 0);
        List<IdleSkipHeadStat> heads = cpu.IdleSkipTopRejectedHeads(8);
        Assert.Contains(
            heads,
            h => h.Head == LoopHead
                && h.Reason == IdleSkipRejectReason.MmioRead
                && h.Address == MmioAddress
                && h.Steps > 0);
    }

    [Fact]
    public void Attribution_PioAndCountdownRejections_AreSeparated()
    {
        (XboxCpu pioCpu, FakeMmioHost pioHost) = CreateIdleCpu(PioPollLoop);
        pioCpu.IdleSkipMeasureOnly = true;
        pioCpu.Execute(50_000);
        Assert.True(pioCpu.IdleSkipRejectedStepsPio > 0);
        Assert.Contains(
            pioCpu.IdleSkipTopRejectedHeads(8),
            h => h.Reason == IdleSkipRejectReason.Pio && h.Address == 0x40);

        (XboxCpu countCpu, FakeMmioHost countHost) = CreateIdleCpu(CountdownLoop);
        countCpu.Context.Ecx = 1000;
        countCpu.IdleSkipMeasureOnly = true;
        countCpu.Execute(100_000);
        Assert.True(countCpu.IdleSkipRejectedStepsStaticRule > 0);
        Assert.Contains(
            countCpu.IdleSkipTopRejectedHeads(8),
            h => h.Reason == IdleSkipRejectReason.StaticRule);
        Assert.Equal(0UL, countHost.Advanced);
    }

    [Fact]
    public void Attribution_ArmablePark_StepsAccumulateInProbeMode()
    {
        (XboxCpu cpu, FakeMmioHost host) = CreateIdleCpu(MmioPollLoopBytes());
        cpu.IdleSkipMeasureOnly = true;
        host.ScheduleRegisterChange(MmioAddress, 10_000, value: 1);

        cpu.Execute(50_000);

        // The poll is fully skippable under the model: its head is armable and
        // probe mode attributes the executed steps to the park instead of a
        // rejection bucket.
        Assert.True(cpu.IdleSkipTriggers >= 1);
        Assert.True(cpu.IdleSkipArmableSteps > 0);
        Assert.Equal(0UL, cpu.IdleSkipRejectedStepsMmioRead);
        Assert.Equal(0UL, host.Advanced);
        Assert.False(cpu.Context.Halted);
    }

    [Fact]
    public void FlagOff_DetectorOff_NoAccountingAndNoSkipping()
    {
        (XboxCpu cpu, FakeMmioHost host) = CreateIdleCpu(MmioPollLoopBytes(), enableIdleSkip: false);
        host.ScheduleRegisterChange(MmioAddress, 1000, value: 1);

        int executed = cpu.Execute(50_000);

        Assert.False(cpu.IdleSkipEnabled);
        Assert.Equal(50_000, executed);
        Assert.Equal(0UL, host.Advanced);
        Assert.Equal(50_000UL, cpu.Context.Tsc);
        Assert.Equal(0UL, cpu.IdleSkipTriggers);
        Assert.Equal(0UL, cpu.IdleSkipRejectedStepsMmioRead);
    }

    private static byte[] MmioPollLoopBytes()
    {
        // mov eax,[0x3000]; test eax,eax; jz 0x1000; hlt
        return new byte[] { 0xA1, 0x00, 0x30, 0x00, 0x00, 0x85, 0xC0, 0x74, 0xF7, 0xF4 };
    }

    private static (XboxCpu Cpu, FakeMmioHost Host) CreateIdleCpu(byte[] code, bool enableIdleSkip = true)
    {
        XboxCpu cpu = CpuHarness.Create(code);
        EnablePaging(cpu);
        cpu.Context.Eflags |= FlagBits.If;

        var host = new FakeMmioHost(cpu.Ram());
        cpu.IdleSkipHost = host;
        cpu.IdleSkipReadModel = host;
        if (enableIdleSkip)
        {
            cpu.IdleSkipEnabled = true;
            cpu.IdleSkipMinIterations = 4;
        }

        return (cpu, host);
    }

    /// <summary>Identity-maps the first 4 MB so candidate loops run with paging on.</summary>
    private static void EnablePaging(XboxCpu cpu)
    {
        PhysicalMemory ram = cpu.Ram();

        // Page directory at 0x4000, page table at 0x5000: PDE[0] -> 0x5000.
        ram.Write32(0x4000, 0x5000u | 7u);
        for (uint i = 0; i < 1024; i++)
        {
            ram.Write32(0x5000 + (i * 4), (i << 12) | 7u);
        }

        cpu.Context.Cr3 = 0x4000;
        cpu.Context.Cr0 |= ControlRegisterBits.Cr0Paging;
        cpu.Mmu.Flush();
    }

    /// <summary>
    /// Fake device implementing both host advance and the §119 read-effect
    /// model: scheduled register changes count down in AdvanceIdle, fire their
    /// value at the deadline, then report deadline 0 (change pending
    /// observation) so the skipper stops with zero overshoot.
    /// </summary>
    private sealed class FakeMmioHost : IIdleSkipHost, IIdleSkipReadModel
    {
        private readonly PhysicalMemory _ram;
        private readonly List<(uint Address, long Deadline, byte Value)> _changes = new();
        private readonly List<long> _irqAt = new();

        public FakeMmioHost(PhysicalMemory ram)
        {
            _ram = ram;
        }

        public bool Irq { get; set; }

        public ulong Advanced { get; private set; }

        /// <summary>Breaks the model (stops classifying everything) once this many cycles have advanced.</summary>
        public ulong? BreakModelAfterAdvance { get; set; }

        public HashSet<uint> NonRam { get; } = new() { MmioAddress };

        public HashSet<uint> Stable { get; } = new();

        public ulong? FireRamPollAtAdvanced { get; set; }

        public long? StepsUntilNextDeadline => null;

        public bool IrqPendingAndEnabled => Irq;

        public void ScheduleRegisterChange(uint address, long stepsFromNow, byte value)
        {
            _changes.Add((address, stepsFromNow, value));
        }

        public void ScheduleIrqAt(long stepsFromNow)
        {
            _irqAt.Add(stepsFromNow);
        }

        public bool IsRam(uint physicalAddress)
        {
            return !NonRam.Contains(physicalAddress);
        }

        public bool TryGetReadEffect(uint physicalAddress, out IdleSkipReadEffect effect)
        {
            if (BreakModelAfterAdvance is ulong threshold && Advanced >= threshold)
            {
                effect = default;
                return false;
            }

            for (int i = 0; i < _changes.Count; i++)
            {
                if (_changes[i].Address == physicalAddress)
                {
                    effect = IdleSkipReadEffect.ChangeIn(_changes[i].Deadline);
                    return true;
                }
            }

            if (Stable.Contains(physicalAddress))
            {
                effect = IdleSkipReadEffect.Stable;
                return true;
            }

            effect = default;
            return false;
        }

        public void AdvanceIdle(ulong steps)
        {
            Advanced += steps;
            long delta = (long)steps;

            for (int i = 0; i < _changes.Count; i++)
            {
                (uint address, long deadline, byte value) = _changes[i];
                if (deadline <= 0)
                {
                    continue; // fired: stays at deadline 0 (pending observation)
                }

                long remaining = deadline - delta;
                if (remaining <= 0)
                {
                    _ram.Write8(address, value);
                    _changes[i] = (address, 0, value);
                }
                else
                {
                    _changes[i] = (address, remaining, value);
                }
            }

            for (int i = _irqAt.Count - 1; i >= 0; i--)
            {
                long remaining = _irqAt[i] - delta;
                if (remaining <= 0)
                {
                    Irq = true;
                    _irqAt.RemoveAt(i);
                }
                else
                {
                    _irqAt[i] = remaining;
                }
            }

            if (FireRamPollAtAdvanced is ulong fireAt && Advanced >= fireAt)
            {
                _ram.Write8(RamPollAddress, 1);
                FireRamPollAtAdvanced = null;
            }
        }
    }
}
