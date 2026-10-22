using System;
using System.Collections.Generic;

namespace XGrid.Cpu;

/// <summary>
/// Bounded blocks formed solely from existing decodes. No speculative fetches.
/// A block dispatch selects an array; the interpreter mnemonic switch remains
/// per instruction. All derived state shares the decode cache's lifetime.
/// </summary>
public sealed class DecodedBlockCache
{
    /// <summary>Maximum original instructions per block.</summary>
    private const int MaxInstructions = 32;

    /// <summary>Maximum retained blocks (and hence at most 131072 decoded references).</summary>
    public const int MaxBlocks = 4096;

    private readonly Dictionary<uint, Block> _blocks = [];
    private Block? _current;
    private int _index;
    private uint _expectedLinear;

    /// <summary>
    /// Gets or sets whether formation resolves pre-specialized per-shape
    /// handlers (§139). Off by default: formation stores entries only and the
    /// interpreter path is unchanged. Enabling drops any handler-less blocks
    /// so every retained block carries its resolved handlers.
    /// </summary>
    public bool SpecEnabled
    {
        get; set
        {
            if (value && !field)
            {
                Invalidate();
            }

            field = value;
        }
    }

    /// <summary>Gets the handler for the member just served by <see cref="TryNext"/> (null when disabled or fallback).</summary>
    public XboxCpu.SpecHandler? CurrentHandler { get; private set; }

    /// <summary>Gets block selections, not mnemonic-switch eliminations.</summary>
    public ulong Dispatches { get; private set; }

    /// <summary>Gets instructions served by blocks.</summary>
    public ulong Instructions { get; private set; }

    /// <summary>Gets blocks formed.</summary>
    public ulong Builds { get; private set; }

    /// <summary>Gets retained-block lookup hits.</summary>
    public ulong Hits { get; private set; }

    /// <summary>Gets invalidations of derived state.</summary>
    public ulong Invalidations { get; private set; }

    /// <summary>Gets bounded-table flushes.</summary>
    public ulong CapFlushes { get; private set; }

    /// <summary>Gets the maximum number of retained blocks.</summary>
    public int PeakCount { get; private set; }

    /// <summary>Gets the number of retained blocks.</summary>
    public int Count => _blocks.Count;

    /// <summary>Gets mismatched source windows detected before execution.</summary>
    public ulong VerifyFailures { get; private set; }

    /// <summary>Gets completed blocks.</summary>
    public ulong Completed { get; private set; }

    /// <summary>Gets cursor exits due to unexpected PC or decode mode.</summary>
    public ulong Redirects { get; private set; }

    /// <summary>Gets cursor exits due to IRQ or faults.</summary>
    public ulong BoundaryExits { get; private set; }

    /// <summary>Gets block members served with a pre-specialized handler (slice 1 cursor value).</summary>
    public ulong ShapeHits { get; private set; }

    /// <summary>Gets block members served by the interpreter while specialization is enabled.</summary>
    public ulong ShapeFallbacks { get; private set; }

    /// <summary>Gets blocks formed with every member pre-specialized (unrolled-runner eligible).</summary>
    public ulong SpecBlocks { get; private set; }

    /// <summary>Gets members executed through the unrolled runner (slice 2).</summary>
    public ulong SpecRan { get; private set; }

    /// <summary>Maximum retained fallback-shape histogram entries.</summary>
    private const int MaxFallbackShapes = 512;

    private readonly Dictionary<XboxCpu.SpecShape, ulong> _fallbackShapes = [];

    /// <summary>Maximum retained served-shape histogram entries.</summary>
    private const int MaxServedShapes = 512;

    private readonly Dictionary<XboxCpu.SpecShape, ulong> _servedShapes = [];

