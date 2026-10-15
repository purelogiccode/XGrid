using System;

namespace XGrid.Cpu;

/// <summary>
/// Opt-in decoded-instruction block cache support (§131 leg (d)):
/// configuration, host-side counters, invalidation entry points, and the
/// fetch-decode integration used by <c>Step</c>. Default off: with no cache
/// constructed the interpreter path is byte-identical to the plain core
/// (flag-off carries only null checks, same discipline as the profiler and
/// the fetch fast path). Host-side state: the enabled flag and counters are
/// untouched by <see cref="Reset"/> and snapshot save/load.
/// </summary>
public sealed partial class XboxCpu
{
    private DecodeCache? _decodeCache;
    private ulong _decodeCount;
    private bool _decodeCacheVerify;
    private DecodedBlockCache? _blocks;

    /// <summary>Gets or sets opt-in block cursor execution. Enabling implies the decode cache.</summary>
    public bool BlockExecutionEnabled
    {
        get => _blocks is not null;
        set
        {
            if (value)
            {
                DecodeCacheEnabled = true;
                _blocks ??= new DecodedBlockCache();
                _decodeCache!.Blocks = _blocks;
            }
            else
            {
                if (_decodeCache is not null)
                {
                    _decodeCache.Blocks = null;
                }

                _blocks = null;
            }
        }
    }

    /// <summary>Gets or sets full-window verification before each block member executes.</summary>
    public bool BlockVerify { get; set; }

    /// <summary>
    /// Gets or sets pre-specialized per-shape execution (§139). Default off.
    /// Enabling implies <see cref="BlockExecutionEnabled"/> (and hence the
    /// decode cache): block members with a resolved shape run their cached
    /// handler instead of the mnemonic switch, in <see cref="Step"/> and in
    /// the <see cref="Execute"/> unrolled runner. Every original boundary is
    /// retained (IRQ poll, TSC, per-member EIP, exact faults, idle/hotspot
    /// observation); trace/break-log callers use <see cref="Step"/> and keep
    /// the member-cursor path. Host-side configuration: untouched by
    /// <see cref="Reset"/> and snapshot save/load.
    /// </summary>
    public bool SpecExecutionEnabled
    {
        get => _blocks is not null && _blocks.SpecEnabled;
        set
        {
            if (value)
            {
                BlockExecutionEnabled = true;
                _blocks!.SpecEnabled = true;
            }
            else if (_blocks is not null)
            {
                _blocks.SpecEnabled = false;
            }
        }
    }

    /// <summary>Gets host-only specialization statistics, or null when block execution is disabled.</summary>
    public DecodedBlockCache? SpecStatistics => _blocks;

    /// <summary>Gets host-only block statistics, or null when disabled.</summary>
    public DecodedBlockCache? BlockStatistics => _blocks;

    /// <summary>
    /// Gets or sets whether the decoded-instruction block cache is enabled
    /// (§131 leg (d)). Default off. Enabling implies
    /// <see cref="FetchFastPathEnabled"/> (cache misses take the
    /// single-translate fetch path) and starts MMU page-table-page tracking
    /// (with a cold TLB, so every back-table page is observed); disabling
    /// drops the cache and stops tracking. Host-side configuration: untouched
    /// by <see cref="Reset"/> and snapshot save/load (like
    /// <see cref="HotspotObserver"/>).
    /// </summary>
    public bool DecodeCacheEnabled
    {
        get => _decodeCache is not null;
        set
        {
            if (value && _decodeCache is null)
            {
                _decodeCache = new DecodeCache();
                Mmu.PageTableTrackingEnabled = true;

                // Cold start: drop any TLB populated before tracking was on,
                // so the next fetch of every cached page walks (and re-records)
                // its page-table pages.
                Mmu.Flush();
                FetchFastPathEnabled = true;
            }
            else if (!value && _decodeCache is not null)
            {
                BlockExecutionEnabled = false;
                _decodeCache = null;
                Mmu.PageTableTrackingEnabled = false;
            }
        }
    }

