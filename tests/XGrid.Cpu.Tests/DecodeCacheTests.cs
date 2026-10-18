using System.Collections.Generic;
using System.IO;
using System.Text;
using XGrid.Memory;
using Xunit;

namespace XGrid.Cpu.Tests;

/// <summary>
/// Decode-cache unit tests (§131 leg (d)): flag defaults off with one decode
/// per step, fill/hit identity with the flag-off run (guest state + profiler
/// shares), page-granular store invalidation including the XBE-decrypt
/// xor-write pattern, page-table-write/CR3/CS/INVLPG/snapshot full flushes,
/// cross-page and non-RAM skips, vector-14/#UD fault precision, entry-cap
/// flush discipline, idle-skip verdict exactness (per-byte NoteAccess
/// replay), the debug-verify tripwire, and the host flush APIs. Synthetic
/// snippets only; title goldens are acceptance runs, not unit tests.
/// </summary>
public sealed class DecodeCacheTests
{
    // nop; nop; mov eax,0x1234; hlt (8 bytes, no data accesses).
    private static readonly byte[] StraightLine = { 0x90, 0x90, 0xB8, 0x34, 0x12, 0x00, 0x00, 0xF4 };

    // nop; nop; jmp 0x1000 (EB FC lands nextEip 0x1004 - 4 back at the head).
    private static readonly byte[] NopLoop = { 0x90, 0x90, 0xEB, 0xFC };

    // mov eax,[0x2000]; test eax,eax; jz 0x1000; hlt (kernel-style poll loop).
    private static readonly byte[] PollLoop = { 0xA1, 0x00, 0x20, 0x00, 0x00, 0x85, 0xC0, 0x74, 0xF7, 0xF4 };

    private const uint PageDirectory = 0x4000;
    private const uint PageTable = 0x5000;

    [Fact]
    public void DecodeCache_OffByDefault_ImpliesFetchFastWhenEnabled()
    {
        XboxCpu cpu = CpuHarness.Create(StraightLine);

        Assert.False(cpu.DecodeCacheEnabled);
        Assert.False(cpu.FetchFastPathEnabled);
        Assert.False(cpu.DecodeCacheVerify);

        cpu.Execute(4);
        Assert.True(cpu.Context.Halted);

        // Flag off: exactly one decode per executed instruction, no cache traffic.
        Assert.Equal(4UL, cpu.DecodeCount);
        Assert.Equal(0UL, cpu.DecodeCacheHits);
        Assert.Equal(0UL, cpu.DecodeCacheMisses);
        Assert.Equal(0, cpu.DecodeCacheEntries);

        cpu.DecodeCacheEnabled = true;
        Assert.True(cpu.DecodeCacheEnabled);
        Assert.True(cpu.FetchFastPathEnabled);

        cpu.DecodeCacheEnabled = false;
        Assert.False(cpu.DecodeCacheEnabled);
        Assert.Equal(0, cpu.DecodeCacheEntries);

        // The host flush APIs are no-ops while disabled.
        cpu.InvalidateDecodeCache();
        cpu.InvalidateDecodeCachePage(0x1000);
    }