    /// <summary>
    /// Gets or sets whether specialized-member serves are tallied by shape
    /// (§175 E3-0, diagnostic only). Default off: while disabled the serve
    /// path pays a single field read and every existing mode stays
    /// byte-identical. While enabled the totals equal <see cref="ShapeHits"/>
    /// (fallback members are already tallied separately).
    /// </summary>
    public bool ShapeTallyEnabled { get; set; }

    /// <summary>Gets served-shape execution counts (empty unless <see cref="ShapeTallyEnabled"/>).</summary>
    public IReadOnlyDictionary<XboxCpu.SpecShape, ulong> ServedShapes => _servedShapes;

    /// <summary>Gets the invalidation generation (bumped on every <see cref="Invalidate"/>).</summary>
    public ulong Generation { get; private set; }

    // C3 raw chaining: a pinned copy of Generation readable from emitted code,
    // so a chain edge can validate its target against the live generation even
    // when a store inside the current block invalidated decodes.
    private readonly ulong[] _generationSlot = GC.AllocateArray<ulong>(1, pinned: true);

    /// <summary>Gets the pinned live-generation word (stable address).</summary>
    internal unsafe ulong* GenerationPointer
    {
        get
        {
            if (field is null)
            {
                fixed (ulong* slot = _generationSlot)
                {
                    field = slot;
                }
            }

            return field;
        }
    }

    /// <summary>Gets emitted tail runners built (DynamicMethod construction).</summary>
    public ulong EmitBuilds { get; private set; }

    /// <summary>Gets emitted-tail cache hits (runner reuse).</summary>
    public ulong EmitHits { get; private set; }

    /// <summary>Gets members served through emitted tails.</summary>
    public ulong EmitRan { get; private set; }

    /// <summary>Gets members baked onto E3-1 inline bodies across built runners.</summary>
    public ulong EmitInlineMembers { get; private set; }

    /// <summary>Gets members baked onto the E3-1b IL fast path across built runners.</summary>
    public ulong EmitFastMembers { get; private set; }

    /// <summary>
    /// Gets or sets E3-1 inline bodies for emitted tails (§175, default off).
    /// Host-side configuration set before boot; runners record the flag they
    /// were built with and rebuild on next use when it differs.
    /// </summary>
    public bool EmitInlineEnabled { get; set; }

    /// <summary>
    /// Gets or sets E3-1d diagnostic counters baked into emitted fast paths
    /// (default off). Counters are baked at runner build time, so runners
    /// record the flag and rebuild when it differs.
    /// </summary>
    public bool EmitInlineCountersEnabled { get; set; }

    /// <summary>
    /// Gets fallback-shape execution counts, tallied at serve time while
    /// specialization is enabled (slice-3 measurement). Totals always equal
    /// <see cref="ShapeFallbacks"/>; beyond <see cref="MaxFallbackShapes"/>
    /// distinct shapes only existing keys keep counting.
    /// </summary>
    public IReadOnlyDictionary<XboxCpu.SpecShape, ulong> FallbackShapes => _fallbackShapes;

    /// <summary>
    /// Drops all blocks and the cursor. Used at the existing physical-page and
    /// mapping invalidation choke points, including empty-table flushes. No
    /// generation counter can wrap/ABA: a held cursor is explicitly discarded.
    /// Conservative all-block eviction on a page drop keeps reverse metadata bounded.
    /// </summary>
    public void Invalidate()
    {
        if (_blocks.Count != 0 || _current is not null)
        {
            Invalidations++;
        }

        Generation++;
        _generationSlot[0] = Generation;
        _blocks.Clear();
        _current = null;
        CurrentHandler = null;
    }

    /// <summary>Ends reuse after an interrupt or fault boundary.</summary>
    public void EndBoundary()
    {
        if (_current is not null)
        {
            BoundaryExits++;
            _current = null;
        }

        CurrentHandler = null;
    }

    /// <summary>Records a missed writer and abandons all derived state.</summary>
    public void NoteVerifyFailure()
    {
        VerifyFailures++;
        Invalidate();
    }

