using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace XGrid.Cpu;

/// <summary>
/// One cached decode: the instruction plus everything needed to serve a hit
/// without touching the bus — the physical base of the 15-byte fetch window
/// (idle-skip access-note replay and the physical-page reverse map), the
/// decode-time operand/address-size defaults (hit validation belt), and the
/// source window packed into 15 bytes (debug-verify mode, §131 gotcha ix).
/// The invalid (#UD) marker is cached like any other decode (gotcha vi).
/// </summary>
/// <remarks>Initializes a cache entry.</remarks>
public readonly struct DecodeCacheEntry(
    DecodedInstruction instruction,
    uint physicalBase,
    uint secondPhysicalBase,
    int crossSplit,
    int operandSize,
    int addressSize,
    ulong source0,
    uint source8,
    ushort source12,
    byte source14)
{
    /// <summary>
    /// The decoded instruction (may be an invalid marker). A public field
    /// (not a property) so emitted runners can address the member instruction
    /// in place with ldelema+ldflda instead of copying the entry.
    /// </summary>
#pragma warning disable CA1051 // Addressable field required for emitted ldflda; the struct stays readonly.
    public readonly DecodedInstruction Instruction = instruction;

#pragma warning restore CA1051

    /// <summary>Gets the physical address of fetch-window byte 0 at fill time.</summary>
    public uint PhysicalBase { get; } = physicalBase;

    /// <summary>
    /// Gets the physical address of fetch-window byte <see cref="CrossSplit"/>
    /// (the first byte of the second page) for a cross-page window; 0 for
    /// single-page windows.
    /// </summary>
    public uint SecondPhysicalBase { get; } = secondPhysicalBase;

    /// <summary>
    /// Gets the fetch-window offset where the second page starts (0 for
    /// single-page windows; 1..14 for cross-page windows, D3).
    /// </summary>
    public int CrossSplit { get; } = crossSplit;

    /// <summary>Gets the physical address of fetch-window byte <paramref name="offset"/> (split-aware).</summary>
    public uint PhysicalOf(int offset)
    {
        return CrossSplit != 0 && offset >= CrossSplit
            ? SecondPhysicalBase + (uint)(offset - CrossSplit)
            : PhysicalBase + (uint)offset;
    }

    /// <summary>Gets the operand-size default the decode was produced with.</summary>
    public int OperandSize { get; } = operandSize;

    /// <summary>Gets the address-size default the decode was produced with.</summary>
    public int AddressSize { get; } = addressSize;

    /// <summary>Gets fetch-window bytes 0-7 (little-endian).</summary>
    private ulong Source0 { get; } = source0;

    /// <summary>Gets fetch-window bytes 8-11 (little-endian).</summary>
    private uint Source8 { get; } = source8;

    /// <summary>Gets fetch-window bytes 12-13 (little-endian).</summary>
    private ushort Source12 { get; } = source12;

    /// <summary>Gets fetch-window byte 14.</summary>
    private byte Source14 { get; } = source14;

    /// <summary>Compares a full 15-byte fetch window against the recorded source bytes.</summary>
    public bool SourceMatches(ReadOnlySpan<byte> window)
    {
        return window.Length >= Decoder.MaxInstructionLength
               && BinaryPrimitives.ReadUInt64LittleEndian(window) == Source0
               && BinaryPrimitives.ReadUInt32LittleEndian(window.Slice(8, 4)) == Source8
               && BinaryPrimitives.ReadUInt16LittleEndian(window.Slice(12, 2)) == Source12
               && window[14] == Source14;
    }
}

/// <summary>
/// <para>
/// Decoded-instruction block cache (§131 leg (d)): keyed by linear fetch
/// address (kernel aliases of one physical page are distinct keys — gotcha v),
/// filled from single-page fetch windows. Base-RAM windows are cached with
/// page-granular store invalidation (gotchas iii/iv for the uncacheable
/// cases). Read-only image windows (flash/BIOS, its mirror, the MCPX ROM
/// overlay — §237) are cached without a store path: no guest store can change
/// those bytes, so they are dropped wholesale by an explicit flush
/// (<see cref="FlushImmutableImages"/>) when the host changes the image state.
/// </para>
/// <para>
/// Invalidation contract: an entry may be served only while (a) the linear
/// fetch address still maps to the same physical page, and (b) the source
/// bytes are unchanged. (a) is preserved by full flushes on every mapping
/// change (Mmu.Flush/FlushPage hooks, CS reload with changed base, snapshot
/// load, Reset) plus full flushes on CPU stores to tracked page-table pages;
/// (b) is preserved by page-granular drops on every CPU store and every
/// device/host physical write (CPU store sites + the five XboxMachine adapter
/// shims + the EOS quirk), and for image windows by
/// <see cref="FlushImmutableImages"/> on the host actions that move them.
/// Host-side measurement state: not part of the
/// guest; the ENABLED flag lives on <c>XboxCpu</c> and is untouched by
/// Reset/snapshot like the profiler and fetch-fast flags.
/// </para>
/// </summary>
public sealed class DecodeCache
{
    /// <summary>
    /// Entry cap (128K, §131 gotcha vii). Reaching it flushes the whole table
    /// (bounded memory, decode-share cost only — no guest-visible effect).
    /// </summary>
    public const int MaxEntries = 131072;