    /// <summary>
    /// Gets or sets debug-verify mode (§131 gotcha ix): on every cache hit the
    /// 15-byte source window is re-read from the entry's physicals and
    /// compared against the recorded bytes; a mismatch counts
    /// <see cref="DecodeCacheVerifyFailures"/> and re-decodes. Off by default;
    /// only meaningful while <see cref="DecodeCacheEnabled"/> is on. Meant to
    /// prove the invalidation discipline exhaustive during bring-up, not for
    /// speed.
    /// </summary>
    public bool DecodeCacheVerify
    {
        get => _decodeCacheVerify;
        set => _decodeCacheVerify = value;
    }

    /// <summary>
    /// Gets the number of <c>Decoder.Decode</c> invocations (one per executed
    /// instruction while the decode cache is off; misses only while it is on
    /// — the decode-avoided share is 1 - this/Executed).
    /// </summary>
    public ulong DecodeCount => _decodeCount;

    /// <summary>Gets the number of instructions served by the decode cache.</summary>
    public ulong DecodeCacheHits => _decodeCache?.Hits ?? 0;

    /// <summary>Gets the number of instructions that missed the decode cache and were decoded.</summary>
    public ulong DecodeCacheMisses => _decodeCache?.Misses ?? 0;

    /// <summary>Gets the number of decode-cache entries inserted.</summary>
    public ulong DecodeCacheFills => _decodeCache?.Fills ?? 0;

    /// <summary>Gets the number of live decode-cache entries.</summary>
    public int DecodeCacheEntries => _decodeCache?.Count ?? 0;

    /// <summary>Gets the high-water mark of live decode-cache entries.</summary>
    public int DecodeCachePeakEntries => _decodeCache?.PeakCount ?? 0;

    /// <summary>Gets the number of physical pages cleared by store invalidation.</summary>
    public ulong DecodeCachePageDrops => _decodeCache?.PageDrops ?? 0;

    /// <summary>Gets the number of full decode-cache flushes (mapping changes, PT-page stores, cap).</summary>
    public ulong DecodeCacheFullFlushes => _decodeCache?.FullFlushes ?? 0;

    /// <summary>Gets the number of fetch windows skipped because they crossed a page.</summary>
    public ulong DecodeCacheSkippedCrossPage => _decodeCache?.SkippedCrossPage ?? 0;

    /// <summary>Gets the number of fetch windows skipped because the physical was non-RAM.</summary>
    public ulong DecodeCacheSkippedNonRam => _decodeCache?.SkippedNonRam ?? 0;

    /// <summary>Gets the number of debug-verify mismatches (missed-invalidation tripwire).</summary>
    public ulong DecodeCacheVerifyFailures => _decodeCache?.VerifyFailures ?? 0;

    /// <summary>
    /// Fully flushes the decode cache (no-op when disabled or empty). The
    /// host-side flush API for writers that bypass the CPU store sites:
    /// snapshot-adjacent loads and any direct <c>Memory.Write*</c> call.
    /// </summary>
    public void InvalidateDecodeCache()
    {
        _decodeCache?.Flush();
    }

    /// <summary>
    /// Drops the decode-cache entries whose source bytes live in the page of
    /// the given physical address (no-op when disabled or the page holds no
    /// entries). The flush API for the five XboxMachine device-adapter shims
    /// and other device/DMA physical writers (§131 invalidation inventory);
    /// page-granular is sound because cached windows never cross a page.
    /// </summary>
    public void InvalidateDecodeCachePage(uint physicalAddress)
    {
        _decodeCache?.DropPhysicalPage(physicalAddress >> 12);
    }

