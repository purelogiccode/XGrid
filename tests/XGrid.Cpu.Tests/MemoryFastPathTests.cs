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

    [Fact]
    public void BytesBulk_FxsaveFxrstor_RoundTrip_PagingOn()
    {
        XboxCpu cpu = CreatePaged(
            0x0F, 0xAE, 0x03, // fxsave [ebx]
            0x0F, 0xAE, 0x0B); // fxrstor [ebx]
        cpu.Context.Fpu.SetRegister(0, Fpu80.One);
        cpu.Context.WriteXmm128(0, 0x1122334455667788UL, 0x99AABBCCDDEEFF00UL);
        cpu.Context.Mxcsr = 0x5F80;
        cpu.Execute(1);

        // The 512 B image landed via the bulk path: image magic intact.
        Assert.Equal(0x0000FFBFu, cpu.Bus().Read32(0x2000 + 28));

        cpu.Context.Fpu.Init();
        cpu.Context.WriteXmm128(0, 0, 0);
        cpu.Context.Mxcsr = 0x1F80;
        cpu.Execute(1);

        Assert.Equal(Fpu80.One, cpu.Context.Fpu.GetRegister(0));
        Assert.Equal((0x1122334455667788UL, 0x99AABBCCDDEEFF00UL), cpu.Context.ReadXmm128(0));
        Assert.Equal(0x5F80u, cpu.Context.Mxcsr);
    }

    [Fact]
    public void BytesBulk_PageCross_MultiChunkExact()
    {
        // 512 B at 0x2F00 spans 0x2F00..0x30FF: two page chunks, both RAM.
        XboxCpu cpu = CreatePaged(
            0x0F, 0xAE, 0x03, // fxsave [ebx]
            0x0F, 0xAE, 0x0B); // fxrstor [ebx]
        cpu.Context.Ebx = 0x2F00;
        cpu.Context.Fpu.SetRegister(0, Fpu80.One);
        cpu.Context.WriteXmm128(3, 0xA5A5A5A5A5A5A5A5UL, 0x5A5A5A5A5A5A5A5AUL);
        cpu.Execute(1);

        Assert.Equal(0x0000FFBFu, cpu.Bus().Read32(0x2F00 + 28));

        cpu.Context.Fpu.Init();
        cpu.Context.WriteXmm128(3, 0, 0);
        cpu.Execute(1);

        Assert.Equal(Fpu80.One, cpu.Context.Fpu.GetRegister(0));
        Assert.Equal((0xA5A5A5A5A5A5A5A5UL, 0x5A5A5A5A5A5A5A5AUL), cpu.Context.ReadXmm128(3));
    }

    [Fact]
    public void BytesBulk_MmioPhysical_FallsBackWithIdenticalTraffic()
    {
        XboxCpu cpu = CreatePaged(
            0x0F, 0xAE, 0x03, // fxsave [ebx]
            0x0F, 0xAE, 0x0B); // fxrstor [ebx]
        var device = new CountingMmioDevice();
        cpu.Bus().AddRegion("test", 0xF0000000, 0x1000, device);
        Map4K(cpu.Bus(), 0x2000, 0xF0000000);
        cpu.Execute(2);

        // Legacy shape: 512 single-byte device accesses each way.
        Assert.Equal(512, device.Writes.Count);
        Assert.Equal(512, device.Reads.Count);
        for (int i = 0; i < 512; i++)
        {
            Assert.Equal(0xF0000000u + (uint)i, device.Writes[i].Address);
            Assert.Equal(1, device.Writes[i].Size);
            Assert.Equal(0xF0000000u + (uint)i, device.Reads[i].Address);
            Assert.Equal(1, device.Reads[i].Size);
        }
    }

    [Fact]
    public void BytesBulk_WriteWatch_StillFiresPerByte()
    {
        XboxCpu cpu = CpuHarness.Create(0x0F, 0xAE, 0x03); // fxsave [ebx]
        cpu.Context.Ebx = 0x2000;
        var hits = new List<(uint Address, int Size, uint Value)>();
        cpu.Bus().WatchStart = 0x2000;
        cpu.Bus().WatchEnd = 0x2200;
        cpu.Bus().WatchHit += (address, size, value) => hits.Add((address, size, value));
        cpu.Step();

        Assert.Equal(512, hits.Count);
        Assert.Equal(0x2000u, hits[0].Address);
        Assert.Equal(0x21FFu, hits[511].Address);
        Assert.All(hits, h => Assert.Equal(1, h.Size));
    }

    [Fact]
    public void ObserverAttached_BytesKeepsPerByteTranslates()
    {
        // Gate pin: with a profiler attached the 512 B fxrstor must take the
        // legacy per-byte path (512 singular data-translate notes on a cold
        // TLB), so idle and TLB tallies never drift.
        XboxCpu cpu = CreatePaged(0x0F, 0xAE, 0x0B); // fxrstor [ebx]
        var counter = new TlbCounter();
        cpu.HotspotObserver = counter;
        cpu.Mmu.Flush();
        cpu.Step();

        Assert.Equal(512, counter.SingularTranslates);
    }

    [Fact]
    public void DataXlat_GuestPteStorePlusInvlpg_ObservesNewMapping()
    {
        // Combined invalidation gate: a guest PTE rewrite through a CPU
        // store must clear the cached translation, so the post-INVLPG walk
        // observes the new mapping (stale either in the SoftTLB or the
        // slice-3 cache reads back 0x11111111).
        XboxCpu cpu = CreatePaged(
            0x8B, 0x03, // mov eax,[ebx] (populates vpn2 -> 0x2000)
            0xB8, 0x00, 0x20, 0x00, 0x00, // mov eax,0x2000
            0xC7, 0x05, 0x08, 0x50, 0x00, 0x00, 0x03, 0x30, 0x00, 0x00, // mov dword [0x5008],0x3003
            0x0F, 0x01, 0x38, // invlpg [eax]
            0x8B, 0x03); // mov eax,[ebx]
        Map4K(cpu.Bus(), 0x5000, 0x5000);
        cpu.Bus().Write32(0x2000, 0x11111111);
        cpu.Bus().Write32(0x3000, 0x22222222);
        cpu.Execute(5);

        Assert.Equal(0x22222222u, cpu.Context.ReadRegister(Gpr.Eax));
    }

    [Fact]
    public void DataXlat_WriteProtectFault_PreservedOnCachedHit()
    {
        // The hit path must replay the WP fault with the identical P=1/W=1
        // data error code: the read populates the (read-only) verdict, the
        // write must still trap to vector 14 with Cr2 + error code 3.
        XboxCpu cpu = CpuHarness.Create(
            0x8B, 0x03, // mov eax,[ebx]
            0x89, 0x03); // mov [ebx],eax
        Map4K(cpu.Bus(), 0x1000, 0x1000);
        Map4K(cpu.Bus(), 0x2000, 0x2000, writable: false);
        Map4K(cpu.Bus(), 0x3000, 0x3000);
        Map4K(cpu.Bus(), 0x6000, 0x6000);
        Map4K(cpu.Bus(), 0x7000, 0x7000); // fault-frame stack page (ESP=0x8000 pushes into 0x7xxx)
        Map4K(cpu.Bus(), 0x8000, 0x8000);
        InstallIdt(cpu);
        cpu.Context.Cr3 = PageDirectory;
        cpu.Context.Cr0 = ControlRegisterBits.Cr0ProtectedMode | ControlRegisterBits.Cr0Paging
            | ControlRegisterBits.Cr0WriteProtect;
        cpu.Mmu.Flush();
        cpu.Context.Ebx = 0x2000;
        cpu.Bus().Write32(0x2000, 0xAAAAAAAAu);
        cpu.Execute(2);

        Assert.Equal(0x3000u, cpu.Context.Eip);
        Assert.Equal(0x2000u, cpu.Context.Cr2);
        Assert.Equal(3u, cpu.Bus().Read32(0x7FF0));
    }

    [Fact]
    public void ObserverAttached_DataAccessKeepsTranslateNotes()
    {
        // Gate pin: with a profiler attached the slice-3 cache stays off, so
        // two cold stores report two singular data-translate notes (1 miss +
        // 1 hit), never collapsing to one.
        XboxCpu cpu = CreatePaged(0x89, 0x03, 0x89, 0x03); // mov [ebx],eax x2
        var counter = new TlbCounter();
        cpu.HotspotObserver = counter;
        cpu.Mmu.Flush();
        cpu.Execute(2);

        Assert.Equal(2, counter.SingularTranslates);
    }

    [Fact]
    public void RepMovsd_RamToRam_ExactWithRegisterCommit()
    {
        XboxCpu cpu = CreatePaged(0xF3, 0xA5); // rep movsd
        for (uint i = 0; i < 8; i++)
        {
            cpu.Bus().Write32(0x2000 + i * 4, 0x11111111u * (i + 1));
        }

        cpu.Context.Esi = 0x2000;
        cpu.Context.Edi = 0x3000;
        cpu.Context.Ecx = 8;
        cpu.Execute(1);

        for (uint i = 0; i < 8; i++)
        {
            Assert.Equal(0x11111111u * (i + 1), cpu.Bus().Read32(0x3000 + i * 4));
        }

        Assert.Equal(0x2020u, cpu.Context.Esi);
        Assert.Equal(0x3020u, cpu.Context.Edi);
        Assert.Equal(0u, cpu.Context.Ecx);
    }

    [Fact]
    public void RepMovsb_MultiPage_ForwardExact()
    {
        XboxCpu cpu = CreatePaged(0xF3, 0xA4); // rep movsb
        for (uint i = 0; i < 128; i++)
        {
            cpu.Bus().Write8(0x2FC0 + i, (byte)(i + 1));
        }

        cpu.Context.Esi = 0x2FC0; // spans 0x2FC0..0x303F (page cross)
        cpu.Context.Edi = 0x2080;
        cpu.Context.Ecx = 128;
        cpu.Execute(1);

        for (uint i = 0; i < 128; i++)
        {
            Assert.Equal((byte)(i + 1), cpu.Bus().Read8(0x2080 + i));
        }

        Assert.Equal(0x3040u, cpu.Context.Esi);
        Assert.Equal(0x2100u, cpu.Context.Edi);
        Assert.Equal(0u, cpu.Context.Ecx);
    }

    [Fact]
    public void RepMovsd_Backward_Df1_Exact()
    {
        XboxCpu cpu = CreatePaged(0xF3, 0xA5); // rep movsd
        for (uint i = 0; i < 4; i++)
        {
            cpu.Bus().Write32(0x20F0 + i * 4, 0xA0u + i);
        }

        cpu.Context.Esi = 0x20FC;
        cpu.Context.Edi = 0x30FC;
        cpu.Context.Ecx = 4;
        cpu.Context.Eflags |= FlagBits.Df;
        cpu.Execute(1);

        for (uint i = 0; i < 4; i++)
        {
            Assert.Equal(0xA0u + i, cpu.Bus().Read32(0x30F0 + i * 4));
        }

        Assert.Equal(0x20ECu, cpu.Context.Esi);
        Assert.Equal(0x30ECu, cpu.Context.Edi);
        Assert.Equal(0u, cpu.Context.Ecx);
    }

    [Fact]
    public void RepMovsd_OverlappingForward_MemmoveOrder()
    {
        XboxCpu cpu = CreatePaged(0xF3, 0xA5); // rep movsd
        for (uint i = 0; i < 12; i++)
        {
            cpu.Bus().Write32(0x2000 + i * 4, i);
        }

        cpu.Context.Esi = 0x2000;
        cpu.Context.Edi = 0x2008; // overlaps src (forward memmove)
        cpu.Context.Ecx = 8;
        cpu.Execute(1);

        // x86 rep movs is iterative, NOT memmove: elements re-read
        // already-overwritten bytes (a naive span copy would yield 0..7 and
        // fail here — this pins the order-exactness).
        for (uint i = 0; i < 8; i++)
        {
            Assert.Equal(i & 1u, cpu.Bus().Read32(0x2008 + i * 4));
        }
    }

    [Fact]
    public void RepStosd_Fill_Exact()
    {
        XboxCpu cpu = CreatePaged(0xF3, 0xAB); // rep stosd
        cpu.Context.Esi = 0x12345678; // stos never touches ESI (regression pin)
        cpu.Context.Edi = 0x2000;
        cpu.Context.Eax = 0xDEADBEEFu;
        cpu.Context.Ecx = 16;
        cpu.Execute(1);

        for (uint i = 0; i < 16; i++)
        {
            Assert.Equal(0xDEADBEEFu, cpu.Bus().Read32(0x2000 + i * 4));
        }

        Assert.Equal(0x2040u, cpu.Context.Edi);
        Assert.Equal(0u, cpu.Context.Ecx);
        Assert.Equal(0x12345678u, cpu.Context.Esi);
    }

    [Fact]
    public void RepStosd_ZeroCount_IsNoOp()
    {
        XboxCpu cpu = CreatePaged(0xF3, 0xAB); // rep stosd
        cpu.Bus().Write32(0x2000, 0x12345678);
        cpu.Context.Edi = 0x2000;
        cpu.Context.Eax = 0xDEADBEEFu;
        cpu.Context.Ecx = 0;
        cpu.Execute(1);

        Assert.Equal(0x12345678u, cpu.Bus().Read32(0x2000));
        Assert.Equal(0x2000u, cpu.Context.Edi);
    }

    [Fact]
    public void RepMovsd_MmioDst_FallsBackWithIdenticalTraffic()
    {
        XboxCpu cpu = CreatePaged(0xF3, 0xA5); // rep movsd
        var device = new CountingMmioDevice();
        cpu.Bus().AddRegion("test", 0xF0000000, 0x1000, device);
        Map4K(cpu.Bus(), 0x3000, 0xF0000000);
        cpu.Bus().Write32(0x2000, 0xAAAAAAAAu);
        cpu.Bus().Write32(0x2004, 0xBBBBBBBu);
        cpu.Context.Esi = 0x2000;
        cpu.Context.Edi = 0x3000;
        cpu.Context.Ecx = 2;
        cpu.Execute(1);

        // Legacy shape: one 32-bit device write per element.
        Assert.Empty(device.Reads);
        Assert.Equal(2, device.Writes.Count);
        Assert.Equal(0xF0000000u, device.Writes[0].Address);
        Assert.Equal(0xF0000004u, device.Writes[1].Address);
        Assert.All(device.Writes, w => Assert.Equal(4, w.Size));
        Assert.Equal(0u, cpu.Context.Ecx);
    }

    [Fact]
    public void RepStosd_WriteWatch_StillFiresPerElement()
    {
        XboxCpu cpu = CpuHarness.Create(0xF3, 0xAB); // rep stosd
        cpu.Context.Edi = 0x2000;
        cpu.Context.Eax = 0xDEADBEEFu;
        cpu.Context.Ecx = 8;
        var hits = new List<(uint Address, int Size, uint Value)>();
        cpu.Bus().WatchStart = 0x2000;
        cpu.Bus().WatchEnd = 0x2020;
        cpu.Bus().WatchHit += (address, size, value) => hits.Add((address, size, value));
        cpu.Step();

        Assert.Equal(8, hits.Count);
        Assert.Equal(0x2000u, hits[0].Address);
        Assert.Equal(0x201Cu, hits[7].Address);
        Assert.All(hits, h => Assert.Equal(4, h.Size));
    }

    [Fact]
    public void RepMovsd_Addr16Wrap_FallsBackExact()
    {
        XboxCpu cpu = CpuHarness.Create(0x67, 0xF3, 0xA5); // addr16 rep movsd
        Map4K(cpu.Bus(), 0x0000, 0x0000);
        Map4K(cpu.Bus(), 0xF000, 0xF000); // SI=0xFFFC lives in page 0xF000
        Map4K(cpu.Bus(), 0x1000, 0x1000);
        Map4K(cpu.Bus(), 0x2000, 0x2000);
        Map4K(cpu.Bus(), 0x3000, 0x3000);
        EnablePaging(cpu);
        cpu.Bus().Write32(0xFFFC, 0x11111111);
        cpu.Bus().Write32(0x0000, 0x22222222);
        cpu.Context.Esi = 0xFFFC; // 0xFFFC+8 wraps 64K -> legacy path
        cpu.Context.Edi = 0x3000;
        cpu.Context.Ecx = 2;
        cpu.Execute(1);

        Assert.Equal(0x11111111u, cpu.Bus().Read32(0x3000));
        Assert.Equal(0x22222222u, cpu.Bus().Read32(0x3004));
        Assert.Equal(0x0004u, cpu.Context.Esi); // two post-iteration advances with 64K wrap
        Assert.Equal(0x3008u, cpu.Context.Edi);
        Assert.Equal(0u, cpu.Context.Ecx);
    }

    [Fact]
    public void ObserverAttached_StringKeepsPerIterationNotes()
    {
        // Gate pin: with a profiler attached rep movsd x2 reports four
        // singular data-translate notes (read+write per iteration).
        XboxCpu cpu = CreatePaged(0xF3, 0xA5); // rep movsd
        cpu.Context.Esi = 0x2000;
        cpu.Context.Edi = 0x3000;
        cpu.Context.Ecx = 2;
        var counter = new TlbCounter();
        cpu.HotspotObserver = counter;
        cpu.Mmu.Flush();
        cpu.Execute(1);

        Assert.Equal(4, counter.SingularTranslates);
    }

    private static void InstallIdt(XboxCpu cpu)
    {
        uint gate = 0x6000 + 14 * 8;
        cpu.Bus().Write16(gate, 0x3000);
        cpu.Bus().Write16(gate + 2, 0x08);
        cpu.Bus().Write16(gate + 4, 0x8E00);
        cpu.Bus().Write16(gate + 6, 0x0000);
        cpu.Context.Idtr.Base = 0x6000;
        cpu.Context.Idtr.Limit = 0x7FF;
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
