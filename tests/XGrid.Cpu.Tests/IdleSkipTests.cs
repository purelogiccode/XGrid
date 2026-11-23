using System.Collections.Generic;
using XGrid.Memory;
using Xunit;

namespace XGrid.Cpu.Tests;

/// <summary>
/// Opt-in idle-skip unit tests: synthetic idle loops against a fake host device
/// with a scheduled event. Covers exact deadline landing, bounded poll-exit
/// overshoot, IRQ hard-stop, MMIO/PIO disable, register-countdown rejection,
/// measure-only probe mode, and flag-off neutrality.
/// </summary>
public sealed class IdleSkipTests
{
    private const uint PollAddress = 0x2000;
    private const uint MmioLookalikeAddress = 0x3000;

    // mov eax,[0x2000]; test eax,eax; jz 0x1000; hlt
    private static readonly byte[] PollLoop = [0xA1, 0x00, 0x20, 0x00, 0x00, 0x85, 0xC0, 0x74, 0xF7, 0xF4];

    // mov eax,[0x3000]; test eax,eax; jz 0x1000; hlt  (0x3000 classified non-RAM by the host)
    private static readonly byte[] MmioPollLoop = [0xA1, 0x00, 0x30, 0x00, 0x00, 0x85, 0xC0, 0x74, 0xF7, 0xF4];

    // in al,0x60; test al,0x80; jnz 0x1000; hlt
    private static readonly byte[] PioPollLoop = [0xE4, 0x60, 0xA8, 0x80, 0x75, 0xFA, 0xF4];

    // dec ecx; jnz 0x1000; hlt   (register countdown: must never be skipped)
    private static readonly byte[] CountdownLoop = [0x49, 0x75, 0xFD, 0xF4];

    // Kernel-idle-thread shape: sti; nop; nop; cli; mov eax,[0x2000]; test; jz 0x1000; hlt
    // (STI in the body: an IRQ is deliverable mid-body even though IF is clear at the head)
    private static readonly byte[] StiIdleLoop =
    [
        0xFB, 0x90, 0x90, 0xFA, 0xA1, 0x00, 0x20, 0x00, 0x00, 0x85, 0xC0, 0x74, 0xF3, 0xF4,
    ];

    // inc dword [0x2000]; jmp 0x1000  (memory write in body: must never be skipped)
    private static readonly byte[] WriteLoop = [0xFF, 0x04, 0x25, 0x00, 0x20, 0x00, 0x00, 0xEB, 0xF7];

    [Fact]
    public void Skip_LandsExactlyOnHostDeadline_AndExitsPollLoop()
    {
        (XboxCpu cpu, FakeIdleHost host) = CreateIdleCpu(PollLoop);
        host.PollAddress = PollAddress;
        host.InitialDeadline = 1000;
        host.FirePollAtDeadline = true;

        int executed = cpu.Execute(1_000_000);

        Assert.True(cpu.Context.Halted);
        Assert.Equal(1000UL, host.Advanced);
        Assert.Equal(1000UL, cpu.IdleSkipSkippedSteps);
        Assert.Equal(1UL, cpu.IdleSkipStopWatch);
        Assert.Equal(0UL, cpu.IdleSkipStopDeadline);
        Assert.True(cpu.IdleSkipTriggers >= 1);
        Assert.Equal(0x100Au, cpu.Context.Eip); // past the hlt at 0x1009
        Assert.True(executed > 1000);
        Assert.Equal(1UL, cpu.IdleSkipHltExecuted);
    }

    [Fact]
    public void Skip_WithoutDeadline_StopsWithinOneChunkOfPollExit()
    {
        (XboxCpu cpu, FakeIdleHost host) = CreateIdleCpu(PollLoop);
        host.PollAddress = PollAddress;
        host.InitialDeadline = null;
        host.FirePollAtAdvanced = 5000;
        int chunk = cpu.IdleSkipChunkSteps;

        cpu.Execute(1_000_000);

        // Poll changed at cycle 5000 inside the second chunk; the skip must stop
        // at the first chunk boundary after the change and never run past it.
        Assert.True(host.Advanced > 5000, $"advanced {host.Advanced} should pass the poll change");
        Assert.True(host.Advanced <= 5000 + (ulong)chunk, $"advanced {host.Advanced} overshoots more than one chunk");
        Assert.Equal(1UL, cpu.IdleSkipStopWatch);
        Assert.True(cpu.Context.Halted);
    }

