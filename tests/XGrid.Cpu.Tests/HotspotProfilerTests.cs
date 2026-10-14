using System;
using System.Collections.Generic;
using System.IO;
using XGrid.Memory;
using Xunit;

namespace XGrid.Cpu.Tests;

/// <summary>
/// Hotspot profiler unit tests (§126): mnemonic counts, the 128-head cap
/// with overflow accounting, TLB hit/miss outcomes, fetch/split and
/// RAM-vs-MMIO data counters, and ranked report output. Synthetic snippets
/// only; the 1B-step title profiles are acceptance runs, not unit tests.
/// </summary>
public sealed class HotspotProfilerTests
{
    // nop; nop; jmp 0x1000 (EB FC lands nextEip 0x1004 - 4 back at the head).
    private static readonly byte[] NopLoop = { 0x90, 0x90, 0xEB, 0xFC };

    // mov eax,[ram]; mov eax,[mmio]; mov [ram],eax; mov ax,[page-end]; hlt.
    private static readonly byte[] AccessSnippet =
    {
        0xA1, 0x00, 0x20, 0x00, 0x00,
        0xA1, 0x00, 0x30, 0x00, 0x00,
        0xA3, 0x00, 0x20, 0x00, 0x00,
        0x66, 0xA1, 0xFF, 0x0F, 0x00, 0x00,
        0xF4,
    };

    [Fact]
    public void Observe_CountsMnemonicsHeadsAndFetches()
    {
        XboxCpu cpu = CpuHarness.Create(NopLoop);
        var profiler = new HotspotProfiler();
        cpu.HotspotObserver = profiler;

        int executed = cpu.Execute(9);

        Assert.Equal(9, executed);
        Assert.Equal(9UL, profiler.TotalInstructions);
        Assert.Equal(6UL, profiler.CountFor(Mnemonic.Nop));
        Assert.Equal(3UL, profiler.CountFor(Mnemonic.Jmp));
        Assert.Equal(0UL, profiler.CountFor(Mnemonic.Hlt));
        Assert.Equal(3UL, profiler.TotalHeadVisits);
        Assert.Equal(1, profiler.TrackedHeadCount);
        Assert.Equal(0UL, profiler.HeadsOverflow);
        Assert.Equal((CpuHarness.CodeAddress, 3UL), profiler.TopHeads(5)[0]);
        Assert.Equal(9UL, profiler.Fetches);
        Assert.Equal(0UL, profiler.FetchCrossPage);
        Assert.Equal(0UL, profiler.TlbHits);
        Assert.Equal(0UL, profiler.TlbMisses);
        Assert.Equal(0UL, profiler.TotalDataAccesses);
        Assert.Equal(0UL, profiler.SplitAccesses);
    }

    [Fact]
    public void Heads_CappedAt128_WithOverflowAccounting()
    {
        // 130 chained forward jumps (EB 01), each landing on a distinct head.
        var code = new byte[130 * 3];
        for (int i = 0; i < 130; i++)
        {
            code[i * 3] = 0xEB;
            code[i * 3 + 1] = 0x01;
            code[i * 3 + 2] = 0x90;
        }

        XboxCpu cpu = CpuHarness.Create(code);
        var profiler = new HotspotProfiler();
        cpu.HotspotObserver = profiler;

        int executed = cpu.Execute(130);

        Assert.Equal(130, executed);
        Assert.Equal(130UL, profiler.TotalInstructions);
        Assert.Equal(130UL, profiler.CountFor(Mnemonic.Jmp));
        Assert.Equal(130UL, profiler.TotalHeadVisits);
        Assert.Equal(HotspotProfiler.MaxTrackedHeads, profiler.TrackedHeadCount);
        Assert.Equal(2UL, profiler.HeadsOverflow);
    }

    [Fact]
    public void Tlb_HitMiss_OnlyWithSideEffects()
    {
        var ram = new PhysicalMemory(0x10000);
        var bus = new AddressSpace(ram, UnmappedAccessPolicy.Throw);
        var context = new XboxContext
        {
            Cr0 = ControlRegisterBits.Cr0ProtectedMode | ControlRegisterBits.Cr0Paging,
            Cr3 = 0x1000,
        };
        var mmu = new Mmu(context, bus);
        bus.Write32(0x1000, 0x2000u | 3u);
        bus.Write32(0x2000 + 0x10u * 4u, 0x8000u | 3u);
        var profiler = new HotspotProfiler();
        mmu.Observer = profiler;

        mmu.Translate(0x00010ABC, false, false, out bool faulted, out _);
        Assert.False(faulted);
        Assert.Equal(1UL, profiler.TlbMisses);
        Assert.Equal(0UL, profiler.TlbHits);

        mmu.Translate(0x00010ABC, false, false, out faulted, out _);
        Assert.False(faulted);
        Assert.Equal(1UL, profiler.TlbMisses);
        Assert.Equal(1UL, profiler.TlbHits);

        // Probe translates (side effects off) report nothing.
        mmu.Translate(0x00010ABC, false, false, out _, out _, sideEffects: false);
        Assert.Equal(1UL, profiler.TlbMisses);
        Assert.Equal(1UL, profiler.TlbHits);

        // Paging disabled reports nothing.
        context.Cr0 = ControlRegisterBits.Cr0ProtectedMode;
        mmu.Translate(0x00010ABC, false, false, out _, out _);
        Assert.Equal(1UL, profiler.TlbMisses);
        Assert.Equal(1UL, profiler.TlbHits);
    }

