using System.IO;
using System.Text;
using XGrid.Memory;
using Xunit;

namespace XGrid.Cpu.Tests;

public sealed class BlockExecutionTests
{
    private static readonly byte[] Loop = [0x40, 0x83, 0xF0, 0x35, 0x48, 0x90, 0xEB, 0xF8];

    // mov eax,[0x2000]; test eax,eax; jz 0x1000; hlt (kernel-style poll loop).
    private static readonly byte[] PollLoopBytes = [0xA1, 0x00, 0x20, 0x00, 0x00, 0x85, 0xC0, 0x74, 0xF7, 0xF4];

    private const uint PageDirectory = 0x4000;
    private const uint PageTable = 0x5000;

    [Fact]
    public void IdleSkip_VerdictsExact_WithBlockExecution()
    {
        (XboxCpu off, PollIdleHost offHost) = CreateIdle(false);
        off.Execute(1_000_000);

        (XboxCpu on, PollIdleHost onHost) = CreateIdle(true);
        on.Execute(1_000_000);

        // Block hits ride the §134 hit-path replay (15 per-byte fetch notes
        // from the entry physicals), so idle-skip verdicts stay exact.
        Assert.True(on.Context.Halted);
        Assert.True(onHost.Advanced > 0);
        Assert.Equal(offHost.Advanced, onHost.Advanced);
        Assert.Equal(off.IdleSkipSkippedSteps, on.IdleSkipSkippedSteps);
        Assert.Equal(off.IdleSkipTriggers, on.IdleSkipTriggers);
        Assert.Equal(off.IdleSkipStopWatch, on.IdleSkipStopWatch);
        Assert.Equal(off.IdleSkipIterations, on.IdleSkipIterations);
        Assert.Equal(off.Context.Eip, on.Context.Eip);
        Assert.Equal(off.Context.Tsc, on.Context.Tsc);
        Assert.True(on.BlockStatistics!.Instructions > 0);
        return;

        static (XboxCpu Cpu, PollIdleHost Host) CreateIdle(bool block)
        {
            XboxCpu cpu = CpuHarness.Create(PollLoopBytes);
            MapIdentity(cpu.Bus(), 0, 4);
            EnablePaging(cpu);
            cpu.Context.Eflags |= FlagBits.If;
            var host = new PollIdleHost(cpu.Ram()) { PollAddress = 0x2000, InitialDeadline = 1000 };
            cpu.IdleSkipHost = host;
            cpu.IdleSkipEnabled = true;
            cpu.IdleSkipMinIterations = 4;
            cpu.DecodeCacheEnabled = true;
            cpu.BlockExecutionEnabled = block;
            return (cpu, host);
        }
    }

    [Fact]
    public void UdMarker_AndVector14_ExactEipAndVector_WithBlockExecution()
    {
        // #UD: BOUND decodes invalid here; the fault discards the cursor and
        // the re-execution after IRET is served by the cached invalid marker.
        XboxCpu ud = CpuHarness.Create(0x62, 0x00, 0x00);
        ud.Ram().Write8(0x3000, 0xCF); // IRET handler
        InstallIdt(ud, 6, 0x3000);
        ud.DecodeCacheEnabled = true;
        ud.BlockExecutionEnabled = true;

        ud.Step();
        Assert.Equal(6, ud.LastFaultVector);
        Assert.Equal(0x1000u, ud.LastFaultEip);
        Assert.Equal(0x3000u, ud.Context.Eip);

        ud.Step(); // IRET back to 0x1000
        Assert.Equal(0x1000u, ud.Context.Eip);

        ud.Step(); // #UD again from the block/decode path
        Assert.Equal(6, ud.LastFaultVector);
        Assert.Equal(0x1000u, ud.LastFaultEip);
        Assert.Equal(1UL, ud.DecodeCacheHits);

        // #PF: unmapped code page faults with the exact vector/EIP/CR2 and
        // the cursor discarded (the fault unwinds before any member runs).
        XboxCpu pf = CpuHarness.Create(0x90, 0xF4);
        MapIdentity(pf.Bus(), 6, 1);
        MapIdentity(pf.Bus(), 7, 1);
        MapIdentity(pf.Bus(), 8, 1);
        EnablePaging(pf);
        InstallIdt(pf, 14, 0x3000);
        pf.Ram().Write8(0x3000, 0xCF); // IRET handler
        pf.Context.Eip = 0x1000; // VPN 0 is deliberately unmapped (DecodeCacheTests shape)
        pf.DecodeCacheEnabled = true;
        pf.BlockExecutionEnabled = true;

        pf.Step();
        Assert.Equal(14, pf.LastFaultVector);
        Assert.Equal(0x1000u, pf.LastFaultEip);
        Assert.Equal(0x1000u, pf.Context.Cr2);
        Assert.Equal(0, pf.BlockStatistics!.Count);
        Assert.Equal(0UL, pf.DecodeCacheMisses);
    }