    [Fact]
    public void FillHit_ServesDecode_StateIdentical_DecodeShareGone()
    {
        var offProfiler = new HotspotProfiler();
        XboxCpu off = CpuHarness.Create(NopLoop);
        MapIdentity(off.Bus(), 0, 4);
        EnablePaging(off);
        off.HotspotObserver = offProfiler;
        int offExecuted = off.Execute(9);

        var onProfiler = new HotspotProfiler();
        XboxCpu on = CpuHarness.Create(NopLoop);
        MapIdentity(on.Bus(), 0, 4);
        EnablePaging(on);
        on.DecodeCacheEnabled = true;
        on.HotspotObserver = onProfiler;
        int onExecuted = on.Execute(9);

        // Guest identity.
        Assert.Equal(offExecuted, onExecuted);
        Assert.Equal(9, onExecuted);
        Assert.Equal(off.Context.Eip, on.Context.Eip);
        Assert.Equal(off.Context.Tsc, on.Context.Tsc);
        Assert.Equal(offProfiler.CountFor(Mnemonic.Nop), onProfiler.CountFor(Mnemonic.Nop));
        Assert.Equal(offProfiler.CountFor(Mnemonic.Jmp), onProfiler.CountFor(Mnemonic.Jmp));
        Assert.Equal(6UL, onProfiler.CountFor(Mnemonic.Nop));
        Assert.Equal(3UL, onProfiler.CountFor(Mnemonic.Jmp));

        // 9 fetches over 3 distinct linears: 3 misses fill, 6 hits serve.
        Assert.Equal(9UL, onProfiler.Fetches);
        Assert.Equal(3UL, on.DecodeCacheMisses);
        Assert.Equal(6UL, on.DecodeCacheHits);
        Assert.Equal(3UL, on.DecodeCacheFills);
        Assert.Equal(3, on.DecodeCacheEntries);
        Assert.Equal(3UL, on.DecodeCount);
        Assert.Equal(0UL, on.DecodeCachePageDrops);
        Assert.Equal(0UL, on.DecodeCacheFullFlushes);

        // Misses translate once each (first walk + two TLB hits); hits
        // translate never. Flag-off pays 15 per fetch.
        Assert.Equal(1UL, onProfiler.TlbMisses);
        Assert.Equal(2UL, onProfiler.TlbHits);
        Assert.Equal(1UL, offProfiler.TlbMisses);
        Assert.Equal(134UL, offProfiler.TlbHits);
        Assert.Equal(6UL, onProfiler.DecodeCacheHits);
        Assert.Equal(3UL, onProfiler.DecodeCacheMisses);

        using var writer = new StringWriter();
        onProfiler.WriteReport(writer, 5);
        string report = writer.ToString();
        Assert.Contains("summary,decodeCacheHits,6,", report);
        Assert.Contains("summary,decodeCacheMisses,3,", report);
    }

    [Fact]
    public void CpuStore_XorWritePattern_DropsPhysicalPage()
    {
        // xor byte [0x2050],0xFF at 0x1000 (self-modifying loop head page
        // pattern of the §34 XBE decrypt), jmp 0x2000; nop nop nop; jmp back
        // (the 0x2000 block is written separately — it lives on page 2).
        var code = new byte[]
        {
            0x80, 0x35, 0x50, 0x20, 0x00, 0x00, 0xFF, // 0x1000: xor byte [0x2050],0xFF
            0xE9, 0xF4, 0x0F, 0x00, 0x00, // 0x1007: jmp 0x2000
        };

        XboxCpu on = CreateFlat(code);
        WritePageTwo(on);
        on.Ram().Write8(0x2050, 0x12);
        on.DecodeCacheEnabled = true;

        XboxCpu off = CreateFlat(code);
        WritePageTwo(off);
        off.Ram().Write8(0x2050, 0x12);

        // Iteration 1 writes before page 2 is cached (no-op drop).
        on.Execute(6);
        off.Execute(6);
        Assert.Equal(0xEDu, on.Ram().Read8(0x2050));

        // Iteration 2's write drops the four live page-2 entries; the next
        // 0x2000 fetch misses again — the decrypt pattern cannot go stale.
        int onExecuted = 6 + on.Execute(3);
        int offExecuted = 6 + off.Execute(3);

        // Guest identity (both runs xored 0x2050 twice).
        Assert.Equal(offExecuted, onExecuted);
        Assert.Equal(off.Context.Eip, on.Context.Eip);
        Assert.Equal(0x12u, on.Ram().Read8(0x2050));
        Assert.Equal(off.Ram().Read8(0x2050), on.Ram().Read8(0x2050));

        Assert.Equal(7UL, on.DecodeCacheMisses);
        Assert.Equal(2UL, on.DecodeCacheHits);
        Assert.Equal(1UL, on.DecodeCachePageDrops);
        Assert.Equal(0UL, on.DecodeCacheFullFlushes);
        Assert.Equal(3, on.DecodeCacheEntries);
        return;

        static void WritePageTwo(XboxCpu cpu)
        {
            cpu.Ram().Write8(0x2000, 0x90);
            cpu.Ram().Write8(0x2001, 0x90);
            cpu.Ram().Write8(0x2002, 0x90);
            cpu.Ram().Write8(0x2003, 0xE9);
            cpu.Ram().Write8(0x2004, 0xF8);
            cpu.Ram().Write8(0x2005, 0xEF);
            cpu.Ram().Write8(0x2006, 0xFF);
            cpu.Ram().Write8(0x2007, 0xFF);
        }
    }

