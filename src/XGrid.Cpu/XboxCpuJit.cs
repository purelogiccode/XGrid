using System;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace XGrid.Cpu;

/// <summary>
/// Fast-mode block JIT (charter phases 0–1, J1 slice; opt-in <c>--jit</c>).
/// An alternative execution engine beside the accurate interpreter: whole
/// blocks of natively-emitted members run from a per-block DynamicMethod that
/// keeps GPRs/EIP/EFLAGS/TSC in IL locals across the block, with inlined
/// translate-cache + RAM-direct memory access and the exact interpreter path
/// as the fallback for every non-fast case. The accurate mode is untouched:
/// this partial only activates when <see cref="JitEnabled"/> is set, and
/// trace/break-log/hotspot/idle/verify runs keep the member path. Fast-mode
/// contract (docs/research-notes/jit-track-charter.md §2): MMIO ordering and
/// fault precision are preserved instruction-exactly; the IRQ poll position
/// is relaxed to block granularity. Host-side configuration: untouched by
/// <see cref="Reset"/> and snapshot save/load.
/// </summary>
public sealed unsafe partial class XboxCpu
{
    private bool _jitEnabled;
    private JitBlockCache? _jitBlocks;

    /// <summary>Progress slot read by the fault path: members completed before the faulting one.</summary>
    private int _jitProgress;

    /// <summary>Gets the total number of instructions served by JIT blocks.</summary>
    public ulong JitServedTotal { get; private set; }

    /// <summary>Gets the number of runner invocations.</summary>
    public ulong JitInvocations { get; private set; }

    /// <summary>Gets the number of RunJitBlock entries (diagnostic).</summary>
    public ulong JitCallCount { get; private set; }

    private long _jitRunnerTicks;

    /// <summary>Gets the accumulated time inside emitted runners, in Stopwatch ticks (diagnostic).</summary>
    public long JitRunnerTicks => _jitRunnerTicks;

    private long _jitOuterTicks;

    /// <summary>Gets the accumulated RunJitBlock call time, in Stopwatch ticks (diagnostic).</summary>
    public long JitOuterTicks => _jitOuterTicks;

    private long _nonJitTicks;

    /// <summary>Gets the accumulated non-JIT execution time in <c>Execute</c>, in Stopwatch ticks (diagnostic).</summary>
    public long NonJitTicks => _nonJitTicks;

    /// <summary>Accumulates outer-loop timing while <c>--jit</c> is on (diagnostic).</summary>
    internal void AddJitOuterTicks(long ticks)
    {
        _jitOuterTicks += ticks;
    }

    /// <summary>Accumulates outer-loop non-JIT timing while <c>--jit</c> is on (diagnostic).</summary>
    internal void AddNonJitTicks(long ticks)
    {
        _nonJitTicks += ticks;
    }

    private long _jitSlowMembers;

    /// <summary>Gets the number of emitted members that took the interpreter slow path (diagnostic).</summary>
    public long JitSlowMembers => _jitSlowMembers;

    private long _jitMmioMembers;

    /// <summary>Gets the number of emitted members served through the device fast path (diagnostic, <c>--jit-mmio</c>).</summary>
    public long JitMmioMembers => _jitMmioMembers;

    /// <summary>
    /// Gets or sets the coverage tally (opt-in diagnostic): why RunJitBlock
    /// yields without serving, how long the non-JIT runs are, and which
    /// mnemonics flow through the non-JIT path.
    /// </summary>
    public bool JitTally { get; set; }

    private readonly long[] _jitYieldReasons = new long[5];

    private readonly long[] _nonJitRunBuckets = new long[7];

    private readonly long[] _nonJitMnemonics = new long[512];

    /// <summary>Gets the RunJitBlock yield reasons (pending, rejected, formBail, mismatch, noProgress).</summary>
    public long[] JitYieldReasons => _jitYieldReasons;

    /// <summary>Gets the non-JIT run-size buckets (1, 2-4, 5-16, 17-64, 65-256, 257-1024, 1025+).</summary>
    public long[] NonJitRunBuckets => _nonJitRunBuckets;

    /// <summary>Gets the per-mnemonic counts of instructions served through the non-JIT path.</summary>
    public long[] NonJitMnemonics => _nonJitMnemonics;

