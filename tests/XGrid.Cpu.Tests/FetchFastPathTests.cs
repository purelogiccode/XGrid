using XGrid.Memory;
using Xunit;

namespace XGrid.Cpu.Tests;

/// <summary>
/// Fetch fast path unit tests (§131 leg (c)): flag defaults off, straight-line
/// identity with single-translate TLB shares, page-crossing fallback with
/// identical totals, 4 MB large-page single-miss coverage, fault precision,
/// and paging-off silence. Synthetic snippets only; title goldens are
/// acceptance runs, not unit tests.
/// </summary>
public sealed class FetchFastPathTests
{
    // nop; nop; mov eax,0x1234; hlt (8 bytes, no data accesses).
    private static readonly byte[] StraightLine = { 0x90, 0x90, 0xB8, 0x34, 0x12, 0x00, 0x00, 0xF4 };

    private const uint PageDirectory = 0x4000;
    private const uint PageTable = 0x5000;

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

    [Fact]
    public void FetchFastPath_OffByDefault()
    {
        XboxCpu cpu = CpuHarness.Create(StraightLine);

        Assert.False(cpu.FetchFastPathEnabled);
    }

    [Fact]
    public void FetchFast_StraightLine_IdenticalState_SingleTranslates()
    {
        var offProfiler = new HotspotProfiler();
        XboxCpu off = CpuHarness.Create(StraightLine);
        MapIdentity(off.Bus(), 0, 4);
        EnablePaging(off);
        off.HotspotObserver = offProfiler;
        int offExecuted = off.Execute(4);

        var onProfiler = new HotspotProfiler();
        XboxCpu on = CpuHarness.Create(StraightLine);
        MapIdentity(on.Bus(), 0, 4);
        EnablePaging(on);
        on.FetchFastPathEnabled = true;
        on.HotspotObserver = onProfiler;
        int onExecuted = on.Execute(4);

        Assert.Equal(offExecuted, onExecuted);
        Assert.True(off.Context.Halted);
        Assert.True(on.Context.Halted);
        Assert.Equal(off.Context.Eip, on.Context.Eip);
        Assert.Equal(0x1008u, on.Context.Eip);
        Assert.Equal(off.Context.ReadRegister(Gpr.Eax), on.Context.ReadRegister(Gpr.Eax));
        Assert.Equal(0x1234u, on.Context.ReadRegister(Gpr.Eax));

        // Legacy: 4 fetches x 15 bytes (first byte misses, rest hit), no data traffic.
        Assert.Equal(4UL, offProfiler.Fetches);
        Assert.Equal(1UL, offProfiler.TlbMisses);
        Assert.Equal(59UL, offProfiler.TlbHits);
        Assert.Equal(0UL, offProfiler.FetchSlowFallbacks);

        // Fast: one translate per fetch (same first-byte miss), same fetches.
        Assert.Equal(4UL, onProfiler.Fetches);
        Assert.Equal(4UL, onProfiler.TotalInstructions);
        Assert.Equal(1UL, onProfiler.TlbMisses);
        Assert.Equal(3UL, onProfiler.TlbHits);
        Assert.Equal(0UL, onProfiler.FetchSlowFallbacks);
        Assert.Equal(0UL, onProfiler.TotalDataAccesses);
    }

    [Fact]
    public void FetchFast_CrossPageWindow_FallsBack_WithIdenticalTotals()
    {
        var offProfiler = new HotspotProfiler();
        XboxCpu off = CreateAtPageEnd(false);
        off.HotspotObserver = offProfiler;
        off.Execute(2);

        var onProfiler = new HotspotProfiler();
        XboxCpu on = CreateAtPageEnd(true);
        on.HotspotObserver = onProfiler;
        on.Execute(2);

        Assert.True(off.Context.Halted);
        Assert.True(on.Context.Halted);
        Assert.Equal(off.Context.Eip, on.Context.Eip);
        Assert.Equal(0x1FFDu, on.Context.Eip);

        // Both fetch windows cross 0x2000, so both take the slow path: the
        // fallback reports byte-identical totals (VPN1 miss + VPN2 miss on
        // the first fetch, all hits after).
        Assert.Equal(2UL, offProfiler.Fetches);
        Assert.Equal(2UL, offProfiler.FetchCrossPage);
        Assert.Equal(0UL, offProfiler.FetchSlowFallbacks);
        Assert.Equal(2UL, onProfiler.Fetches);
        Assert.Equal(2UL, onProfiler.FetchCrossPage);
        Assert.Equal(2UL, onProfiler.FetchSlowFallbacks);
        Assert.Equal(offProfiler.TlbHits, onProfiler.TlbHits);
        Assert.Equal(offProfiler.TlbMisses, onProfiler.TlbMisses);
        Assert.Equal(2UL, onProfiler.TlbMisses);
        Assert.Equal(28UL, onProfiler.TlbHits);
        return;

        static XboxCpu CreateAtPageEnd(bool fast)
        {
            XboxCpu cpu = CpuHarness.Create();
            cpu.Bus().Write8(0x1FFB, 0x90);
            cpu.Bus().Write8(0x1FFC, 0xF4);
            MapIdentity(cpu.Bus(), 0, 4);
            EnablePaging(cpu);
            cpu.Context.Eip = 0x1FFB;
            cpu.FetchFastPathEnabled = fast;
            return cpu;
        }
    }

