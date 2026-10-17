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
    public const int MaxInstructions = 32;

    /// <summary>Maximum retained blocks (and hence at most 131072 decoded references).</summary>
    public const int MaxBlocks = 4096;

    private readonly Dictionary<uint, Block> _blocks = new();
    private Block? _current;
    private int _index;
    private uint _expectedLinear;
    private XboxCpu.SpecHandler? _currentHandler;
    private bool _specEnabled;

    /// <summary>
    /// Gets or sets whether formation resolves pre-specialized per-shape
    /// handlers (§139). Off by default: formation stores entries only and the
    /// interpreter path is unchanged. Enabling drops any handler-less blocks
    /// so every retained block carries its resolved handlers.
    /// </summary>
    public bool SpecEnabled
    {
        get => _specEnabled;
        set
        {
            if (value && !_specEnabled)
            {
                Invalidate();
            }

            _specEnabled = value;
        }
    }

    /// <summary>Gets the handler for the member just served by <see cref="TryNext"/> (null when disabled or fallback).</summary>
    public XboxCpu.SpecHandler? CurrentHandler => _currentHandler;

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
    public const int MaxFallbackShapes = 512;

    private readonly Dictionary<XboxCpu.SpecShape, ulong> _fallbackShapes = new();

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

        _blocks.Clear();
        _current = null;
        _currentHandler = null;
    }

    /// <summary>Ends reuse after an interrupt or fault boundary.</summary>
    public void EndBoundary()
    {
        if (_current is not null)
        {
            BoundaryExits++;
            _current = null;
        }

        _currentHandler = null;
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
            _currentHandler = null;
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
                    _currentHandler = null;
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
            _current = block;
            _index = 0;
        }

        entry = block.Entries[_index];
        _currentHandler = block.Handlers is { } handlers ? handlers[_index] : null;
        if (_specEnabled)
        {
            if (_currentHandler is null)
            {
                ShapeFallbacks++;
                NoteFallbackShape(entry.Instruction);
            }
            else
            {
                ShapeHits++;
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
        uint physicalPage = 0;
        while (count < MaxInstructions && (next >> 12) == (linear >> 12)
                                       && (next & 0xFFF) <= 0x1000 - Decoder.MaxInstructionLength
                                       && decodes.TryGet(next, operandSize, addressSize, out DecodeCacheEntry entry))
        {
            if (count == 0)
            {
                physicalPage = entry.PhysicalBase >> 12;
            }
            else if ((entry.PhysicalBase >> 12) != physicalPage)
            {
                break;
            }

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
        if (_specEnabled)
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

        return new Block(entries[..count].ToArray(), operandSize, addressSize, handlers, allSpecialized);
    }

    /// <summary>
    /// Conservative slice-4 formation (extended by slices 10-13): only
    /// common straight-line integer/data operations may continue, now
    /// including the MMX 64-bit moves (pure data through the x87-aliased
    /// file), the Prefetch/Fence hints (interpreter nops), scalar SSE
    /// arithmetic/converts (single-helper verbatim bodies) and x87
    /// loads/stores/integer moves plus ST(0) add/multiply (read-before-
    /// mutate, EIP-precise). Every branch, trap, system/mode, string/REP,
    /// I/O and unlisted instruction ends the block (including future
    /// mnemonics).
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
        if (instruction.Rep != RepPrefix.None && !XboxCpu.IsMandatoryF3Scalar(instruction.Mnemonic))
        {
            return true;
        }

        return instruction.Mnemonic is not (
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
            or Mnemonic.Fadd or Mnemonic.Fmul);
    }

    private sealed record Block(
        DecodeCacheEntry[] Entries,
        int OperandSize,
        int AddressSize,
        XboxCpu.SpecHandler?[]? Handlers,
        bool AllSpecialized);
}