    /// <summary>Records one member executed through the unrolled runner.</summary>
    public void NoteSpecRan()
    {
        SpecRan++;
    }

    private void NoteFallbackShape(in DecodedInstruction instruction)
    {
        XboxCpu.SpecShape shape = XboxCpu.SpecShape.From(instruction);
        if (_fallbackShapes.TryGetValue(shape, out ulong count))
        {
            _fallbackShapes[shape] = count + 1;
        }
        else if (_fallbackShapes.Count < MaxFallbackShapes)
        {
            _fallbackShapes[shape] = 1;
        }
    }

    private void NoteServedShape(in DecodedInstruction instruction)
    {
        XboxCpu.SpecShape shape = XboxCpu.SpecShape.From(instruction);
        if (_servedShapes.TryGetValue(shape, out ulong count))
        {
            _servedShapes[shape] = count + 1;
        }
        else if (_servedShapes.Count < MaxServedShapes)
        {
            _servedShapes[shape] = 1;
        }
    }

    /// <summary>
    /// Returns the top served shapes by execution count (diagnostic only;
    /// never on a hot path). Empty unless <see cref="ShapeTallyEnabled"/>.
    /// </summary>
    /// <param name="count">Maximum shapes to return.</param>
    /// <returns>Shapes ordered by descending execution count.</returns>
    public (XboxCpu.SpecShape Shape, ulong Count)[] TopServedShapes(int count)
    {
        var tallies = new List<KeyValuePair<XboxCpu.SpecShape, ulong>>(_servedShapes);
        tallies.Sort(static (a, b) => b.Value.CompareTo(a.Value));
        int take = Math.Min(count, tallies.Count);
        var top = new (XboxCpu.SpecShape Shape, ulong Count)[take];
        for (int i = 0; i < take; i++)
        {
            top[i] = (tallies[i].Key, tallies[i].Value);
        }

        return top;
    }

    /// <summary>
    /// Returns the top fallback shapes by execution count (diagnostic only;
    /// never on a hot path).
    /// </summary>
    /// <param name="count">Maximum shapes to return.</param>
    /// <returns>Shapes ordered by descending execution count.</returns>
    public (XboxCpu.SpecShape Shape, ulong Count)[] TopFallbackShapes(int count)
    {
        var tallies = new List<KeyValuePair<XboxCpu.SpecShape, ulong>>(_fallbackShapes);
        tallies.Sort(static (a, b) => b.Value.CompareTo(a.Value));
        int take = Math.Min(count, tallies.Count);
        var top = new (XboxCpu.SpecShape Shape, ulong Count)[take];
        for (int i = 0; i < take; i++)
        {
            top[i] = (tallies[i].Key, tallies[i].Value);
        }

        return top;
    }

    /// <summary>
    /// Selects one member at an original Step boundary. Only linear addresses
    /// and decode defaults are baked in: EIP, CS and control-register values
    /// are not specialized. Mapping changes ride DecodeCache.Flush; segment
    /// reloads ride its existing changed-base flush. The caller performs IRQ
    /// checks first and executes exactly one unchanged interpreter body.
    /// </summary>
    public bool TryNext(DecodeCache decodes, uint linear, int operandSize, int addressSize, out DecodeCacheEntry entry)
    {
        Block? block = _current;
        if (block is not null && (_expectedLinear != linear || block.OperandSize != operandSize ||
                                  block.AddressSize != addressSize))
        {
            Redirects++;
            block = null;
            _current = null;
            CurrentHandler = null;
        }

        if (block is null)
        {
            if (_blocks.TryGetValue(linear, out block) && block.OperandSize == operandSize &&
                block.AddressSize == addressSize)
            {
                Hits++;
            }
            else
            {
                block = Form(decodes, linear, operandSize, addressSize);
                if (block is null)
                {
                    entry = default;
                    CurrentHandler = null;
                    return false;
                }

                if (_blocks.Count >= MaxBlocks)
                {
                    Invalidate();
                    CapFlushes++;
                }

                _blocks[linear] = block;
                Builds++;
                if (block.AllSpecialized)
                {
                    SpecBlocks++;
                }

                PeakCount = Math.Max(PeakCount, _blocks.Count);
            }

            Dispatches++;
            block.Visits++;
            _current = block;
            _index = 0;
        }

        entry = block.Entries[_index];
        CurrentHandler = block.Handlers is { } handlers ? handlers[_index] : null;
        if (SpecEnabled)
        {
            if (CurrentHandler is null)
            {
                ShapeFallbacks++;
                NoteFallbackShape(entry.Instruction);
            }
            else
            {
                ShapeHits++;
                if (ShapeTallyEnabled)
                {
                    NoteServedShape(entry.Instruction);
                }
            }
        }

        _index++;
        _expectedLinear = linear + (uint)entry.Instruction.Length;
        Instructions++;
        if (_index == block.Entries.Length)
        {
            Completed++;
            _current = null;
        }

        return true;
    }