    [Fact]
    public void EveryStepAndBudget_MatchesDecodeCache_StateAndInstructionCounts()
    {
        XboxCpu plain = Create(false);
        XboxCpu block = Create(true);
        var plainProfile = new HotspotProfiler();
        var blockProfile = new HotspotProfiler();
        plain.HotspotObserver = plainProfile;
        block.HotspotObserver = blockProfile;
        for (int i = 0; i < 200; i++)
        {
            // Alternating the same APIs used by trace/break-log and machine batches.
            if ((i & 1) == 0)
            {
                plain.Step();
                block.Step();
            }
            else
            {
                Assert.Equal(plain.Execute(i), block.Execute(i));
            }

            Assert.Equal(State(plain), State(block));
            Assert.Equal(plain.DecodeCacheHits, block.DecodeCacheHits);
            Assert.Equal(plain.DecodeCount, block.DecodeCount);
            Assert.Equal(plainProfile.TotalInstructions, blockProfile.TotalInstructions);
        }

        Assert.True(block.BlockStatistics!.Instructions > 1000);
        Assert.True(block.BlockStatistics.Dispatches < block.BlockStatistics.Instructions / 2);
    }

    [Fact]
    public void Verify_RejectsStaleMemberBeforeExecution()
    {
        XboxCpu cpu = Create(true);
        cpu.BlockVerify = true;
        cpu.Execute(5); // Warm whole loop.
        cpu.Step(); // First member of active block.
        cpu.Ram().Write8(0x1001, 0x90); // Bypass hooks deliberately.
        cpu.Step();
        Assert.Equal(0x1002u, cpu.Context.Eip); // Fresh NOP, not stale XOR.
        Assert.Equal(1UL, cpu.BlockStatistics!.VerifyFailures);
    }

    [Fact]
    public void HostWriteAndSnapshotLoad_DiscardActiveMembers()
    {
        XboxCpu cpu = Create(true);
        cpu.Execute(6);
        byte[] state = State(cpu);
        cpu.Ram().Write8(0x1001, 0x90);
        cpu.InvalidateDecodeCachePage(0x1001);
        cpu.Step();
        Assert.Equal(0x1002u, cpu.Context.Eip);
        using var stream = new MemoryStream(state);
        using var reader = new BinaryReader(stream, Encoding.UTF8);
        cpu.LoadState(reader);
        Assert.Equal(0, cpu.BlockStatistics!.Count);
        cpu.Step();
        Assert.Equal(0x1002u, cpu.Context.Eip);
    }

    private static XboxCpu Create(bool block)
    {
        XboxCpu cpu = CpuHarness.Create(Loop);
        cpu.DecodeCacheEnabled = true;
        cpu.BlockExecutionEnabled = block;
        return cpu;
    }

