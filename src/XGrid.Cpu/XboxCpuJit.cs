using System;

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
public sealed partial class XboxCpu
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

    /// <summary>Pending interrupt vector reported by an exact-mode runner (-1 none).</summary>
    private int _jitPending = -1;

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
                    case JitBlockCache.Verdict.Rejected:
                    case JitBlockCache.Verdict.Pending:
                        return total;
                    case JitBlockCache.Verdict.Absent:
                        block = FormJitBlock(cache, jit, fetchLinear, generation);
                        if (block is null)
                        {
                            return total;
                        }

                        break;
                }
            }

            if (block is null
                || block.HeadEip != Context.Eip
                || block.Generation != generation
                || block.OperandSize != operandSize
                || block.AddressSize != addressSize)
            {
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
            int served;
            try
            {
                long runnerStart = System.Diagnostics.Stopwatch.GetTimestamp();
                served = block.Runner(this, block.Entries, budget - total);
                long runnerTicks = System.Diagnostics.Stopwatch.GetTimestamp() - runnerStart;
                _jitRunnerTicks += runnerTicks;
                int bucket = served <= 1 ? 0 : served <= 4 ? 1 : served <= 16 ? 2 : served <= 31 ? 3 : 4;
                _jitBucketTicks[bucket] += runnerTicks;
                _jitBucketCounts[bucket]++;
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
                return total;
            }
        }

        return total;
    }

    /// <summary>
    /// Forms the block headed at <paramref name="headLinear"/>: members are
    /// consecutive decode-cache entries on one 4 KB fetch page, each planned
    /// to a native shape; the block ends at a terminator, at a member with no
    /// native shape, at the page edge, or at the member cap. A formation that
    /// ran out of cached entries is transient (no verdict is recorded — the
    /// member path fills the cache and the next attempt sees further); a head
    /// whose first member has no native shape is a cached permanent
    /// rejection until the generation moves.
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
            if ((linear & 0xFFFu) + (uint)Decoder.MaxInstructionLength > 0x1000u)
            {
                covered = true;
                break;
            }

            if (!cache.TryGet(linear, operandSize, addressSize, out DecodeCacheEntry entry))
            {
                break;
            }

            JitMemberPlan plan = JitPlanner.Plan(entry.Instruction);
            if (plan.Kind == JitKind.None)
            {
                covered = true;
                break;
            }

            plans[count] = plan with { EntryIndex = count };
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

        JitRun? runner = JitEmitter.Build(headLinear, plans.AsSpan(0, count), JitExact);
        if (runner is null)
        {
            jit.Store(headLinear, generation, null);
            return null;
        }

        var block = new JitBlock(
            headLinear, headEip, operandSize, addressSize, generation, count,
            entries.AsSpan(0, count).ToArray(), runner);
        jit.Store(headLinear, generation, block);
        return block;
    }
}