    private Block? Form(DecodeCache decodes, uint linear, int operandSize, int addressSize)
    {
        Span<DecodeCacheEntry> entries = stackalloc DecodeCacheEntry[MaxInstructions];
        int count = 0;
        uint next = linear;

        // D3b: spec blocks may span a page boundary (the decode cache carries
        // cross-page entries since D3a, the runner's window replay and verify
        // both go through entry.PhysicalOf(i), and invalidation is the
        // conservative all-block eviction). The old single-page and 15-byte
        // window guards made every head in the last 14 bytes of a page fall
        // through to the ~500 ns interpreter Step path.
        while (count < MaxInstructions
               && decodes.TryGet(next, operandSize, addressSize, out DecodeCacheEntry entry))
        {
            entries[count++] = entry;
            if (Terminates(entry.Instruction))
            {
                break;
            }

            next += (uint)entry.Instruction.Length;
        }

        if (count == 0)
        {
            return null;
        }

        XboxCpu.SpecHandler?[]? handlers = null;
        bool allSpecialized = false;
        if (SpecEnabled)
        {
            handlers = new XboxCpu.SpecHandler?[count];
            allSpecialized = true;
            for (int i = 0; i < count; i++)
            {
                XboxCpu.SpecHandler? handler = XboxCpu.ResolveSpec(entries[i].Instruction);
                handlers[i] = handler;
                if (handler is null)
                {
                    allSpecialized = false;
                }
            }
        }

        uint[]? linears = null;
        if (allSpecialized)
        {
            linears = new uint[count];
            uint memberLinear = linear;
            for (int i = 0; i < count; i++)
            {
                linears[i] = memberLinear;
                memberLinear += (uint)entries[i].Instruction.Length;
            }
        }

        return new Block(entries[..count].ToArray(), operandSize, addressSize, handlers, allSpecialized, linear, linears);
    }

