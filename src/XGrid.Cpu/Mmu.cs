using System;
using XGrid.Memory;

namespace XGrid.Cpu;

/// <summary>
/// 32-bit non-PAE paging unit with a software TLB (plan section 9). Handles 4 KB and 4 MB pages,
/// A/D bits, and write-protect faults.
/// </summary>
public sealed class Mmu
{
    /// <summary>
    /// Physical pages eligible for page-table tracking: every base-RAM page
    /// in every real configuration (same bound as <c>DecodeCache</c>; page
    /// tables above it are not tracked — they do not occur in practice).
    /// </summary>
    private const int TrackedPages = 0x20000000 >> 12;

    private const int TrackedPageWords = TrackedPages / 64;

    private readonly XboxContext _context;
    private readonly IMemoryBus _memory;
    private readonly SoftTlb _tlb = new();
    private ulong[]? _pageTablePages;

    /// <summary>Creates an MMU over the given context and physical bus.</summary>
    public Mmu(XboxContext context, IMemoryBus memory)
    {
        _context = context ?? throw new System.ArgumentNullException(nameof(context));
        _memory = memory ?? throw new System.ArgumentNullException(nameof(memory));
    }

    /// <summary>
    /// Gets or sets the optional hotspot observer (§126). Null (default) means
    /// no accounting; when set, every attempted <c>SoftTlb</c> lookup reports
    /// exactly one hit or miss (probe translates with <c>sideEffects</c> off
    /// and paging-disabled translates report nothing).
    /// </summary>
    public IHotspotObserver? Observer { get; set; }

    /// <summary>
    /// Gets or sets a hook invoked after the whole TLB is flushed (every
    /// mapping-change site funnels through <see cref="Flush"/>: CR0/CR3/CR4
    /// writes, INVD/WBINVD, Reset, snapshot load). The decode cache (§131 leg
    /// (d)) uses it for its full flush; null (default) means no hook.
    /// </summary>
    public Action? OnFlush { get; set; }

    /// <summary>
    /// Gets or sets a hook invoked after one TLB page is flushed (INVLPG).
    /// The decode cache conservatively full-flushes on it; null (default)
    /// means no hook.
    /// </summary>
    public Action<uint>? OnFlushPage { get; set; }

    /// <summary>
    /// Gets or sets page-table-page tracking (§131 leg (d)): when enabled,
    /// every page-table walk records the physical pages of the PDE (and PTE)
    /// it reads so the decode cache can full-flush when the guest writes one.
    /// Off (default): no recording, zero behavior change. Cleared by
    /// <see cref="Flush"/> together with the TLB (a new epoch re-records on
    /// the next walks).
    /// </summary>
    public bool PageTableTrackingEnabled
    {
        get => _pageTablePages is not null;
        set
        {
            if (value)
            {
                _pageTablePages ??= new ulong[TrackedPageWords];
            }
            else
            {
                _pageTablePages = null;
            }
        }
    }

    /// <summary>Gets whether paging is active (CR0.PE and CR0.PG).</summary>
    public bool IsEnabled => (_context.Cr0 & ControlRegisterBits.Cr0Paging) != 0 && _context.IsProtectedMode;

    /// <summary>Invalidates the whole TLB.</summary>
    public void Flush()
    {
        _tlb.Flush();
        if (_pageTablePages is not null)
        {
            Array.Clear(_pageTablePages);
        }

        OnFlush?.Invoke();
    }

    /// <summary>Invalidates the TLB entry for one virtual address.</summary>
    public void FlushPage(uint virtualAddress)
    {
        _tlb.FlushPage(virtualAddress >> 12);
        OnFlushPage?.Invoke(virtualAddress);
    }

    /// <summary>
    /// Returns whether the given physical page (address >> 12) was observed as
    /// a page-table page in the current flush epoch. Guest stores to such a
    /// page may remap other cached pages, so the decode cache full-flushes on
    /// them (over-flush is sound; §131 gotcha i — the MMU's own A/D-bit writes
    /// never reach the CPU store hooks, so they cannot false-trigger this).
    /// </summary>
    public bool IsPageTablePage(uint physicalPage)
    {
        ulong[]? pages = _pageTablePages;
        return pages is not null
               && physicalPage < TrackedPages
               && (pages[physicalPage >> 6] & (1UL << (int)(physicalPage & 63))) != 0;
    }

    private void TrackPageTablePage(uint physicalPage)
    {
        ulong[]? pages = _pageTablePages;
        if (pages is not null && physicalPage < TrackedPages)
        {
            pages[physicalPage >> 6] |= 1UL << (int)(physicalPage & 63);
        }
    }


    /// <summary>Translates a linear address to a physical address.</summary>
    /// <returns>The physical address, or zero when <paramref name="faulted"/> is true.</returns>
    public uint Translate(
        uint linearAddress,
        bool isWrite,
        bool isInstruction,
        out bool faulted,
        out uint errorCode,
        bool sideEffects = true)
    {
        return TranslateCore(linearAddress, isWrite, isInstruction, out faulted, out errorCode, sideEffects,
            notify: true, out _, out _);
    }

