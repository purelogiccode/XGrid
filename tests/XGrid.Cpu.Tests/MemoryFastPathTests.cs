using System;
using System.Collections.Generic;
using XGrid.Memory;
using Xunit;

namespace XGrid.Cpu.Tests;

// Memory-path slice 1 (§161): the RAM-direct fast path + wide-access
// single-translate fusions must be bit-exact with the legacy bus path.
// These tests pin the exactness boundaries: RAM round trips (fast active),
// page-crossing (fallback), MMIO physical (fallback with identical device
// traffic), the write-watch guard, and the observer gate (fusions off when
// profiled so idle/TLB tallies never drift).
public sealed class MemoryFastPathTests
{
    private const uint PageDirectory = 0x4000;
    private const uint PageTable = 0x5000;

    private static void Map4K(AddressSpace bus, uint virtualAddress, uint physicalAddress, bool writable = true)
    {
        uint pdeAddress = PageDirectory + ((virtualAddress >> 22) & 0x3FF) * 4;
        if ((bus.Read32(pdeAddress) & 1u) == 0)
        {
            bus.Write32(pdeAddress, PageTable | 3u);
        }

        uint pteAddress = PageTable + ((virtualAddress >> 12) & 0x3FF) * 4;
        bus.Write32(pteAddress, (physicalAddress & 0xFFFFF000u) | 1u | (writable ? 2u : 0u));
    }

    private static void EnablePaging(XboxCpu cpu)
    {
        cpu.Context.Cr3 = PageDirectory;
        cpu.Context.Cr0 = ControlRegisterBits.Cr0ProtectedMode | ControlRegisterBits.Cr0Paging;
        cpu.Mmu.Flush();
    }

    private static XboxCpu CreatePaged(params byte[] code)
    {
        XboxCpu cpu = CpuHarness.Create(code);
        Map4K(cpu.Bus(), 0x1000, 0x1000);
        Map4K(cpu.Bus(), 0x2000, 0x2000);
        Map4K(cpu.Bus(), 0x3000, 0x3000);
        Map4K(cpu.Bus(), 0x8000, 0x8000);
        EnablePaging(cpu);
        cpu.Context.Ebx = 0x2000;
        return cpu;
    }

    [Fact]
    public void Ram32_RoundTrip_PagingOn()
    {
        XboxCpu cpu = CreatePaged(
            0xB8, 0x78, 0x56, 0x34, 0x12, // mov eax,0x12345678
            0x89, 0x03, // mov [ebx],eax
            0xB8, 0x00, 0x00, 0x00, 0x00, // mov eax,0
            0x8B, 0x03); // mov eax,[ebx]
        cpu.Execute(4);

        Assert.Equal(0x12345678u, cpu.Context.Eax);
        Assert.Equal(0x12345678u, cpu.Bus().Read32(0x2000));
    }

    [Fact]
    public void Xmm128_RoundTrip_PagingOn()
    {
        XboxCpu cpu = CreatePaged(
            0x0F, 0x10, 0x03, // movups xmm0,[ebx]
            0x0F, 0x11, 0x43, 0x10); // movups [ebx+0x10],xmm0
        cpu.Bus().Write32(0x2000, 0x11111111);
        cpu.Bus().Write32(0x2004, 0x22222222);
        cpu.Bus().Write32(0x2008, 0x33333333);
        cpu.Bus().Write32(0x200C, 0x44444444);
        cpu.Execute(2);

        Assert.Equal(0x11111111u, cpu.Bus().Read32(0x2010));
        Assert.Equal(0x22222222u, cpu.Bus().Read32(0x2014));
        Assert.Equal(0x33333333u, cpu.Bus().Read32(0x2018));
        Assert.Equal(0x44444444u, cpu.Bus().Read32(0x201C));
    }

    [Fact]
    public void Xmm128_PageCross_FallsBackExact()
    {
        XboxCpu cpu = CreatePaged(
            0x0F, 0x10, 0x03, // movups xmm0,[ebx]
            0x0F, 0x11, 0x43, 0x10); // movups [ebx+0x10],xmm0
        cpu.Context.Ebx = 0x2FF8;
        cpu.Bus().Write32(0x2FF8, 0xAAAAAAAAu);
        cpu.Bus().Write32(0x2FFC, 0xBBBBBBBu);
        cpu.Bus().Write32(0x3000, 0xCCCCCCCCu);
        cpu.Bus().Write32(0x3004, 0xDDDDDDDDu);
        cpu.Execute(2);

        // Store lands at 0x3008..0x3017 (also crossing) — byte-exact.
        Assert.Equal(0xAAAAAAAAu, cpu.Bus().Read32(0x3008));
        Assert.Equal(0xBBBBBBBu, cpu.Bus().Read32(0x300C));
        Assert.Equal(0xCCCCCCCCu, cpu.Bus().Read32(0x3010));
        Assert.Equal(0xDDDDDDDDu, cpu.Bus().Read32(0x3014));
    }