    [Fact]
    public void Skip_PendingEnabledIrqAtStart_NeverSkips()
    {
        (XboxCpu cpu, FakeIdleHost host) = CreateIdleCpu(PollLoop);
        host.PollAddress = PollAddress;
        host.Irq = true;

        cpu.Execute(50_000);

        Assert.Equal(0UL, host.Advanced);
        Assert.Equal(0UL, cpu.IdleSkipSkippedSteps);
        Assert.True(cpu.IdleSkipTriggers >= 1);
        Assert.True(cpu.IdleSkipStopIrq >= 1);
        Assert.False(cpu.Context.Halted);
    }

    [Fact]
    public void Skip_IrqRaisedAtDeadline_StopsExactlyOnDeadline()
    {
        (XboxCpu cpu, FakeIdleHost host) = CreateIdleCpu(PollLoop);
        host.PollAddress = PollAddress;
        host.InitialDeadline = 1000;
        host.FireIrqAtDeadline = true;

        cpu.Execute(50_000);

        // The chunk that reaches the deadline raises the IRQ; the next stop
        // condition check wakes immediately and no further time is advanced.
        Assert.Equal(1000UL, host.Advanced);
        Assert.True(cpu.IdleSkipStopIrq >= 1);
        Assert.Equal(0UL, cpu.IdleSkipStopDeadline);
        Assert.False(cpu.Context.Halted);
    }

    [Fact]
    public void Skip_MmioClassifiedBody_DisablesSkip()
    {
        (XboxCpu cpu, FakeIdleHost host) = CreateIdleCpu(MmioPollLoop);
        host.PollAddress = MmioLookalikeAddress;
        host.NonRam.Add(MmioLookalikeAddress);

        cpu.Execute(50_000);

        Assert.Equal(0UL, host.Advanced);
        Assert.Equal(0UL, cpu.IdleSkipTriggers);
        Assert.True(cpu.IdleSkipAccessAborts >= 1);
    }

    [Fact]
    public void Skip_PioBody_DisablesSkip()
    {
        (XboxCpu cpu, FakeIdleHost host) = CreateIdleCpu(PioPollLoop);
        host.PollAddress = null;

        cpu.Execute(50_000);

        Assert.Equal(0UL, host.Advanced);
        Assert.Equal(0UL, cpu.IdleSkipTriggers);
        Assert.True(cpu.IdleSkipAccessAborts >= 1);
    }

    [Fact]
    public void Skip_RegisterCountdown_IsRejectedByFixedPointRules()
    {
        (XboxCpu cpu, FakeIdleHost host) = CreateIdleCpu(CountdownLoop);
        cpu.Context.Ecx = 1000;

        cpu.Execute(100_000);

        Assert.Equal(0UL, host.Advanced);
        Assert.Equal(0UL, cpu.IdleSkipTriggers);
        Assert.True(cpu.IdleSkipStaticRejects >= 1);
        Assert.True(cpu.Context.Halted);
    }

    [Fact]
    public void Skip_MemoryWriteInBody_IsRejected()
    {
        (XboxCpu cpu, FakeIdleHost host) = CreateIdleCpu(WriteLoop);

        cpu.Execute(50_000);

        Assert.Equal(0UL, host.Advanced);
        Assert.Equal(0UL, cpu.IdleSkipTriggers);
        Assert.True(cpu.IdleSkipAccessAborts >= 1);
    }

