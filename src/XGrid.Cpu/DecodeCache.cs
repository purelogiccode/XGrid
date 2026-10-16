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
public readonly struct DecodeCacheEntry
{
    /// <summary>Gets the decoded instruction (may be an invalid marker).</summary>
    public DecodedInstruction Instruction { get; init; }

    /// <summary>Gets the physical address of fetch-window byte 0 at fill time.</summary>
    public uint PhysicalBase { get; init; }

    /// <summary>Gets the operand-size default the decode was produced with.</summary>
    public int OperandSize { get; init; }

    /// <summary>Gets the address-size default the decode was produced with.</summary>
    public int AddressSize { get; init; }

    /// <summary>Gets fetch-window bytes 0-7 (little-endian).</summary>
    public ulong Source0 { get; init; }

    /// <summary>Gets fetch-window bytes 8-11 (little-endian).</summary>
    public uint Source8 { get; init; }

    /// <summary>Gets fetch-window bytes 12-13 (little-endian).</summary>
    public ushort Source12 { get; init; }

    /// <summary>Gets fetch-window byte 14.</summary>
    public byte Source14 { get; init; }

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
/// filled only from single-page RAM fetch windows (cross-page and non-RAM
/// windows are never cached — gotchas iii/iv), invalidated conservatively.
/// </para>
/// <para>
/// Invalidation contract: an entry may be served only while (a) the linear
/// fetch address still maps to the same physical page, and (b) the source
/// bytes are unchanged. (a) is preserved by full flushes on every mapping
/// change (Mmu.Flush/FlushPage hooks, CS reload with changed base, snapshot
/// load, Reset) plus full flushes on CPU stores to tracked page-table pages;
/// (b) is preserved by page-granular drops on every CPU store and every
/// device/host physical write (CPU store sites + the five XboxMachine adapter
/// shims + the EOS quirk). Host-side measurement state: not part of the
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
    /// Highest exclusive physical address eligible for caching (512 MB):
    /// every base-RAM physical in every real configuration is below it, and
    /// every MMIO window and RAM-alias window (0x0C000000/0x80000000/0xF0000000)
    /// is above it, so the check alone excludes MMIO fetches; the idle-skip
    /// host's IsRam classification is consulted as a second gate.
    /// </summary>
    public const uint MaxTrackedPhysical = 0x20000000;

    private const int TrackedPages = (int)(MaxTrackedPhysical >> 12);
    private const int ActivePageWords = TrackedPages / 64;

    private readonly Dictionary<uint, DecodeCacheEntry> _entries = new();
    private readonly Dictionary<uint, List<uint>> _byPhysicalPage = new();
    private readonly ulong[] _activePages = new ulong[ActivePageWords];

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
    public void Insert(
        uint fetchLinear,
        in DecodedInstruction instruction,
        uint physicalBase,
        ReadOnlySpan<byte> sourceWindow,
        int operandSize,
        int addressSize)
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

        uint physicalPage = physicalBase >> 12;
        if (physicalPage >= TrackedPages)
        {
            // Defense in depth: the CPU only inserts base-RAM fetches, but an
            // alias-window or MMIO physical must never enter the table (the
            // active-page bitset could not track it for store drops).
            SkippedNonRam++;
            return;
        }

        // Replacement may follow verify failure or a changed decode default.
        // Derived arrays must not retain the old entry (including aliases).
        if (_entries.TryGetValue(fetchLinear, out DecodeCacheEntry previous))
        {
            Blocks?.Invalidate();
            if ((previous.PhysicalBase >> 12) != physicalPage)
            {
                DropPhysicalPage(previous.PhysicalBase >> 12);
            }
        }

        if (!_entries.TryGetValue(fetchLinear, out DecodeCacheEntry _))
        {
            if (!_byPhysicalPage.TryGetValue(physicalPage, out List<uint>? keys))
            {
                keys = new List<uint>();
                _byPhysicalPage.Add(physicalPage, keys);
            }

            keys.Add(fetchLinear);
            MarkActive(physicalPage);
        }

        _entries[fetchLinear] = new DecodeCacheEntry
        {
            Instruction = instruction,
            PhysicalBase = physicalBase,
            OperandSize = operandSize,
            AddressSize = addressSize,
            Source0 = BinaryPrimitives.ReadUInt64LittleEndian(sourceWindow),
            Source8 = BinaryPrimitives.ReadUInt32LittleEndian(sourceWindow.Slice(8, 4)),
            Source12 = BinaryPrimitives.ReadUInt16LittleEndian(sourceWindow.Slice(12, 2)),
            Source14 = sourceWindow[14],
        };
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
        Array.Clear(_activePages);
        FullFlushes++;
    }

    private void MarkActive(uint physicalPage)
    {
        _activePages[physicalPage >> 6] |= 1UL << (int)(physicalPage & 63);
    }

    private void ClearActive(uint physicalPage)
    {
        _activePages[physicalPage >> 6] &= ~(1UL << (int)(physicalPage & 63));
    }

    private bool IsActive(uint physicalPage)
    {
        return physicalPage < TrackedPages
               && (_activePages[physicalPage >> 6] & (1UL << (int)(physicalPage & 63))) != 0;
    }
}
