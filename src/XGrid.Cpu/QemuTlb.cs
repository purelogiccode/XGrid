using System;
using System.Runtime.InteropServices;

namespace XGrid.Cpu;

/// <summary>
/// Direct-mapped software TLB shaped like QEMU's <c>CPUTLBEntry</c>
/// (xemu 0.8.139 <c>include/exec/tlb-common.h</c>): one entry holds the
/// page-tagged read, write and code addresses plus the addend that turns a
/// virtual address into the backing address. The low 12 bits of each tag carry
/// the valid bit (and, in a full port, the MMIO/slow flags).
/// </summary>
/// <remarks>
/// This is the Phase A of the CPU engine port (P1S2): a standalone reference
/// shape first, then the <c>Mmu</c> wiring and the measured probe. The current
/// <see cref="SoftTlb"/> stays the engine's TLB until the differential and the
/// probe measurement are recorded.
/// </remarks>
internal sealed class QemuTlb
{
    /// <summary>The guest page shift (4 KB pages, <c>TARGET_PAGE_BITS</c>).</summary>
    public const int PageShift = 12;

    /// <summary>The guest page mask.</summary>
    public const uint PageMask = 0xFFFFF000u;

    private const int IndexBits = 10;
    private const int EntryCount = 1 << IndexBits;
    private const uint IndexMask = EntryCount - 1;

    /// <summary>The valid flag carried in the low bits of every tag.</summary>
    public const uint ValidBit = 1u;

    private readonly Entry[] _entries = new Entry[EntryCount];

    /// <summary>Gets the number of entries (diagnostics).</summary>
    public static int Capacity => EntryCount;

    /// <summary>Probes a read (QEMU <c>addr_read</c>).</summary>
    /// <param name="virtualAddress">The virtual address.</param>
    /// <param name="physicalAddress">Receives the backing address on a hit.</param>
    /// <returns>True on a hit.</returns>
    public bool TryRead(uint virtualAddress, out uint physicalAddress)
    {
        ref Entry entry = ref _entries[(virtualAddress >> PageShift) & IndexMask];
        uint page = virtualAddress & PageMask;
        if ((entry.AddrRead & PageMask) == page && (entry.AddrRead & ValidBit) != 0)
        {
            physicalAddress = virtualAddress + entry.Addend;
            return true;
        }

        physicalAddress = 0;
        return false;
    }

    /// <summary>Probes a write (QEMU <c>addr_write</c>); a read-only page misses.</summary>
    /// <param name="virtualAddress">The virtual address.</param>
    /// <param name="physicalAddress">Receives the backing address on a hit.</param>
    /// <returns>True on a hit.</returns>
    public bool TryWrite(uint virtualAddress, out uint physicalAddress)
    {
        ref Entry entry = ref _entries[(virtualAddress >> PageShift) & IndexMask];
        uint page = virtualAddress & PageMask;
        if ((entry.AddrWrite & PageMask) == page && (entry.AddrWrite & ValidBit) != 0)
        {
            physicalAddress = virtualAddress + entry.Addend;
            return true;
        }

        physicalAddress = 0;
        return false;
    }

    /// <summary>Probes an instruction fetch (QEMU <c>addr_code</c>).</summary>
    /// <param name="virtualAddress">The virtual address.</param>
    /// <param name="physicalAddress">Receives the backing address on a hit.</param>
    /// <returns>True on a hit.</returns>
    public bool TryCode(uint virtualAddress, out uint physicalAddress)
    {
        ref Entry entry = ref _entries[(virtualAddress >> PageShift) & IndexMask];
        uint page = virtualAddress & PageMask;
        if ((entry.AddrCode & PageMask) == page && (entry.AddrCode & ValidBit) != 0)
        {
            physicalAddress = virtualAddress + entry.Addend;
            return true;
        }

        physicalAddress = 0;
        return false;
    }

    /// <summary>Inserts or replaces the entry for one page.</summary>
    /// <param name="virtualAddress">Any address inside the mapped page.</param>
    /// <param name="physicalPage">The backing page base (4 KB aligned).</param>
    /// <param name="writable">Whether the page is writable for the current mode.</param>
    /// <param name="executable">Whether the page is executable.</param>
    public void Insert(uint virtualAddress, uint physicalPage, bool writable, bool executable)
    {
        uint page = virtualAddress & PageMask;
        uint tag = page | ValidBit;
        ref Entry entry = ref _entries[(virtualAddress >> PageShift) & IndexMask];
        entry.AddrRead = tag;
        entry.AddrWrite = writable ? tag : 0u;
        entry.AddrCode = executable ? tag : 0u;
        entry.Addend = (physicalPage & PageMask) - page;
    }

    /// <summary>Invalidates the entry that maps the given page, if any.</summary>
    /// <param name="virtualAddress">Any address inside the page.</param>
    public void FlushPage(uint virtualAddress)
    {
        uint page = virtualAddress & PageMask;
        ref Entry entry = ref _entries[(virtualAddress >> PageShift) & IndexMask];
        if ((entry.AddrRead & PageMask) == page ||
            (entry.AddrWrite & PageMask) == page ||
            (entry.AddrCode & PageMask) == page)
        {
            entry = default;
        }
    }

    /// <summary>Invalidates every entry (CR3/CR0/INVLPG/INVD and snapshot load).</summary>
    public void Flush()
    {
        Array.Clear(_entries);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Entry
    {
        public uint AddrRead;
        public uint AddrWrite;
        public uint AddrCode;
        public uint Addend;
    }
}