    /// <summary>Gets the number of non-JIT runs (diagnostic).</summary>
    public ulong NonJitCalls { get; private set; }

    /// <summary>Gets the number of instructions served through the non-JIT path (diagnostic).</summary>
    public ulong NonJitInstructions { get; private set; }

    /// <summary>Tallies one non-JIT run (called by <c>Execute</c> while the JIT is on).</summary>
    internal void NoteNonJitRun(int ran)
    {
        NonJitCalls++;
        NonJitInstructions += (ulong)ran;
        if (!JitTally)
        {
            return;
        }

        int bucket = ran <= 1 ? 0 : ran <= 4 ? 1 : ran <= 16 ? 2 : ran <= 64 ? 3 : ran <= 256 ? 4 : ran <= 1024 ? 5 : 6;
        _nonJitRunBuckets[bucket]++;
    }

    /// <summary>Per-mnemonic tally of members that took the interpreter fallback (XGRID_SLOW=1).</summary>
    public long[] SlowMemberCounts { get; } = new long[512];

    /// <summary>Tallies one non-JIT member mnemonic (called by the spec runner while the JIT is on).</summary>
    internal void NoteNonJitMnemonic(Mnemonic mnemonic)
    {
        int index = (int)mnemonic;
        if ((uint)index < 512u)
        {
            _nonJitMnemonics[index]++;
        }
    }

    /// <summary>Gets the number of heads neither built nor rejected (diagnostic).</summary>
    public int JitPendingHeadCount => _jitBlocks?.CountPendingHeads() ?? 0;

    private long _jitRevalidations;

    /// <summary>Gets the number of stale blocks kept alive by byte revalidation (diagnostic).</summary>
    public long JitRevalidations => _jitRevalidations;

    /// <summary>Gets the decode/block generation the JIT cache is operating under (diagnostic).</summary>
    public ulong JitBlockGeneration => _blocks?.Generation ?? 0;

    /// <summary>
    /// Gets or sets the maximum instructions one non-JIT (spec) run serves
    /// before the fast-mode engine gets another chance at the head. Without
    /// a cap, a single spec run can swallow a whole ~20K-instruction region
    /// and every buildable head inside it (the menu's rendering regions at
    /// 1B: 93% of the unserved mass sat in such runs).
    /// </summary>
    public int JitSpecSlice { get; set; } = 256;

    private readonly long[] _jitRejectedMnemonics = new long[512];

    /// <summary>Gets the first-member mnemonics of permanently rejected heads (diagnostic).</summary>
    public long[] JitRejectedMnemonics => _jitRejectedMnemonics;

    /// <summary>Tallies the mnemonic of a head whose first member has no native shape (diagnostic).</summary>
    private void NoteRejectedMnemonic(Mnemonic mnemonic)
    {
        int index = (int)mnemonic;
        if ((uint)index < 512u)
        {
            _jitRejectedMnemonics[index]++;
        }
    }

    private readonly long[] _jitBucketTicks = new long[5];

    private readonly long[] _jitBucketCounts = new long[5];

    /// <summary>Gets per-size-bucket invocation ticks (1, 2-4, 5-16, 17-31, 32) (diagnostic).</summary>
    public (long Ticks, long Count)[] JitBuckets
    {
        get
        {
            var result = new (long, long)[5];
            for (int i = 0; i < 5; i++)
            {
                result[i] = (_jitBucketTicks[i], _jitBucketCounts[i]);
            }

            return result;
        }
    }

    /// <summary>Gets the number of block faults delivered.</summary>
    public ulong JitFaults { get; private set; }

    /// <summary>Gets the number of runners built.</summary>
    public ulong JitBlocksBuilt => _jitBlocks?.BlocksBuilt ?? 0;

    /// <summary>Gets the number of heads with a cached permanent rejection.</summary>
    public ulong JitRejectedHeads => _jitBlocks?.Rejected ?? 0;

    /// <summary>Gets the number of formation attempts deferred because member entries were not cached yet.</summary>
    public ulong JitTransientBails => _jitBlocks?.TransientBails ?? 0;

    /// <summary>Gets the number of heads currently cached.</summary>
    public int JitHeadCacheCount => _jitBlocks?.Count ?? 0;