    [Fact]
    public void PageTableWrite_FullFlushes_ThenRefills()
    {
        var code = new byte[]
        {
            0x90, // 0x1000: nop
            0xC7, 0x05, 0x00, 0x50, 0x00, 0x00, 0x03, 0x00, 0x00, 0x00, // 0x1001: mov dword [0x5000], 3
            0xF4, // 0x100B: hlt
        };

        XboxCpu cpu = CpuHarness.Create(code);
        MapIdentity(cpu.Bus(), 0, 8); // VPN0..7 so the store to 0x5000 (VPN5) lands
        EnablePaging(cpu);
        cpu.DecodeCacheEnabled = true;

        cpu.Execute(1);
        Assert.Equal(1, cpu.DecodeCacheEntries);

        // The store hits the observed page-table page (VPN0's PTE page,
        // recorded by the first fetch's walk): full flush, even though the
        // written value is mapping-neutral (over-flush is sound, §131 i).
        cpu.Execute(1);
        Assert.Equal(1UL, cpu.DecodeCacheFullFlushes);
        Assert.Equal(0, cpu.DecodeCacheEntries);

        cpu.Execute(1);
        Assert.True(cpu.Context.Halted);
        Assert.Equal(3UL, cpu.DecodeCacheMisses);
        Assert.Equal(0UL, cpu.DecodeCacheHits);
        Assert.Equal(1, cpu.DecodeCacheEntries);
        Assert.Equal(3u, cpu.Bus().Read32(PageTable));
    }

    [Fact]
    public void Cr3Write_AndInvlpg_FullFlush()
    {
        var code = new byte[]
        {
            0x90, // 0x1000: nop
            0xB8, 0x00, 0x40, 0x00, 0x00, // 0x1001: mov eax, 0x4000
            0x0F, 0x22, 0xD8, // 0x1006: mov cr3, eax
            0x0F, 0x01, 0x3D, 0x00, 0x10, 0x00, 0x00, // 0x1009: invlpg [0x1000]
            0xF4, // 0x1010: hlt
        };

        XboxCpu cpu = CpuHarness.Create(code);
        MapIdentity(cpu.Bus(), 0, 4);
        EnablePaging(cpu);
        cpu.DecodeCacheEnabled = true;

        cpu.Execute(3); // nop, mov eax, mov cr3
        Assert.Equal(1UL, cpu.DecodeCacheFullFlushes);
        Assert.Equal(0, cpu.DecodeCacheEntries);

        cpu.Execute(2); // invlpg, hlt
        Assert.Equal(2UL, cpu.DecodeCacheFullFlushes);
        Assert.True(cpu.Context.Halted);
        Assert.Equal(1, cpu.DecodeCacheEntries);
    }

    [Fact]
    public void CsReload_SameBaseKeepsTable_ChangedBaseFlushes()
    {
        XboxCpu cpu = CpuHarness.Create(
            0x90, // 0x1000: nop
            0xEA, 0x00, 0x10, 0x00, 0x00, 0x08, 0x00, // 0x1001: jmp far 0x08:0x1000 (same CS)
            0xEA, 0x00, 0x00, 0x00, 0x00, 0x18, 0x00); // 0x1008: jmp far 0x18:0 (base 0x2000)

        // Code descriptor at selector 0x18 with base 0x2000.
        cpu.Bus().Write32(0x7018, 0x2000FFFF);
        cpu.Bus().Write32(0x701C, 0x00CF9A00);
        cpu.Ram().Write8(0x2000, 0x90); // landing nop

        cpu.DecodeCacheEnabled = true;

        // Same-selector reload (every interrupt/iret does this): the table
        // stays warm — the third fetch of 0x1000 is a hit.
        cpu.Execute(3);
        Assert.Equal(1UL, cpu.DecodeCacheHits);
        Assert.Equal(0UL, cpu.DecodeCacheFullFlushes);
        Assert.Equal(2, cpu.DecodeCacheEntries);

        // Changed base: full flush at the jump, then the landing page refills.
        cpu.Context.Eip = 0x1008;
        cpu.Execute(1);
        Assert.Equal(1UL, cpu.DecodeCacheFullFlushes);
        Assert.Equal(0x2000u, cpu.Context.GetSegmentBase(SegmentRegister.Cs));
        Assert.Equal(0u, cpu.Context.Eip);
        Assert.Equal(0, cpu.DecodeCacheEntries);

        cpu.Execute(1);
        Assert.Equal(1, cpu.DecodeCacheEntries);
        Assert.Equal(1u, cpu.Context.Eip); // Eip is the offset; the fetch linear was 0x2000
        Assert.Equal(4UL, cpu.DecodeCacheMisses);
        Assert.Equal(1UL, cpu.DecodeCacheHits);
    }