    /// <summary>
    /// Conservative slice-4 formation (extended by slices 10-20): only
    /// common straight-line integer/data operations may continue, now
    /// including the MMX 64-bit moves (pure data through the x87-aliased
    /// file), the Prefetch/Fence hints (interpreter nops), scalar SSE
    /// arithmetic/converts (single-helper verbatim bodies), x87
    /// loads/stores/integer moves plus ST(0) arithmetic (read-before-
    /// mutate, EIP-precise), the slice-14 reg-forms, the slice-16
    /// remainder, the slice-17 compares/unary/tag-control, and the
    /// slice-18 packed-SSE moves (verbatim read-before-write mirrors),
    /// and the slice-20 remainder (carry-rotates via the shared shift
    /// helper, sign-extension, flag push/pop, conditional set/move,
    /// timestamp), plus slice-5 Cli/Sti (straight-line IF-flag writes,
    /// EIP-precise, still fallback-interpreted: interrupt delivery is
    /// polled before every instruction in both Step and RunSpecBlock, so
    /// merging blocks across them moves no delivery point — it only
    /// erases the 1-instruction boundary dispatches, which are 95-100%
    /// of the remaining fallback tail), plus slice-7 Fldcw/Fnstcw/Fwait/
    /// Emms (straight-line FPU-control one-liners, verbatim mirrors). Every branch, trap, system/mode, string/REP, I/O and
    /// unlisted instruction ends the block (including future mnemonics).
    /// Memory faults are never predicted; they exit at the original boundary.
    /// </summary>
    public static bool Terminates(in DecodedInstruction instruction)
    {
        if (!instruction.IsValid || instruction.Length <= 0)
        {
            return true;
        }

        // Slice 12: the F3 scalar-SSE shapes carry Rep==Rep (the 0xF3 byte
        // is their mandatory opcode prefix, never a repeat) but are pure
        // straight-line data ops, so they continue like the whitelist below.
        // Every other Rep-prefixed instruction still terminates.
        return (instruction.Rep != RepPrefix.None && !XboxCpu.IsMandatoryF3Scalar(instruction.Mnemonic))
            || instruction.Mnemonic is not (
            Mnemonic.Nop or Mnemonic.Mov or Mnemonic.Push or Mnemonic.Pop or Mnemonic.Xchg or Mnemonic.Lea
            or Mnemonic.Add or Mnemonic.Or or Mnemonic.Adc or Mnemonic.Sbb or Mnemonic.And or Mnemonic.Sub
            or Mnemonic.Xor or Mnemonic.Cmp or Mnemonic.Inc or Mnemonic.Dec or Mnemonic.Test or Mnemonic.Not
            or Mnemonic.Neg or Mnemonic.Clc or Mnemonic.Stc or Mnemonic.Cld or Mnemonic.Std or Mnemonic.Cmc
            or Mnemonic.Pushf or Mnemonic.Shl or Mnemonic.Shr or Mnemonic.Sar or Mnemonic.Rol or Mnemonic.Ror
            or Mnemonic.Rcl or Mnemonic.Rcr or Mnemonic.Mul or Mnemonic.Imul or Mnemonic.Movzx or Mnemonic.Movsx
            or Mnemonic.Setcc or Mnemonic.Cmovcc or Mnemonic.Leave or Mnemonic.Enter or Mnemonic.Bswap
            or Mnemonic.Xadd or Mnemonic.Cmpxchg or Mnemonic.Bsf or Mnemonic.Bsr or Mnemonic.Bt
            or Mnemonic.Bts or Mnemonic.Btr or Mnemonic.Btc or Mnemonic.Cbw or Mnemonic.Cdq
            or Mnemonic.Shld or Mnemonic.Shrd or Mnemonic.Cmpxchg8B or Mnemonic.Movq or Mnemonic.Movntq
            or Mnemonic.Prefetch or Mnemonic.Sfence or Mnemonic.Lfence or Mnemonic.Mfence
            or Mnemonic.Addss or Mnemonic.Subss or Mnemonic.Mulss or Mnemonic.Divss
            or Mnemonic.Minss or Mnemonic.Maxss or Mnemonic.Cvtss2Si or Mnemonic.Cvttss2Si
            or Mnemonic.Fld or Mnemonic.Fst or Mnemonic.Fstp or Mnemonic.Fild or Mnemonic.Fistp
            or Mnemonic.Fadd or Mnemonic.Fmul or Mnemonic.Fsub or Mnemonic.Fsubr
            or Mnemonic.Fdiv or Mnemonic.Fdivr or Mnemonic.Fxch
            or Mnemonic.Faddp or Mnemonic.Fmulp or Mnemonic.Fsubp or Mnemonic.Fsubrp
            or Mnemonic.Fdivp or Mnemonic.Fdivrp
            or Mnemonic.Fld1 or Mnemonic.Fldl2T or Mnemonic.Fldl2E or Mnemonic.Fldpi
            or Mnemonic.Fldlg2 or Mnemonic.Fldln2 or Mnemonic.Fldz
            or Mnemonic.Div or Mnemonic.Idiv
            or Mnemonic.Fiadd or Mnemonic.Fimul or Mnemonic.Fisub or Mnemonic.Fisubr
            or Mnemonic.Fidiv or Mnemonic.Fidivr or Mnemonic.Fnstsw
            or Mnemonic.Fcom or Mnemonic.Fcomp or Mnemonic.Fucom or Mnemonic.Fucomp
            or Mnemonic.Ficom or Mnemonic.Ficomp
            or Mnemonic.Fchs or Mnemonic.Fabs or Mnemonic.Ftst
            or Mnemonic.Ffree or Mnemonic.Ffreep or Mnemonic.Fdecstp or Mnemonic.Fincstp
            or Mnemonic.Fnclex or Mnemonic.Fninit
            or Mnemonic.Movaps or Mnemonic.Movups or Mnemonic.Movss
            or Mnemonic.Movlps or Mnemonic.Movhps or Mnemonic.Movhlps or Mnemonic.Movlhps
            or Mnemonic.Movntps or Mnemonic.Movmskps
            or Mnemonic.Sqrtps or Mnemonic.Sqrtss or Mnemonic.Rsqrtps or Mnemonic.Rsqrtss
            or Mnemonic.Rcpps or Mnemonic.Rcpss
            or Mnemonic.Addps or Mnemonic.Subps or Mnemonic.Mulps or Mnemonic.Divps
            or Mnemonic.Minps or Mnemonic.Maxps
            or Mnemonic.Andps or Mnemonic.Andnps or Mnemonic.Orps or Mnemonic.Xorps
            or Mnemonic.Cmpps or Mnemonic.Cmpss or Mnemonic.Comiss or Mnemonic.Ucomiss
            or Mnemonic.Popf or Mnemonic.Rdtsc
            or Mnemonic.Cli or Mnemonic.Sti
            or Mnemonic.Fldcw or Mnemonic.Fnstcw or Mnemonic.Fwait or Mnemonic.Emms);
    }