    /// <summary>
    /// Fetch-decode integration for one step with the cache enabled (§131
    /// leg (d)): single-page windows are looked up by linear fetch address
    /// and validated against the current operand/address-size defaults;
    /// hits replay the idle-skip fetch access notes from the entry's
    /// physicals (gotcha iii) and never touch the bus or the MMU; misses
    /// take the byte-identical fetch-fast path (cross-page windows the
    /// legacy path) and fill the cache when the physical is RAM.
    /// Slice 1 (§139): when specialization is enabled, the block cursor's
    /// resolved handler is surfaced alongside the member; otherwise null and
    /// the caller runs the unchanged interpreter body.
    /// </summary>
    private DecodedInstruction FetchDecodeCached(DecodeCache cache, Span<byte> buffer, out SpecHandler? spec)
    {
        spec = null;
        uint segmentBase = Context.GetSegmentBase(SegmentRegister.Cs);
        uint fetchLinear = segmentBase + Context.Eip;
        bool crossesPage = (fetchLinear & 0xFFFu) + (uint)buffer.Length > 0x1000u;

        DecodeCacheEntry entry = default;
        bool blockHit = _blocks is not null && !crossesPage
            && _blocks.TryNext(cache, fetchLinear, Context.DefaultOperandSize, Context.DefaultAddressSize, out entry);
        if (blockHit || (!crossesPage
            && cache.TryGet(fetchLinear, Context.DefaultOperandSize, Context.DefaultAddressSize, out entry)))
        {
            _hotspot?.NoteFetch(false);

            // Per-byte idle-skip access notes (physicals from the entry) so
            // idle-skip verdicts stay exact without any bus traffic.
            IdleSkipDetector? idle = _idle;
            if (idle is not null)
            {
                for (int i = 0; i < buffer.Length; i++)
                {
                    idle.NoteAccess(entry.PhysicalBase + (uint)i, isWrite: false, isInstruction: true);
                }
            }

            if (_decodeCacheVerify || (BlockVerify && _blocks is not null))
            {
                for (int i = 0; i < buffer.Length; i++)
                {
                    buffer[i] = Memory.Read8(entry.PhysicalBase + (uint)i);
                }

                if (!entry.SourceMatches(buffer))
                {
                    // A writer escaped the invalidation discipline: count the
                    // tripwire and decode fresh (self-healing).
                    cache.NoteVerifyFailure();
                    if (BlockVerify && _blocks is not null)
                    {
                        _blocks.NoteVerifyFailure();
                        cache.DropPhysicalPage(entry.PhysicalBase >> 12);
                    }

                    return DecodeFresh(cache, buffer, fetchLinear, crossesPage: false);
                }
            }

            cache.NoteHit();
            _hotspot?.NoteDecodeCacheHit();
            if (blockHit && _blocks is { SpecEnabled: true })
            {
                spec = _blocks.CurrentHandler;
            }

            return entry.Instruction;
        }

        return DecodeFresh(cache, buffer, fetchLinear, crossesPage);
    }