    [Fact]
    public void DataAccess_ClassifiesRamVsMmio_AndCountsSplits()
    {
        XboxCpu cpu = CpuHarness.Create(AccessSnippet);

        // Paging on with an identity map for VPN 0..3 (code, page-end,
        // RAM/MMIO data pages) so the split takes the per-byte path.
        AddressSpace bus = cpu.Bus();
        bus.Write32(0x4000, 0x5000u | 3u);
        for (uint vpn = 0; vpn < 4; vpn++)
        {
            bus.Write32(0x5000 + vpn * 4, (vpn << 12) | 3u);
        }

        cpu.Context.Cr3 = 0x4000;
        cpu.Context.Cr0 = ControlRegisterBits.Cr0ProtectedMode | ControlRegisterBits.Cr0Paging;

        var host = new StubIdleHost();
        host.NonRam.Add(0x3000);
        cpu.IdleSkipHost = host;
        var profiler = new HotspotProfiler();
        cpu.HotspotObserver = profiler;

        cpu.Execute(100);

        Assert.True(cpu.Context.Halted);
        Assert.Equal(5UL, profiler.TotalInstructions);
        Assert.Equal(1UL, profiler.CountFor(Mnemonic.Hlt));
        Assert.Equal(3UL, profiler.DataReadsRam);
        Assert.Equal(0UL, profiler.DataWritesMmio);
        Assert.Equal(1UL, profiler.DataReadsMmio);
        Assert.Equal(1UL, profiler.DataWritesRam);
        Assert.Equal(1UL, profiler.SplitAccesses);
        Assert.Equal(5UL, profiler.Fetches);

        // 5 fetches x 15 bytes (one VPN1 miss, rest hits) plus 5 data
        // translates (VPN2/VPN3/VPN0 misses, VPN2/VPN1 hits).
        Assert.Equal(4UL, profiler.TlbMisses);
        Assert.Equal(76UL, profiler.TlbHits);
    }

    [Fact]
    public void WriteReport_RanksDescending_AndSummarizes()
    {
        var profiler = new HotspotProfiler();
        var mov = new DecodedInstruction { Mnemonic = Mnemonic.Mov, Length = 5 };
        var nop = new DecodedInstruction { Mnemonic = Mnemonic.Nop, Length = 1 };
        var jmp = new DecodedInstruction { Mnemonic = Mnemonic.Jmp, Length = 2 };

        profiler.Observe(mov, 0x1000, 0x1005);
        profiler.Observe(mov, 0x1005, 0x100A);
        profiler.Observe(mov, 0x100A, 0x100F);
        profiler.Observe(nop, 0x100F, 0x1010);
        profiler.Observe(jmp, 0x1010, 0x2000);
        profiler.Observe(jmp, 0x2000, 0x2000);
        profiler.NoteTlbHit();
        profiler.NoteTlbMiss();
        profiler.NoteDataAccess(isWrite: false, isRam: true);
        profiler.NoteSplitAccess();

        Assert.Equal((Mnemonic.Mov, 3UL), profiler.TopMnemonics(2)[0]);
        Assert.Equal((Mnemonic.Jmp, 2UL), profiler.TopMnemonics(2)[1]);
        Assert.Equal((0x2000u, 2UL), profiler.TopHeads(5)[0]);

        using var writer = new StringWriter();
        profiler.WriteReport(writer, 5);
        string report = writer.ToString();

        Assert.Contains("summary,totalInstructions,6,", report);
        Assert.Contains("summary,tlbHits,1,", report);
        Assert.Contains("summary,splitAccesses,1,", report);
        Assert.Contains("mnemonic,Mov,3,", report);
        Assert.Contains("head,0x00002000,2,", report);
        Assert.True(report.IndexOf("mnemonic,Mov,3,", StringComparison.Ordinal) < report.IndexOf("mnemonic,Jmp,2,", StringComparison.Ordinal));
    }

    private sealed class StubIdleHost : IIdleSkipHost
    {
        public HashSet<uint> NonRam { get; } = new();

        public bool IrqPendingAndEnabled => false;

        public long? StepsUntilNextDeadline => null;

        public bool IsRam(uint physicalAddress)
        {
            return !NonRam.Contains(physicalAddress);
        }

        public void AdvanceIdle(ulong steps)
        {
        }
    }
}