    private sealed record Block(
        DecodeCacheEntry[] Entries,
        int OperandSize,
        int AddressSize,
        XboxCpu.SpecHandler?[]? Handlers,
        bool AllSpecialized,
        uint HeadLinear,
        uint[]? EmitLinears)
    {
        /// <summary>Head selections served (tiering visits for lazy emission).</summary>
        public ulong Visits;

        /// <summary>Cached tail runner (null until built).</summary>
        public EmitTailRunner? Runner;

        /// <summary>Invalidation generation the cached runner was built for.</summary>
        public ulong RunnerGeneration;

        /// <summary>Whether the cached runner was built with E3-1 inline bodies.</summary>
        public bool RunnerInline;

        /// <summary>Whether the cached runner was built with E3-1d diagnostic counters.</summary>
        public bool RunnerCounters;
    }

    /// <summary>
    /// Head visits before a block's tail runner is built (§172 tiering).
    /// Cold single-visit code never pays DynamicMethod construction; only
    /// hot blocks amortize it. Member-path service is exact at every visit,
    /// so the threshold changes only the serve vehicle, never behavior.
    /// </summary>
    public const int EmitThreshold = 1024;

    /// <summary>
    /// Opens the current cursor's tail for emitted service: the cursor must be
    /// active exactly one member in (member 0 just ran through the member
    /// path) on a fully-specialized block whose head crossed the tiering
    /// threshold. Start &gt; 1 (after a mid-block bail) stays on the member
    /// path, as do pre-threshold visits.
    /// </summary>
    internal bool TryBeginTail(
        out DecodeCacheEntry[] entries,
        out EmitTailRunner? runner)
    {
        Block? block = _current;
        if (block is not null && SpecEnabled && _index == 1 && block.AllSpecialized
            && block.Handlers is not null && block.EmitLinears is not null && block.Entries.Length >= 2
            && block.Visits >= EmitThreshold)
        {
            entries = block.Entries;
            if (block.Runner is { } cached && block.RunnerGeneration == Generation
                && block.RunnerInline == EmitInlineEnabled
                && block.RunnerCounters == EmitInlineCountersEnabled)
            {
                EmitHits++;
                runner = cached;
                return true;
            }

            runner = BlockEmitter.Build(block.HeadLinear, block.Entries, block.Handlers, block.EmitLinears,
                EmitInlineEnabled, EmitInlineCountersEnabled, out int inlineMembers, out int fastMembers);
            if (runner is null)
            {
                entries = null!;
                return false;
            }

            block.Runner = runner;
            block.RunnerGeneration = Generation;
            block.RunnerInline = EmitInlineEnabled;
            block.RunnerCounters = EmitInlineCountersEnabled;
            EmitInlineMembers += (ulong)inlineMembers;
            EmitFastMembers += (ulong)fastMembers;
            EmitBuilds++;
            return true;
        }

        entries = null!;
        runner = null;
        return false;
    }