    /// <summary>Gets the number of visits to heads below the build threshold.</summary>
    public ulong JitPendingVisits => _jitBlocks?.PendingVisits ?? 0;

    /// <summary>
    /// Gets or sets the number of times a head must be reached before its
    /// block is built (a DynamicMethod compile is expensive and cold code
    /// must not pay for it). Default 1024; tests lower it for immediate
    /// building.
    /// </summary>
    public int JitBuildThreshold { get; set; } = 1024;

    /// <summary>
    /// Gets or sets exact-boundary mode: the emitted runner polls interrupts
    /// per member (mirroring <c>Step</c>'s boundary exactly, TSC++ then poll)
    /// instead of the fast mode's once-per-block poll. This makes fast-mode
    /// runs bit-exact against the interpreter for the same input, at the cost
    /// of a poll per instruction. Default off.
    /// </summary>
    public bool JitExact { get; set; }

    /// <summary>
    /// Gets or sets the device fast path (<c>--jit-mmio</c>, D1 slice): emitted
    /// members whose probe translated a single-page access into non-RAM
    /// physical space (devices) call <c>ReadPhysicalJit</c>/<c>WritePhysicalJit</c>
    /// with the already-translated address instead of the full interpreter
    /// sync + re-translate. Bit-exact by construction (same RawRead/RawWrite
    /// the interpreter reaches; the probe checked translation and writability,
    /// so no fault can be skipped). Default off.
    /// </summary>
    public bool JitMmioFast { get; set; }

    /// <summary>
    /// Gets or sets the fast-mode idle-wait skip (<c>--jit-spin</c>, opt-in):
    /// a store-free block cycle that keeps re-enabling interrupts with nothing
    /// pending is the kernel idle loop (sti/nop/nop/cli/cmp/je at 0x8001B02E);
    /// after enough consecutive such blocks the runner advances device time
    /// (and the guest TSC) in chunks until an interrupt is pending instead of
    /// executing the spin instruction by instruction. Fast mode only; runs
    /// with it are not bit-exact by construction (time advances in lumps).
    /// </summary>
    public bool JitSpinSkip { get; set; }

    /// <summary>Consecutive store-free STI blocks required before an idle-wait skip fires.</summary>
    public const int JitSpinMinStreak = 32;

    /// <summary>
    /// Gets or sets the per-invocation runner timing (opt-in diagnostic): two
    /// timestamp calls plus bucket accounting per block invocation cost
    /// ~15-25 ns each, which is a large fraction of the fixed per-block cost
    /// on call-heavy code. Default off; the bucket counts stay exact.
    /// </summary>
    public bool JitTiming { get; set; }

    /// <summary>Cap on guest steps advanced per idle-wait skip (two PIT ticks).</summary>
    public const int JitSpinMaxSkipSteps = 16 * 1024 * 1024;

    private int _jitSpinStreak;

    /// <summary>Gets the number of fast-mode idle-wait skips fired (diagnostic).</summary>
    public long JitSpinSkips { get; private set; }

    /// <summary>Gets the number of guest steps advanced by fast-mode idle-wait skips (diagnostic).</summary>
    public long JitSpinSkippedSteps { get; private set; }

    /// <summary>Pending interrupt vector reported by an exact-mode runner (-1 none).</summary>
    private int _jitPending = -1;

    // TEMP-DIAG: block-level trace of (head, entry, members, served, exit) for
    // the fast-IL vs raw differential. Bounded to a TSC window so long runs
    // produce a few hundred lines instead of hundreds of millions.
    private static readonly bool BlockTraceEnabled = string.Equals(
        Environment.GetEnvironmentVariable("XGRID_BLOCK_TRACE"),
        "1",
        StringComparison.Ordinal);

    private static readonly long BlockTraceFrom = long.TryParse(
        Environment.GetEnvironmentVariable("XGRID_BLOCK_TRACE_FROM"),
        NumberStyles.Integer,
        CultureInfo.InvariantCulture,
        out long blockTraceFrom)
        ? blockTraceFrom
        : 0;

    private static readonly long BlockTraceTo = long.TryParse(
        Environment.GetEnvironmentVariable("XGRID_BLOCK_TRACE_TO"),
        NumberStyles.Integer,
        CultureInfo.InvariantCulture,
        out long blockTraceTo)
        ? blockTraceTo
        : long.MaxValue;