    /// <summary>
    /// Highest exclusive physical address eligible for page-tracked caching
    /// (512 MB): every base-RAM physical in every real configuration is below
    /// it, and every MMIO window and RAM-alias window (0x0C000000/0x80000000/
    /// 0xF0000000) is above it, so the check alone excludes MMIO fetches; the
    /// idle-skip host's IsRam classification is consulted as a second gate.
    /// Physicals above it are only cacheable when the bus classifies them as an
    /// immutable image window (§237).
    /// </summary>
    public const uint MaxTrackedPhysical = 0x20000000;

    private const int TrackedPages = (int)(MaxTrackedPhysical >> 12);
    private const int ActivePageWords = TrackedPages / 64;

    private readonly Dictionary<uint, DecodeCacheEntry> _entries = [];
    private readonly Dictionary<uint, List<uint>> _byPhysicalPage = [];

    // Immutable image entries (§237): no store invalidation is possible, so
    // they carry a key set instead of a reverse physical index and are dropped
    // wholesale by FlushImmutableImages. The window list is what a store site
    // tests against (typically one entry: the flash/overlay window).
    private readonly HashSet<uint> _imageKeys = [];
    private readonly List<(uint Base, uint Size)> _imageWindows = [];

    /// <summary>Gets the active-physical-page bitset (C3 raw store guard pins it).</summary>
    internal ulong[] ActivePageBits { get; } = new ulong[ActivePageWords];

    /// <summary>Gets or sets optional derived blocks; null leaves the legacy cache unchanged.</summary>
    public DecodedBlockCache? Blocks { get; set; }

    /// <summary>Gets the number of fetches served from the cache.</summary>
    public ulong Hits { get; private set; }

    /// <summary>Gets the number of fetches that missed (or skipped) and decoded.</summary>
    public ulong Misses { get; private set; }

    /// <summary>Gets the number of entries inserted.</summary>
    public ulong Fills { get; private set; }

    /// <summary>Gets the number of fetch windows skipped because they crossed a page (gotcha iv).</summary>
    public ulong SkippedCrossPage { get; private set; }

    /// <summary>Gets the number of fetch windows skipped because the physical was non-RAM (gotcha iii).</summary>
    public ulong SkippedNonRam { get; private set; }

    /// <summary>Gets the number of physical pages actually cleared by store invalidation.</summary>
    public ulong PageDrops { get; private set; }

    /// <summary>Gets the number of immutable-image entries inserted (flash/ROM windows, §237).</summary>
    public ulong ImageFills { get; private set; }

    /// <summary>Gets the number of wholesale image-entry drops (host image state changed).</summary>
    public ulong ImageFlushes { get; private set; }

    /// <summary>Gets the number of immutable-image entries currently cached.</summary>
    public int ImageCount => _imageKeys.Count;

    /// <summary>Gets the number of effective full flushes (mapping changes, PT-page stores, cap).</summary>
    public ulong FullFlushes { get; private set; }

    /// <summary>Gets the number of debug-verify mismatches (missed-invalidation tripwire, gotcha ix).</summary>
    public ulong VerifyFailures { get; private set; }

    /// <summary>Gets the number of live entries.</summary>
    public int Count => _entries.Count;

    /// <summary>Gets the high-water mark of live entries.</summary>
    public int PeakCount { get; private set; }

    /// <summary>Looks up a cached decode; sizes must match the fill-time defaults.</summary>
    public bool TryGet(uint fetchLinear, int operandSize, int addressSize, out DecodeCacheEntry entry)
    {
        if (_entries.TryGetValue(fetchLinear, out entry)
            && entry.OperandSize == operandSize
            && entry.AddressSize == addressSize)
        {
            return true;
        }

        entry = default;
        return false;
    }