    [Fact]
    public void Xmm128_MmioPhysical_FallsBackWithIdenticalTraffic()
    {
        XboxCpu cpu = CreatePaged(
            0x0F, 0x10, 0x03, // movups xmm0,[ebx]
            0x0F, 0x11, 0x03); // movups [ebx],xmm0
        var device = new CountingMmioDevice();
        cpu.Bus().AddRegion("test", 0xF0000000, 0x1000, device);
        Map4K(cpu.Bus(), 0x2000, 0xF0000000);
        cpu.Execute(2);

        // Legacy shape: two 64-bit halves = four 32-bit device accesses each way.
        Assert.Equal(4, device.Reads.Count);
        Assert.Equal(4, device.Writes.Count);
        for (int i = 0; i < 4; i++)
        {
            Assert.Equal(0xF0000000u + (uint)(i * 4), device.Reads[i].Address);
            Assert.Equal(4, device.Reads[i].Size);
            Assert.Equal(0xF0000000u + (uint)(i * 4), device.Writes[i].Address);
            Assert.Equal(4, device.Writes[i].Size);
        }
    }

    [Fact]
    public void Fpu80_RoundTrip_PagingOn()
    {
        XboxCpu cpu = CreatePaged(
            0xD9, 0xE8, // fld1
            0xDB, 0x3B, // fstp m80 [ebx]
            0xDB, 0x2B, // fld m80 [ebx]
            0xDD, 0x5B, 0x10); // fstp m64 [ebx+0x10]
        cpu.Execute(4);

        double value = BitConverter.Int64BitsToDouble((long)cpu.Bus().Read64(0x2010));
        Assert.Equal(1.0, value);
    }

    [Fact]
    public void Fpu80_PageCross_FallsBackExact()
    {
        XboxCpu cpu = CreatePaged(
            0xD9, 0xE8, // fld1
            0xDB, 0x3B, // fstp m80 [ebx]
            0xDB, 0x2B, // fld m80 [ebx]
            0xDD, 0x5B, 0x10); // fstp m64 [ebx+0x10]
        cpu.Context.Ebx = 0x2FFB; // 10-byte m80 crosses into 0x3000
        cpu.Execute(4);

        double value = BitConverter.Int64BitsToDouble((long)cpu.Bus().Read64(0x300B));
        Assert.Equal(1.0, value);
    }

    [Fact]
    public void WriteWatch_StillFiresThroughFastPath()
    {
        XboxCpu cpu = CpuHarness.Create(0x89, 0x03); // mov [ebx],eax
        cpu.Context.Eax = 0xA5A5A5A5;
        cpu.Context.Ebx = 0x2000;
        var hits = new List<(uint Address, int Size, uint Value)>();
        cpu.Bus().WatchStart = 0x2000;
        cpu.Bus().WatchEnd = 0x2004;
        cpu.Bus().WatchHit += (address, size, value) => hits.Add((address, size, value));
        cpu.Step();

        Assert.Single(hits);
        Assert.Equal(0x2000u, hits[0].Address);
        Assert.Equal(4, hits[0].Size);
        Assert.Equal(0xA5A5A5A5u, hits[0].Value);
        Assert.Equal(0xA5A5A5A5u, cpu.Bus().Read32(0x2000));
    }

    [Fact]
    public void ObserverAttached_WideAccessKeepsTwoTranslates()
    {
        // Gate pin: with a profiler attached the XMM128 load must take the
        // legacy two-translate path (1 miss + 1 hit on a cold TLB), so idle
        // and TLB tallies never drift. Fetch uses batched (plural) notes;
        // only singular data-translate notes are counted here.
        XboxCpu cpu = CreatePaged(0x0F, 0x10, 0x03); // movups xmm0,[ebx]
        var counter = new TlbCounter();
        cpu.HotspotObserver = counter;
        cpu.Mmu.Flush();
        cpu.Step();

        Assert.Equal(2, counter.SingularTranslates);
    }

    private sealed class CountingMmioDevice : IMmioDevice
    {
        public readonly List<(uint Address, int Size)> Reads = [];
        public readonly List<(uint Address, int Size)> Writes = [];

        public uint Read(uint address, int size)
        {
            Reads.Add((address, size));
            return 0x12345678;
        }

        public void Write(uint address, int size, uint value)
        {
            Writes.Add((address, size));
        }
    }

    private sealed class TlbCounter : IHotspotObserver
    {
        public int SingularTranslates;

        public void NoteTlbHit()
        {
            SingularTranslates++;
        }

        public void NoteTlbMiss()
        {
            SingularTranslates++;
        }

        public void Observe(in DecodedInstruction instruction, uint eipBefore, uint eipAfter)
        {
        }

        public void NoteFetch(bool crossesPage)
        {
        }

        public void NoteTlbHits(int count)
        {
        }

        public void NoteTlbMisses(int count)
        {
        }

        public void NoteDataAccess(bool isWrite, bool isRam)
        {
        }

        public void NoteSplitAccess()
        {
        }

        public void NoteFetchSlow()
        {
        }

        public void NoteDecodeCacheHit()
        {
        }

        public void NoteDecodeCacheMiss()
        {
        }
    }
}
