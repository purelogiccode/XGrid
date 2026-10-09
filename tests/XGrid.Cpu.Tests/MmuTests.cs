using XGrid.Memory;
using Xunit;

namespace XGrid.Cpu.Tests;

public sealed class MmuTests
{
    private const uint PageDirectory = 0x1000;
    private const uint PageTable = 0x2000;

    private static (XboxContext Context, AddressSpace Bus, Mmu Mmu) Create(bool paging = true)
    {
        var ram = new PhysicalMemory(0x10000);
        var bus = new AddressSpace(ram, UnmappedAccessPolicy.Throw);
        var context = new XboxContext
        {
            Cr0 = ControlRegisterBits.Cr0ProtectedMode | (paging ? ControlRegisterBits.Cr0Paging : 0u),
            Cr3 = PageDirectory,
        };
        var mmu = new Mmu(context, bus);
        return (context, bus, mmu);
    }

    private static void Map4K(AddressSpace bus, uint virtualAddress, uint physicalAddress, bool writable = true,
        bool present = true)
    {
        uint pdeAddress = PageDirectory + ((virtualAddress >> 22) & 0x3FF) * 4;
        if ((bus.Read32(pdeAddress) & 1u) == 0)
        {
            bus.Write32(pdeAddress, PageTable | 3u);
        }

        uint pteAddress = PageTable + ((virtualAddress >> 12) & 0x3FF) * 4;
        bus.Write32(pteAddress, (physicalAddress & 0xFFFFF000u) | (present ? 1u : 0u) | (writable ? 2u : 0u));
    }

    [Fact]
    public void PagingDisabled_IdentityTranslation()
    {
        (_, _, Mmu mmu) = Create(paging: false);

        uint physical = mmu.Translate(0x12345, false, false, out bool faulted, out _);

        Assert.False(faulted);
        Assert.Equal(0x12345u, physical);
    }

    [Fact]
    public void PageWalk_4K()
    {
        (_, AddressSpace bus, Mmu mmu) = Create();
        Map4K(bus, 0x00401000, 0x00008000);

        uint physical = mmu.Translate(0x00401234, false, false, out bool faulted, out _);

        Assert.False(faulted);
        Assert.Equal(0x00008234u, physical);
    }

    [Fact]
    public void NotPresent_FaultsWithNotPresentCode()
    {
        (_, _, Mmu mmu) = Create();

        uint physical = mmu.Translate(0x00401000, false, false, out bool faulted, out uint errorCode);

        Assert.True(faulted);
        Assert.Equal(0u, physical);
        Assert.Equal(0u, errorCode & 1u);
    }

    [Fact]
    public void NotPresent_Write_SetsWriteBit()
    {
        (_, _, Mmu mmu) = Create();

        mmu.Translate(0x00401000, true, false, out bool faulted, out uint errorCode);

        Assert.True(faulted);
        Assert.True((errorCode & 2u) != 0);
    }

    [Fact]
    public void ReadOnly_WithWriteProtect_Faults()
    {
        (XboxContext context, AddressSpace bus, Mmu mmu) = Create();
        Map4K(bus, 0x00401000, 0x00008000, writable: false);
        context.Cr0 |= ControlRegisterBits.Cr0WriteProtect;

        mmu.Translate(0x00401000, true, false, out bool faulted, out uint errorCode);

        Assert.True(faulted);
        Assert.True((errorCode & 1u) != 0);
    }

    [Fact]
    public void ReadOnly_WithoutWriteProtect_Succeeds()
    {
        (_, AddressSpace bus, Mmu mmu) = Create();
        Map4K(bus, 0x00401000, 0x00008000, writable: false);

        uint physical = mmu.Translate(0x00401000, true, false, out bool faulted, out _);

        Assert.False(faulted);
        Assert.Equal(0x00008000u, physical);
    }

    [Fact]
    public void AccessedBit_SetAfterRead()
    {
        (_, AddressSpace bus, Mmu mmu) = Create();
        Map4K(bus, 0x00401000, 0x00008000);

        mmu.Translate(0x00401000, false, false, out _, out _);

        uint pte = bus.Read32(PageTable + ((0x00401000u >> 12) & 0x3FF) * 4);
        Assert.True((pte & 0x20u) != 0);
    }

    [Fact]
    public void DirtyBit_SetAfterWrite()
    {
        (_, AddressSpace bus, Mmu mmu) = Create();
        Map4K(bus, 0x00401000, 0x00008000);

        mmu.Translate(0x00401000, true, false, out _, out _);

        uint pte = bus.Read32(PageTable + ((0x00401000u >> 12) & 0x3FF) * 4);
        Assert.True((pte & 0x40u) != 0);
    }

    [Fact]
    public void FourMegabytePage_Maps()
    {
        (XboxContext context, AddressSpace bus, Mmu mmu) = Create();
        context.Cr4 |= ControlRegisterBits.Cr4PageSizeExtensions;
        uint pdeAddress = PageDirectory + ((0x00500000u >> 22) & 0x3FF) * 4;
        bus.Write32(pdeAddress, 0x00C00000u | 0x83u);

        uint physical = mmu.Translate(0x00512345, false, false, out bool faulted, out _);

        Assert.False(faulted);
        Assert.Equal(0x00D12345u, physical);
    }

    [Fact]
    public void Tlb_CachesUntilFlushPage()
    {
        (_, AddressSpace bus, Mmu mmu) = Create();
        Map4K(bus, 0x00401000, 0x00008000);

        uint first = mmu.Translate(0x00401000, false, false, out _, out _);
        Map4K(bus, 0x00401000, 0x00009000);
        uint cached = mmu.Translate(0x00401000, false, false, out _, out _);

        Assert.Equal(0x00008000u, first);
        Assert.Equal(0x00008000u, cached);

        mmu.FlushPage(0x00401000);
        uint refreshed = mmu.Translate(0x00401000, false, false, out _, out _);

        Assert.Equal(0x00009000u, refreshed);
    }

    [Fact]
    public void Flush_InvalidatesEverything()
    {
        (_, AddressSpace bus, Mmu mmu) = Create();
        Map4K(bus, 0x00401000, 0x00008000);

        mmu.Translate(0x00401000, false, false, out _, out _);
        Map4K(bus, 0x00401000, 0x0000A000);
        mmu.Flush();

        uint refreshed = mmu.Translate(0x00401000, false, false, out _, out _);

        Assert.Equal(0x0000A000u, refreshed);
    }

    [Fact]
    public void InstructionFetch_SetsInstructionBit()
    {
        (_, _, Mmu mmu) = Create();

        mmu.Translate(0x00401000, false, true, out bool faulted, out uint errorCode);

        Assert.True(faulted);
        Assert.True((errorCode & 0x10u) != 0);
    }
}