    /// <summary>
    /// Inserts (or overwrites) the decode for one single-page fetch window and
    /// links it into the physical-page reverse map. Reaching
    /// <see cref="MaxEntries"/> flushes the table first (bounded memory).
    /// </summary>
    /// <param name="fetchLinear">The linear fetch address (the key).</param>
    /// <param name="instruction">The decoded instruction.</param>
    /// <param name="physicalBase">The physical address of fetch-window byte 0.</param>
    /// <param name="secondPhysicalBase">The second page's physical for a cross-page window (0 otherwise).</param>
    /// <param name="crossSplit">The fetch-window offset where the second page starts (0 for single-page).</param>
    /// <param name="sourceWindow">The 15-byte fetch window the decode came from.</param>
    /// <param name="operandSize">The decode-time operand-size default.</param>
    /// <param name="addressSize">The decode-time address-size default.</param>
    /// <param name="imageWindowBase">The immutable image window's first address, or 0 for a page-tracked (RAM) fetch.</param>
    /// <param name="imageWindowSize">The immutable image window's size in bytes (0 for a page-tracked fetch).</param>
    public void Insert(
        uint fetchLinear,
        in DecodedInstruction instruction,
        uint physicalBase,
        uint secondPhysicalBase,
        int crossSplit,
        ReadOnlySpan<byte> sourceWindow,
        int operandSize,
        int addressSize,
        uint imageWindowBase = 0,
        uint imageWindowSize = 0)
    {
        if (sourceWindow.Length < Decoder.MaxInstructionLength)
        {
            throw new ArgumentException(
                $"Fetch window must be {Decoder.MaxInstructionLength} bytes.", nameof(sourceWindow));
        }

        if (_entries.Count >= MaxEntries)
        {
            Flush();
        }

        bool image = imageWindowSize != 0;
        uint physicalPage = physicalBase >> 12;
        uint secondPage = secondPhysicalBase >> 12;
        if (!image)
        {
            if (physicalPage >= TrackedPages)
            {
                // Defense in depth: the CPU only inserts base-RAM fetches, but an
                // alias-window or MMIO physical must never enter the table (the
                // active-page bitset could not track it for store drops).
                SkippedNonRam++;
                return;
            }

            if (crossSplit != 0 && secondPage >= TrackedPages)
            {
                SkippedNonRam++;
                return;
            }
        }

        // Replacement may follow verify failure or a changed decode default.
        // Derived arrays must not retain the old entry (including aliases).
        if (_entries.TryGetValue(fetchLinear, out DecodeCacheEntry previous))
        {
            Blocks?.Invalidate();
            bool previousImage = (previous.PhysicalBase >> 12) >= TrackedPages;
            if (previousImage)
            {
                _imageKeys.Remove(fetchLinear);
            }

            if (!previousImage && (previous.PhysicalBase >> 12) != physicalPage)
            {
                DropPhysicalPage(previous.PhysicalBase >> 12);
            }

            if (!previousImage && previous.CrossSplit != 0 && (previous.SecondPhysicalBase >> 12) != physicalPage)
            {
                DropPhysicalPage(previous.SecondPhysicalBase >> 12);
            }
        }

        if (image)
        {
            _imageKeys.Add(fetchLinear);
            if (!_imageWindows.Contains((imageWindowBase, imageWindowSize)))
            {
                _imageWindows.Add((imageWindowBase, imageWindowSize));
            }

            ImageFills++;
        }
        else if (!_entries.ContainsKey(fetchLinear))
        {
            if (!_byPhysicalPage.TryGetValue(physicalPage, out List<uint>? keys))
            {
                keys = [];
                _byPhysicalPage.Add(physicalPage, keys);
            }

            keys.Add(fetchLinear);
            MarkActive(physicalPage);
            if (crossSplit != 0 && secondPage != physicalPage)
            {
                if (!_byPhysicalPage.TryGetValue(secondPage, out List<uint>? secondKeys))
                {
                    secondKeys = [];
                    _byPhysicalPage.Add(secondPage, secondKeys);
                }

                secondKeys.Add(fetchLinear);
                MarkActive(secondPage);
            }
        }

        _entries[fetchLinear] = new DecodeCacheEntry(
            instruction,
            physicalBase,
            secondPhysicalBase,
            crossSplit,
            operandSize,
            addressSize,
            BinaryPrimitives.ReadUInt64LittleEndian(sourceWindow),
            BinaryPrimitives.ReadUInt32LittleEndian(sourceWindow.Slice(8, 4)),
            BinaryPrimitives.ReadUInt16LittleEndian(sourceWindow.Slice(12, 2)),
            sourceWindow[14]);
        Fills++;
        if (_entries.Count > PeakCount)
        {
            PeakCount = _entries.Count;
        }
    }

    /// <summary>
    /// Records one cache miss (a decode happened). Called for every step the
    /// cache does not serve, including cross-page and non-RAM skips, so
    /// hits+misses equals steps.
    /// </summary>
    public void NoteMiss()
    {
        Misses++;
    }

    /// <summary>Records one cache hit (no fetch, no translate, no decode).</summary>
    public void NoteHit()
    {
        Hits++;
    }

    /// <summary>Records one debug-verify mismatch (source bytes changed without invalidation).</summary>
    public void NoteVerifyFailure()
    {
        VerifyFailures++;
    }