    /// <summary>
    /// Accounts for tail members served by an emitted runner: decode hits,
    /// block instructions, specialization tallies and the cursor advance. Safe
    /// when the cursor is already gone (a mid-tail store invalidated the
    /// table): served members still count their hits and instructions.
    /// </summary>
    internal void NoteEmittedRan(int count, DecodeCache cache)
    {
        for (int i = 0; i < count; i++)
        {
            cache.NoteHit();
        }

        if (count <= 0)
        {
            return;
        }

        Instructions += (ulong)count;
        ShapeHits += (ulong)count;
        SpecRan += (ulong)count;
        EmitRan += (ulong)count;

        Block? block = _current;
        if (block is null)
        {
            return;
        }

        int start = _index;
        _index += count;
        for (int k = start; k < _index; k++)
        {
            _expectedLinear += (uint)block.Entries[k].Instruction.Length;
        }

        if (_index == block.Entries.Length)
        {
            Completed++;
            _current = null;
        }
    }

    /// <summary>
    /// Accounts for the in-flight member of an emitted-tail fault/pending
    /// exit: the member the runner was serving when it bailed. Mirrors the
    /// member path's <see cref="TryNext"/> counting for that member
    /// (Instructions, ShapeHits and the cursor advance). The decode hit and
    /// SpecRan follow the member path's order: the IRQ poll precedes the hit
    /// (so a pending exit earns no hit), while a faulting body runs after it
    /// (so a fault exit earns both the hit and SpecRan). Called before the
    /// exit's EndBoundary so the cursor state (including completion) matches
    /// the member path exactly.
    /// </summary>
    /// <param name="cache">The decode cache (hit accounting).</param>
    /// <param name="countBody">True for fault exits (body entered).</param>
    internal void NoteEmittedExit(DecodeCache cache, bool countBody)
    {
        Block? block = _current;
        if (block is null)
        {
            return;
        }

        Instructions++;
        ShapeHits++;
        if (countBody)
        {
            cache.NoteHit();
            SpecRan++;
        }

        _expectedLinear += (uint)block.Entries[_index].Instruction.Length;
        _index++;
        if (_index == block.Entries.Length)
        {
            Completed++;
            _current = null;
        }
    }

    /// <summary>
    /// Records an emitted-tail EIP mismatch: the cursor is dropped without
    /// invalidating the retained block, exactly as the <see cref="TryNext"/>
    /// redirect path.
    /// </summary>
    internal void NoteRedirect()
    {
        Redirects++;
        _current = null;
        CurrentHandler = null;
    }
}