    private static readonly uint BlockTraceHead = uint.TryParse(
        Environment.GetEnvironmentVariable("XGRID_BLOCK_TRACE_HEAD"),
        NumberStyles.HexNumber,
        CultureInfo.InvariantCulture,
        out uint blockTraceHead)
        ? blockTraceHead
        : uint.MaxValue;

    /// <summary>
    /// Gets or sets opt-in fast-mode block execution (<c>--jit</c>). Default
    /// off. Enabling implies <see cref="SpecExecutionEnabled"/> (and hence
    /// block execution and the decode cache): the interpreter member path
    /// remains the fallback for every head the planner cannot serve and for
    /// every non-fast member access.
    /// </summary>
    public bool JitEnabled
    {
        get => _jitEnabled;
        set
        {
            if (value)
            {
                SpecExecutionEnabled = true;
                _jitBlocks ??= new JitBlockCache();
                _jitSlowMembers = 0;
                _jitMmioMembers = 0;
            }

            _jitEnabled = value;
        }
    }

    /// <summary>Drops every cached JIT block verdict (host-side; no guest state).</summary>
    public void InvalidateJitBlocks()
    {
        _jitBlocks?.Invalidate();
    }

    /// <summary>
    /// Serves instructions at the current EIP through built blocks, chaining
    /// block to block inside one call until the budget runs out, control
    /// leaves the built set, or a boundary (IRQ delivery, fault) fires.
    /// Returns the number of instructions served (0 when no block covers the
    /// head, in which case the caller falls through to the member path).
    /// Mirrors the member path's boundary treatment: one TSC increment per
    /// member, the per-block IRQ poll (a delivered vector consumes one budget
    /// unit exactly as <c>Step</c>), exact fault delivery, and decode-cache
    /// boundary resets.
    /// </summary>
    private int RunJitBlock(int budget)
    {
        if (!_jitEnabled || budget <= 0)
        {
            return 0;
        }

        JitCallCount++;
        DecodeCache? cache = _decodeCache;
        DecodedBlockCache? blocks = _blocks;
        JitBlockCache? jit = _jitBlocks;
        if (cache is null || blocks is null || !blocks.SpecEnabled || jit is null
            || _idle is not null || _hotspot is not null
            || DecodeCacheVerify || BlockVerify || blocks.ShapeTallyEnabled
            || Mmu.Observer is not null)
        {
            return 0;
        }

        ulong generation = blocks.Generation;
        bool rawTrace = _rawTrace;

        // Loop invariants: nothing servable inside RunJitBlock can change CS
        // (segment loads are excluded from planned members and CallSpec), so
        // the fetch base and decode sizes are stable across the whole call.
        uint csBase = Context.GetSegmentBase(SegmentRegister.Cs);
        int operandSize = Context.DefaultOperandSize;
        int addressSize = Context.DefaultAddressSize;
        int total = 0;
        while (total < budget && !Context.Halted)
        {
            uint fetchLinear = csBase + Context.Eip;
            JitBlock? block = jit.TryMemo(fetchLinear, generation);
            if (block is null)
            {
                switch (jit.Lookup(fetchLinear, generation, JitBuildThreshold, out block))
                {
                    case JitBlockCache.Verdict.Pending:
                        _jitYieldReasons[0]++;

                        return total;
                    case JitBlockCache.Verdict.Rejected:
                        _jitYieldReasons[1]++;

                        return total;
                    case JitBlockCache.Verdict.Stale:
                        if (block is not null && RevalidateJitBlock(block))
                        {
                            // The bytes under this linear are unchanged: the
                            // baked block is still exact — keep it and skip
                            // the rebuild entirely.
                            jit.Refresh(fetchLinear, generation, block);
                            _jitRevalidations++;
                            break;
                        }

                        jit.NoteStale(fetchLinear);
                        block = FormJitBlock(cache, jit, fetchLinear, generation);
                        if (block is null)
                        {
                            _jitYieldReasons[2]++;

                            return total;
                        }

                        break;
                    case JitBlockCache.Verdict.Absent:
                        block = FormJitBlock(cache, jit, fetchLinear, generation);
                        if (block is null)
                        {
                            _jitYieldReasons[2]++;

                            return total;
                        }

                        break;
                }
            }

            if (block is null
                || block.HeadEip != Context.Eip
                || block.OperandSize != operandSize
                || block.AddressSize != addressSize)
            {
                _jitYieldReasons[3]++;

                return total;
            }

            // Fast mode's boundary: one IRQ poll per block; exact mode polls
            // per member inside the runner instead (mirroring Step's order).
            if (!JitExact && InterruptSource is not null && (Context.Eflags & FlagBits.If) != 0)
            {
                int vector = InterruptSource.Acknowledge();
                if (vector >= 0)
                {
                    Context.Tsc++;
                    Context.Halted = false;
                    if (TryTransferToInterrupt(vector, Context.Eip, false, 0))
                    {
                        InterruptCount++;
                        blocks.EndBoundary();
                        total++;
                        continue;
                    }
                }
            }

            _jitProgress = 0;
            _jitPending = -1;

            // Fast-mode idle-wait skip (--jit-spin): count consecutive
            // store-free blocks that re-enable interrupts; after enough of
            // them nothing is pending and the guest is idling, so advance
            // device time until a wakeup can arrive instead of spinning.
            if (JitSpinSkip)
            {
                if (block.HasStore)
                {
                    _jitSpinStreak = 0;
                }
                else if (block.HasSti)
                {
                    _jitSpinStreak++;
                    if (_jitSpinStreak >= JitSpinMinStreak && TrySkipIdleWait(budget - total, out int skipSteps))
                    {
                        total += skipSteps;
                        _jitSpinStreak = 0;
                        continue;
                    }
                }
            }

            int served;
            uint blockEntryEip = Context.Eip;
            long runnerStart = JitTiming ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
            try
            {
                if (block.RawRunner is not null && budget - total >= block.MemberCount)
                {
                    // The raw runner keeps guest state in host registers for the
                    // whole block, so it needs the full member budget up front
                    // (otherwise it yields and the caller serves the tail).
                    // The CPU is passed as a GC handle and the entries array is
                    // pinned at formation: both stay valid across the helper
                    // calls a raw block makes (a GC may move objects there).
                    RawState rawState = BuildRawState();
                    IntPtr selfHandle = RawSelfHandle;
                    DecodeCacheEntry[] entryArray = block.Entries;
                    IntPtr entriesPointer = Unsafe.As<DecodeCacheEntry[], IntPtr>(ref entryArray);
                    RawEnter();
                    try
                    {
                        served = block.RawRunner(selfHandle, &rawState, entriesPointer, budget - total);
                    }
                    finally
                    {
                        RawExit();
                    }

                    NoteRawInvocation();
                    NoteRawServed(served);
                    if (_rawTrace)
                    {
                        System.IO.File.AppendAllText(
                            System.IO.Path.Combine(System.IO.Path.GetTempPath(), "xgrid-raw-trace.txt"),
                            $"head={block.HeadEip:X5} served={served} eip={Context.Eip:X5} " +
                            $"eax={Context.Eax:X} ecx={Context.Ecx:X} edx={Context.Edx:X} " +
                            $"eflags={Context.Eflags:X} tsc={Context.Tsc} built={JitRawBlocksBuilt} inv={JitRawInvocations}" + Environment.NewLine);
                    }
                }
                else if (block.Runner is not null)
                {
                    served = block.Runner(this, block.Entries, budget - total);
                }
                else
                {
                    served = 0;
                }
            }
            catch (PageFaultSignal fault)
            {
                int done = _jitProgress;
                blocks.EndBoundary();
                JitFaults++;
                Context.Cr2 = fault.LinearAddress;
                LastFaultVector = 14;
                LastFaultEip = Context.Eip;
                LastFaultErrorCode = fault.ErrorCode;
                if (!TryTransferToInterrupt(14, Context.Eip, true, fault.ErrorCode))
                {
                    throw new UnhandledGuestException(14, Context.Eip);
                }

                // The faulting member consumes its boundary unit (RunSpecBlock
                // parity: the caller counts it), so mirror the returned count.
                JitServedTotal += (ulong)(done + 1);
                JitInvocations++;
                return total + done + 1;
            }

            if (BlockTraceEnabled)
            {
                long blockTsc = (long)Context.Tsc;
                if (blockTsc >= BlockTraceFrom && blockTsc <= BlockTraceTo)
                {
                    string tracePath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "xgrid-block-trace.txt");
                    System.IO.File.AppendAllText(
                        tracePath,
                        $"B head={block.HeadEip:X5} entry={blockEntryEip:X5} members={block.MemberCount} " +
                        $"served={served} exit={Context.Eip:X5} tsc={blockTsc} raw={block.RawRunner is not null}" +
                        Environment.NewLine);

                    if (block.HeadEip == BlockTraceHead)
                    {
                        var members = new System.Text.StringBuilder();
                        uint memberEip = block.HeadEip;
                        for (int m = 0; m < block.MemberCount; m++)
                        {
                            DecodedInstruction member = block.Entries[m].Instruction;
                            members.Append("  M").Append(m)
                                .Append(" eip=").Append(memberEip.ToString("X5", CultureInfo.InvariantCulture))
                                .Append(" len=").Append(member.Length.ToString(CultureInfo.InvariantCulture))
                                .Append(' ').Append(member.Mnemonic)
                                .AppendLine();
                            memberEip += (uint)member.Length;
                        }

                        System.IO.File.AppendAllText(tracePath, members.ToString());
                    }
                }
            }

            if (JitTiming)
            {
                long runnerTicks = System.Diagnostics.Stopwatch.GetTimestamp() - runnerStart;
                _jitRunnerTicks += runnerTicks;
                int bucket = served <= 1 ? 0 : served <= 4 ? 1 : served <= 16 ? 2 : served <= 31 ? 3 : 4;
                _jitBucketTicks[bucket] += runnerTicks;
                _jitBucketCounts[bucket]++;
            }

            // Exact mode: a runner reported a pending vector at a member
            // boundary. Deliver it exactly as RunSpecBlock does (the member
            // was not served), then keep serving.
            if (_jitPending >= 0)
            {
                int vector = _jitPending;
                Context.Halted = false;
                if (TryTransferToInterrupt(vector, Context.Eip, false, 0))
                {
                    InterruptCount++;
                    blocks.EndBoundary();
                    JitServedTotal += (ulong)(served + 1);
                    JitInvocations++;
                    total += served + 1;
                    continue;
                }
            }

            JitServedTotal += (ulong)served;
            JitInvocations++;
            total += served;
            if (served == 0)
            {
                _jitYieldReasons[4]++;

                return total;
            }
        }