    /// <summary>
    /// Slice-2 unrolled runner (§139): serves consecutive block members
    /// without returning through <see cref="Step"/> per member. Used only
    /// inside <see cref="Execute"/> (trace/break-log callers use Step and keep
    /// the member-cursor path). Every original boundary is retained per
    /// member: TSC increment, IRQ poll at the Step site (a delivered IRQ ends
    /// the boundary and consumes the budget unit), full-window verify before
    /// execution, per-member EIP, exact fault EIP/vector/error/CR2, the
    /// 15-entry idle NoteAccess replay and the hotspot/idle observations.
    /// Members without a resolved handler run the identical interpreter body.
    /// Returns the number of members executed (0 when no block serves the
    /// current head: the caller owns the miss accounting through Step).
    /// </summary>
    private int RunSpecBlock(int budget)
    {
        DecodedBlockCache? blocks = _blocks;
        DecodeCache? cache = _decodeCache;
        if (blocks is null || cache is null || budget <= 0)
        {
            return 0;
        }

        int ran = 0;
        Span<byte> window = stackalloc byte[Decoder.MaxInstructionLength];
        while (ran < budget && !Context.Halted)
        {
            uint fetchLinear = Context.GetSegmentBase(SegmentRegister.Cs) + Context.Eip;
            if ((fetchLinear & 0xFFFu) + (uint)window.Length > 0x1000u)
            {
                break;
            }

            if (!blocks.TryNext(
                cache,
                fetchLinear,
                Context.DefaultOperandSize,
                Context.DefaultAddressSize,
                out DecodeCacheEntry entry))
            {
                break;
            }

            Context.Tsc++;

            if (InterruptSource is not null && (Context.Eflags & FlagBits.If) != 0)
            {
                int vector = InterruptSource.Acknowledge();
                if (vector >= 0)
                {
                    Context.Halted = false;
                    if (TryTransferToInterrupt(vector, Context.Eip, false, 0))
                    {
                        InterruptCount++;
                        blocks.EndBoundary();
                        return ran + 1;
                    }
                }
            }

            DecodedInstruction instruction = entry.Instruction;
            SpecHandler? spec = blocks.CurrentHandler;

            _hotspot?.NoteFetch(false);

            IdleSkipDetector? idle = _idle;
            if (idle is not null)
            {
                for (int i = 0; i < window.Length; i++)
                {
                    idle.NoteAccess(entry.PhysicalBase + (uint)i, isWrite: false, isInstruction: true);
                }
            }

            bool servedFresh = false;
            if (_decodeCacheVerify || BlockVerify)
            {
                for (int i = 0; i < window.Length; i++)
                {
                    window[i] = Memory.Read8(entry.PhysicalBase + (uint)i);
                }

                if (!entry.SourceMatches(window))
                {
                    // Same self-healing path as the Step fetch: count the
                    // tripwire, drop the page and decode the current bytes
                    // fresh (no hit is recorded for the stale member).
                    cache.NoteVerifyFailure();
                    if (BlockVerify)
                    {
                        blocks.NoteVerifyFailure();
                        cache.DropPhysicalPage(entry.PhysicalBase >> 12);
                    }

                    try
                    {
                        instruction = DecodeFresh(cache, window, fetchLinear, crossesPage: false);
                    }
                    catch (PageFaultSignal fault)
                    {
                        blocks.EndBoundary();
                        Context.Cr2 = fault.LinearAddress;
                        LastFaultVector = 14;
                        LastFaultEip = Context.Eip;
                        LastFaultErrorCode = fault.ErrorCode;
                        if (!TryTransferToInterrupt(14, Context.Eip, true, fault.ErrorCode))
                        {
                            throw new UnhandledGuestException(14, Context.Eip);
                        }

                        return ran + 1;
                    }

                    servedFresh = true;
                    spec = null;
                }
            }

            if (!servedFresh)
            {
                cache.NoteHit();
                _hotspot?.NoteDecodeCacheHit();
            }

            if (!instruction.IsValid)
            {
                blocks.EndBoundary();
                LastFaultVector = 6;
                LastFaultEip = Context.Eip;
                LastFaultErrorCode = 0;
                if (!TryTransferToInterrupt(6, Context.Eip, false, 0))
                {
                    throw new UnsupportedInstructionException(Context.Eip, instruction.Opcode);
                }

                return ran + 1;
            }

            uint eipBefore = Context.Eip;
            try
            {
                if (spec is null)
                {
                    ExecuteInstruction(instruction);
                }
                else
                {
                    blocks.NoteSpecRan();
                    spec(this, instruction);
                }
            }
            catch (PageFaultSignal fault)
            {
                blocks.EndBoundary();
                Context.Cr2 = fault.LinearAddress;
                LastFaultVector = 14;
                LastFaultEip = Context.Eip;
                LastFaultErrorCode = fault.ErrorCode;
                if (!TryTransferToInterrupt(14, Context.Eip, true, fault.ErrorCode))
                {
                    throw new UnhandledGuestException(14, Context.Eip);
                }

                return ran + 1;
            }

            idle?.Observe(instruction, eipBefore, Context.Eip);
            _hotspot?.Observe(instruction, eipBefore, Context.Eip);
            ran++;
        }

        return ran;
    }