    [Fact]
    public void SnapshotLoad_FullFlushes_RefillsAfterLoad()
    {
        XboxCpu cpu = CpuHarness.Create(NopLoop);
        MapIdentity(cpu.Bus(), 0, 4);
        EnablePaging(cpu);
        cpu.DecodeCacheEnabled = true;

        cpu.Execute(6);
        Assert.Equal(3, cpu.DecodeCacheEntries);
        Assert.Equal(3UL, cpu.DecodeCacheHits);

        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            cpu.SaveState(writer);
        }

        stream.Position = 0;
        using (var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true))
        {
            cpu.LoadState(reader);
        }

        // LoadState flushes via the Mmu.Flush choke point (tables are RAM
        // state; a stale decode would be a silent divergence).
        Assert.Equal(0, cpu.DecodeCacheEntries);

        cpu.Execute(3);
        Assert.Equal(6UL, cpu.DecodeCacheMisses);
        Assert.Equal(3UL, cpu.DecodeCacheHits);
        Assert.Equal(3, cpu.DecodeCacheEntries);
    }

    [Fact]
    public void CrossPageWindows_AreCached_NonRamWindows_AreNot()
    {
        // Cross-page: code straddling 0x2000 (D3 caches these entries with a
        // second physical base; both pages are RAM, so both fills land).
        XboxCpu cross = CpuHarness.Create();
        cross.Bus().Write8(0x1FFB, 0x90);
        cross.Bus().Write8(0x1FFC, 0xF4);
        MapIdentity(cross.Bus(), 0, 4);
        EnablePaging(cross);
        cross.Context.Eip = 0x1FFB;
        cross.DecodeCacheEnabled = true;
        cross.Execute(2);

        Assert.True(cross.Context.Halted);
        Assert.Equal(0UL, cross.DecodeCacheSkippedCrossPage);
        Assert.Equal(2UL, cross.DecodeCacheMisses);
        Assert.Equal(0UL, cross.DecodeCacheHits);
        Assert.Equal(2, cross.DecodeCacheEntries);

        // Non-RAM: the idle-skip host classifies the code page as MMIO
        // (flash/BIOS-style fetches are never cached, gotcha iii).
        XboxCpu mmio = CpuHarness.Create(StraightLine);
        var host = new StubIdleHost();
        host.NonRam.Add(CpuHarness.CodeAddress);
        mmio.IdleSkipHost = host;
        mmio.DecodeCacheEnabled = true;
        mmio.Execute(4);

        Assert.True(mmio.Context.Halted);
        Assert.Equal(4UL, mmio.DecodeCacheSkippedNonRam);
        Assert.Equal(4UL, mmio.DecodeCacheMisses);
        Assert.Equal(0, mmio.DecodeCacheEntries);
    }

    [Fact]
    public void CrossPageEntries_ServeFromCache_AndDropOnSecondPageStore()
    {
        // Loop body in the 0x1FF5..0x1FFF boundary zone: every instruction
        // there has a 15-byte window that crosses into page 0x2000.
        XboxCpu cpu = CpuHarness.Create();
        MapIdentity(cpu.Bus(), 0, 16);
        EnablePaging(cpu);
        PhysicalMemory ram = cpu.Ram();
        ram.Write8(0x1FF0, 0xB9); // mov ecx,3
        ram.Write8(0x1FF1, 0x03);
        ram.Write8(0x1FF5, 0x49); // dec ecx
        for (uint i = 0x1FF6; i <= 0x1FFF; i++)
        {
            ram.Write8(i, 0x90); // nops
        }

        ram.Write8(0x2000, 0x75); // jne 0x1FF5
        ram.Write8(0x2001, 0xF3);
        ram.Write8(0x2002, 0xF4); // hlt
        cpu.Context.Eip = 0x1FF0;
        cpu.DecodeCacheEnabled = true;

        XboxCpu off = CpuHarness.Create();
        MapIdentity(off.Bus(), 0, 16);
        EnablePaging(off);
        PhysicalMemory offRam = off.Ram();
        for (uint i = 0x1FF0; i <= 0x2002; i++)
        {
            offRam.Write8(i, ram.Read8(i));
        }

        off.Context.Eip = 0x1FF0;

        cpu.Execute(200);
        off.Execute(200);
        Assert.True(cpu.Context.Halted);
        Assert.Equal(off.Context.Eip, cpu.Context.Eip);
        Assert.Equal(off.Context.Ecx, cpu.Context.Ecx);
        Assert.Equal(0UL, cpu.DecodeCacheSkippedCrossPage);
        Assert.True(cpu.DecodeCacheHits > 0, $"hits={cpu.DecodeCacheHits}");
        Assert.True(cpu.DecodeCacheEntries > 0, $"entries={cpu.DecodeCacheEntries}");
        int entriesAfterLoop = cpu.DecodeCacheEntries;

        // A data store into the second page (0x2000) must drop the cross-page
        // entries through the reverse-map registration of both pages.
        cpu.Context.Halted = false;
        cpu.Context.Eip = 0x3000;
        ram.Write8(0x3000, 0xA3); // mov [0x2F00],eax
        ram.Write8(0x3001, 0x00);
        ram.Write8(0x3002, 0x2F);
        ram.Write8(0x3003, 0x00);
        ram.Write8(0x3004, 0x00);
        ram.Write8(0x3005, 0xF4); // hlt
        cpu.Execute(3);

        Assert.True(cpu.Context.Halted);
        Assert.True(cpu.DecodeCacheEntries < entriesAfterLoop,
            $"entries after store={cpu.DecodeCacheEntries} (before={entriesAfterLoop})");
    }

    [Fact]
    public void FaultPrecision_Vector14_MatchesFlagOff()
    {
        (XboxCpu off, _) = CreateFaulting(false);
        off.Step();

        (XboxCpu on, _) = CreateFaulting(true);
        on.Step();

        // The unmapped code page faults on the miss-path fetch with the same
        // vector/EIP/error code as the flag-off run; nothing was filled.
        Assert.Equal(14, on.LastFaultVector);
        Assert.Equal(off.LastFaultVector, on.LastFaultVector);
        Assert.Equal(0x1000u, on.LastFaultEip);
        Assert.Equal(off.LastFaultEip, on.LastFaultEip);
        Assert.Equal(off.LastFaultErrorCode, on.LastFaultErrorCode);
        Assert.Equal(off.Context.Cr2, on.Context.Cr2);
        Assert.Equal(0x1000u, on.Context.Cr2);
        Assert.Equal(off.Context.Eip, on.Context.Eip);
        Assert.Equal(0, on.DecodeCacheEntries);

        // The faulting fetch records no miss: the signal unwinds before the
        // miss is counted (the flag-off run likewise records nothing).
        Assert.Equal(0UL, on.DecodeCacheMisses);
        Assert.Equal(0UL, on.DecodeCacheHits);
        return;

        static (XboxCpu Cpu, HotspotProfiler Profiler) CreateFaulting(bool cache)
        {
            XboxCpu cpu = CpuHarness.Create(StraightLine);
            MapIdentity(cpu.Bus(), 0, 1);
            MapIdentity(cpu.Bus(), 6, 1);
            MapIdentity(cpu.Bus(), 7, 1);
            MapIdentity(cpu.Bus(), 8, 1);
            EnablePaging(cpu);
            InstallIdt(cpu, 14, 0x3000);
            cpu.DecodeCacheEnabled = cache;
            var profiler = new HotspotProfiler();
            cpu.HotspotObserver = profiler;
            return (cpu, profiler);
        }
    }

    [Fact]
    public void UdMarker_CachedWithExactEip()
    {
        // 0x62 (BOUND) decodes invalid in this core: vector 6 with the exact
        // EIP, from the cache on the re-execution after the IRET handler.
        XboxCpu cpu = CpuHarness.Create(0x62, 0x00, 0x00);
        cpu.Ram().Write8(0x3000, 0xCF); // IRET handler
        InstallIdt(cpu, 6, 0x3000);
        cpu.DecodeCacheEnabled = true;

        cpu.Step(); // #UD (miss + fill of the invalid marker) -> handler
        Assert.Equal(6, cpu.LastFaultVector);
        Assert.Equal(0x1000u, cpu.LastFaultEip);
        Assert.Equal(0x3000u, cpu.Context.Eip);

        cpu.Step(); // IRET back to 0x1000
        Assert.Equal(0x1000u, cpu.Context.Eip);

        cpu.Step(); // #UD again — served from the cached invalid marker
        Assert.Equal(6, cpu.LastFaultVector);
        Assert.Equal(0x1000u, cpu.LastFaultEip);
        Assert.Equal(1UL, cpu.DecodeCacheHits);
        Assert.Equal(2UL, cpu.DecodeCacheMisses);
    }

    [Fact]
    public void IdleSkip_VerdictsExact_WithCache()
    {
        (XboxCpu off, PollIdleHost offHost) = CreateIdle(false);
        off.Execute(1_000_000);

        (XboxCpu on, PollIdleHost onHost) = CreateIdle(true);
        on.Execute(1_000_000);

        // Hits replay the per-byte fetch NoteAccess calls (gotcha iii), so
        // every idle-skip verdict matches the flag-off run exactly.
        Assert.True(on.Context.Halted);
        Assert.True(onHost.Advanced > 0);
        Assert.Equal(offHost.Advanced, onHost.Advanced);
        Assert.Equal(off.IdleSkipSkippedSteps, on.IdleSkipSkippedSteps);
        Assert.Equal(off.IdleSkipTriggers, on.IdleSkipTriggers);
        Assert.Equal(off.IdleSkipStopWatch, on.IdleSkipStopWatch);
        Assert.Equal(off.IdleSkipIterations, on.IdleSkipIterations);
        Assert.Equal(off.Context.Eip, on.Context.Eip);
        Assert.Equal(off.Context.Tsc, on.Context.Tsc);
        Assert.True(on.DecodeCacheHits > 0);
        return;

        static (XboxCpu Cpu, PollIdleHost Host) CreateIdle(bool cache)
        {
            XboxCpu cpu = CpuHarness.Create(PollLoop);
            MapIdentity(cpu.Bus(), 0, 4);
            EnablePaging(cpu);
            cpu.Context.Eflags |= FlagBits.If;
            var host = new PollIdleHost(cpu.Ram()) { PollAddress = 0x2000, InitialDeadline = 1000 };
            cpu.IdleSkipHost = host;
            cpu.IdleSkipEnabled = true;
            cpu.IdleSkipMinIterations = 4;
            cpu.DecodeCacheEnabled = cache;
            return (cpu, host);
        }
    }

    [Fact]
    public void VerifyMode_TripwiresHostBypassedWrite()
    {
        XboxCpu cpu = CpuHarness.Create(StraightLine);
        cpu.DecodeCacheEnabled = true;
        cpu.DecodeCacheVerify = true;

        cpu.Execute(4);
        Assert.Equal(4, cpu.DecodeCacheEntries);

        // Host-side write that bypasses every invalidation hook (simulated
        // missed writer): hlt at 0x1007 becomes nop.
        cpu.Ram().Write8(0x1007, 0x90);
        cpu.Context.Halted = false;
        cpu.Context.Eip = 0x1000;

        cpu.Execute(4);

        // The stored source window is the full 15-byte fetch window, and this
        // snippet is only 8 bytes long, so every window covers 0x1007: all
        // four entries trip the verify, re-decode, and re-fill (self-heal).
        // The modified 0x1007 decodes to nop, not hlt.
        Assert.Equal(4UL, cpu.DecodeCacheVerifyFailures);
        Assert.Equal(0UL, cpu.DecodeCacheHits);
        Assert.Equal(8UL, cpu.DecodeCacheMisses);
        Assert.Equal(0x1008u, cpu.Context.Eip);
        Assert.False(cpu.Context.Halted);
    }

    [Fact]
    public void EntryCap_FlushesAtMaxEntries_Bounded()
    {
        var memory = new PhysicalMemory(0x40000);
        var bus = new AddressSpace(memory, UnmappedAccessPolicy.Throw);
        var cpu = new XboxCpu(bus);
        cpu.Reset();
        cpu.Context.Cr0 = 1;
        cpu.Context.CodeSegment32 = true;
        cpu.Context.SetSegment(SegmentRegister.Cs, 0x08, 0);
        cpu.Context.SetSegment(SegmentRegister.Ss, 0x10, 0);
        cpu.Context.Eflags = FlagBits.Reserved1;
        cpu.Context.Esp = 0x3F000;

        // 196608 nops (0x1000..0x30FFF) then hlt (0x31000). Fetch windows
        // within 14 bytes of a page boundary cross it; since D3 those windows
        // are cached too (both pages are RAM here), so every step fills.
        memory.GetSpan(0x1000, 0x30000).Fill(0x90);
        memory.Write8(0x31000, 0xF4);
        cpu.Context.Eip = 0x1000;
        cpu.DecodeCacheEnabled = true;

        int executed = cpu.Execute(0x30001);

        Assert.True(cpu.Context.Halted);
        Assert.Equal(0x30001, executed);

        // The linear nop stream never revisits an address, so every step
        // misses; the fill that would exceed the cap flushes the table once.
        Assert.Equal(0UL, cpu.DecodeCacheHits);
        Assert.Equal((ulong)executed, cpu.DecodeCacheMisses);
        Assert.Equal(1UL, cpu.DecodeCacheFullFlushes);
        Assert.Equal(DecodeCache.MaxEntries, cpu.DecodeCachePeakEntries);
        Assert.True(cpu.DecodeCacheEntries > 0);
        Assert.True(cpu.DecodeCacheEntries < DecodeCache.MaxEntries);
        Assert.Equal(0UL, cpu.DecodeCacheSkippedCrossPage);
    }

    [Fact]
    public void HostFlushApis_DropPageAndFull()
    {
        XboxCpu cpu = CpuHarness.Create(NopLoop);
        cpu.DecodeCacheEnabled = true;
        cpu.Execute(3);
        Assert.Equal(3, cpu.DecodeCacheEntries);

        // Page-drop API (device-shim route): all three entries share page 0.
        cpu.InvalidateDecodeCachePage(0x1001);
        Assert.Equal(0, cpu.DecodeCacheEntries);

        cpu.Execute(3);
        Assert.Equal(3, cpu.DecodeCacheEntries);

        cpu.InvalidateDecodeCache();
        Assert.Equal(0, cpu.DecodeCacheEntries);
        Assert.Equal(1UL, cpu.DecodeCacheFullFlushes);
    }

    private static XboxCpu CreateFlat(params byte[] code)
    {
        XboxCpu cpu = CpuHarness.Create(code);
        return cpu;
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

    private sealed class StubIdleHost : IIdleSkipHost
    {
        public HashSet<uint> NonRam { get; } = new();

        public bool IrqPendingAndEnabled => false;

        public long? StepsUntilNextDeadline => null;

        public bool IsRam(uint physicalAddress)
        {
            return !NonRam.Contains(physicalAddress & 0xFFFFF000u);
        }

        public void AdvanceIdle(ulong steps)
        {
        }
    }

    /// <summary>
    /// Fake host with one scheduled event: a deadline in cycles from skip
    /// start that fires the poll write when the advance reaches it (the
    /// IdleSkipTests shape, trimmed to what the verdict test needs).
    /// </summary>
    private sealed class PollIdleHost : IIdleSkipHost
    {
        private readonly PhysicalMemory _ram;
        private long? _deadline;

        public PollIdleHost(PhysicalMemory ram)
        {
            _ram = ram;
        }

        public ulong Advanced { get; private set; }

        public long? InitialDeadline { get; set; }

        public uint? PollAddress { get; set; }

        private byte PollValue { get; set; } = 1;

        private HashSet<uint> NonRam { get; } = new();

        public long? StepsUntilNextDeadline => _deadline ??= InitialDeadline;

        public bool IrqPendingAndEnabled => false;

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
                    if (PollAddress is { } poll)
                    {
                        _ram.Write8(poll, PollValue);
                    }
                }
            }
        }
    }
}