    /// <summary>
    /// Translates a linear address exactly like <see cref="Translate"/> but
    /// reports nothing to the hotspot observer; instead the TLB outcome is
    /// returned so the caller can accumulate batch tallies (fetch fast path,
    /// §131 leg (a)). Page-table side effects (A/D bits, TLB insert) are
    /// identical to <see cref="Translate"/>.
    /// </summary>
    /// <param name="linearAddress">The linear address to translate.</param>
    /// <param name="isWrite">True when the access is a write.</param>
    /// <param name="isInstruction">True when the access is an instruction fetch.</param>
    /// <param name="faulted">Set to true when translation faults.</param>
    /// <param name="errorCode">Receives the page-fault error code.</param>
    /// <param name="tlbHit">True when the lookup hit the SoftTLB.</param>
    /// <param name="tlbCounted">False when paging is disabled or
    /// <paramref name="sideEffects"/> is off (nothing would be reported).</param>
    /// <param name="sideEffects">Whether to apply page-table side effects (accessed/dirty bits, TLB insert).</param>
    /// <returns>The physical address, or zero when <paramref name="faulted"/> is true.</returns>
    public uint TranslateUncounted(
        uint linearAddress,
        bool isWrite,
        bool isInstruction,
        out bool faulted,
        out uint errorCode,
        out bool tlbHit,
        out bool tlbCounted,
        bool sideEffects = true)
    {
        return TranslateCore(linearAddress, isWrite, isInstruction, out faulted, out errorCode, sideEffects,
            notify: false, out tlbHit, out tlbCounted);
    }

    private uint TranslateCore(
        uint linearAddress,
        bool isWrite,
        bool isInstruction,
        out bool faulted,
        out uint errorCode,
        bool sideEffects,
        bool notify,
        out bool tlbHit,
        out bool tlbCounted)
    {
        faulted = false;
        errorCode = 0;
        tlbHit = false;
        tlbCounted = false;

        if (!IsEnabled)
        {
            return linearAddress;
        }

        uint virtualPageNumber = linearAddress >> 12;
        if (sideEffects && _tlb.TryLookup(virtualPageNumber, out uint cachedPage, out bool cachedWritable))
        {
            tlbHit = true;
            tlbCounted = true;
            if (notify)
            {
                Observer?.NoteTlbHit();
            }

            if (isWrite && !cachedWritable && (_context.Cr0 & ControlRegisterBits.Cr0WriteProtect) != 0)
            {
                faulted = true;
                errorCode = PageFaultErrorCode(protection: true, isWrite, isInstruction);
                return 0;
            }

            return cachedPage + (linearAddress & 0xFFFu);
        }

        if (sideEffects)
        {
            tlbCounted = true;
            if (notify)
            {
                Observer?.NoteTlbMiss();
            }
        }

        uint pageDirectoryEntryAddress = (_context.Cr3 & 0xFFFFF000u) + ((linearAddress >> 22) & 0x3FFu) * 4u;
        TrackPageTablePage(pageDirectoryEntryAddress >> 12);
        uint pageDirectoryEntry = _memory.Read32(pageDirectoryEntryAddress);
        if ((pageDirectoryEntry & 1u) == 0)
        {
            faulted = true;
            errorCode = PageFaultErrorCode(protection: false, isWrite, isInstruction);
            return 0;
        }

        if ((pageDirectoryEntry & 0x80u) != 0 && (_context.Cr4 & ControlRegisterBits.Cr4PageSizeExtensions) != 0)
        {
            bool largeWritable = (pageDirectoryEntry & 2u) != 0;
            if (isWrite && !largeWritable && (_context.Cr0 & ControlRegisterBits.Cr0WriteProtect) != 0)
            {
                faulted = true;
                errorCode = PageFaultErrorCode(protection: true, isWrite, isInstruction);
                return 0;
            }

            uint largePage = pageDirectoryEntry & 0xFFC00000u;
            if (sideEffects)
            {
                _memory.Write32(pageDirectoryEntryAddress, pageDirectoryEntry | 0x20u | (isWrite ? 0x40u : 0u));
            }

            return largePage + (linearAddress & 0x3FFFFFu);
        }

        uint pageTableEntryAddress = (pageDirectoryEntry & 0xFFFFF000u) + ((linearAddress >> 12) & 0x3FFu) * 4u;
        TrackPageTablePage(pageTableEntryAddress >> 12);
        uint pageTableEntry = _memory.Read32(pageTableEntryAddress);
        if ((pageTableEntry & 1u) == 0)
        {
            faulted = true;
            errorCode = PageFaultErrorCode(protection: false, isWrite, isInstruction);
            return 0;
        }

        bool writable = (pageTableEntry & 2u) != 0;
        if (isWrite && !writable && (_context.Cr0 & ControlRegisterBits.Cr0WriteProtect) != 0)
        {
            faulted = true;
            errorCode = PageFaultErrorCode(protection: true, isWrite, isInstruction);
            return 0;
        }

        uint physicalPage = pageTableEntry & 0xFFFFF000u;
        if (sideEffects)
        {
            _memory.Write32(pageTableEntryAddress, pageTableEntry | 0x20u | (isWrite ? 0x40u : 0u));
            _tlb.Insert(virtualPageNumber, physicalPage, writable);
        }

        return physicalPage + (linearAddress & 0xFFFu);
    }

    private static uint PageFaultErrorCode(bool protection, bool isWrite, bool isInstruction)
    {
        uint code = 0;
        if (protection)
        {
            code |= 1u;
        }

        if (isWrite)
        {
            code |= 2u;
        }

        if (isInstruction)
        {
            code |= 0x10u;
        }

        return code;
    }
}