    /// <summary>Records one fetch window skipped because it crossed a page.</summary>
    public void NoteSkippedCrossPage()
    {
        SkippedCrossPage++;
    }

    /// <summary>Records one fetch window skipped because its physical was non-RAM.</summary>
    public void NoteSkippedNonRam()
    {
        SkippedNonRam++;
    }

    private readonly Dictionary<uint, long>? _nonRamPages =
        string.Equals(Environment.GetEnvironmentVariable("XGRID_NONRAM"), "1", StringComparison.Ordinal)
            ? []
            : null;

    /// <summary>Gets the non-cacheable fetch pages seen when XGRID_NONRAM=1 (diagnostic).</summary>
    public IReadOnlyDictionary<uint, long>? NonRamPages => _nonRamPages;

    /// <summary>Tallies the physical page of a non-cacheable fetch (diagnostic).</summary>
    public void NoteNonRamPage(uint physical)
    {
        Dictionary<uint, long>? pages = _nonRamPages;
        if (pages is null)
        {
            return;
        }

        pages.TryGetValue(physical, out long count);
        pages[physical] = count + 1;
    }

    /// <summary>
    /// Drops every entry whose source bytes live in the given physical page
    /// (page-granular store invalidation; a no-op for pages with no entries —
    /// the active-page bitset makes the common case a bounds check and a bit).
    /// </summary>
    public void DropPhysicalPage(uint physicalPage)
    {
        if (!IsActive(physicalPage))
        {
            return;
        }

        Blocks?.Invalidate();
        if (_byPhysicalPage.TryGetValue(physicalPage, out List<uint>? keys))
        {
            foreach (uint key in keys)
            {
                _entries.Remove(key);
            }

            _byPhysicalPage.Remove(physicalPage);
        }

        ClearActive(physicalPage);
        PageDrops++;
    }

    /// <summary>Drops every entry and reverse-map link (full flush; a no-op when empty).</summary>
    public void Flush()
    {
        Blocks?.Invalidate();
        if (_entries.Count == 0 && _byPhysicalPage.Count == 0)
        {
            return;
        }

        _entries.Clear();
        _byPhysicalPage.Clear();
        _imageKeys.Clear();
        _imageWindows.Clear();
        Array.Clear(ActivePageBits);
        FullFlushes++;
    }

    /// <summary>
    /// Drops every immutable-image entry (flash/BIOS mirror/overlay, §237).
    /// The host calls this whenever the image content behind a cached window
    /// can change: the MCPX ROM overlay being disabled, a snapshot load, or a
    /// Reset — i.e. any state change a guest store cannot cause. Guest stores
    /// reach it through <see cref="NoteStoreToImageWindow"/>, which is sound by
    /// over-invalidation (a store into a read-only image window is ignored by
    /// the device, so the bytes are unchanged and dropping the entries only
    /// costs a re-decode).
    /// </summary>
    public void FlushImmutableImages()
    {
        if (_imageKeys.Count == 0)
        {
            return;
        }

        Blocks?.Invalidate();
        foreach (uint key in _imageKeys)
        {
            _entries.Remove(key);
        }

        _imageKeys.Clear();
        _imageWindows.Clear();
        ImageFlushes++;
    }

    /// <summary>
    /// Store-site hook for the immutable-image class: when the store touches a
    /// window that has cached entries, drop them (see
    /// <see cref="FlushImmutableImages"/>). The common case is one field read —
    /// no image entry is cached, or the address is base RAM.
    /// </summary>
    /// <param name="physical">The store's physical address.</param>
    /// <param name="sizeInBits">The store width in bits.</param>
    /// <returns>True when image entries were dropped.</returns>
    public bool NoteStoreToImageWindow(uint physical, int sizeInBits)
    {
        if (_imageKeys.Count == 0)
        {
            return false;
        }

        // Half-open [physical, physical + size) per window end address; the
        // last store byte is what has to fall inside.
        ulong last = (ulong)physical + (uint)((sizeInBits / 8) - 1);
        bool inside = false;
        foreach ((uint windowBase, uint windowSize) in _imageWindows)
        {
            if (physical >= windowBase && last < (ulong)windowBase + windowSize)
            {
                inside = true;
                break;
            }
        }

        if (!inside)
        {
            return false;
        }

        FlushImmutableImages();
        return true;
    }

    private void MarkActive(uint physicalPage)
    {
        ActivePageBits[physicalPage >> 6] |= 1UL << (int)(physicalPage & 63);
    }

    private void ClearActive(uint physicalPage)
    {
        ActivePageBits[physicalPage >> 6] &= ~(1UL << (int)(physicalPage & 63));
    }

    private bool IsActive(uint physicalPage)
    {
        return physicalPage < TrackedPages
               && (ActivePageBits[physicalPage >> 6] & (1UL << (int)(physicalPage & 63))) != 0;
    }
}