    /// <summary>
    /// The miss path: byte-identical fetch (single-translate for single-page
    /// windows, the legacy per-byte loop — with the §131a counted variant
    /// when a profiler is attached — for cross-page windows) plus a decode,
    /// then a fill attempt. Faults propagate exactly as the flag-off path
    /// (no fill happens: the decode never ran).
    /// </summary>
    private DecodedInstruction DecodeFresh(DecodeCache cache, Span<byte> buffer, uint fetchLinear, bool crossesPage)
    {
        uint basePhysical = 0;
        if (!crossesPage)
        {
            basePhysical = FetchFast(fetchLinear, buffer);
        }
        else
        {
            uint segmentBase = Context.GetSegmentBase(SegmentRegister.Cs);
            IHotspotObserver? observer = _hotspot;
            if (observer is not null)
            {
                observer.NoteFetchSlow();
                FetchCounted(observer, segmentBase, buffer, crossesPage: true);
            }
            else
            {
                FetchLegacy(segmentBase, buffer);
            }
        }

        DecodedInstruction instruction = Decoder.Decode(
            buffer,
            Context.DefaultOperandSize,
            Context.DefaultAddressSize);
        _decodeCount++;
        cache.NoteMiss();
        _hotspot?.NoteDecodeCacheMiss();

        if (crossesPage)
        {
            cache.NoteSkippedCrossPage();
        }
        else if (IsCacheableFetch(basePhysical))
        {
            cache.Insert(
                fetchLinear,
                instruction,
                basePhysical,
                buffer,
                Context.DefaultOperandSize,
                Context.DefaultAddressSize);
        }
        else
        {
            cache.NoteSkippedNonRam();
        }

        return instruction;
    }

    /// <summary>
    /// RAM gate for cache fills: base-RAM physicals only (the bound alone
    /// excludes every MMIO window and RAM-alias window on the Xbox map), plus
    /// the idle-skip host's classification as a second gate (null host = flat
    /// test RAM). Non-RAM fetches (flash/BIOS, MMIO) are never cached so no
    /// device register read can go stale behind the cache (gotcha iii).
    /// </summary>
    private bool IsCacheableFetch(uint physical)
    {
        if (physical >= DecodeCache.MaxTrackedPhysical)
        {
            return false;
        }

        IIdleSkipHost? host = IdleSkipHost;
        return host is null || host.IsRam(physical);
    }

    /// <summary>
    /// CPU-store invalidation hook (§131 invalidation site #1): every guest
    /// store funnels through the <c>WriteMemory</c>/<c>RawWrite</c>/
    /// <c>WriteMemory64</c>/<c>WriteMemoryBytes</c> sites with its
    /// post-translate physical. A store to an observed page-table page
    /// full-flushes (the mapping behind other cached pages may have changed);
    /// any other store drops the affected physical page(s). Flag off: a
    /// single null check.
    /// </summary>
    private void NoteStorePhysical(uint physical, int sizeInBits)
    {
        DecodeCache? cache = _decodeCache;
        if (cache is null)
        {
            return;
        }

        uint firstPage = physical >> 12;
        uint lastPage = (physical + (uint)(sizeInBits / 8) - 1u) >> 12;
        if (Mmu.IsPageTablePage(firstPage) || (lastPage != firstPage && Mmu.IsPageTablePage(lastPage)))
        {
            cache.Flush();
            return;
        }

        cache.DropPhysicalPage(firstPage);
        if (lastPage != firstPage)
        {
            cache.DropPhysicalPage(lastPage);
        }
    }

    /// <summary>
    /// Mmu.Flush hook: every mapping change (CR0.PG/CR3/CR4.PSE writes,
    /// INVD/WBINVD, Reset, snapshot load) funnels here, so the full flush is
    /// exhaustive over the §131 inventory. Null cache (flag off): no-op.
    /// </summary>
    private void OnMmuFlush()
    {
        _decodeCache?.Flush();
    }

    /// <summary>
    /// Mmu.FlushPage hook (INVLPG): conservative full flush — a linear-page
    /// drop would need a second (linear) index the v1 table does not carry.
    /// </summary>
    private void OnMmuFlushPage(uint virtualAddress)
    {
        _decodeCache?.Flush();
    }
}