        return total;
    }

    /// <summary>
    /// Lazily revalidates a block whose generation went stale: every member's
    /// 15-byte source window must still match what its linear address yields
    /// through the current mapping. The baked code depends only on the decoded
    /// instruction bytes (the operand/address-size defaults are checked
    /// separately), so a byte-identical window means the block is still exact
    /// and can be kept; any mismatch or fault drops it for a fresh formation.
    /// This is what keeps the guest's page-table churn (thousands of decode
    /// flushes per 100M steps) from rebuilding every hot block from scratch.
    /// </summary>
    private bool RevalidateJitBlock(JitBlock block)
    {
        Span<byte> window = stackalloc byte[Decoder.MaxInstructionLength];
        uint linear = block.HeadLinear;
        foreach (DecodeCacheEntry entry in block.Entries)
        {
            // Page-aware 15-byte window (D3b): a member in the last 14 bytes
            // of a page compares against bytes from both pages, exactly the
            // window it was decoded from. One translate per page touched.
            uint at = linear;
            int offset = 0;
            while (offset < window.Length)
            {
                uint physical = Mmu.Translate(at, isWrite: false, isInstruction: true, out bool faulted, out _);
                if (faulted)
                {
                    return false;
                }

                int chunk = Math.Min(window.Length - offset, (int)(0x1000u - (at & 0xFFFu)));
                for (int i = 0; i < chunk; i++)
                {
                    window[offset + i] = Memory.Read8(physical + (uint)i);
                }

                at += (uint)chunk;
                offset += chunk;
            }

            if (!entry.SourceMatches(window))
            {
                return false;
            }

            linear += (uint)entry.Instruction.Length;
        }

        return true;
    }

    /// <summary>
    /// Advances device time (and the guest TSC) in <see cref="IdleSkipChunkSteps"/>
    /// chunks until an interrupt is pending, the host's next deadline passes,
    /// the budget runs out, or the skip cap is reached. The advanced count is
    /// reported both to the outer budget and to the host's no-double-advance
    /// counter (<c>TakeIdleAdvanced</c>).
    /// </summary>
    private bool TrySkipIdleWait(int budget, out int skipped)
    {
        skipped = 0;
        IIdleSkipHost? host = IdleSkipHost;
        if (host is null || budget <= 0)
        {
            return false;
        }

        int cap = Math.Min(budget, JitSpinMaxSkipSteps);
        int advanced = 0;
        long? until = host.StepsUntilNextDeadline;
        while (advanced < cap && !host.IrqPendingAndEnabled)
        {
            int chunk = Math.Min(IdleSkipChunkSteps, cap - advanced);
            if (until is { } remaining)
            {
                if (remaining <= 0)
                {
                    break;
                }

                chunk = (int)Math.Min(chunk, remaining);
            }

            host.AdvanceIdle((ulong)chunk);
            advanced += chunk;
            if (until is { } deadline)
            {
                until = deadline - chunk;
            }
        }

        if (advanced == 0)
        {
            return false;
        }

        Context.Tsc += (ulong)advanced;
        _idleAdvanced += advanced;
        JitSpinSkips++;
        JitSpinSkippedSteps += advanced;
        skipped = advanced;
        return true;
    }

    /// <summary>
    /// Fills one decode-cache entry on demand by translating and decoding the
    /// current bytes at <paramref name="fetchLinear"/>. Formation must not
    /// depend on the member path having warmed the cache ahead of the head:
    /// after a code-page drop (any store to a page holding cached code) the
    /// page's entries are gone, and a formation walk would otherwise bail
    /// transiently, get retry-suppressed, and lose its build window.
    /// Returns false when the window is uncacheable (page edge, non-RAM,
    /// fetch fault) — the walk then ends transiently as before.
    /// </summary>
    private bool TryFillDecodeEntry(
        DecodeCache cache,
        uint fetchLinear,
        int operandSize,
        int addressSize,
        out DecodeCacheEntry entry)
    {
        entry = default;
        bool crossesPage = (fetchLinear & 0xFFFu) + (uint)Decoder.MaxInstructionLength > 0x1000u;
        Span<byte> window = stackalloc byte[Decoder.MaxInstructionLength];
        try
        {
            if (crossesPage)
            {
                // D3b: a head in the last 14 bytes of a page decodes through
                // the two-page fetch; the entry carries the second base.
                FetchFastCrossPage(fetchLinear, window, out _);
            }
            else
            {
                FetchFast(fetchLinear, window);
            }
        }
        catch (PageFaultSignal)
        {
            return false;
        }
        catch (XGrid.Memory.MemoryAccessException)
        {
            return false;
        }

        DecodeFresh(cache, window, fetchLinear, crossesPage);
        return cache.TryGet(fetchLinear, operandSize, addressSize, out entry);
    }

    /// <summary>
    /// Forms the block headed at <paramref name="headLinear"/>: members are
    /// consecutive decode-cache entries (cross-page windows supported since
    /// D3b, so heads in the last 14 bytes of a page form too), each planned
    /// to a native shape; the block ends at a terminator, at a member with no
    /// native shape, or at the member cap. A formation that ran out of cached
    /// entries is transient (no verdict is recorded - the member path fills
    /// the cache and the next attempt sees further); a head whose first
    /// member has no native shape is a cached permanent rejection until the
    /// generation moves.
    /// </summary>
    private JitBlock? FormJitBlock(DecodeCache cache, JitBlockCache jit, uint headLinear, ulong generation)
    {
        int operandSize = Context.DefaultOperandSize;
        int addressSize = Context.DefaultAddressSize;
        uint headEip = Context.Eip;
        var plans = new JitMemberPlan[JitEmitter.MaxMembers];
        var entries = new DecodeCacheEntry[JitEmitter.MaxMembers];
        int count = 0;
        uint linear = headLinear;
        bool covered = false;
        while (count < JitEmitter.MaxMembers)
        {
            if (!cache.TryGet(linear, operandSize, addressSize, out DecodeCacheEntry entry)
                && !TryFillDecodeEntry(cache, linear, operandSize, addressSize, out entry))
            {
                break;
            }

            JitMemberPlan plan = JitPlanner.Plan(entry.Instruction);
            if (plan.Kind == JitKind.None)
            {
                if (count == 0 && JitTally)
                {
                    NoteRejectedMnemonic(entry.Instruction.Mnemonic);
                }

                covered = true;
                break;
            }

            plans[count] = plan with { EntryIndex = count, Mnemonic = entry.Instruction.Mnemonic };
            entries[count] = entry;
            count++;
            linear += (uint)entry.Instruction.Length;
            if (plan.IsTerminator || count == JitEmitter.MaxMembers)
            {
                covered = true;
                break;
            }
        }

        if (!covered)
        {
            // The walk ran out of cached entries before reaching a stable
            // end: no verdict, retried after a visit delay.
            jit.NoteTransientBail(headLinear, generation);
            return null;
        }

        if (count == 0)
        {
            jit.Store(headLinear, generation, null);
            return null;
        }

        // C2 slice 1: stamp the block's write mask onto every plan so the
        // emitter's sync sites (fault/interpreter paths) can store exactly
        // the registers this block may change.
        uint blockWrite = 0;
        for (int i = 0; i < count; i++)
        {
            blockWrite |= JitStateMask.WriteMask(plans[i]);
        }

        if (blockWrite != 0)
        {
            for (int i = 0; i < count; i++)
            {
                plans[i] = plans[i] with { BlockWriteMask = blockWrite };
            }
        }

        // C3: fast mode prefers a native runner. Exact mode keeps the IL
        // runner (per-member polls and the debugging contract), and any host
        // that cannot execute raw code falls back to it too.
        RawRun? rawRunner = null;
        JitRun? runner = null;
        if (JitRawBlocks && !JitExact)
        {
            nint rawPointer = RawBlockEmitter.TryBuild(headEip, plans.AsSpan(0, count), JitMmioFast);
            if (rawPointer != 0)
            {
                rawRunner = Marshal.GetDelegateForFunctionPointer<RawRun>(rawPointer);
                NoteRawBlockBuilt();
            }
        }

        // The IL runner is also built: a raw block is only usable when the
        // remaining budget covers every member, and the caller must have
        // something to serve the tail with (a yield would desynchronise the
        // step and TSC accounting).
        runner = JitEmitter.Build(headLinear, plans.AsSpan(0, count), JitExact, JitMmioFast);
        if (runner is null)
        {
            jit.Store(headLinear, generation, null);
            return null;
        }
        if (runner is null)
        {
            jit.Store(headLinear, generation, null);
            return null;
        }

        bool hasSti = false;
        bool hasStore = false;
        for (int i = 0; i < count; i++)
        {
            JitKind kind = plans[i].Kind;
            hasSti |= kind == JitKind.Sti;
            hasStore |= kind is JitKind.MovStore or JitKind.MovStoreImm or JitKind.IncM or JitKind.DecM
                or JitKind.FpuStoreReal or JitKind.FpuStoreRealPop
                or JitKind.PushR or JitKind.PushI or JitKind.PopR
                or JitKind.CallRel or JitKind.CallReg or JitKind.CallMem or JitKind.CallSpec
                or JitKind.CallInterpreter or JitKind.Invlpg;
            if ((kind == JitKind.AluMR || kind == JitKind.AluMI) && plans[i].AluOp != JitAluOp.Cmp)
            {
                // AluMR/AluMI are read-modify-writes except the compare forms,
                // which the emitter itself runs as pure reads.
                hasStore = true;
            }
        }

        // The entries array is pinned for the block's lifetime: the raw runner
        // receives its address and a GC during a helper call must not move it.
        DecodeCacheEntry[] pinnedEntries = GC.AllocateArray<DecodeCacheEntry>(count, pinned: true);
        entries.AsSpan(0, count).CopyTo(pinnedEntries);

        var block = new JitBlock(
            headLinear, headEip, operandSize, addressSize, generation, count,
            pinnedEntries, runner, rawRunner, hasSti, hasStore);
        jit.Store(headLinear, generation, block);
        return block;
    }
}