    [Fact]
    public void Skip_StiBody_WakesOnPendingIrqEvenWithIfClearAtHead()
    {
        (XboxCpu cpu, FakeIdleHost host) = CreateIdleCpu(StiIdleLoop);
        host.PollAddress = PollAddress;
        host.InitialDeadline = 1000;
        host.FireIrqAtDeadline = true;

        cpu.Execute(100_000);

        // The body executes STI, so the IRQ hard-stop must not be gated on the
        // IF flag at the loop head (CLI ran last there). The chunk that reaches
        // the deadline raises the IRQ; no further time is advanced.
        Assert.Equal(1000UL, host.Advanced);
        Assert.True(cpu.IdleSkipStopIrq >= 1);
        Assert.Equal(0UL, cpu.IdleSkipStopDeadline);
        Assert.False(cpu.Context.Halted);
    }

    [Fact]
    public void Skip_MeasureOnlyCountsTriggersButNeverAdvances()
    {
        (XboxCpu cpu, FakeIdleHost host) = CreateIdleCpu(PollLoop);
        cpu.IdleSkipMeasureOnly = true;
        host.PollAddress = PollAddress;
        host.InitialDeadline = 1000;
        host.FirePollAtDeadline = true;

        cpu.Execute(50_000);

        Assert.Equal(0UL, host.Advanced);
        Assert.True(cpu.IdleSkipTriggers >= 1);
        Assert.False(cpu.Context.Halted);
    }

    [Fact]
    public void Skip_DisabledByDefault_IsByteIdenticalToPlainInterpretation()
    {
        (XboxCpu cpu, FakeIdleHost host) = CreateIdleCpu(PollLoop, enableIdleSkip: false);
        host.PollAddress = PollAddress;
        host.InitialDeadline = 1000;
        host.FirePollAtDeadline = true;

        int executed = cpu.Execute(50_000);

        Assert.False(cpu.IdleSkipEnabled);
        Assert.Equal(50_000, executed);
        Assert.Equal(0UL, host.Advanced);
        Assert.Equal(0UL, cpu.IdleSkipTriggers);
        Assert.Equal(50_000UL, cpu.Context.Tsc);
        Assert.False(cpu.Context.Halted);
    }

    private static (XboxCpu Cpu, FakeIdleHost Host) CreateIdleCpu(byte[] code, bool enableIdleSkip = true)
    {
        XboxCpu cpu = CpuHarness.Create(code);
        EnablePaging(cpu);
        cpu.Context.Eflags |= FlagBits.If;

        var host = new FakeIdleHost(cpu.Ram());
        cpu.IdleSkipHost = host;
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
    /// Fake device with one scheduled event: a deadline in cycles from skip start
    /// that fires the poll write and/or an IRQ when the advance reaches it.
    /// </summary>
    private sealed class FakeIdleHost(PhysicalMemory ram) : IIdleSkipHost
    {
        private long? _deadline;

        public bool Irq { get; set; }

        public ulong Advanced { get; private set; }

        public long? InitialDeadline { get; set; }

        public bool FirePollAtDeadline { get; set; }

        public bool FireIrqAtDeadline { get; set; }

        public ulong? FirePollAtAdvanced { get; set; }

        // ReSharper disable once MemberHidesStaticFromOuterClass
        public uint? PollAddress { get; set; }

        private byte PollValue { get; } = 1;

        public HashSet<uint> NonRam { get; } = [];

        public long? StepsUntilNextDeadline => _deadline ??= InitialDeadline;

        public bool IrqPendingAndEnabled => Irq;

        public bool IsRam(uint physicalAddress)
        {
            return !NonRam.Contains(physicalAddress);
        }

        public void AdvanceIdle(ulong steps)
        {
            Advanced += steps;
            if (_deadline is { } deadline)
            {
                _deadline = deadline > (long)steps ? deadline - (long)steps : 0;
                if (_deadline == 0)
                {
                    InitialDeadline = null;
                    _deadline = null;
                    if (FirePollAtDeadline && PollAddress is { } deadlinePoll)
                    {
                        ram.Write8(deadlinePoll, PollValue);
                    }

                    if (FireIrqAtDeadline)
                    {
                        Irq = true;
                    }
                }
            }

            if (FirePollAtAdvanced is { } fireAt && Advanced >= fireAt && PollAddress is { } address)
            {
                ram.Write8(address, PollValue);
                FirePollAtAdvanced = null;
            }
        }
    }
}