    [Fact]
    public void FetchFast_LargePage_OneMissPerFetch()
    {
        var offProfiler = new HotspotProfiler();
        XboxCpu off = CreateLarge(false);
        off.HotspotObserver = offProfiler;
        off.Execute(4);

        var onProfiler = new HotspotProfiler();
        XboxCpu on = CreateLarge(true);
        on.HotspotObserver = onProfiler;
        on.Execute(4);

        Assert.True(off.Context.Halted);
        Assert.True(on.Context.Halted);
        Assert.Equal(off.Context.Eip, on.Context.Eip);
        Assert.Equal(off.Context.ReadRegister(Gpr.Eax), on.Context.ReadRegister(Gpr.Eax));

        // Large pages never sit in the SoftTLB: the legacy path misses on
        // every byte, the fast path on one directory walk per fetch.
        Assert.Equal(60UL, offProfiler.TlbMisses);
        Assert.Equal(0UL, offProfiler.TlbHits);
        Assert.Equal(4UL, onProfiler.TlbMisses);
        Assert.Equal(0UL, onProfiler.TlbHits);
        Assert.Equal(4UL, onProfiler.Fetches);
        Assert.Equal(0UL, onProfiler.FetchSlowFallbacks);
        return;

        static XboxCpu CreateLarge(bool fast)
        {
            XboxCpu cpu = CpuHarness.Create(StraightLine);
            cpu.Bus().Write32(PageDirectory, 0x83u);
            cpu.Context.Cr3 = PageDirectory;
            cpu.Context.Cr4 = ControlRegisterBits.Cr4PageSizeExtensions;
            cpu.Context.Cr0 = ControlRegisterBits.Cr0ProtectedMode | ControlRegisterBits.Cr0Paging;
            cpu.Mmu.Flush();
            cpu.FetchFastPathEnabled = fast;
            return cpu;
        }
    }

    [Fact]
    public void FetchFast_FaultPrecision_MatchesSlowPath()
    {
        (XboxCpu off, HotspotProfiler offProfiler) = CreateFaulting(false);
        off.Step();

        (XboxCpu on, HotspotProfiler onProfiler) = CreateFaulting(true);
        on.Step();

        // Code page VPN1 is unmapped: the fetch faults and vector 14 handles it.
        Assert.Equal(0x3000u, off.Context.Eip);
        Assert.Equal(off.Context.Eip, on.Context.Eip);
        Assert.Equal(off.Context.Cr2, on.Context.Cr2);
        Assert.Equal(0x1000u, on.Context.Cr2);
        Assert.Equal(off.LastFaultVector, on.LastFaultVector);
        Assert.Equal(14, on.LastFaultVector);
        Assert.Equal(off.LastFaultEip, on.LastFaultEip);
        Assert.Equal(0x1000u, on.LastFaultEip);

        // Both report exactly one fetch and identical TLB outcomes: the slow
        // path faults on byte 0 before bytes 1..14 are attempted, and the
        // vector-14 delivery walks (gate/stack/descriptor) are shared.
        Assert.Equal(offProfiler.Fetches, onProfiler.Fetches);
        Assert.Equal(1UL, onProfiler.Fetches);
        Assert.Equal(offProfiler.TlbHits, onProfiler.TlbHits);
        Assert.Equal(offProfiler.TlbMisses, onProfiler.TlbMisses);
        return;

        static (XboxCpu Cpu, HotspotProfiler Profiler) CreateFaulting(bool fast)
        {
            XboxCpu cpu = CpuHarness.Create(StraightLine);
            MapIdentity(cpu.Bus(), 0, 1);
            MapIdentity(cpu.Bus(), 6, 1);
            MapIdentity(cpu.Bus(), 7, 1);
            MapIdentity(cpu.Bus(), 8, 1);
            EnablePaging(cpu);
            InstallIdt(cpu, 14, 0x3000);
            cpu.FetchFastPathEnabled = fast;
            var profiler = new HotspotProfiler();
            cpu.HotspotObserver = profiler;
            return (cpu, profiler);
        }
    }

    [Fact]
    public void FetchFast_PagingOff_NoTlbTraffic()
    {
        var profiler = new HotspotProfiler();
        XboxCpu cpu = CpuHarness.Create(StraightLine);
        cpu.FetchFastPathEnabled = true;
        cpu.HotspotObserver = profiler;

        cpu.Execute(4);

        Assert.True(cpu.Context.Halted);
        Assert.Equal(0x1008u, cpu.Context.Eip);
        Assert.Equal(0x1234u, cpu.Context.ReadRegister(Gpr.Eax));
        Assert.Equal(4UL, profiler.Fetches);
        Assert.Equal(0UL, profiler.TlbHits);
        Assert.Equal(0UL, profiler.TlbMisses);
        Assert.Equal(0UL, profiler.FetchSlowFallbacks);
    }
}