    private static byte[] State(XboxCpu cpu)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            cpu.SaveState(writer);
        }

        return stream.ToArray();
    }

    private static void MapIdentity(AddressSpace bus, uint firstVpn, uint count)
    {
        bus.Write32(PageDirectory, PageTable | 3u);
        for (uint vpn = firstVpn; vpn < firstVpn + count; vpn++)
        {
            bus.Write32(PageTable + vpn * 4, (vpn << 12) | 3u);
        }
    }

    private static void EnablePaging(XboxCpu cpu)
    {
        cpu.Context.Cr3 = PageDirectory;
        cpu.Context.Cr0 = ControlRegisterBits.Cr0ProtectedMode | ControlRegisterBits.Cr0Paging;
        cpu.Mmu.Flush();
    }

    private static void InstallIdt(XboxCpu cpu, int vector, uint handler, uint idtAddress = 0x6000)
    {
        uint gate = idtAddress + (uint)vector * 8;
        cpu.Bus().Write16(gate, (ushort)(handler & 0xFFFF));
        cpu.Bus().Write16(gate + 2, 0x08);
        cpu.Bus().Write16(gate + 4, 0x8E00);
        cpu.Bus().Write16(gate + 6, (ushort)(handler >> 16));
        cpu.Context.Idtr.Base = idtAddress;
        cpu.Context.Idtr.Limit = 0x7FF;
    }

    /// <summary>
    /// Fake host with one scheduled event: a deadline in cycles from skip
    /// start that fires the poll write when the advance reaches it (the
    /// IdleSkipTests shape, trimmed to what the verdict test needs).
    /// </summary>
    [Fact]
    public void JitSpinSkip_AdvancesIdleWait_ToHostEvent()
    {
        // Kernel idle-loop shape: sti; nop; nop; cli; mov eax,[0x2000];
        // test eax,eax; jz back; hlt. The poll byte is written by the host's
        // AdvanceIdle at its deadline, so the loop can only exit through a
        // fast-forwarded device event.
        byte[] code =
        [
            0xFB, 0x90, 0x90, 0xFA, 0xA1, 0x00, 0x20, 0x00, 0x00, 0x85, 0xC0, 0x74, 0xF3, 0xF4,
        ];

        XboxCpu cpu = CpuHarness.Create(code);
        MapIdentity(cpu.Bus(), 0, 4);
        EnablePaging(cpu);
        cpu.Context.Eflags |= FlagBits.If;
        cpu.DecodeCacheEnabled = true;
        cpu.JitSpecSlice = int.MaxValue;
        cpu.JitBuildThreshold = 1;
        cpu.JitEnabled = true;
        cpu.JitSpinSkip = true;
        var host = new PollIdleHost(cpu.Ram()) { PollAddress = 0x2000, InitialDeadline = 1000 };
        cpu.IdleSkipHost = host;

        int executed = cpu.Execute(50_000_000);

        Assert.True(cpu.Context.Halted);
        Assert.True(cpu.JitSpinSkips > 0, $"skips={cpu.JitSpinSkips}");
        Assert.True(cpu.JitSpinSkippedSteps > 0, $"skipped={cpu.JitSpinSkippedSteps}");
        Assert.True(executed < 5000, $"executed={executed}");
        Assert.True(host.Advanced > 0);

        // Without the flag the loop cannot exit (nothing advances the host):
        // the same program spins to the budget and the skip never fires.
        XboxCpu off = CpuHarness.Create(code);
        MapIdentity(off.Bus(), 0, 4);
        EnablePaging(off);
        off.Context.Eflags |= FlagBits.If;
        off.DecodeCacheEnabled = true;
        off.JitSpecSlice = int.MaxValue;
        off.JitBuildThreshold = 1;
        off.JitEnabled = true;
        var offHost = new PollIdleHost(off.Ram()) { PollAddress = 0x2000, InitialDeadline = 1000 };
        off.IdleSkipHost = offHost;

        int offExecuted = off.Execute(1_000_000);

        Assert.False(off.Context.Halted);
        Assert.Equal(0, off.JitSpinSkips);
        Assert.Equal(1_000_000, offExecuted);
        Assert.Equal(0UL, offHost.Advanced);
    }

    private sealed class PollIdleHost(PhysicalMemory ram) : IIdleSkipHost
    {
        private long? _deadline;

        public ulong Advanced { get; private set; }

        public long? InitialDeadline { get; set; }

        public uint? PollAddress { get; init; }

        private byte PollValue { get; } = 1;

        public long? StepsUntilNextDeadline => _deadline ??= InitialDeadline;

        public bool IrqPendingAndEnabled => false;

        public bool IsRam(uint _) => true;

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
                    if (PollAddress is { } poll)
                    {
                        ram.Write8(poll, PollValue);
                    }
                }
            }
        }
    }
}
