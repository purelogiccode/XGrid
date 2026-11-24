using System.Diagnostics;
using XGrid.Memory;
using Xunit;
using Xunit.Abstractions;

namespace XGrid.Cpu.Tests;

/// <summary>
/// P1S2 phase A: the QEMU-shaped TLB (<c>CPUTLBEntry</c>) is differentially
/// checked against the current <see cref="Mmu"/> translation, and its probe is
/// measured against the existing <see cref="SoftTlb"/>.
/// </summary>
public sealed class QemuTlbTests(ITestOutputHelper output)
{
    private const uint PageDirectory = 0x1000;
    private const uint PageTable = 0x2000;

    private static (AddressSpace Bus, Mmu Mmu) Create()
    {
        var ram = new PhysicalMemory(0x10000);
        var bus = new AddressSpace(ram, UnmappedAccessPolicy.Throw);
        var context = new XboxContext
        {
            Cr0 = ControlRegisterBits.Cr0ProtectedMode | ControlRegisterBits.Cr0Paging,
            Cr3 = PageDirectory,
        };
        return (bus, new Mmu(context, bus));
    }

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

    [Fact]
    public void ReadWriteCode_UseSeparateTags()
    {
        var tlb = new QemuTlb();
        tlb.Insert(0x00401000, 0x00008000, writable: true, executable: true);

        Assert.True(tlb.TryRead(0x00401234, out uint read));
        Assert.True(tlb.TryWrite(0x00401234, out uint write));
        Assert.True(tlb.TryCode(0x00401234, out uint code));
        Assert.Equal(0x00008234u, read);
        Assert.Equal(read, write);
        Assert.Equal(read, code);
    }

    [Fact]
    public void ReadOnly_WriteMisses()
    {
        var tlb = new QemuTlb();
        tlb.Insert(0x00401000, 0x00008000, writable: false, executable: true);

        Assert.True(tlb.TryRead(0x00401234, out _));
        Assert.False(tlb.TryWrite(0x00401234, out _));
        Assert.True(tlb.TryCode(0x00401234, out _));
    }

    [Fact]
    public void NonExecutable_CodeMisses()
    {
        var tlb = new QemuTlb();
        tlb.Insert(0x00401000, 0x00008000, writable: true, executable: false);

        Assert.True(tlb.TryRead(0x00401234, out _));
        Assert.False(tlb.TryCode(0x00401234, out _));
    }

    [Fact]
    public void Addend_MapsEveryPageOffset()
    {
        var tlb = new QemuTlb();
        tlb.Insert(0x00401000, 0x00008000, writable: true, executable: true);

        for (uint offset = 0; offset < 0x1000; offset += 0x111)
        {
            Assert.True(tlb.TryRead(0x00401000 + offset, out uint physical));
            Assert.Equal(0x00008000u + offset, physical);
        }
    }

    [Fact]
    public void FlushPage_InvalidatesAllTags()
    {
        var tlb = new QemuTlb();
        tlb.Insert(0x00401000, 0x00008000, writable: true, executable: true);
        tlb.Insert(0x00402000, 0x00009000, writable: true, executable: true);

        tlb.FlushPage(0x00401345);

        Assert.False(tlb.TryRead(0x00401000, out _));
        Assert.False(tlb.TryWrite(0x00401000, out _));
        Assert.False(tlb.TryCode(0x00401000, out _));
        Assert.True(tlb.TryRead(0x00402000, out _));
    }

    [Fact]
    public void Flush_InvalidatesEverything()
    {
        var tlb = new QemuTlb();
        tlb.Insert(0x00401000, 0x00008000, writable: true, executable: true);
        tlb.Flush();

        Assert.False(tlb.TryRead(0x00401000, out _));
    }

    [Fact]
    public void Collision_ReplacesTheEntry()
    {
        var tlb = new QemuTlb();
        // 0x00401000 and 0x00801000 share the same 10-bit index (VPN 0x401/0x801)
        // but not the page.
        tlb.Insert(0x00401000, 0x00008000, writable: true, executable: true);
        tlb.Insert(0x00801000, 0x00009000, writable: true, executable: true);

        Assert.False(tlb.TryRead(0x00401000, out _));
        Assert.True(tlb.TryRead(0x00801000, out uint physical));
        Assert.Equal(0x00009000u, physical);
    }

    [Fact]
    public void Probe_MatchesMmuTranslate()
    {
        (AddressSpace bus, Mmu mmu) = Create();
        Map4K(bus, 0x00401000, 0x00008000);

        uint page = mmu.Translate(0x00401000, false, false, out bool faulted, out _);
        Assert.False(faulted);

        var tlb = new QemuTlb();
        tlb.Insert(0x00401000, page & QemuTlb.PageMask, writable: true, executable: true);

        for (uint offset = 0; offset < 0x1000; offset += 0x37)
        {
            uint expected = mmu.Translate(0x00401000 + offset, false, false, out faulted, out _);
            Assert.False(faulted);
            Assert.True(tlb.TryRead(0x00401000 + offset, out uint actual));
            Assert.Equal(expected, actual);
        }
    }

    [Fact]
    public void ProbeCost_IsMeasuredAgainstTheCurrentSoftTlb()
    {
        var qemu = new QemuTlb();
        var soft = new SoftTlb();
        for (uint page = 0; page < (uint)QemuTlb.Capacity; page++)
        {
            qemu.Insert(page << QemuTlb.PageShift, (page + 0x100) << QemuTlb.PageShift, true, true);
            soft.Insert(page, (page + 0x100) << QemuTlb.PageShift, true);
        }

        const int Iterations = 4_000_000;
        uint checksum = 0;

        // Warm both probes so the first-touch JIT does not skew the timing.
        for (int i = 0; i < 100_000; i++)
        {
            qemu.TryRead((uint)(i & 0x3FF) << QemuTlb.PageShift, out uint a);
            soft.TryLookup((uint)(i & 0x3FF), out uint p, out _);
            checksum += a + p;
        }

        var qemuWatch = Stopwatch.StartNew();
        for (int i = 0; i < Iterations; i++)
        {
            uint vaddr = (uint)(i & 0x3FF) << QemuTlb.PageShift;
            if (qemu.TryRead(vaddr, out uint physical))
            {
                checksum += physical;
            }
        }

        qemuWatch.Stop();

        var softWatch = Stopwatch.StartNew();
        for (int i = 0; i < Iterations; i++)
        {
            uint page = (uint)(i & 0x3FF);
            if (soft.TryLookup(page, out uint physical, out _))
            {
                checksum += physical + (page & 0xFFF);
            }
        }

        softWatch.Stop();

        double qemuNs = qemuWatch.Elapsed.TotalNanoseconds / Iterations;
        double softNs = softWatch.Elapsed.TotalNanoseconds / Iterations;
        output.WriteLine($"QemuTlb read: {qemuNs:F2} ns/lookup; SoftTlb lookup: {softNs:F2} ns/lookup; checksum={checksum}");

        Assert.True(qemuNs > 0);
        Assert.True(softNs > 0);
    }
}
