using System;
using System.Diagnostics.CodeAnalysis;
using XGrid.Core.Devices;
using XGrid.Memory;

namespace XGrid.Cpu;

/// <summary>Interpreted Pentium III-class CPU (plan section 8).</summary>
[SuppressMessage("Performance", "CA1822:Mark members as static")]
public sealed partial class XboxCpu
{
    private uint _sysenterCs;
    private uint _sysenterEsp;
    private uint _sysenterEip;
    private IHotspotObserver? _hotspot;

    // Memory-path slice 1 (§161): RAM-direct fast path. When the bus is the
    // standard AddressSpace, its RAM is cached here so physical accesses can
    // skip the IMemoryBus interface dispatch + region-table branch and hit
    // the same Ram methods the bus would call. The bus object (and RAM size)
    // is fixed for the CPU's lifetime — snapshots reload RAM contents in
    // place — so the cache never goes stale. Null (non-AddressSpace bus)
    // means the legacy bus path everywhere.
    private readonly PhysicalMemory? _ramDirect;
    private readonly AddressSpace? _ramSpace;
    private readonly uint _ramSize;

    // Memory-path slice 3 (§162): 4-entry direct-mapped data-translate
    // cache (linear VPN tag → physical 4 KB-slice base + PTE writable
    // verdict). A hit skips the two Translate call layers + SoftTLB lookup;
    // a miss populates one slot via TranslateDataFast (same core, same side
    // effects, no observer notes — hence the FastWideAllowed gate, shared
    // with slices 1–2: with any profiler attached the legacy path keeps
    // idle/TLB tallies exact). Invalid entries carry tag uint.MaxValue (a
    // 20-bit VPN never matches); _dataXlatArmed lets the store hook skip the
    // page-table check when the cache is empty. Cleared on every mapping
    // change (OnMmuFlush/OnMmuFlushPage) and on any CPU store to a tracked
    // page-table page (same signal that full-flushes the decode cache —
    // guest PTE rewrites must take effect; the MMU's own A/D-bit writes
    // never remap, and device/DMA page-table writes are outside the
    // SoftTLB's contract too). Fetch (isInstruction) never consults it.
    private readonly uint[] _dataXlatTag = [uint.MaxValue, uint.MaxValue, uint.MaxValue, uint.MaxValue];
    private readonly uint[] _dataXlatPhys = new uint[4];
    private readonly bool[] _dataXlatWritable = new bool[4];
    private bool _dataXlatArmed;

    /// <summary>Creates a CPU over the given memory and I/O buses.</summary>
    public XboxCpu(IMemoryBus memory, IIoBus? ioBus = null)
    {
        Memory = memory ?? throw new ArgumentNullException(nameof(memory));
        Io = ioBus ?? NullIoBus.Instance;
        Context = new XboxContext();
        Mmu = new Mmu(Context, memory);
        _ramSpace = memory as AddressSpace;
        _ramDirect = _ramSpace?.Ram;
        _ramSize = _ramDirect?.Size ?? 0;

        // Every mapping-change site funnels through Mmu.Flush/FlushPage (CR
        // writes, INVD/WBINVD, INVLPG, Reset, snapshot load); the decode-cache
        // full flush rides the same choke point (null-check no-op when off).
        Mmu.OnFlush = OnMmuFlush;
        Mmu.OnFlushPage = OnMmuFlushPage;

        // Slice-3: the data-translate cache needs the page-table-page signal
        // in every mode (including --no-decode-cache, whose setter leaves
        // tracking on for this reason), so a guest PTE rewrite always
        // invalidates cached translations. Tracking is host-side only.
        Mmu.PageTableTrackingEnabled = true;
    }

    /// <summary>Gets the architectural state.</summary>
    public XboxContext Context { get; }

    /// <summary>Gets the memory bus.</summary>
    public IMemoryBus Memory { get; }

    /// <summary>Gets the I/O port bus.</summary>
    public IIoBus Io { get; }

    /// <summary>Gets or sets the external interrupt controller (PIC) acknowledged when IF is set.</summary>
    public IInterruptSource? InterruptSource { get; set; }

    /// <summary>Gets the number of hardware interrupts delivered to the guest.</summary>
    public int InterruptCount { get; private set; }

    /// <summary>Gets the paging unit.</summary>
    public Mmu Mmu { get; }

    /// <summary>
    /// Gets or sets the optional hotspot profiler (§126). Null (default) leaves
    /// the interpreter path byte-identical; when set, the CPU reports every
    /// executed instruction, fetch window, SoftTLB outcome, and translated
    /// data access. Host-side measurement state: untouched by
    /// <see cref="Reset"/> and snapshot save/load.
    /// </summary>
    public IHotspotObserver? HotspotObserver
    {
        get => _hotspot;
        set
        {
            _hotspot = value;
            Mmu.Observer = value;
        }
    }

    /// <summary>
    /// Gets or sets whether the single-translate fetch fast path is enabled
    /// (§131 leg (c)). Default off: the interpreter path is byte-identical
    /// to the legacy core. When on, a fetch window lying within one 4 KB
    /// page is translated once instead of per byte; page-crossing windows
    /// take the legacy per-byte path. Host-side configuration: untouched by
    /// <see cref="Reset"/> and snapshot save/load (like
    /// <see cref="HotspotObserver"/>).
    /// </summary>
    public bool FetchFastPathEnabled { get; set; }

    /// <summary>Gets the vector of the most recent guest fault delivered to the IDT (0 if none).</summary>
    public int LastFaultVector { get; private set; }

    /// <summary>Gets the EIP of the most recent guest fault delivered to the IDT (0 if none).</summary>
    public uint LastFaultEip { get; private set; }

    /// <summary>Gets the error code of the most recent guest fault (0 if none or not applicable).</summary>
    public uint LastFaultErrorCode { get; private set; }

    /// <summary>Returns the CPU to its power-on state.</summary>
    public void Reset()
    {
        Context.Reset();
        Mmu.Flush();
        _jitBlocks?.Invalidate();
        LastFaultVector = 0;
        LastFaultEip = 0;
        LastFaultErrorCode = 0;
        _idle?.HardReset();
        _idleAdvanced = 0;
    }

    /// <summary>Fetches, decodes, and executes one instruction.</summary>
    public void Step()
    {
        try
        {
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
                        _blocks?.EndBoundary();
                        return;
                    }
                }
            }

            Span<byte> buffer = stackalloc byte[Decoder.MaxInstructionLength];
            DecodedInstruction instruction;
            if (_decodeCache is null)
            {
                // Flag off (default): byte-identical legacy path.
                Fetch(buffer);
                instruction = Decoder.Decode(
                    buffer,
                    Context.DefaultOperandSize,
                    Context.DefaultAddressSize);
                DecodeCount++;
            }
            else
            {
                instruction = FetchDecodeCached(_decodeCache, buffer, out SpecHandler? spec);
                if (spec is not null && instruction.IsValid)
                {
                    // Slice 1 (§139): the cursor's pre-specialized body for
                    // this exact member. Boundary, fault and observation
                    // treatment below is unchanged.
                    uint specEipBefore = Context.Eip;
                    spec(this, instruction);
                    _idle?.Observe(instruction, specEipBefore, Context.Eip);
                    _hotspot?.Observe(instruction, specEipBefore, Context.Eip);
                    return;
                }
            }

            if (!instruction.IsValid)
            {
                _blocks?.EndBoundary();
                LastFaultVector = 6;
                LastFaultEip = Context.Eip;
                LastFaultErrorCode = 0;
                if (!TryTransferToInterrupt(6, Context.Eip, false, 0))
                {
                    throw new UnsupportedInstructionException(Context.Eip, instruction.Opcode);
                }

                return;
            }

            uint eipBefore = Context.Eip;
            ExecuteInstruction(instruction);
            _idle?.Observe(instruction, eipBefore, Context.Eip);
            _hotspot?.Observe(instruction, eipBefore, Context.Eip);
        }
        catch (PageFaultSignal fault)
        {
            _blocks?.EndBoundary();
            Context.Cr2 = fault.LinearAddress;
            LastFaultVector = 14;
            LastFaultEip = Context.Eip;
            LastFaultErrorCode = fault.ErrorCode;
            if (!TryTransferToInterrupt(14, Context.Eip, true, fault.ErrorCode))
            {
                throw new UnhandledGuestException(14, Context.Eip);
            }
        }
    }

    /// <summary>Executes instructions until the limit is reached or the CPU halts.</summary>
    /// <returns>The number of instructions executed (including any idle-skipped steps).</returns>
    public int Execute(int maxInstructions)
    {
        int executed = 0;
        _idleAdvanced = 0;
        while (executed < maxInstructions && !Context.Halted)
        {
            if (_idle is { TriggerPending: true })
            {
                if (Context.Eip != _idle.Head)
                {
                    // Control left the loop before the batch boundary consumed the
                    // trigger (e.g. an interrupt was delivered): drop it.
                    _idle.NoteTriggerDropped();
                    _idle.ClearTrigger();
                }
                else
                {
                    int skipped = RunIdleSkip(maxInstructions - executed);
                    if (_idle.TriggerPending)
                    {
                        // Budget exhausted before any skip could run; keep the
                        // trigger for the next Execute call.
                        break;
                    }

                    executed += skipped;
                    continue;
                }
            }

            if (_jitEnabled)
            {
                // Fast-mode JIT (opt-in): serve whole blocks natively. A zero
                // return means no block covers this head; the member paths
                // below own the miss (and the bootstrap fills the cache).
                long jitStart = System.Diagnostics.Stopwatch.GetTimestamp();
                int jitRan = RunJitBlock(maxInstructions - executed);
                if (jitRan > 0)
                {
                    AddJitOuterTicks(System.Diagnostics.Stopwatch.GetTimestamp() - jitStart);
                    executed += jitRan;
                    continue;
                }

                AddJitOuterTicks(System.Diagnostics.Stopwatch.GetTimestamp() - jitStart);
                long nonJitStart = System.Diagnostics.Stopwatch.GetTimestamp();
                int nonJitRan = RunNonJit(maxInstructions - executed);
                AddNonJitTicks(System.Diagnostics.Stopwatch.GetTimestamp() - nonJitStart);
                NoteNonJitRun(nonJitRan);
                executed += nonJitRan;
                continue;
            }

            if (_blocks is { SpecEnabled: true })
            {
                // Slice 2 (§139): serve blocked members through the unrolled
                // runner. Only used inside Execute (trace/break-log callers use
                // Step and keep the member-cursor path).
                int specRan = RunSpecBlock(maxInstructions - executed);
                if (specRan > 0)
                {
                    executed += specRan;
                    continue;
                }
            }

            Step();
            executed++;
        }

        return executed;
    }

    /// <summary>
    /// The non-JIT fallback inside <see cref="Execute"/> while the fast-mode
    /// engine is enabled: the spec-block runner (sliced so the JIT gets a
    /// chance at the head every <see cref="JitSpecSlice"/> instructions),
    /// else one stepped instruction. Always makes progress.
    /// </summary>
    private int RunNonJit(int budget)
    {
        if (_blocks is { SpecEnabled: true })
        {
            int slice = JitSpecSlice > 0 && JitSpecSlice < budget ? JitSpecSlice : budget;
            int specRan = RunSpecBlock(slice);
            if (specRan > 0)
            {
                return specRan;
            }
        }

        Step();
        return 1;
    }

    private void Fetch(Span<byte> buffer)
    {
        uint segmentBase = Context.GetSegmentBase(SegmentRegister.Cs);
        uint fetchLinear = segmentBase + Context.Eip;
        bool crossesPage = (fetchLinear & 0xFFFu) + (uint)buffer.Length > 0x1000u;
        if (FetchFastPathEnabled && !crossesPage)
        {
            FetchFast(fetchLinear, buffer);
            return;
        }

        IHotspotObserver? observer = _hotspot;
        if (observer is null)
        {
            FetchLegacy(segmentBase, buffer);
            return;
        }

        if (FetchFastPathEnabled)
        {
            observer.NoteFetchSlow();
        }

        FetchCounted(observer, segmentBase, buffer, crossesPage);
    }

    /// <summary>
    /// Flag-off fetch loop (byte-identical legacy per-byte translate + read;
    /// extracted verbatim so the decode-cache miss path shares it).
    /// </summary>
    private void FetchLegacy(uint segmentBase, Span<byte> buffer)
    {
        for (int i = 0; i < buffer.Length; i++)
        {
            uint linear = segmentBase + Context.Eip + (uint)i;
            buffer[i] = Memory.Read8(TranslateLinear(linear, isWrite: false, isInstruction: true));
        }
    }

    /// <summary>
    /// Single-translate fetch (§131 leg (c)). The caller guarantees the
    /// window lies within one 4 KB page, hence within one 4 MB region, so a
    /// single lookup resolves every byte: a SoftTLB hit for 4 KB pages, one
    /// directory walk for 4 MB large pages (large-page residency is the
    /// whole-page win — large pages never sit in the TLB, reported as one
    /// miss per the observer contract). Faults, A/D side effects, TLB
    /// inserts, per-byte idle-skip access notes and bus reads are identical
    /// to the slow path: every byte shares one page mapping, so the slow
    /// path always faults on byte 0 with no prior access notes — exactly
    /// what the single translate reports.
    /// </summary>
    /// <returns>The physical address of fetch-window byte 0 (decode cache fill, §131 leg (d)).</returns>
    private uint FetchFast(uint fetchLinear, Span<byte> buffer)
    {
        _hotspot?.NoteFetch(false);
        uint basePhysical = Mmu.Translate(fetchLinear, isWrite: false, isInstruction: true, out bool faulted,
            out uint errorCode);
        if (faulted)
        {
            throw new PageFaultSignal(fetchLinear, errorCode);
        }

        for (int i = 0; i < buffer.Length; i++)
        {
            uint physical = basePhysical + (uint)i;
            _idle?.NoteAccess(physical, isWrite: false, isInstruction: true);
            buffer[i] = Memory.Read8(physical);
        }

        return basePhysical;
    }

    /// <summary>
    /// Profiler-on fetch: byte-identical translates with batched TLB tallies
    /// (§131 leg (a)). The reported totals equal the fifteen per-byte
    /// outcomes. Each iteration accumulates its outcome before any fault is
    /// raised (a throwing call never returns its outs to the faulting frame,
    /// so the batch cannot rely on try/finally alone); the finally only
    /// covers iterations that already returned.
    /// </summary>
    private void FetchCounted(IHotspotObserver observer, uint segmentBase, Span<byte> buffer, bool crossesPage)
    {
        observer.NoteFetch(crossesPage);
        int tlbHits = 0;
        int tlbMisses = 0;
        try
        {
            for (int i = 0; i < buffer.Length; i++)
            {
                uint linear = segmentBase + Context.Eip + (uint)i;
                uint physical = TranslateLinearCounted(linear, out bool tlbHit, out bool tlbCounted, out bool faulted,
                    out uint errorCode);
                if (tlbCounted)
                {
                    if (tlbHit)
                    {
                        tlbHits++;
                    }
                    else
                    {
                        tlbMisses++;
                    }
                }

                if (faulted)
                {
                    throw new PageFaultSignal(linear, errorCode);
                }

                _idle?.NoteAccess(physical, isWrite: false, isInstruction: true);
                buffer[i] = Memory.Read8(physical);
            }
        }
        finally
        {
            if (tlbHits != 0)
            {
                observer.NoteTlbHits(tlbHits);
            }

            if (tlbMisses != 0)
            {
                observer.NoteTlbMisses(tlbMisses);
            }
        }
    }

    private void ExecuteInstruction(in DecodedInstruction instruction)
    {
        uint nextEip = Context.Eip + (uint)instruction.Length;

        switch (instruction.Mnemonic)
        {
            case Mnemonic.Nop:
                break;

            case Mnemonic.Mov:
                WriteOperand(instruction.Operand1, ReadOperand(instruction.Operand2));
                break;

            case Mnemonic.Lea:
                WriteOperand(instruction.Operand1, ComputeAddress(instruction.Operand2.Memory));
                break;

            case Mnemonic.Xchg:
                {
                    uint first = ReadOperand(instruction.Operand1);
                    uint second = ReadOperand(instruction.Operand2);
                    WriteOperand(instruction.Operand1, second);
                    WriteOperand(instruction.Operand2, first);
                    break;
                }

            case Mnemonic.Push:
                Push(ReadOperand(instruction.Operand1), instruction.Operand1.Size, instruction.AddressSize);
                break;

            case Mnemonic.Pop:
                WriteOperand(instruction.Operand1, Pop(instruction.Operand1.Size, instruction.AddressSize));
                break;

            case Mnemonic.Add:
            case Mnemonic.Or:
            case Mnemonic.Adc:
            case Mnemonic.Sbb:
            case Mnemonic.And:
            case Mnemonic.Sub:
            case Mnemonic.Xor:
            case Mnemonic.Cmp:
                ExecuteAlu(instruction);
                break;

            case Mnemonic.Inc:
                {
                    uint value = ReadOperand(instruction.Operand1);
                    Context.Eflags = AluOps.Inc(value, instruction.Operand1.Size, Context.Eflags, out uint result);
                    WriteOperand(instruction.Operand1, result);
                    break;
                }

            case Mnemonic.Dec:
                {
                    uint value = ReadOperand(instruction.Operand1);
                    Context.Eflags = AluOps.Dec(value, instruction.Operand1.Size, Context.Eflags, out uint result);
                    WriteOperand(instruction.Operand1, result);
                    break;
                }

            case Mnemonic.Test:
                {
                    uint value = ReadOperand(instruction.Operand1) & ReadOperand(instruction.Operand2);
                    value &= AluOps.Mask(instruction.Operand1.Size);
                    Context.Eflags = AluOps.Logical(value, instruction.Operand1.Size, Context.Eflags);
                    break;
                }

            case Mnemonic.Not:
                WriteOperand(instruction.Operand1, ~ReadOperand(instruction.Operand1));
                break;

            case Mnemonic.Neg:
                {
                    uint value = ReadOperand(instruction.Operand1);
                    Context.Eflags = AluOps.Neg(value, instruction.Operand1.Size, Context.Eflags, out uint result);
                    WriteOperand(instruction.Operand1, result);
                    break;
                }

            case Mnemonic.Jmp:
                Context.Eip = instruction.Operand1.Kind == OperandKind.Immediate
                    ? nextEip + instruction.Operand1.Immediate
                    : ReadOperand(instruction.Operand1);
                return;

            case Mnemonic.Jcc:
                if (EvaluateCondition(instruction.Condition))
                {
                    Context.Eip = nextEip + instruction.Operand1.Immediate;
                    return;
                }

                break;

            case Mnemonic.Call:
                {
                    uint target = instruction.Operand1.Kind == OperandKind.Immediate
                        ? nextEip + instruction.Operand1.Immediate
                        : ReadOperand(instruction.Operand1);
                    Push(nextEip, instruction.OperandSize, instruction.AddressSize);
                    Context.Eip = target;
                    return;
                }

            case Mnemonic.Ret:
                Context.Eip = Pop(instruction.OperandSize, instruction.AddressSize);
                if (instruction.Operand1.Kind == OperandKind.Immediate)
                {
                    AdjustStackPointer(instruction.Operand1.Immediate, instruction.AddressSize);
                }

                return;

            case Mnemonic.JmpFar:
                {
                    (uint offset, ushort selector) = ReadFarTarget(instruction);
                    LoadSegment(SegmentRegister.Cs, selector);
                    Context.Eip = offset;
                    return;
                }

            case Mnemonic.CallFar:
                {
                    (uint offset, ushort selector) = ReadFarTarget(instruction);
                    Push(Context.GetSegmentSelector(SegmentRegister.Cs), instruction.OperandSize, instruction.AddressSize);
                    Push(nextEip, instruction.OperandSize, instruction.AddressSize);
                    LoadSegment(SegmentRegister.Cs, selector);
                    Context.Eip = offset;
                    return;
                }

            case Mnemonic.Retf:
                {
                    uint offset = Pop(instruction.OperandSize, instruction.AddressSize);
                    ushort selector = (ushort)Pop(instruction.OperandSize, instruction.AddressSize);
                    LoadSegment(SegmentRegister.Cs, selector);
                    Context.Eip = offset;

                    if (instruction.Operand1.Kind == OperandKind.Immediate)
                    {
                        AdjustStackPointer(instruction.Operand1.Immediate, instruction.AddressSize);
                    }

                    return;
                }

            case Mnemonic.MovSreg:
                LoadSegment((SegmentRegister)instruction.Operand1.Immediate, (ushort)ReadOperand(instruction.Operand2));
                break;

            case Mnemonic.MovSregRead:
                WriteOperand(instruction.Operand1,
                    Context.GetSegmentSelector((SegmentRegister)instruction.Operand2.Immediate));
                break;

            case Mnemonic.PushSegment:
                Push(
                    Context.GetSegmentSelector((SegmentRegister)instruction.Operand1.Immediate),
                    instruction.OperandSize,
                    instruction.AddressSize);
                break;

            case Mnemonic.PopSegment:
                LoadSegment((SegmentRegister)instruction.Operand1.Immediate,
                    (ushort)Pop(instruction.OperandSize, instruction.AddressSize));
                break;

            case Mnemonic.Loop:
            case Mnemonic.LoopE:
            case Mnemonic.LoopNe:
                {
                    uint counter = (ReadCounter(instruction.AddressSize) - 1u) & AluOps.Mask(instruction.AddressSize);
                    WriteCounter(counter, instruction.AddressSize);

                    bool take = instruction.Mnemonic switch
                    {
                        Mnemonic.Loop => counter != 0,
                        Mnemonic.LoopE => counter != 0 && (Context.Eflags & FlagBits.Zf) != 0,
                        _ => counter != 0 && (Context.Eflags & FlagBits.Zf) == 0,
                    };

                    if (take)
                    {
                        Context.Eip = nextEip + instruction.Operand1.Immediate;
                        return;
                    }

                    break;
                }

            case Mnemonic.Jecxz:
                if (ReadCounter(instruction.AddressSize) == 0)
                {
                    Context.Eip = nextEip + instruction.Operand1.Immediate;
                    return;
                }

                break;

            case Mnemonic.Hlt:
                Context.Halted = true;
                break;

            case Mnemonic.Clc:
                Context.Eflags &= ~FlagBits.Cf;
                break;

            case Mnemonic.Stc:
                Context.Eflags |= FlagBits.Cf;
                break;

            case Mnemonic.Cmc:
                Context.Eflags ^= FlagBits.Cf;
                break;

            case Mnemonic.Cld:
                Context.Eflags &= ~FlagBits.Df;
                break;

            case Mnemonic.Std:
                Context.Eflags |= FlagBits.Df;
                break;

            case Mnemonic.Cli:
                Context.Eflags &= ~FlagBits.If;
                break;

            case Mnemonic.Sti:
                Context.Eflags |= FlagBits.If;
                break;

            case Mnemonic.Pushf:
                Push(Context.Eflags | FlagBits.Reserved1, instruction.OperandSize, instruction.AddressSize);
                break;

            case Mnemonic.Popf:
                Context.Eflags = Pop(instruction.OperandSize, instruction.AddressSize) | FlagBits.Reserved1;
                break;

            case Mnemonic.Shl:
            case Mnemonic.Shr:
            case Mnemonic.Sar:
            case Mnemonic.Rol:
            case Mnemonic.Ror:
            case Mnemonic.Rcl:
            case Mnemonic.Rcr:
                ExecuteShift(instruction);
                break;

            case Mnemonic.Shld:
            case Mnemonic.Shrd:
                ExecuteDoubleShift(instruction);
                break;

            case Mnemonic.Mul:
            case Mnemonic.Imul:
            case Mnemonic.Div:
            case Mnemonic.Idiv:
                if (ExecuteMulDiv(instruction))
                {
                    return;
                }

                break;

            case Mnemonic.Aam:
            case Mnemonic.Aad:
                if (ExecuteBcdAdjust(instruction))
                {
                    return;
                }

                break;

            case Mnemonic.Int3:
                if (!TryTransferToInterrupt(3, nextEip, false, 0))
                {
                    throw new UnhandledGuestException(3, Context.Eip);
                }

                return;

            case Mnemonic.Interrupt:
                if (!TryTransferToInterrupt((int)instruction.Operand1.Immediate, nextEip, false, 0))
                {
                    throw new UnhandledGuestException((int)instruction.Operand1.Immediate, Context.Eip);
                }

                return;

            case Mnemonic.Into:
                if ((Context.Eflags & FlagBits.Of) != 0)
                {
                    if (!TryTransferToInterrupt(4, nextEip, false, 0))
                    {
                        throw new UnhandledGuestException(4, Context.Eip);
                    }

                    return;
                }

                break;

            case Mnemonic.Iret:
                ExecuteIret(instruction);
                return;

            case Mnemonic.Lgdt:
                LoadDescriptorTable(Context.Gdtr, instruction.Operand1);
                break;

            case Mnemonic.Lidt:
                LoadDescriptorTable(Context.Idtr, instruction.Operand1);
                break;

            case Mnemonic.Sgdt:
                StoreDescriptorTable(Context.Gdtr, instruction.Operand1);
                break;

            case Mnemonic.Sidt:
                StoreDescriptorTable(Context.Idtr, instruction.Operand1);
                break;

            case Mnemonic.MovCrRead:
                Context.WriteRegister(
                    (Gpr)instruction.Operand1.RegisterIndex,
                    ReadControlRegister((int)instruction.Operand2.Immediate));
                break;

            case Mnemonic.MovCrWrite:
                WriteControlRegister(
                    (int)instruction.Operand2.Immediate,
                    Context.ReadRegister((Gpr)instruction.Operand1.RegisterIndex));
                break;

            case Mnemonic.MovDrRead:
                Context.WriteRegister(
                    (Gpr)instruction.Operand1.RegisterIndex,
                    Context.ReadDebugRegister((int)instruction.Operand2.Immediate));
                break;

            case Mnemonic.MovDrWrite:
                Context.WriteDebugRegister(
                    (int)instruction.Operand2.Immediate,
                    Context.ReadRegister((Gpr)instruction.Operand1.RegisterIndex));
                break;

            case Mnemonic.Invlpg:
                Mmu.FlushPage(ComputeAddress(instruction.Operand1.Memory));
                break;

            case Mnemonic.Invd:
            case Mnemonic.Wbinvd:
                Mmu.Flush();
                break;

            case Mnemonic.Arpl:
                ExecuteArpl(instruction);
                break;

            case Mnemonic.Leave:
                ExecuteLeave(instruction);
                break;

            case Mnemonic.Enter:
                ExecuteEnter(instruction);
                break;

            case Mnemonic.Bswap:
                {
                    Gpr register = (Gpr)instruction.Operand1.RegisterIndex;
                    uint value = Context.ReadRegister(register);
                    Context.WriteRegister(
                        register,
                        (value >> 24) | ((value >> 8) & 0x0000FF00u) | ((value << 8) & 0x00FF0000u) | (value << 24));
                    break;
                }

            case Mnemonic.Cbw:
                {
                    if (instruction.OperandSize == 16)
                    {
                        sbyte value = (sbyte)Context.ReadRegister8(0);
                        Context.WriteRegister16(Gpr.Eax, (ushort)value);
                    }
                    else
                    {
                        short value = (short)Context.ReadRegister16(Gpr.Eax);
                        Context.WriteRegister(Gpr.Eax, (uint)value);
                    }

                    break;
                }

            case Mnemonic.Cdq:
                {
                    if (instruction.OperandSize == 16)
                    {
                        short value = (short)Context.ReadRegister16(Gpr.Eax);
                        Context.WriteRegister16(Gpr.Edx, value < 0 ? (ushort)0xFFFF : (ushort)0);
                    }
                    else
                    {
                        int value = (int)Context.ReadRegister(Gpr.Eax);
                        Context.WriteRegister(Gpr.Edx, value < 0 ? 0xFFFFFFFFu : 0u);
                    }

                    break;
                }

            case Mnemonic.Sldt:
                WriteOperand(instruction.Operand1, Context.Ldtr);
                break;

            case Mnemonic.Str:
                WriteOperand(instruction.Operand1, Context.Tr);
                break;

            case Mnemonic.Lldt:
                Context.Ldtr = (ushort)ReadOperand(instruction.Operand1);
                break;

            case Mnemonic.Ltr:
                Context.Tr = (ushort)ReadOperand(instruction.Operand1);
                break;

            case Mnemonic.Verr:
                ExecuteVerify(instruction, forWrite: false);
                break;

            case Mnemonic.Verw:
                ExecuteVerify(instruction, forWrite: true);
                break;

            case Mnemonic.Movzx:
            case Mnemonic.Movsx:
                ExecuteMoveExtend(instruction);
                break;

            case Mnemonic.Pusha:
                ExecutePushAll(instruction);
                break;

            case Mnemonic.Popa:
                ExecutePopAll(instruction);
                break;

            case Mnemonic.Setcc:
                WriteOperand(instruction.Operand1, EvaluateCondition(instruction.Condition) ? 1u : 0u);
                break;

            case Mnemonic.Cmovcc:
                if (EvaluateCondition(instruction.Condition))
                {
                    WriteOperand(instruction.Operand1, ReadOperand(instruction.Operand2));
                }

                break;

            case Mnemonic.Cpuid:
                ExecuteCpuid();
                break;

            case Mnemonic.Rdtsc:
                Context.Eax = (uint)Context.Tsc;
                Context.Edx = (uint)(Context.Tsc >> 32);
                break;

            case Mnemonic.Rdmsr:
                {
                    uint value = ReadMsr(Context.Ecx, out uint high);
                    Context.Eax = value;
                    Context.Edx = high;
                    break;
                }

            case Mnemonic.Wrmsr:
                WriteMsr(Context.Ecx, Context.Eax, Context.Edx);
                break;

            case Mnemonic.Movs:
            case Mnemonic.Cmps:
            case Mnemonic.Stos:
            case Mnemonic.Lods:
            case Mnemonic.Scas:
            case Mnemonic.Ins:
            case Mnemonic.Outs:
                ExecuteString(instruction);
                break;

            case >= Mnemonic.Fwait and <= Mnemonic.Fsincos:
            case Mnemonic.Ficom:
            case Mnemonic.Ficomp:
                ExecuteFpu(instruction);
                break;

            case >= Mnemonic.Movd and <= Mnemonic.Maskmovq:
                ExecuteMmx(instruction);
                break;

            case >= Mnemonic.Movaps and <= Mnemonic.Movntps:
                ExecuteSse(instruction);
                break;

            case Mnemonic.Lfence:
            case Mnemonic.Mfence:
                break;

            case Mnemonic.In:
                ExecuteIn(instruction);
                break;

            case Mnemonic.Out:
                ExecuteOut(instruction);
                break;

            case Mnemonic.Xadd:
                ExecuteXadd(instruction);
                break;

            case Mnemonic.Cmpxchg:
                ExecuteCmpxchg(instruction);
                break;

            case Mnemonic.Cmpxchg8B:
                ExecuteCmpxchg8B(instruction);
                break;

            case Mnemonic.Bsf:
                ExecuteBitScan(instruction, reverse: false);
                break;

            case Mnemonic.Bsr:
                ExecuteBitScan(instruction, reverse: true);
                break;

            case Mnemonic.Bt:
                ExecuteBitTest(instruction, operation: 0);
                break;

            case Mnemonic.Bts:
                ExecuteBitTest(instruction, operation: 1);
                break;

            case Mnemonic.Btr:
                ExecuteBitTest(instruction, operation: 2);
                break;

            case Mnemonic.Btc:
                ExecuteBitTest(instruction, operation: 3);
                break;

            default:
                throw new UnsupportedInstructionException(Context.Eip, instruction.Opcode);
        }

        Context.Eip = nextEip;
    }

    private void ExecuteAlu(in DecodedInstruction instruction)
    {
        uint first = ReadOperand(instruction.Operand1);
        uint second = ReadOperand(instruction.Operand2);
        int size = instruction.Operand1.Size;
        uint carry = (Context.Eflags & FlagBits.Cf) != 0 ? 1u : 0u;

        uint flags;
        uint result;

        switch (instruction.Mnemonic)
        {
            case Mnemonic.Add:
                flags = AluOps.Add(first, second, 0, size, Context.Eflags, out result);
                break;
            case Mnemonic.Adc:
                flags = AluOps.Add(first, second, carry, size, Context.Eflags, out result);
                break;
            case Mnemonic.Sub:
                flags = AluOps.Sub(first, second, 0, size, Context.Eflags, out result);
                break;
            case Mnemonic.Sbb:
                flags = AluOps.Sub(first, second, carry, size, Context.Eflags, out result);
                break;
            case Mnemonic.And:
                result = first & second & AluOps.Mask(size);
                flags = AluOps.Logical(result, size, Context.Eflags);
                break;
            case Mnemonic.Or:
                result = (first | second) & AluOps.Mask(size);
                flags = AluOps.Logical(result, size, Context.Eflags);
                break;
            case Mnemonic.Xor:
                result = (first ^ second) & AluOps.Mask(size);
                flags = AluOps.Logical(result, size, Context.Eflags);
                break;
            default:
                Context.Eflags = AluOps.Sub(first, second, 0, size, Context.Eflags, out _);
                return;
        }

        Context.Eflags = flags;
        WriteOperand(instruction.Operand1, result);
    }

    private uint ReadOperand(in Operand operand)
    {
        switch (operand.Kind)
        {
            case OperandKind.Register:
                return operand.Size switch
                {
                    8 => Context.ReadRegister8(operand.RegisterIndex),
                    16 => Context.ReadRegister16((Gpr)operand.RegisterIndex),
                    _ => Context.ReadRegister((Gpr)operand.RegisterIndex),
                };
            case OperandKind.Immediate:
                return operand.Immediate;
            case OperandKind.Memory:
                return ReadMemory(ComputeAddress(operand.Memory), operand.Size);
            default:
                throw new InvalidOperationException("Cannot read an empty operand.");
        }
    }

    private void WriteOperand(in Operand operand, uint value)
    {
        switch (operand.Kind)
        {
            case OperandKind.Register:
                switch (operand.Size)
                {
                    case 8:
                        Context.WriteRegister8(operand.RegisterIndex, (byte)value);
                        break;
                    case 16:
                        Context.WriteRegister16((Gpr)operand.RegisterIndex, (ushort)value);
                        break;
                    default:
                        Context.WriteRegister((Gpr)operand.RegisterIndex, value);
                        break;
                }

                break;

            case OperandKind.Memory:
                WriteMemory(ComputeAddress(operand.Memory), operand.Size, value);
                break;

            default:
                throw new InvalidOperationException("Cannot write an immediate operand.");
        }
    }

    private uint ComputeAddress(in MemoryOperand memory)
    {
        uint offset;

        if (memory.Is16Bit)
        {
            uint baseValue = memory.HasBase ? Context.ReadRegister16(memory.Base) : 0u;
            uint indexValue = memory.HasIndex ? Context.ReadRegister16(memory.Index) : 0u;
            offset = (baseValue + indexValue + memory.Displacement) & 0xFFFFu;
        }
        else
        {
            uint baseValue = memory.HasBase ? Context.ReadRegister(memory.Base) : 0u;
            uint indexValue = memory.HasIndex ? Context.ReadRegister(memory.Index) * (uint)memory.Scale : 0u;
            offset = baseValue + indexValue + memory.Displacement;
        }

        SegmentRegister segment = memory.SegmentOverride ?? DefaultSegment(memory);
        return Context.GetSegmentBase(segment) + offset;
    }

    private static SegmentRegister DefaultSegment(in MemoryOperand memory)
    {
        if (memory.Is16Bit)
        {
            return memory.HasBase && memory.Base == Gpr.Ebp ? SegmentRegister.Ss : SegmentRegister.Ds;
        }

        return memory.HasBase && (memory.Base == Gpr.Esp || memory.Base == Gpr.Ebp)
            ? SegmentRegister.Ss
            : SegmentRegister.Ds;
    }

    private uint ReadMemory(uint address, int size)
    {
        if (!Mmu.IsEnabled)
        {
            uint direct = RawRead(address, size);
            if (_hotspot is not null)
            {
                if (CrossesPage(address, size))
                {
                    _hotspot.NoteSplitAccess();
                }

                _hotspot.NoteDataAccess(isWrite: false, isRam: ClassifyHotspot(address));
            }

            return direct;
        }

        if (CrossesPage(address, size))
        {
            _hotspot?.NoteSplitAccess();
            int bytes = size / 8;
            uint value = 0;
            for (int i = 0; i < bytes; i++)
            {
                uint physical = TranslateLinear(address + (uint)i, isWrite: false, isInstruction: false);
                _hotspot?.NoteDataAccess(isWrite: false, isRam: ClassifyHotspot(physical));
                value |= (uint)Memory.Read8(physical) << (8 * i);
            }

            return value;
        }

        uint translated = TranslateLinear(address, isWrite: false, isInstruction: false);
        uint result = RawRead(translated, size);
        _hotspot?.NoteDataAccess(isWrite: false, isRam: ClassifyHotspot(translated));
        return result;
    }

    private void WriteMemory(uint address, int size, uint value)
    {
        if (!Mmu.IsEnabled)
        {
            RawWrite(address, size, value);
            if (_hotspot is not null)
            {
                if (CrossesPage(address, size))
                {
                    _hotspot.NoteSplitAccess();
                }

                _hotspot.NoteDataAccess(isWrite: true, isRam: ClassifyHotspot(address));
            }

            return;
        }

        if (CrossesPage(address, size))
        {
            _hotspot?.NoteSplitAccess();
            int bytes = size / 8;
            for (int i = 0; i < bytes; i++)
            {
                uint physical = TranslateLinear(address + (uint)i, isWrite: true, isInstruction: false);
                _hotspot?.NoteDataAccess(isWrite: true, isRam: ClassifyHotspot(physical));
                Memory.Write8(physical, (byte)(value >> (8 * i)));
                NoteStorePhysical(physical, 8);
            }

            return;
        }

        uint translated = TranslateLinear(address, isWrite: true, isInstruction: false);
        RawWrite(translated, size, value);
        _hotspot?.NoteDataAccess(isWrite: true, isRam: ClassifyHotspot(translated));
    }

    private uint RawRead(uint address, int size)
    {
        // Slice-1 RAM fast path: same Ram method the AddressSpace branch
        // would call (reads carry no watch/counter side effects), so the
        // only skipped work is the interface dispatch + region branch.
        PhysicalMemory? ram = _ramDirect;
        if (ram is not null)
        {
            uint length = size == 8 ? 1u : size == 16 ? 2u : 4u;
            if ((ulong)address + length <= _ramSize)
            {
                return size == 8 ? ram.Read8(address) : size == 16 ? ram.Read16(address) : ram.Read32(address);
            }
        }

        return size switch
        {
            8 => Memory.Read8(address),
            16 => Memory.Read16(address),
            _ => Memory.Read32(address),
        };
    }

    private void RawWrite(uint address, int size, uint value)
    {
        // Slice-1 RAM fast path: NoteStorePhysical still runs first (decode
        // page-drop is correctness, not observability), and the AddressSpace
        // write watch is preserved by falling back whenever it could fire.
        // Ram.Write* runs the same present-dirty choke the bus path hits.
        PhysicalMemory? ram = _ramDirect;
        if (ram is not null)
        {
            uint length = size == 8 ? 1u : size == 16 ? 2u : 4u;
            if ((ulong)address + length <= _ramSize && !IsWatched(address, length))
            {
                NoteStorePhysical(address, size);
                if (size == 8)
                {
                    ram.Write8(address, (byte)value);
                }
                else if (size == 16)
                {
                    ram.Write16(address, (ushort)value);
                }
                else
                {
                    ram.Write32(address, value);
                }

                return;
            }
        }

        NoteStorePhysical(address, size);
        switch (size)
        {
            case 8:
                Memory.Write8(address, (byte)value);
                break;
            case 16:
                Memory.Write16(address, (ushort)value);
                break;
            default:
                Memory.Write32(address, value);
                break;
        }
    }

    /// <summary>
    /// Slice-1 guard: true when the AddressSpace write watch could fire for
    /// [address, address+length), in which case the caller takes the legacy
    /// bus path so CheckWatch + WatchHit run exactly as before.
    /// </summary>
    private bool IsWatched(uint address, uint length)
    {
        AddressSpace? space = _ramSpace;
        if (space is null || space.WatchEnd <= space.WatchStart)
        {
            return false;
        }

        ulong start = address;
        ulong end = start + length;
        return start < space.WatchEnd && space.WatchStart < end;
    }

    /// <summary>
    /// Slice-1 gate for multi-translate fusions (XMM128/FPU80): those change
    /// the per-access translate count (2→1), which changes idle-access and
    /// TLB-outcome tallies, so they run only when no observer can see the
    /// difference. Narrow 8/16/32/64 paths keep identical note counts and
    /// need no gate. Hotspot data notes are emitted by the callers either
    /// way and are unaffected.
    /// </summary>
    private bool FastWideAllowed => _hotspot is null && _idle is null && Mmu.Observer is null;

    private static bool CrossesPage(uint address, int sizeInBits)
    {
        return (address & 0xFFFu) + (uint)(sizeInBits / 8) > 0x1000u;
    }

    private uint TranslateLinear(uint linearAddress, bool isWrite, bool isInstruction)
    {
        // Slice-3 data-translate cache: direct-mapped on the VPN low bits.
        // Hit replays the SoftTLB hit branch exactly (WP-gated writability
        // fault with the same P=1/W=1 data error code); miss runs the full
        // core with identical side effects and populates the slot. The
        // idle-skip access note fires on both paths exactly once per call,
        // like the legacy path; only the Mmu-observer TLB notes are skipped,
        // which the FastWideAllowed gate covers.
        if (!isInstruction && FastWideAllowed)
        {
            uint vpn = linearAddress >> 12;
            int slot = (int)(vpn & 3u);
            if (_dataXlatTag[slot] == vpn)
            {
                if (isWrite && !_dataXlatWritable[slot]
                    && (Context.Cr0 & ControlRegisterBits.Cr0WriteProtect) != 0)
                {
                    throw new PageFaultSignal(linearAddress, 3u);
                }

                uint hit = _dataXlatPhys[slot] + (linearAddress & 0xFFFu);
                _idle?.NoteAccess(hit, isWrite, isInstruction);
                return hit;
            }

            uint solved = Mmu.TranslateDataFast(linearAddress, isWrite, out bool fastFaulted,
                out uint fastError, out uint pageBase, out bool pageWritable);
            if (fastFaulted)
            {
                throw new PageFaultSignal(linearAddress, fastError);
            }

            _dataXlatTag[slot] = vpn;
            _dataXlatPhys[slot] = pageBase;
            _dataXlatWritable[slot] = pageWritable;
            _dataXlatArmed = true;
            _idle?.NoteAccess(solved, isWrite, isInstruction);
            return solved;
        }

        uint physical = Mmu.Translate(linearAddress, isWrite, isInstruction, out bool faulted, out uint errorCode);
        if (faulted)
        {
            throw new PageFaultSignal(linearAddress, errorCode);
        }

        _idle?.NoteAccess(physical, isWrite, isInstruction);
        return physical;
    }

    private void InvalidateDataXlat()
    {
        if (!_dataXlatArmed)
        {
            return;
        }

        _dataXlatTag[0] = uint.MaxValue;
        _dataXlatTag[1] = uint.MaxValue;
        _dataXlatTag[2] = uint.MaxValue;
        _dataXlatTag[3] = uint.MaxValue;
        _dataXlatArmed = false;
    }

    /// <summary>
    /// Translates for the profiler-on fetch path (§131 leg (a)): like
    /// <see cref="TranslateLinear"/> but returns the TLB outcome and any
    /// fault instead of notifying or throwing, so the caller accumulates the
    /// batch before raising the fault. Idle-skip access notes stay with the
    /// caller (after the fault check), preserving the legacy order of
    /// guest-visible effects (fault → no access note).
    /// </summary>
    private uint TranslateLinearCounted(uint linearAddress, out bool tlbHit, out bool tlbCounted, out bool faulted,
        out uint errorCode)
    {
        return Mmu.TranslateUncounted(linearAddress, isWrite: false, isInstruction: true, out faulted, out errorCode,
            out tlbHit, out tlbCounted);
    }

    /// <summary>
    /// Classifies a translated data address as RAM-backed for the hotspot
    /// profiler. No idle-skip host (unit-test harness) means flat test RAM.
    /// </summary>
    private bool ClassifyHotspot(uint physicalAddress)
    {
        return IdleSkipHost?.IsRam(physicalAddress) != false;
    }

    private sealed class PageFaultSignal : Exception
    {
        public PageFaultSignal(uint linearAddress, uint errorCode)
        {
            LinearAddress = linearAddress;
            ErrorCode = errorCode;
        }

        // ReSharper disable once UnusedMember.Local
        public PageFaultSignal(string? message, Exception? innerException) : base(message, innerException)
        {
        }

        // ReSharper disable once UnusedMember.Local
        public PageFaultSignal()
        {
        }

        // ReSharper disable once UnusedMember.Local
        public PageFaultSignal(string? message) : base(message)
        {
        }

        public uint LinearAddress { get; }

        public uint ErrorCode { get; }
    }

    private void Push(uint value, int operandSize, int addressSize)
    {
        uint delta = (uint)(operandSize / 8);

        if (addressSize == 16)
        {
            uint sp = ((Context.Esp & 0xFFFFu) - delta) & 0xFFFFu;
            Context.Esp = (Context.Esp & 0xFFFF0000u) | sp;
            WriteMemory(Context.GetSegmentBase(SegmentRegister.Ss) + sp, operandSize, value);
        }
        else
        {
            uint sp = Context.Esp - delta;
            Context.Esp = sp;
            WriteMemory(Context.GetSegmentBase(SegmentRegister.Ss) + sp, operandSize, value);
        }
    }

    private uint Pop(int operandSize, int addressSize)
    {
        uint delta = (uint)(operandSize / 8);

        if (addressSize == 16)
        {
            uint sp = Context.Esp & 0xFFFFu;
            uint value = ReadMemory(Context.GetSegmentBase(SegmentRegister.Ss) + sp, operandSize);
            sp = (sp + delta) & 0xFFFFu;
            Context.Esp = (Context.Esp & 0xFFFF0000u) | sp;
            return value;
        }
        else
        {
            uint sp = Context.Esp;
            uint value = ReadMemory(Context.GetSegmentBase(SegmentRegister.Ss) + sp, operandSize);
            Context.Esp = sp + delta;
            return value;
        }
    }

    private void AdjustStackPointer(uint amount, int addressSize)
    {
        if (addressSize == 16)
        {
            uint sp = ((Context.Esp & 0xFFFFu) + amount) & 0xFFFFu;
            Context.Esp = (Context.Esp & 0xFFFF0000u) | sp;
        }
        else
        {
            Context.Esp += amount;
        }
    }

    private (uint Offset, ushort Selector) ReadFarTarget(in DecodedInstruction instruction)
    {
        if (instruction.Operand1.Kind != OperandKind.Memory)
        {
            return (instruction.Operand1.Immediate, (ushort)instruction.Operand2.Immediate);
        }

        uint address = ComputeAddress(instruction.Operand1.Memory);
        uint offset = ReadMemory(address, instruction.OperandSize);
        ushort selector = (ushort)ReadMemory(address + (uint)(instruction.OperandSize / 8), 16);
        return (offset, selector);
    }

    private void LoadSegment(SegmentRegister segment, ushort selector)
    {
        uint baseAddress;
        bool code32 = false;
        if (Context.IsProtectedMode && (selector & 0xFFF8) != 0)
        {
            uint descriptor = Context.Gdtr.Base + (uint)(selector & 0xFFF8);
            uint descriptorPhysical = Mmu.Translate(descriptor, isWrite: false, isInstruction: false,
                out bool descriptorFaulted, out _);
            if (descriptorFaulted)
            {
                descriptorPhysical = descriptor;
            }

            uint low = Memory.Read32(descriptorPhysical);
            uint high = Memory.Read32(descriptorPhysical + 4);
            baseAddress = ((low >> 16) & 0xFFFFu) | ((high & 0xFFu) << 16) | (high & 0xFF000000u);
            code32 = ((high >> 22) & 1u) != 0;
        }
        else
        {
            baseAddress = (uint)selector << 4;
        }

        // Decode-cache invalidation (§131 leg (d)): the cache is keyed by
        // linear fetch address (Cs base + Eip) and validated against the
        // operand/address-size defaults derived from CS.D/B, so a reload that
        // keeps base and D/B identical cannot change any cached decode —
        // including the exact Eip of a cached #UD marker. Only a changed base
        // or D/B flushes (CR0.PE is covered by the CR-write flush;
        // SYSENTER/IRET/interrupt paths all funnel through here).
        bool csChanged = segment == SegmentRegister.Cs
                         && (Context.GetSegmentBase(SegmentRegister.Cs) != baseAddress
                             || Context.CodeSegment32 != code32);

        Context.SetSegment(segment, selector, baseAddress);
        if (segment == SegmentRegister.Cs)
        {
            Context.CodeSegment32 = code32;
            if (csChanged)
            {
                _decodeCache?.Flush();
            }
        }
    }

    private uint ReadCounter(int addressSize)
    {
        return addressSize == 16 ? Context.Ecx & 0xFFFFu : Context.Ecx;
    }

    private void WriteCounter(uint value, int addressSize)
    {
        if (addressSize == 16)
        {
            Context.WriteRegister16(Gpr.Ecx, (ushort)value);
        }
        else
        {
            Context.WriteRegister(Gpr.Ecx, value);
        }
    }

    private void ExecuteShift(in DecodedInstruction instruction)
    {
        uint value = ReadOperand(instruction.Operand1);
        int count = instruction.Operand2.Kind == OperandKind.Register
            ? Context.ReadRegister8(1)
            : (int)instruction.Operand2.Immediate;
        int size = instruction.Operand1.Size;

        uint result;
        uint flags;

        switch (instruction.Mnemonic)
        {
            case Mnemonic.Shl:
                flags = ShiftOps.Shl(value, count, size, Context.Eflags, out result);
                break;
            case Mnemonic.Shr:
                flags = ShiftOps.Shr(value, count, size, Context.Eflags, out result);
                break;
            case Mnemonic.Sar:
                flags = ShiftOps.Sar(value, count, size, Context.Eflags, out result);
                break;
            case Mnemonic.Rol:
                flags = ShiftOps.Rol(value, count, size, Context.Eflags, out result);
                break;
            case Mnemonic.Ror:
                flags = ShiftOps.Ror(value, count, size, Context.Eflags, out result);
                break;
            case Mnemonic.Rcl:
                flags = ShiftOps.Rcl(value, count, size, Context.Eflags, out result);
                break;
            default:
                flags = ShiftOps.Rcr(value, count, size, Context.Eflags, out result);
                break;
        }

        Context.Eflags = flags;
        WriteOperand(instruction.Operand1, result);
    }

    private void ExecuteDoubleShift(in DecodedInstruction instruction)
    {
        uint destination = ReadOperand(instruction.Operand1);
        uint source = ReadOperand(instruction.Operand2);
        int count = instruction.Operand3.Kind == OperandKind.Register
            ? Context.ReadRegister8(1)
            : (int)instruction.Operand3.Immediate;
        int size = instruction.Operand1.Size;

        uint flags = instruction.Mnemonic == Mnemonic.Shld
            ? ShiftOps.Shld(destination, source, count, size, Context.Eflags, out var result)
            : ShiftOps.Shrd(destination, source, count, size, Context.Eflags, out result);

        Context.Eflags = flags;
        WriteOperand(instruction.Operand1, result);
    }

    private bool ExecuteMulDiv(in DecodedInstruction instruction)
    {
        if (instruction.Mnemonic == Mnemonic.Imul)
        {
            ExecuteImul(instruction);
            return false;
        }

        int size = instruction.Operand1.Size;
        uint source = ReadOperand(instruction.Operand1);

        return instruction.Mnemonic switch
        {
            Mnemonic.Mul => ExecuteMul(source, size),
            Mnemonic.Div => ExecuteDiv(source, size),
            _ => ExecuteIdiv(source, size),
        };
    }

    private bool ExecuteBcdAdjust(in DecodedInstruction instruction)
    {
        uint radix = instruction.Operand1.Immediate & 0xFFu;
        if (instruction.Mnemonic == Mnemonic.Aam)
        {
            if (radix == 0)
            {
                return RaiseDivideError();
            }

            byte value = Context.ReadRegister8(0);
            Context.WriteRegister8(4, (byte)(value / radix));
            Context.WriteRegister8(0, (byte)(value % radix));
            Context.Eflags = AluOps.Logical(Context.ReadRegister8(0), 8, Context.Eflags);
            return false;
        }

        byte high = Context.ReadRegister8(4);
        byte low = Context.ReadRegister8(0);
        Context.WriteRegister8(0, (byte)((high * radix) + low));
        Context.WriteRegister8(4, 0);
        Context.Eflags = AluOps.Logical(Context.ReadRegister8(0), 8, Context.Eflags);
        return false;
    }

    private bool ExecuteMul(uint source, int size)
    {
        switch (size)
        {
            case 8:
                {
                    uint product = (uint)Context.ReadRegister8(0) * (source & 0xFFu);
                    Context.WriteRegister16(Gpr.Eax, (ushort)product);
                    Context.Eflags = SetMultiplyFlags(Context.Eflags, (product & 0xFF00u) != 0);
                    break;
                }

            case 16:
                {
                    uint product = (uint)Context.ReadRegister16(Gpr.Eax) * (source & 0xFFFFu);
                    Context.WriteRegister16(Gpr.Eax, (ushort)product);
                    Context.WriteRegister16(Gpr.Edx, (ushort)(product >> 16));
                    Context.Eflags = SetMultiplyFlags(Context.Eflags, (product & 0xFFFF0000u) != 0);
                    break;
                }

            default:
                {
                    ulong product = (ulong)Context.ReadRegister(Gpr.Eax) * source;
                    Context.WriteRegister(Gpr.Eax, (uint)product);
                    Context.WriteRegister(Gpr.Edx, (uint)(product >> 32));
                    Context.Eflags = SetMultiplyFlags(Context.Eflags, (product >> 32) != 0);
                    break;
                }
        }

        return false;
    }

    private void ExecuteImul(in DecodedInstruction instruction)
    {
        if (instruction.Operand3.Kind == OperandKind.Immediate)
        {
            int size = instruction.Operand1.Size;
            long product = SignedValue(ReadOperand(instruction.Operand2), size)
                           * SignedValue(instruction.Operand3.Immediate, size);
            uint result = (uint)product & AluOps.Mask(size);
            Context.Eflags = SetMultiplyFlags(Context.Eflags, product != SignedValue(result, size));
            WriteOperand(instruction.Operand1, result);
            return;
        }

        if (instruction.Operand2.Kind != OperandKind.None)
        {
            int size = instruction.Operand1.Size;
            long product = SignedValue(ReadOperand(instruction.Operand1), size)
                           * SignedValue(ReadOperand(instruction.Operand2), size);
            uint result = (uint)product & AluOps.Mask(size);
            Context.Eflags = SetMultiplyFlags(Context.Eflags, product != SignedValue(result, size));
            WriteOperand(instruction.Operand1, result);
            return;
        }

        uint source = ReadOperand(instruction.Operand1);
        switch (instruction.Operand1.Size)
        {
            case 8:
                {
                    int product = (sbyte)Context.ReadRegister8(0) * (sbyte)source;
                    Context.WriteRegister16(Gpr.Eax, (ushort)product);
                    Context.Eflags = SetMultiplyFlags(Context.Eflags, product != (sbyte)product);
                    break;
                }

            case 16:
                {
                    int product = (short)Context.ReadRegister16(Gpr.Eax) * (short)source;
                    Context.WriteRegister16(Gpr.Eax, (ushort)product);
                    Context.WriteRegister16(Gpr.Edx, (ushort)(product >> 16));
                    Context.Eflags = SetMultiplyFlags(Context.Eflags, product != (short)product);
                    break;
                }

            default:
                {
                    long product = (long)(int)Context.ReadRegister(Gpr.Eax) * (int)source;
                    Context.WriteRegister(Gpr.Eax, (uint)product);
                    Context.WriteRegister(Gpr.Edx, (uint)(product >> 32));
                    Context.Eflags = SetMultiplyFlags(Context.Eflags, product != (int)product);
                    break;
                }
        }
    }

    private bool ExecuteDiv(uint source, int size)
    {
        switch (size)
        {
            case 8:
                {
                    uint divisor = source & 0xFFu;
                    if (divisor == 0)
                    {
                        return RaiseDivideError();
                    }

                    uint dividend = Context.ReadRegister16(Gpr.Eax);
                    uint quotient = dividend / divisor;
                    if (quotient > 0xFFu)
                    {
                        return RaiseDivideError();
                    }

                    Context.WriteRegister8(0, (byte)quotient);
                    Context.WriteRegister8(4, (byte)(dividend % divisor));
                    break;
                }

            case 16:
                {
                    uint divisor = source & 0xFFFFu;
                    if (divisor == 0)
                    {
                        return RaiseDivideError();
                    }

                    uint dividend = ((uint)Context.ReadRegister16(Gpr.Edx) << 16) | Context.ReadRegister16(Gpr.Eax);
                    uint quotient = dividend / divisor;
                    if (quotient > 0xFFFFu)
                    {
                        return RaiseDivideError();
                    }

                    Context.WriteRegister16(Gpr.Eax, (ushort)quotient);
                    Context.WriteRegister16(Gpr.Edx, (ushort)(dividend % divisor));
                    break;
                }

            default:
                {
                    if (source == 0)
                    {
                        return RaiseDivideError();
                    }

                    ulong dividend = ((ulong)Context.ReadRegister(Gpr.Edx) << 32) | Context.ReadRegister(Gpr.Eax);
                    ulong quotient = dividend / source;
                    if (quotient > 0xFFFFFFFFu)
                    {
                        return RaiseDivideError();
                    }

                    Context.WriteRegister(Gpr.Eax, (uint)quotient);
                    Context.WriteRegister(Gpr.Edx, (uint)(dividend % source));
                    break;
                }
        }

        return false;
    }

    private bool ExecuteIdiv(uint source, int size)
    {
        switch (size)
        {
            case 8:
                {
                    int divisor = (sbyte)source;
                    if (divisor == 0)
                    {
                        return RaiseDivideError();
                    }

                    int dividend = (short)Context.ReadRegister16(Gpr.Eax);
                    int quotient = dividend / divisor;
                    if (quotient is < sbyte.MinValue or > sbyte.MaxValue)
                    {
                        return RaiseDivideError();
                    }

                    Context.WriteRegister8(0, (byte)quotient);
                    Context.WriteRegister8(4, (byte)(dividend % divisor));
                    break;
                }

            case 16:
                {
                    int divisor = (short)source;
                    if (divisor == 0)
                    {
                        return RaiseDivideError();
                    }

                    int dividend = (int)(((uint)Context.ReadRegister16(Gpr.Edx) << 16) | Context.ReadRegister16(Gpr.Eax));
                    if (dividend == int.MinValue && divisor == -1)
                    {
                        return RaiseDivideError();
                    }

                    int quotient = dividend / divisor;
                    if (quotient is < short.MinValue or > short.MaxValue)
                    {
                        return RaiseDivideError();
                    }

                    Context.WriteRegister16(Gpr.Eax, (ushort)quotient);
                    Context.WriteRegister16(Gpr.Edx, (ushort)(dividend % divisor));
                    break;
                }

            default:
                {
                    int divisor = (int)source;
                    if (divisor == 0)
                    {
                        return RaiseDivideError();
                    }

                    long dividend = (long)(((ulong)Context.ReadRegister(Gpr.Edx) << 32) | Context.ReadRegister(Gpr.Eax));
                    if (dividend == long.MinValue && divisor == -1)
                    {
                        return RaiseDivideError();
                    }

                    long quotient = dividend / divisor;
                    if (quotient is < int.MinValue or > int.MaxValue)
                    {
                        return RaiseDivideError();
                    }

                    Context.WriteRegister(Gpr.Eax, (uint)quotient);
                    Context.WriteRegister(Gpr.Edx, (uint)(dividend % divisor));
                    break;
                }
        }

        return false;
    }

    private bool RaiseDivideError()
    {
        if (TryTransferToInterrupt(0, Context.Eip, false, 0))
        {
            return true;
        }

        throw new UnhandledGuestException(0, Context.Eip);
    }

    private static long SignedValue(uint value, int size)
    {
        return size switch
        {
            8 => (sbyte)value,
            16 => (short)value,
            _ => (int)value,
        };
    }

    private static uint SetMultiplyFlags(uint eflags, bool overflow)
    {
        uint flags = eflags & ~(FlagBits.Cf | FlagBits.Of);
        if (overflow)
        {
            flags |= FlagBits.Cf | FlagBits.Of;
        }

        return flags;
    }

    private bool TryTransferToInterrupt(int vector, uint returnEip, bool hasErrorCode, uint errorCode)
    {
        if (Context.IsProtectedMode)
        {
            if (Context.Idtr.Limit == 0)
            {
                return false;
            }

            uint gate = Context.Idtr.Base + ((uint)vector * 8);
            uint gatePhysical = Mmu.Translate(gate, isWrite: false, isInstruction: false, out bool gateFaulted, out _);
            if (gateFaulted)
            {
                return false;
            }

            ushort offsetLow = Memory.Read16(gatePhysical);
            ushort selector = Memory.Read16(gatePhysical + 2);
            ushort attributes = Memory.Read16(gatePhysical + 4);
            ushort offsetHigh = Memory.Read16(gatePhysical + 6);
            uint handler = offsetLow | ((uint)offsetHigh << 16);

            if ((attributes & 0x8000) == 0)
            {
                return false;
            }

            PushInterruptFrame(selector, returnEip, hasErrorCode, errorCode, 32);

            bool interruptGate = ((attributes >> 8) & 0x1Fu) == 0x0Eu;
            if (interruptGate)
            {
                Context.Eflags &= ~(FlagBits.If | FlagBits.Tf);
            }

            LoadSegment(SegmentRegister.Cs, selector);
            Context.Eip = handler;
            return true;
        }

        uint entry = (uint)vector * 4;
        ushort offset = Memory.Read16(entry);
        ushort segment = Memory.Read16(entry + 2);

        PushInterruptFrame(segment, returnEip, hasErrorCode, errorCode, 16);
        Context.Eflags &= ~(FlagBits.If | FlagBits.Tf);
        Context.SetSegment(SegmentRegister.Cs, segment, (uint)segment << 4);
        Context.Eip = offset;
        return true;
    }

    private void PushInterruptFrame(ushort selector, uint returnEip, bool hasErrorCode, uint errorCode, int frameSize)
    {
        int addressSize = frameSize == 32 ? 32 : 16;
        Push(Context.Eflags | FlagBits.Reserved1, frameSize, addressSize);
        Push(selector, frameSize, addressSize);
        Push(returnEip, frameSize, addressSize);

        if (hasErrorCode)
        {
            Push(errorCode, frameSize, addressSize);
        }
    }

    private void ExecuteIret(in DecodedInstruction instruction)
    {
        uint eip = Pop(instruction.OperandSize, instruction.AddressSize);
        ushort cs = (ushort)Pop(instruction.OperandSize, instruction.AddressSize);
        uint flags = Pop(instruction.OperandSize, instruction.AddressSize);

        Context.Eflags = (flags & 0x003F7FD5u) | FlagBits.Reserved1;

        LoadSegment(SegmentRegister.Cs, cs);
        Context.Eip = eip;
    }

    private void LoadDescriptorTable(DescriptorTableRegister table, in Operand operand)
    {
        uint address = ComputeAddress(operand.Memory);
        table.Limit = (ushort)ReadMemory(address, 16);
        table.Base = ReadMemory(address + 2, 32);
    }

    private void StoreDescriptorTable(DescriptorTableRegister table, in Operand operand)
    {
        uint address = ComputeAddress(operand.Memory);
        WriteMemory(address, 16, table.Limit);
        WriteMemory(address + 2, 32, table.Base);
    }

    private uint ReadControlRegister(int index)
    {
        return index switch
        {
            0 => Context.Cr0,
            2 => Context.Cr2,
            3 => Context.Cr3,
            4 => Context.Cr4,
            _ => 0u,
        };
    }

    private void WriteControlRegister(int index, uint value)
    {
        switch (index)
        {
            case 0:
                {
                    uint previous = Context.Cr0;
                    Context.Cr0 = value;
                    if (((previous ^ value) & ControlRegisterBits.Cr0Paging) != 0)
                    {
                        Mmu.Flush();
                    }

                    break;
                }

            case 2:
                Context.Cr2 = value;
                break;
            case 3:
                Context.Cr3 = value;
                Mmu.Flush();
                break;
            case 4:
                {
                    uint previous = Context.Cr4;
                    Context.Cr4 = value;
                    if (((previous ^ value) & ControlRegisterBits.Cr4PageSizeExtensions) != 0)
                    {
                        Mmu.Flush();
                    }

                    break;
                }

            default:
                return;
        }

        Mmu.Flush();
    }

    private void ExecuteMoveExtend(in DecodedInstruction instruction)
    {
        uint value = ReadOperand(instruction.Operand2);

        if (instruction.Operand2.Size == 8)
        {
            value = instruction.Mnemonic == Mnemonic.Movzx ? value & 0xFFu : (uint)(int)(sbyte)value;
        }
        else
        {
            value = instruction.Mnemonic == Mnemonic.Movzx ? value & 0xFFFFu : (uint)(int)(short)value;
        }

        WriteOperand(instruction.Operand1, value);
    }

    private void ExecuteArpl(in DecodedInstruction instruction)
    {
        ushort destination = (ushort)ReadOperand(instruction.Operand1);
        ushort source = (ushort)ReadOperand(instruction.Operand2);

        if ((destination & 3) < (source & 3))
        {
            Context.Eflags |= FlagBits.Zf;
            WriteOperand(instruction.Operand1, (uint)((destination & 0xFFFC) | (source & 3)));
        }
        else
        {
            Context.Eflags &= ~FlagBits.Zf;
        }
    }

    private void ExecuteLeave(in DecodedInstruction instruction)
    {
        if (instruction.OperandSize == 16)
        {
            Context.Esp = (Context.Esp & 0xFFFF0000u) | (Context.Ebp & 0xFFFFu);
            Context.WriteRegister16(Gpr.Ebp, (ushort)Pop(16, instruction.AddressSize));
        }
        else
        {
            Context.Esp = Context.Ebp;
            Context.Ebp = Pop(32, instruction.AddressSize);
        }
    }

    private void ExecuteEnter(in DecodedInstruction instruction)
    {
        int size = instruction.OperandSize;
        int addressSize = instruction.AddressSize;
        int delta = size / 8;
        uint allocation = instruction.Operand1.Immediate & 0xFFFFu;
        int nesting = (int)(instruction.Operand2.Immediate & 0x1Fu);

        Push(ReadGeneral(Gpr.Ebp, size), size, addressSize);
        uint frameTemp = Context.Esp;
        for (int i = 1; i < nesting; i++)
        {
            if (addressSize == 16)
            {
                Context.Ebp = (Context.Ebp & 0xFFFF0000u) | ((Context.Ebp - (uint)delta) & 0xFFFFu);
            }
            else
            {
                Context.Ebp -= (uint)delta;
            }

            uint frameBase = Context.GetSegmentBase(SegmentRegister.Ss)
                             + (addressSize == 16 ? Context.Ebp & 0xFFFFu : Context.Ebp);
            Push(ReadMemory(frameBase, size), size, addressSize);
        }

        if (nesting > 0)
        {
            Push(frameTemp, size, addressSize);
        }

        if (addressSize == 16)
        {
            Context.Ebp = (Context.Ebp & 0xFFFF0000u) | (frameTemp & 0xFFFFu);
            Context.Esp = (Context.Esp & 0xFFFF0000u) | ((Context.Esp - allocation) & 0xFFFFu);
        }
        else
        {
            Context.Ebp = frameTemp;
            Context.Esp -= allocation;
        }
    }

    private void ExecuteVerify(in DecodedInstruction instruction, bool forWrite)
    {
        ushort selector = (ushort)ReadOperand(instruction.Operand1);
        bool valid = false;
        if ((selector & 0xFFF8) != 0)
        {
            uint descriptor = Context.Gdtr.Base + (uint)(selector & 0xFFF8);
            uint descriptorPhysical =
                Mmu.Translate(descriptor, isWrite: false, isInstruction: false, out bool faulted, out _);
            if (!faulted)
            {
                uint high = Memory.Read32(descriptorPhysical + 4);
                bool present = (high & 0x8000u) != 0;
                int type = (int)((high >> 8) & 0x0Fu);
                bool code = (type & 0x8) != 0;
                bool readable = !code || (type & 0x2) != 0;
                bool writable = !code && (type & 0x2) != 0;
                valid = present && (forWrite ? writable : readable);
            }
        }

        if (valid)
        {
            Context.Eflags |= FlagBits.Zf;
        }
        else
        {
            Context.Eflags &= ~FlagBits.Zf;
        }
    }

    private void ExecutePushAll(in DecodedInstruction instruction)
    {
        int size = instruction.OperandSize;
        int addressSize = instruction.AddressSize;
        uint originalSp = Context.Esp;

        Push(ReadGeneral(Gpr.Eax, size), size, addressSize);
        Push(ReadGeneral(Gpr.Ecx, size), size, addressSize);
        Push(ReadGeneral(Gpr.Edx, size), size, addressSize);
        Push(ReadGeneral(Gpr.Ebx, size), size, addressSize);
        Push(originalSp, size, addressSize);
        Push(ReadGeneral(Gpr.Ebp, size), size, addressSize);
        Push(ReadGeneral(Gpr.Esi, size), size, addressSize);
        Push(ReadGeneral(Gpr.Edi, size), size, addressSize);
    }

    private void ExecutePopAll(in DecodedInstruction instruction)
    {
        int size = instruction.OperandSize;
        int addressSize = instruction.AddressSize;

        WriteGeneral(Gpr.Edi, Pop(size, addressSize), size);
        WriteGeneral(Gpr.Esi, Pop(size, addressSize), size);
        WriteGeneral(Gpr.Ebp, Pop(size, addressSize), size);
        Pop(size, addressSize);
        WriteGeneral(Gpr.Ebx, Pop(size, addressSize), size);
        WriteGeneral(Gpr.Edx, Pop(size, addressSize), size);
        WriteGeneral(Gpr.Ecx, Pop(size, addressSize), size);
        WriteGeneral(Gpr.Eax, Pop(size, addressSize), size);
    }

    private uint ReadGeneral(Gpr register, int size)
    {
        return size == 16 ? Context.ReadRegister16(register) : Context.ReadRegister(register);
    }

    private void WriteGeneral(Gpr register, uint value, int size)
    {
        if (size == 16)
        {
            Context.WriteRegister16(register, (ushort)value);
        }
        else
        {
            Context.WriteRegister(register, value);
        }
    }

    private void ExecuteCpuid()
    {
        switch (Context.Eax)
        {
            case 0:
                Context.Eax = 2;
                Context.Ebx = 0x756E6547;
                Context.Ecx = 0x6C65746E;
                Context.Edx = 0x49656E69;
                break;
            case 1:
                Context.Eax = 0x0000068A;
                Context.Ebx = 0;
                Context.Ecx = 0;
                Context.Edx = 0x0383F9FF;
                break;
            case 0x80000000:
                Context.Eax = 0;
                Context.Ebx = 0x756E6547;
                Context.Ecx = 0x6C65746E;
                Context.Edx = 0x49656E69;
                break;
            default:
                Context.Eax = 0x03020101;
                Context.Ebx = 0;
                Context.Ecx = 0;
                Context.Edx = 0x0C040841;
                break;
        }
    }

    private uint ReadMsr(uint index, out uint high)
    {
        high = 0;

        switch (index)
        {
            case 0x10:
                high = (uint)(Context.Tsc >> 32);
                return (uint)Context.Tsc;
            case 0x2A:
                return 0xC5040000u;
            case 0x174:
                return _sysenterCs;
            case 0x175:
                return _sysenterEsp;
            case 0x176:
                return _sysenterEip;
            default:
                return 0;
        }
    }

    private void WriteMsr(uint index, uint value, uint high)
    {
        switch (index)
        {
            case 0x10:
                Context.Tsc = value | ((ulong)high << 32);
                break;
            case 0x174:
                _sysenterCs = value;
                break;
            case 0x175:
                _sysenterEsp = value;
                break;
            case 0x176:
                _sysenterEip = value;
                break;
            default:
                break;
        }
    }

    private void ExecuteString(in DecodedInstruction instruction)
    {
        int size = instruction.OperandSize;
        int addressSize = instruction.AddressSize;

        if (instruction.Rep == RepPrefix.None)
        {
            ExecuteStringIteration(instruction, size, addressSize);
            return;
        }

        if (ExecuteStringBulk(instruction, size, addressSize))
        {
            return;
        }

        while (ReadCounter(addressSize) != 0)
        {
            ExecuteStringIteration(instruction, size, addressSize);
            WriteCounter(ReadCounter(addressSize) - 1u, addressSize);

            if (instruction.Mnemonic is Mnemonic.Cmps or Mnemonic.Scas)
            {
                bool zero = (Context.Eflags & FlagBits.Zf) != 0;
                if (instruction.Rep == RepPrefix.Rep && !zero)
                {
                    break;
                }

                if (instruction.Rep == RepPrefix.Repne && zero)
                {
                    break;
                }
            }
        }
    }

    /// <summary>
    /// Slice-4 bulk path for `rep movs` / `rep stos` (§165): runs of
    /// elements move at two translates per page-chunk plus direct RAM
    /// element ops in legacy order, instead of two translates + two bus
    /// dispatches per element. Only the two ZF-independent moves qualify
    /// (Cmps/Scas exit early on ZF; Lods/Ins/Outs touch registers or IO
    /// per iteration). Gated on <see cref="FastWideAllowed"/> (per-chunk
    /// translate/idle/hotspot notes replace per-iteration ones) with
    /// per-chunk fallback to the exact legacy iteration for non-RAM or
    /// watched chunks, so MMIO traffic, watch hits, unmapped counters and
    /// fault partial states (indices/counter updated per chunk) are all
    /// identical. Returns false when the legacy loop must run.
    /// </summary>
    private bool ExecuteStringBulk(in DecodedInstruction instruction, int size, int addressSize)
    {
        if (instruction.Mnemonic is not (Mnemonic.Movs or Mnemonic.Stos) || !FastWideAllowed)
        {
            return false;
        }

        PhysicalMemory? ram = _ramDirect;
        if (ram is null)
        {
            return false;
        }

        uint count = ReadCounter(addressSize);
        if (count == 0)
        {
            return true;
        }

        bool decrement = (Context.Eflags & FlagBits.Df) != 0;
        int delta = size / 8;
        ulong totalBytes = (ulong)count * (ulong)delta;
        uint srcIndex = ReadIndex(Gpr.Esi, addressSize);
        uint dstIndex = ReadIndex(Gpr.Edi, addressSize);

        if (addressSize == 16)
        {
            // Indices wrap mod 2^16 per iteration; bulk needs a wrap-free run
            // so the touched linears stay contiguous.
            ulong span = totalBytes - (ulong)delta;
            if (!decrement)
            {
                if ((ulong)srcIndex + totalBytes > 0x10000u || (ulong)dstIndex + totalBytes > 0x10000u)
                {
                    return false;
                }
            }
            else if (srcIndex < span || dstIndex < span)
            {
                return false;
            }
        }
        else if (decrement)
        {
            ulong span = totalBytes - (ulong)delta;
            if (srcIndex < span || dstIndex < span
                || (ulong)srcIndex + (ulong)delta > 0x1_0000_0000u
                || (ulong)dstIndex + (ulong)delta > 0x1_0000_0000u)
            {
                return false;
            }
        }
        else if ((ulong)srcIndex + totalBytes > 0x1_0000_0000u
            || (ulong)dstIndex + totalBytes > 0x1_0000_0000u)
        {
            return false;
        }

        uint dsBase = Context.GetSegmentBase(SegmentRegister.Ds);
        uint esBase = Context.GetSegmentBase(SegmentRegister.Es);
        bool paging = Mmu.IsEnabled;
        bool move = instruction.Mnemonic == Mnemonic.Movs;
        uint fill = move ? 0 : ReadAccumulator(size);

        if (!paging)
        {
            // Lowest touched index per string (direction-independent range);
            // the pre-checks above guarantee no wrap, so the range is exact.
            ulong spanBytes = totalBytes - (ulong)delta;
            uint srcLo = dsBase + (decrement ? (uint)(srcIndex - spanBytes) : srcIndex);
            uint dstLo = esBase + (decrement ? (uint)(dstIndex - spanBytes) : dstIndex);
            if ((ulong)dstLo + totalBytes > _ramSize || IsWatched(dstLo, (uint)totalBytes))
            {
                return false;
            }

            if (move && (ulong)srcLo + totalBytes > _ramSize)
            {
                return false;
            }

            // No translates and no faults possible: move per page (decode
            // page-drop granularity) in legacy order, then commit registers.
            MoveSpanRam(ram, dsBase, esBase, srcIndex, dstIndex, count, delta, size, fill,
                move, decrement, addressSize);
            AdvanceBulk(srcIndex, dstIndex, count, delta, decrement, addressSize, move);
            return true;
        }

        uint srcCursor = srcIndex;
        uint dstCursor = dstIndex;
        uint remaining = count;
        while (remaining > 0)
        {
            uint srcLinear = dsBase + srcCursor;
            uint dstLinear = esBase + dstCursor;

            // Same-page run length from the cursor element. A straddling
            // element (its bytes cross a page boundary) takes exactly one
            // legacy iteration, whose split byte loop is already exact.
            uint dstOff = dstLinear & 0xFFFu;
            uint iters;
            if (!decrement)
            {
                iters = (0x1000u - dstOff) / (uint)delta;
            }
            else if (dstOff + (uint)delta > 0x1000u)
            {
                iters = 0;
            }
            else
            {
                iters = dstOff / (uint)delta + 1u;
            }

            if (move && iters > 0)
            {
                uint srcOff = srcLinear & 0xFFFu;
                uint srcIters;
                if (!decrement)
                {
                    srcIters = (0x1000u - srcOff) / (uint)delta;
                }
                else if (srcOff + (uint)delta > 0x1000u)
                {
                    srcIters = 0;
                }
                else
                {
                    srcIters = srcOff / (uint)delta + 1u;
                }

                iters = Math.Min(iters, srcIters);
            }

            // Element straddling a page boundary: one exact legacy iteration
            // (its split byte loop), then re-chunk. Rare by alignment.
            if (iters == 0)
            {
                ExecuteStringIteration(instruction, size, addressSize);
                WriteCounter(ReadCounter(addressSize) - 1u, addressSize);
                srcCursor = ReadIndex(Gpr.Esi, addressSize);
                dstCursor = ReadIndex(Gpr.Edi, addressSize);
                remaining--;
                continue;
            }

            if (iters > remaining)
            {
                iters = remaining;
            }

            uint bytes = iters * (uint)delta;
            uint srcPhys = 0;
            if (move)
            {
                // Throws with indices/counter at the chunk start: matches the
                // legacy fault partial state.
                srcPhys = TranslateLinear(srcLinear, isWrite: false, isInstruction: false);
            }

            uint dstPhys = TranslateLinear(dstLinear, isWrite: true, isInstruction: false);
            if ((ulong)dstPhys + bytes > _ramSize || IsWatched(dstPhys, bytes)
                || (move && (ulong)srcPhys + bytes > _ramSize))
            {
                for (uint i = 0; i < iters; i++)
                {
                    ExecuteStringIteration(instruction, size, addressSize);
                    WriteCounter(ReadCounter(addressSize) - 1u, addressSize);
                }

                srcCursor = ReadIndex(Gpr.Esi, addressSize);
                dstCursor = ReadIndex(Gpr.Edi, addressSize);
                remaining -= iters;
                continue;
            }

            uint elementSpan = bytes - (uint)delta;
            uint srcChunk = move
                ? srcPhys - (decrement ? elementSpan : 0u)
                : 0u;
            uint dstChunk = dstPhys - (decrement ? elementSpan : 0u);
            NoteStorePhysical(dstChunk, (int)bytes * 8);
            MoveChunkRam(ram, srcChunk, dstChunk, iters, delta, size, fill,
                move, decrement);

            if (decrement)
            {
                srcCursor -= iters * (uint)delta;
                dstCursor -= iters * (uint)delta;
            }
            else
            {
                srcCursor += iters * (uint)delta;
                dstCursor += iters * (uint)delta;
            }

            remaining -= iters;
            if (move)
            {
                WriteIndex(Gpr.Esi, srcCursor, addressSize);
            }

            WriteIndex(Gpr.Edi, dstCursor, addressSize);
            WriteCounter(ReadCounter(addressSize) - iters, addressSize);
        }

        return true;
    }

    private void MoveSpanRam(PhysicalMemory ram, uint dsBase, uint esBase, uint srcStart, uint dstStart,
        uint count, int delta, int size, uint fill, bool move, bool decrement, int addressSize)
    {
        // MMU-off whole-span move: chunked per destination page for the
        // decode page drop, elements in legacy visit order (overlap and
        // aliasing exact by order). Only the mnemonic's own index commits
        // (rep stos never touches ESI); nothing can fault, so the end state
        // always matches the legacy loop.
        uint srcCursor = srcStart;
        uint dstCursor = dstStart;
        uint remaining = count;
        while (remaining > 0)
        {
            uint dstLinear = esBase + dstCursor;
            uint iters;
            if (!decrement)
            {
                iters = (0x1000u - (dstLinear & 0xFFFu)) / (uint)delta;
            }
            else if ((dstLinear & 0xFFFu) + (uint)delta > 0x1000u)
            {
                iters = 0;
            }
            else
            {
                iters = ((dstLinear & 0xFFFu) / (uint)delta) + 1u;
            }

            if (iters == 0)
            {
                iters = 1;
            }

            if (iters > remaining)
            {
                iters = remaining;
            }

            uint srcLinear = dsBase + srcCursor;
            uint bytes = iters * (uint)delta;
            uint dstChunk = decrement ? dstLinear - (bytes - (uint)delta) : dstLinear;
            uint srcChunk = decrement ? srcLinear - (bytes - (uint)delta) : srcLinear;
            NoteStorePhysical(dstChunk, (int)bytes * 8);
            MoveChunkRam(ram, srcChunk, dstChunk, iters, delta, size, fill, move, decrement);

            if (decrement)
            {
                srcCursor -= bytes;
                dstCursor -= bytes;
            }
            else
            {
                srcCursor += bytes;
                dstCursor += bytes;
            }

            remaining -= iters;
            if (move)
            {
                WriteIndex(Gpr.Esi, srcCursor, addressSize);
            }

            WriteIndex(Gpr.Edi, dstCursor, addressSize);
            WriteCounter(ReadCounter(addressSize) - iters, addressSize);
        }
    }

    private static void MoveChunkRam(PhysicalMemory ram, uint srcBase, uint dstBase, uint iters, int delta, int size,
        uint fill, bool move, bool decrement = false)
    {
        // Elements in legacy visit order: chunk base is the lowest address;
        // backward runs visit high to low. Order-exact under any overlap.
        for (uint k = 0; k < iters; k++)
        {
            uint o = (decrement ? iters - 1u - k : k) * (uint)delta;
            uint s = srcBase + o;
            uint d = dstBase + o;
            if (move)
            {
                switch (size)
                {
                    case 8:
                        ram.Write8(d, ram.Read8(s));
                        break;
                    case 16:
                        ram.Write16(d, ram.Read16(s));
                        break;
                    default:
                        ram.Write32(d, ram.Read32(s));
                        break;
                }
            }
            else
            {
                switch (size)
                {
                    case 8:
                        ram.Write8(d, (byte)fill);
                        break;
                    case 16:
                        ram.Write16(d, (ushort)fill);
                        break;
                    default:
                        ram.Write32(d, fill);
                        break;
                }
            }
        }
    }

    private void AdvanceBulk(uint srcStart, uint dstStart, uint count, int delta, bool decrement,
        int addressSize, bool move)
    {
        uint total = count * (uint)delta;
        uint dstEnd = decrement ? dstStart - total : dstStart + total;
        if (move)
        {
            uint srcEnd = decrement ? srcStart - total : srcStart + total;
            WriteIndex(Gpr.Esi, srcEnd, addressSize);
        }

        WriteIndex(Gpr.Edi, dstEnd, addressSize);
        WriteCounter(0, addressSize);
    }

    private void WriteIndex(Gpr register, uint value, int addressSize)
    {
        if (addressSize == 16)
        {
            Context.WriteRegister16(register, (ushort)value);
        }
        else
        {
            Context.WriteRegister(register, value);
        }
    }

    private void ExecuteStringIteration(in DecodedInstruction instruction, int size, int addressSize)
    {
        bool decrement = (Context.Eflags & FlagBits.Df) != 0;
        int delta = size / 8;
        uint sourceIndex = ReadIndex(Gpr.Esi, addressSize);
        uint destinationIndex = ReadIndex(Gpr.Edi, addressSize);
        uint sourceAddress = Context.GetSegmentBase(SegmentRegister.Ds) + sourceIndex;
        uint destinationAddress = Context.GetSegmentBase(SegmentRegister.Es) + destinationIndex;

        switch (instruction.Mnemonic)
        {
            case Mnemonic.Movs:
                WriteMemory(destinationAddress, size, ReadMemory(sourceAddress, size));
                AdvanceIndex(Gpr.Esi, decrement, delta, addressSize);
                AdvanceIndex(Gpr.Edi, decrement, delta, addressSize);
                break;

            case Mnemonic.Cmps:
                Context.Eflags = AluOps.Sub(
                    ReadMemory(sourceAddress, size),
                    ReadMemory(destinationAddress, size),
                    0,
                    size,
                    Context.Eflags,
                    out _);
                AdvanceIndex(Gpr.Esi, decrement, delta, addressSize);
                AdvanceIndex(Gpr.Edi, decrement, delta, addressSize);
                break;

            case Mnemonic.Stos:
                WriteMemory(destinationAddress, size, ReadAccumulator(size));
                AdvanceIndex(Gpr.Edi, decrement, delta, addressSize);
                break;

            case Mnemonic.Lods:
                WriteAccumulator(size, ReadMemory(sourceAddress, size));
                AdvanceIndex(Gpr.Esi, decrement, delta, addressSize);
                break;

            case Mnemonic.Ins:
                WriteMemory(destinationAddress, size, ReadIo((ushort)Context.Edx, size));
                AdvanceIndex(Gpr.Edi, decrement, delta, addressSize);
                break;

            case Mnemonic.Outs:
                WriteIo((ushort)Context.Edx, size, ReadMemory(sourceAddress, size));
                AdvanceIndex(Gpr.Esi, decrement, delta, addressSize);
                break;

            default:
                Context.Eflags = AluOps.Sub(
                    ReadAccumulator(size),
                    ReadMemory(destinationAddress, size),
                    0,
                    size,
                    Context.Eflags,
                    out _);
                AdvanceIndex(Gpr.Edi, decrement, delta, addressSize);
                break;
        }
    }

    private uint ReadAccumulator(int size)
    {
        return size switch
        {
            8 => Context.ReadRegister8(0),
            16 => Context.ReadRegister16(Gpr.Eax),
            _ => Context.ReadRegister(Gpr.Eax),
        };
    }

    private void WriteAccumulator(int size, uint value)
    {
        switch (size)
        {
            case 8:
                Context.WriteRegister8(0, (byte)value);
                break;
            case 16:
                Context.WriteRegister16(Gpr.Eax, (ushort)value);
                break;
            default:
                Context.WriteRegister(Gpr.Eax, value);
                break;
        }
    }

    private uint ReadIndex(Gpr register, int addressSize)
    {
        return addressSize == 16 ? Context.ReadRegister16(register) : Context.ReadRegister(register);
    }

    private void AdvanceIndex(Gpr register, bool decrement, int delta, int addressSize)
    {
        uint value = ReadIndex(register, addressSize);
        value = decrement ? value - (uint)delta : value + (uint)delta;

        if (addressSize == 16)
        {
            Context.WriteRegister16(register, (ushort)value);
        }
        else
        {
            Context.WriteRegister(register, value);
        }
    }

    private FpuState Fpu => Context.Fpu;

    private ulong ReadMemory64(uint address)
    {
        if (!Mmu.IsEnabled)
        {
            // Slice 1: one direct 64-bit RAM read instead of two bus
            // dispatches (little-endian combine is identical by construction).
            PhysicalMemory? ram = _ramDirect;
            if (ram is not null && (ulong)address + 8 <= _ramSize)
            {
                return ram.Read64(address);
            }

            return (ulong)Memory.Read32(address) | ((ulong)Memory.Read32(address + 4) << 32);
        }

        if (!CrossesPage(address, 64))
        {
            uint physical = TranslateLinear(address, isWrite: false, isInstruction: false);
            PhysicalMemory? ram = _ramDirect;
            if (ram is not null && (ulong)physical + 8 <= _ramSize)
            {
                return ram.Read64(physical);
            }

            return (ulong)Memory.Read32(physical) | ((ulong)Memory.Read32(physical + 4) << 32);
        }

        ulong value = 0;
        for (int i = 0; i < 8; i++)
        {
            value |= (ulong)Memory.Read8(TranslateLinear(address + (uint)i, isWrite: false, isInstruction: false)) <<
                     (8 * i);
        }

        return value;
    }

    private void WriteMemory64(uint address, ulong value)
    {
        if (!Mmu.IsEnabled)
        {
            // Slice 1: mirror of the read fusion. NoteStorePhysical still
            // runs first; the write watch is preserved via fallback.
            PhysicalMemory? ram = _ramDirect;
            if (ram is not null && (ulong)address + 8 <= _ramSize && !IsWatched(address, 8))
            {
                NoteStorePhysical(address, 64);
                ram.Write64(address, value);
                return;
            }

            NoteStorePhysical(address, 64);
            Memory.Write32(address, (uint)value);
            Memory.Write32(address + 4, (uint)(value >> 32));
            return;
        }

        if (!CrossesPage(address, 64))
        {
            uint physical = TranslateLinear(address, isWrite: true, isInstruction: false);
            PhysicalMemory? ram = _ramDirect;
            if (ram is not null && (ulong)physical + 8 <= _ramSize && !IsWatched(physical, 8))
            {
                NoteStorePhysical(physical, 64);
                ram.Write64(physical, value);
                return;
            }

            NoteStorePhysical(physical, 64);
            Memory.Write32(physical, (uint)value);
            Memory.Write32(physical + 4, (uint)(value >> 32));
            return;
        }

        for (int i = 0; i < 8; i++)
        {
            uint physical = TranslateLinear(address + (uint)i, isWrite: true, isInstruction: false);
            Memory.Write8(physical, (byte)(value >> (8 * i)));
            NoteStorePhysical(physical, 8);
        }
    }

    private Fpu80 ReadMemoryFpu80(uint address)
    {
        // Slice 1: single-translate 80-bit RAM read (two translates + a
        // hotspot-noted 16-bit tail in the legacy path). Gated on no
        // observers (note counts change 2-idle+1-hotspot to 1-idle+0);
        // one-page guard keeps fault behavior identical (the second
        // translate can only fault on a different page).
        PhysicalMemory? ram = _ramDirect;
        if (FastWideAllowed && ram is not null
            && (address & 0xFFFu) + 10 <= 0x1000u && (ulong)address + 10 <= _ramSize)
        {
            uint physical = TranslateLinear(address, isWrite: false, isInstruction: false);
            if ((ulong)physical + 10 <= _ramSize)
            {
                ulong significand = ram.Read64(physical);
                ushort signExponent = ram.Read16(physical + 8);
                return new Fpu80(signExponent, significand);
            }
        }

        ulong legacySignificand = ReadMemory64(address);
        ushort legacySignExponent = (ushort)ReadMemory(address + 8, 16);
        return new Fpu80(legacySignExponent, legacySignificand);
    }

    private void WriteMemoryFpu80(uint address, Fpu80 value)
    {
        // Slice 1: mirror of the read fusion. NoteStorePhysical(80) on one
        // page drops exactly the page the legacy two drops hit (idempotent);
        // present-dirty accumulates by OR so chunking is invisible.
        PhysicalMemory? ram = _ramDirect;
        if (FastWideAllowed && ram is not null
            && (address & 0xFFFu) + 10 <= 0x1000u && (ulong)address + 10 <= _ramSize)
        {
            uint physical = TranslateLinear(address, isWrite: true, isInstruction: false);
            if ((ulong)physical + 10 <= _ramSize && !IsWatched(physical, 10))
            {
                NoteStorePhysical(physical, 80);
                ram.Write64(physical, value.Significand);
                ram.Write16(physical + 8, value.SignExponent);
                return;
            }
        }

        WriteMemory64(address, value.Significand);
        WriteMemory(address + 8, 16, value.SignExponent);
    }

    private void ReadMemoryBytes(uint address, Span<byte> destination)
    {
        int length = destination.Length;
        if (length == 0)
        {
            return;
        }

        // Slice-2 bulk path: Fxsave/Fxrstor move 512 B (512 translates +
        // 512 bus dispatches in the legacy loop). RAM-only spans copy in
        // page-bounded chunks with one translate per page; anything touching
        // MMIO/unmapped falls back to the exact per-byte loop for that
        // chunk. Fault behavior is identical: the chunk-start translate can
        // only fault for its own page, so the first-failing linear matches
        // the legacy first-failing byte, and already-copied chunks match the
        // legacy partial fill. Gated on FastWideAllowed (translate/idle/TLB
        // note counts change N→pages); the MMU-off shape needs no gate (no
        // translates, no observer-visible difference).
        PhysicalMemory? ram = _ramDirect;
        if (!Mmu.IsEnabled)
        {
            if (ram is not null && (ulong)address + (ulong)length <= _ramSize)
            {
                ram.GetReadOnlySpan(address, length).CopyTo(destination);
                return;
            }

            for (int i = 0; i < length; i++)
            {
                destination[i] = Memory.Read8(address + (uint)i);
            }

            return;
        }

        if (FastWideAllowed && ram is not null)
        {
            int offset = 0;
            while (offset < length)
            {
                uint linear = address + (uint)offset;
                int chunk = Math.Min(length - offset, (int)(0x1000u - (linear & 0xFFFu)));
                uint physical = TranslateLinear(linear, isWrite: false, isInstruction: false);
                if ((ulong)physical + (ulong)chunk <= _ramSize)
                {
                    ram.GetReadOnlySpan(physical, chunk).CopyTo(destination.Slice(offset, chunk));
                }
                else
                {
                    for (int i = 0; i < chunk; i++)
                    {
                        destination[offset + i] = Memory.Read8(TranslateLinear(linear + (uint)i, isWrite: false, isInstruction: false));
                    }
                }

                offset += chunk;
            }

            return;
        }

        for (int i = 0; i < length; i++)
        {
            destination[i] = Memory.Read8(TranslateLinear(address + (uint)i, isWrite: false, isInstruction: false));
        }
    }

    private void WriteMemoryBytes(uint address, ReadOnlySpan<byte> source)
    {
        int length = source.Length;
        if (length == 0)
        {
            return;
        }

        // Slice-2 mirror of the read bulk path. Per-chunk order matches the
        // legacy loop (translate → store → page drop) so fault partial
        // writes are identical; NoteStorePhysical per page-bounded chunk
        // drops exactly the pages the legacy per-byte drops hit
        // (idempotent), and WriteSpan runs the same present-dirty choke.
        // Watch overlap falls back per chunk to preserve per-byte WatchHit
        // traffic. Same gating as the read path.
        PhysicalMemory? ram = _ramDirect;
        if (!Mmu.IsEnabled)
        {
            if (ram is not null && (ulong)address + (ulong)length <= _ramSize && !IsWatched(address, (uint)length))
            {
                int offset = 0;
                while (offset < length)
                {
                    uint chunkBase = address + (uint)offset;
                    int chunk = Math.Min(length - offset, (int)(0x1000u - (chunkBase & 0xFFFu)));
                    NoteStorePhysical(chunkBase, chunk * 8);
                    ram.WriteSpan(chunkBase, source.Slice(offset, chunk));
                    offset += chunk;
                }

                return;
            }

            for (int i = 0; i < length; i++)
            {
                NoteStorePhysical(address + (uint)i, 8);
                Memory.Write8(address + (uint)i, source[i]);
            }

            return;
        }

        if (FastWideAllowed && ram is not null)
        {
            int offset = 0;
            while (offset < length)
            {
                uint linear = address + (uint)offset;
                int chunk = Math.Min(length - offset, (int)(0x1000u - (linear & 0xFFFu)));
                uint physical = TranslateLinear(linear, isWrite: true, isInstruction: false);
                if ((ulong)physical + (ulong)chunk <= _ramSize && !IsWatched(physical, (uint)chunk))
                {
                    NoteStorePhysical(physical, chunk * 8);
                    ram.WriteSpan(physical, source.Slice(offset, chunk));
                }
                else
                {
                    for (int i = 0; i < chunk; i++)
                    {
                        uint phys = TranslateLinear(linear + (uint)i, isWrite: true, isInstruction: false);
                        Memory.Write8(phys, source[offset + i]);
                        NoteStorePhysical(phys, 8);
                    }
                }

                offset += chunk;
            }

            return;
        }

        for (int i = 0; i < source.Length; i++)
        {
            if (Mmu.IsEnabled)
            {
                uint physical = TranslateLinear(address + (uint)i, isWrite: true, isInstruction: false);
                Memory.Write8(physical, source[i]);
                NoteStorePhysical(physical, 8);
            }
            else
            {
                NoteStorePhysical(address + (uint)i, 8);
                Memory.Write8(address + (uint)i, source[i]);
            }
        }
    }

    private uint ReadGprOrMemory(in Operand operand)
    {
        return operand.Kind == OperandKind.Register
            ? Context.ReadRegister((Gpr)operand.RegisterIndex)
            : ReadMemory(ComputeAddress(operand.Memory), operand.Size);
    }

    private void WriteGprOrMemory(in Operand operand, uint value)
    {
        if (operand.Kind == OperandKind.Register)
        {
            Context.WriteRegister((Gpr)operand.RegisterIndex, value);
        }
        else
        {
            WriteMemory(ComputeAddress(operand.Memory), operand.Size, value);
        }
    }

    private void ExecuteIn(in DecodedInstruction instruction)
    {
        ushort port = ReadIoPort(instruction.Operand1);
        WriteOperand(instruction.Operand2, ReadIo(port, instruction.Operand2.Size));
    }

    private void ExecuteOut(in DecodedInstruction instruction)
    {
        ushort port = ReadIoPort(instruction.Operand1);
        WriteIo(port, instruction.Operand2.Size, ReadOperand(instruction.Operand2));
    }

    private uint ReadIo(ushort port, int size)
    {
        return size switch
        {
            8 => Io.Read8(port),
            16 => Io.Read16(port),
            _ => Io.Read32(port),
        };
    }

    private void WriteIo(ushort port, int size, uint value)
    {
        switch (size)
        {
            case 8:
                Io.Write8(port, (byte)value);
                break;
            case 16:
                Io.Write16(port, (ushort)value);
                break;
            default:
                Io.Write32(port, value);
                break;
        }
    }

    private void ExecuteXadd(in DecodedInstruction instruction)
    {
        uint destination = ReadOperand(instruction.Operand1);
        Context.Eflags = AluOps.Add(
            destination,
            ReadOperand(instruction.Operand2),
            0,
            instruction.Operand1.Size,
            Context.Eflags,
            out uint result);
        WriteOperand(instruction.Operand2, destination);
        WriteOperand(instruction.Operand1, result);
    }

    private void ExecuteCmpxchg(in DecodedInstruction instruction)
    {
        int size = instruction.Operand1.Size;
        uint destination = ReadOperand(instruction.Operand1);
        uint accumulator = ReadAccumulator(size);
        Context.Eflags = AluOps.Sub(accumulator, destination, 0, size, Context.Eflags, out _);
        if (accumulator == destination)
        {
            WriteOperand(instruction.Operand1, ReadOperand(instruction.Operand2));
        }
        else
        {
            WriteAccumulator(size, destination);
        }
    }

    private void ExecuteCmpxchg8B(in DecodedInstruction instruction)
    {
        uint address = ComputeAddress(instruction.Operand1.Memory);
        uint low = ReadMemory(address, 32);
        uint high = ReadMemory(address + 4, 32);
        if (low == Context.ReadRegister(Gpr.Eax) && high == Context.ReadRegister(Gpr.Edx))
        {
            WriteMemory(address, 32, Context.ReadRegister(Gpr.Ebx));
            WriteMemory(address + 4, 32, Context.ReadRegister(Gpr.Ecx));
            Context.Eflags |= FlagBits.Zf;
        }
        else
        {
            Context.WriteRegister(Gpr.Eax, low);
            Context.WriteRegister(Gpr.Edx, high);
            Context.Eflags &= ~FlagBits.Zf;
        }
    }

    private void ExecuteBitScan(in DecodedInstruction instruction, bool reverse)
    {
        uint source = ReadOperand(instruction.Operand2);
        int size = instruction.Operand2.Size;
        uint masked = size == 16 ? source & 0xFFFFu : source;
        if (masked == 0)
        {
            Context.Eflags |= FlagBits.Zf;
            return;
        }

        Context.Eflags &= ~FlagBits.Zf;
        uint index = reverse
            ? (uint)System.Numerics.BitOperations.Log2(masked)
            : (uint)System.Numerics.BitOperations.TrailingZeroCount(masked);
        WriteOperand(instruction.Operand1, index);
    }

    private void ExecuteBitTest(in DecodedInstruction instruction, int operation)
    {
        int size = instruction.Operand1.Size;
        int bitWidth = size == 16 ? 16 : 32;
        uint bitIndex = ReadOperand(instruction.Operand2);

        if (instruction.Operand1.Kind == OperandKind.Memory)
        {
            int bytes = size / 8;
            uint address = ComputeAddress(instruction.Operand1.Memory);
            address += (bitIndex / (uint)bitWidth) * (uint)bytes;
            uint offset = bitIndex % (uint)bitWidth;
            uint value = ReadMemory(address, size);
            uint mask = 1u << (int)offset;
            SetCarry((value & mask) != 0);
            value = operation switch
            {
                1 => value | mask,
                2 => value & ~mask,
                3 => value ^ mask,
                _ => value,
            };

            if (operation != 0)
            {
                WriteMemory(address, size, value);
            }

            return;
        }

        uint destination = ReadOperand(instruction.Operand1);
        uint registerMask = 1u << (int)(bitIndex & (uint)(bitWidth - 1));
        SetCarry((destination & registerMask) != 0);
        uint result = operation switch
        {
            1 => destination | registerMask,
            2 => destination & ~registerMask,
            3 => destination ^ registerMask,
            _ => destination,
        };

        if (operation != 0)
        {
            WriteOperand(instruction.Operand1, result);
        }
    }

    private void SetCarry(bool value)
    {
        if (value)
        {
            Context.Eflags |= FlagBits.Cf;
        }
        else
        {
            Context.Eflags &= ~FlagBits.Cf;
        }
    }

    private ushort ReadIoPort(in Operand operand)
    {
        return operand.Kind == OperandKind.Register
            ? Context.ReadRegister16(Gpr.Edx)
            : (ushort)operand.Immediate;
    }

    private sealed class NullIoBus : IIoBus
    {
        public static NullIoBus Instance { get; } = new();

        public byte Read8(ushort port)
        {
            return 0xFF;
        }

        public ushort Read16(ushort port)
        {
            return 0xFFFF;
        }

        public uint Read32(ushort port)
        {
            return 0xFFFFFFFFu;
        }

        public void Write8(ushort port, byte value)
        {
        }

        public void Write16(ushort port, ushort value)
        {
        }

        public void Write32(ushort port, uint value)
        {
        }
    }

    private Fpu80 ReadFpuReal(in Operand operand)
    {
        if (operand.Kind == OperandKind.Register)
        {
            return Fpu.GetRegister(operand.RegisterIndex);
        }

        uint address = ComputeAddress(operand.Memory);
        return operand.Size switch
        {
            32 => Fpu80.FromDouble(BitConverter.UInt32BitsToSingle((uint)ReadMemory(address, 32))),
            64 => Fpu80.FromDouble(BitConverter.Int64BitsToDouble((long)ReadMemory64(address))),
            _ => ReadMemoryFpu80(address),
        };
    }

    private Fpu80 ReadFpuInteger(in Operand operand)
    {
        uint address = ComputeAddress(operand.Memory);
        return operand.Size switch
        {
            16 => Fpu80.FromInt64((short)ReadMemory(address, 16)),
            32 => Fpu80.FromInt64((int)ReadMemory(address, 32)),
            _ => Fpu80.FromInt64((long)ReadMemory64(address)),
        };
    }

    private void WriteFpuReal(in Operand operand, Fpu80 value)
    {
        if (operand.Kind == OperandKind.Register)
        {
            Fpu.SetRegister(operand.RegisterIndex, value);
            return;
        }

        uint address = ComputeAddress(operand.Memory);
        switch (operand.Size)
        {
            case 32:
                WriteMemory(address, 32, BitConverter.SingleToUInt32Bits((float)value.ToDouble()));
                break;
            case 64:
                WriteMemory64(address, (ulong)BitConverter.DoubleToInt64Bits(value.ToDouble()));
                break;
            default:
                WriteMemoryFpu80(address, value);
                break;
        }
    }

    private void WriteFpuInteger(in Operand operand, Fpu80 value)
    {
        uint address = ComputeAddress(operand.Memory);
        int mode = Fpu.RoundingMode;
        switch (operand.Size)
        {
            case 16:
                if (!value.TryToInt16(mode, out short small))
                {
                    small = unchecked((short)0x8000);
                    Fpu.SetException(0);
                }

                WriteMemory(address, 16, (ushort)small);
                break;

            case 32:
                if (!value.TryToInt32(mode, out int word))
                {
                    word = unchecked((int)0x80000000u);
                    Fpu.SetException(0);
                }

                WriteMemory(address, 32, (uint)word);
                break;

            default:
                if (!value.TryToInt64(mode, out long wide))
                {
                    wide = long.MinValue;
                    Fpu.SetException(0);
                }

                WriteMemory64(address, (ulong)wide);
                break;
        }
    }

    private static Fpu80 ApplyFpuArithmetic(Mnemonic mnemonic, Fpu80 left, Fpu80 right)
    {
        return mnemonic switch
        {
            Mnemonic.Fadd or Mnemonic.Faddp or Mnemonic.Fiadd => Fpu80.Add(left, right),
            Mnemonic.Fmul or Mnemonic.Fmulp or Mnemonic.Fimul => Fpu80.Multiply(left, right),
            Mnemonic.Fsub or Mnemonic.Fsubp or Mnemonic.Fisub => Fpu80.Subtract(left, right),
            Mnemonic.Fsubr or Mnemonic.Fsubrp or Mnemonic.Fisubr => Fpu80.Subtract(right, left),
            Mnemonic.Fdiv or Mnemonic.Fdivp or Mnemonic.Fidiv => Fpu80.Divide(left, right),
            _ => Fpu80.Divide(right, left),
        };
    }

    private void SetFpuCompare(Fpu80 left, Fpu80 right)
    {
        if (left.IsNaN || right.IsNaN)
        {
            Fpu.SetConditionCodes(true, true, true);
            Fpu.SetException(0);
            return;
        }

        double a = left.ToDouble();
        double b = right.ToDouble();
        Fpu.SetConditionCodes(a == b, false, a < b);
    }

    private void SetFpuFlagsCompare(Fpu80 left, Fpu80 right)
    {
        uint flags = Context.Eflags &
                     ~(FlagBits.Zf | FlagBits.Pf | FlagBits.Cf | FlagBits.Of | FlagBits.Sf | FlagBits.Af);

        if (left.IsNaN || right.IsNaN)
        {
            flags |= FlagBits.Zf | FlagBits.Pf | FlagBits.Cf;
        }
        else
        {
            double a = left.ToDouble();
            double b = right.ToDouble();
            if (a < b)
            {
                flags |= FlagBits.Cf;
            }
            else if (a == b)
            {
                flags |= FlagBits.Zf;
            }
        }

        Context.Eflags = flags;
    }

    private void ExecuteFpu(in DecodedInstruction instruction)
    {
        switch (instruction.Mnemonic)
        {
            case Mnemonic.Fwait:
            case Mnemonic.Fnop:
                break;

            case Mnemonic.Fld:
                Fpu.Push(ReadFpuReal(instruction.Operand1));
                break;

            case Mnemonic.Fst:
                WriteFpuReal(instruction.Operand1, Fpu.GetRegister(0));
                break;

            case Mnemonic.Fstp:
                WriteFpuReal(instruction.Operand1, Fpu.GetRegister(0));
                Fpu.Pop();
                break;

            case Mnemonic.Fild:
                Fpu.Push(ReadFpuInteger(instruction.Operand1));
                break;

            case Mnemonic.Fist:
                WriteFpuInteger(instruction.Operand1, Fpu.GetRegister(0));
                break;

            case Mnemonic.Fistp:
                WriteFpuInteger(instruction.Operand1, Fpu.GetRegister(0));
                Fpu.Pop();
                break;

            case Mnemonic.Fbld:
                Fpu.Push(ReadBcd(ComputeAddress(instruction.Operand1.Memory)));
                break;

            case Mnemonic.Fbstp:
                WriteBcd(ComputeAddress(instruction.Operand1.Memory), Fpu.GetRegister(0));
                Fpu.Pop();
                break;

            case Mnemonic.Fxch:
                Fpu.Exchange(instruction.Operand1.RegisterIndex);
                break;

            case Mnemonic.Fadd:
            case Mnemonic.Fmul:
            case Mnemonic.Fsub:
            case Mnemonic.Fsubr:
            case Mnemonic.Fdiv:
            case Mnemonic.Fdivr:
                {
                    Fpu80 left = ReadFpuReal(instruction.Operand1);
                    Fpu80 right = ReadFpuReal(instruction.Operand2);
                    WriteFpuReal(instruction.Operand1, ApplyFpuArithmetic(instruction.Mnemonic, left, right));
                    break;
                }

            case Mnemonic.Faddp:
            case Mnemonic.Fmulp:
            case Mnemonic.Fsubp:
            case Mnemonic.Fsubrp:
            case Mnemonic.Fdivp:
            case Mnemonic.Fdivrp:
                {
                    Fpu80 left = Fpu.GetRegister(instruction.Operand1.RegisterIndex);
                    Fpu80 right = Fpu.GetRegister(0);
                    Fpu.SetRegister(instruction.Operand1.RegisterIndex,
                        ApplyFpuArithmetic(instruction.Mnemonic, left, right));
                    Fpu.Pop();
                    break;
                }

            case Mnemonic.Fiadd:
            case Mnemonic.Fimul:
            case Mnemonic.Fisub:
            case Mnemonic.Fisubr:
            case Mnemonic.Fidiv:
            case Mnemonic.Fidivr:
                Fpu.SetRegister(0,
                    ApplyFpuArithmetic(instruction.Mnemonic, Fpu.GetRegister(0), ReadFpuInteger(instruction.Operand1)));
                break;

            case Mnemonic.Fcom:
            case Mnemonic.Fcomp:
            case Mnemonic.Fucom:
            case Mnemonic.Fucomp:
                {
                    Operand compared = instruction.Operand2.Kind == OperandKind.None
                        ? instruction.Operand1
                        : instruction.Operand2;
                    SetFpuCompare(Fpu.GetRegister(0), ReadFpuReal(compared));
                    if (instruction.Mnemonic is Mnemonic.Fcomp or Mnemonic.Fucomp)
                    {
                        Fpu.Pop();
                    }

                    break;
                }

            case Mnemonic.Ficom:
            case Mnemonic.Ficomp:
                {
                    SetFpuCompare(Fpu.GetRegister(0), ReadFpuInteger(instruction.Operand1));
                    if (instruction.Mnemonic == Mnemonic.Ficomp)
                    {
                        Fpu.Pop();
                    }

                    break;
                }

            case Mnemonic.Fcompp:
            case Mnemonic.Fucompp:
                SetFpuCompare(Fpu.GetRegister(0), Fpu.GetRegister(1));
                Fpu.Pop();
                Fpu.Pop();
                break;

            case Mnemonic.Fcomi:
            case Mnemonic.Fcomip:
            case Mnemonic.Fucomi:
            case Mnemonic.Fucomip:
                SetFpuFlagsCompare(Fpu.GetRegister(0), Fpu.GetRegister(instruction.Operand2.RegisterIndex));
                if (instruction.Mnemonic is Mnemonic.Fcomip or Mnemonic.Fucomip)
                {
                    Fpu.Pop();
                }

                break;

            case Mnemonic.Ftst:
                SetFpuCompare(Fpu.GetRegister(0), Fpu80.Zero);
                break;

            case Mnemonic.Fxam:
                ExecuteFxam();
                break;

            case Mnemonic.Fchs:
                Fpu.SetRegister(0, Fpu.GetRegister(0).Negate());
                break;

            case Mnemonic.Fabs:
                Fpu.SetRegister(0, Fpu.GetRegister(0).Abs());
                break;

            case Mnemonic.Fsqrt:
                Fpu.SetRegister(0, Fpu80.SquareRoot(Fpu.GetRegister(0)));
                break;

            case Mnemonic.Frndint:
                Fpu.SetRegister(0, Fpu80.FromDouble(RoundToInteger(Fpu.GetRegister(0).ToDouble(), Fpu.RoundingMode)));
                break;

            case Mnemonic.Fscale:
                {
                    double scale = Math.Truncate(Fpu.GetRegister(1).ToDouble());
                    int exponent = (int)Math.Clamp(scale, -32768, 32767);
                    Fpu.SetRegister(0, Fpu80.FromDouble(Math.ScaleB(Fpu.GetRegister(0).ToDouble(), exponent)));
                    break;
                }

            case Mnemonic.Fxtract:
                ExecuteFxtract();
                break;

            case Mnemonic.Fprem:
            case Mnemonic.Fprem1:
                ExecuteFprem(instruction.Mnemonic == Mnemonic.Fprem1);
                break;

            case Mnemonic.Fdecstp:
                Fpu.Top = (Fpu.Top - 1) & 7;
                break;

            case Mnemonic.Fincstp:
                Fpu.Top = (Fpu.Top + 1) & 7;
                break;

            case Mnemonic.Ffree:
                Fpu.Free(instruction.Operand1.RegisterIndex);
                break;

            case Mnemonic.Ffreep:
                Fpu.Free(instruction.Operand1.RegisterIndex);
                Fpu.Pop();
                break;

            case Mnemonic.Fninit:
                Fpu.Init();
                break;

            case Mnemonic.Fnclex:
                Fpu.ClearExceptions();
                break;

            case Mnemonic.Fldcw:
                Fpu.ControlWord = (ushort)ReadMemory(ComputeAddress(instruction.Operand1.Memory), 16);
                break;

            case Mnemonic.Fnstcw:
                WriteMemory(ComputeAddress(instruction.Operand1.Memory), 16, Fpu.ControlWord);
                break;

            case Mnemonic.Fnstsw:
                if (instruction.Operand1.Kind == OperandKind.None)
                {
                    Context.WriteRegister16(Gpr.Eax, Fpu.StatusWord);
                }
                else
                {
                    WriteMemory(ComputeAddress(instruction.Operand1.Memory), 16, Fpu.StatusWord);
                }

                break;

            case Mnemonic.Fldenv:
                LoadFpuEnvironment(ComputeAddress(instruction.Operand1.Memory), instruction.OperandSize);
                break;

            case Mnemonic.Fnstenv:
                SaveFpuEnvironment(ComputeAddress(instruction.Operand1.Memory), instruction.OperandSize);
                break;

            case Mnemonic.Fnsave:
                SaveFpuState(ComputeAddress(instruction.Operand1.Memory), instruction.OperandSize);
                break;

            case Mnemonic.Frstor:
                RestoreFpuState(ComputeAddress(instruction.Operand1.Memory), instruction.OperandSize);
                break;

            case Mnemonic.Fcmovb:
            case Mnemonic.Fcmove:
            case Mnemonic.Fcmovbe:
            case Mnemonic.Fcmovu:
            case Mnemonic.Fcmovnb:
            case Mnemonic.Fcmovne:
            case Mnemonic.Fcmovnbe:
            case Mnemonic.Fcmovnu:
                if (EvaluateFcmovCondition(instruction.Mnemonic))
                {
                    Fpu.SetRegister(0, Fpu.GetRegister(instruction.Operand2.RegisterIndex));
                }

                break;

            case Mnemonic.Fld1:
                Fpu.Push(Fpu80.One);
                break;
            case Mnemonic.Fldl2T:
                Fpu.Push(Fpu80.Log2Ten);
                break;
            case Mnemonic.Fldl2E:
                Fpu.Push(Fpu80.Log2E);
                break;
            case Mnemonic.Fldpi:
                Fpu.Push(Fpu80.Pi);
                break;
            case Mnemonic.Fldlg2:
                Fpu.Push(Fpu80.Log10Two);
                break;
            case Mnemonic.Fldln2:
                Fpu.Push(Fpu80.LnTwo);
                break;
            case Mnemonic.Fldz:
                Fpu.Push(Fpu80.Zero);
                break;

            case Mnemonic.F2Xm1:
                Fpu.SetRegister(0, Fpu80.FromDouble(Math.Pow(2.0, Fpu.GetRegister(0).ToDouble()) - 1.0));
                break;

            case Mnemonic.Fyl2X:
                {
                    double result = Fpu.GetRegister(1).ToDouble() * Math.Log2(Fpu.GetRegister(0).ToDouble());
                    Fpu.SetRegister(1, Fpu80.FromDouble(result));
                    Fpu.Pop();
                    break;
                }

            case Mnemonic.Fyl2Xp1:
                {
                    double result = Fpu.GetRegister(1).ToDouble() * Math.Log2(Fpu.GetRegister(0).ToDouble() + 1.0);
                    Fpu.SetRegister(1, Fpu80.FromDouble(result));
                    Fpu.Pop();
                    break;
                }

            case Mnemonic.Fptan:
                Fpu.SetRegister(0, Fpu80.FromDouble(Math.Tan(Fpu.GetRegister(0).ToDouble())));
                Fpu.Push(Fpu80.One);
                break;

            case Mnemonic.Fpatan:
                {
                    double result = Math.Atan2(Fpu.GetRegister(1).ToDouble(), Fpu.GetRegister(0).ToDouble());
                    Fpu.SetRegister(1, Fpu80.FromDouble(result));
                    Fpu.Pop();
                    break;
                }

            case Mnemonic.Fsin:
                Fpu.SetRegister(0, Fpu80.FromDouble(Math.Sin(Fpu.GetRegister(0).ToDouble())));
                break;

            case Mnemonic.Fcos:
                Fpu.SetRegister(0, Fpu80.FromDouble(Math.Cos(Fpu.GetRegister(0).ToDouble())));
                break;

            case Mnemonic.Fsincos:
                {
                    double angle = Fpu.GetRegister(0).ToDouble();
                    Fpu.SetRegister(0, Fpu80.FromDouble(Math.Sin(angle)));
                    Fpu.Push(Fpu80.FromDouble(Math.Cos(angle)));
                    break;
                }

            default:
                throw new UnsupportedInstructionException(Context.Eip, instruction.Opcode);
        }
    }

    private void ExecuteFxam()
    {
        Fpu80 value = Fpu.GetRegister(0);
        int tag = (Fpu.TagWord >> (Fpu.Top * 2)) & 3;

        bool c3;
        bool c2;
        bool c0;

        if (tag == FpuState.TagEmpty)
        {
            (c3, c2, c0) = (true, false, true);
        }
        else if (value.IsNaN)
        {
            (c3, c2, c0) = (false, false, true);
        }
        else if (value.IsInfinity)
        {
            (c3, c2, c0) = (false, true, true);
        }
        else if (value.IsZero)
        {
            (c3, c2, c0) = (true, false, false);
        }
        else if (value.Exponent == 0 || value.IsUnnormal)
        {
            (c3, c2, c0) = (true, true, false);
        }
        else
        {
            (c3, c2, c0) = (false, true, false);
        }

        Fpu.SetConditionCodes(c3, c2, c0);
        Fpu.SetC1(tag != FpuState.TagEmpty && value.IsNegative);
    }

    private void ExecuteFxtract()
    {
        Fpu80 value = Fpu.GetRegister(0);
        double magnitude = Math.Abs(value.ToDouble());

        if (magnitude == 0 || double.IsInfinity(magnitude) || double.IsNaN(magnitude))
        {
            double exponent = magnitude == 0 ? double.NegativeInfinity : double.PositiveInfinity;
            if (double.IsNaN(magnitude))
            {
                exponent = double.NaN;
            }

            Fpu.SetRegister(0, Fpu80.FromDouble(exponent));
            Fpu.Push(Fpu80.FromDouble(value.IsNegative ? -magnitude : magnitude));
            return;
        }

        int exponentValue = Math.ILogB(magnitude);
        double significand = Math.ScaleB(magnitude, -exponentValue);
        Fpu.SetRegister(0, Fpu80.FromInt32(exponentValue));
        Fpu.Push(Fpu80.FromDouble(value.IsNegative ? -significand : significand));
    }

    private void ExecuteFprem(bool ieee)
    {
        double dividend = Fpu.GetRegister(0).ToDouble();
        double divisor = Fpu.GetRegister(1).ToDouble();

        if (double.IsNaN(dividend) || double.IsNaN(divisor) || double.IsInfinity(dividend) || divisor == 0)
        {
            Fpu.SetRegister(0, Fpu80.FromDouble(double.NaN));
            Fpu.SetConditionCodes(false, false, false);
            return;
        }

        double quotient;
        double remainder;
        if (ieee)
        {
            remainder = Math.IEEERemainder(dividend, divisor);
            quotient = Math.Round((dividend - remainder) / divisor);
        }
        else
        {
            quotient = Math.Truncate(dividend / divisor);
            remainder = dividend - (quotient * divisor);
        }

        Fpu.SetRegister(0, Fpu80.FromDouble(remainder));
        long q = (long)quotient;
        Fpu.SetConditionCodes((q & 4) != 0, false, (q & 1) != 0);
        Fpu.SetC1((q & 2) != 0);
    }

    private static double RoundToInteger(double value, int roundingMode)
    {
        return roundingMode switch
        {
            1 => Math.Floor(value),
            2 => Math.Ceiling(value),
            3 => Math.Truncate(value),
            _ => Math.Round(value, MidpointRounding.ToEven),
        };
    }

    private bool EvaluateFcmovCondition(Mnemonic mnemonic)
    {
        uint flags = Context.Eflags;
        bool cf = (flags & FlagBits.Cf) != 0;
        bool zf = (flags & FlagBits.Zf) != 0;
        bool pf = (flags & FlagBits.Pf) != 0;

        return mnemonic switch
        {
            Mnemonic.Fcmovb => cf,
            Mnemonic.Fcmove => zf,
            Mnemonic.Fcmovbe => cf || zf,
            Mnemonic.Fcmovu => pf,
            Mnemonic.Fcmovnb => !cf,
            Mnemonic.Fcmovne => !zf,
            Mnemonic.Fcmovnbe => !cf && !zf,
            _ => !pf,
        };
    }

    private Fpu80 ReadBcd(uint address)
    {
        Span<byte> bytes = stackalloc byte[10];
        ReadMemoryBytes(address, bytes);

        ulong magnitude = 0;
        ulong multiplier = 1;
        for (int i = 0; i < 9; i++)
        {
            magnitude += (ulong)(bytes[i] & 0xF) * multiplier;
            multiplier *= 10;
            magnitude += (ulong)(bytes[i] >> 4) * multiplier;
            multiplier *= 10;
        }

        Fpu80 value = Fpu80.FromInt64((long)magnitude);
        return (bytes[9] & 0x80) != 0 ? value.Negate() : value;
    }

    private void WriteBcd(uint address, Fpu80 value)
    {
        Span<byte> bytes = stackalloc byte[10];
        bytes.Clear();

        if (!value.TryToInt64(3, out long integer) || integer == long.MinValue)
        {
            Fpu.SetException(0);
        }
        else
        {
            bool negative = integer < 0;
            ulong magnitude = negative ? (ulong)(-(integer + 1)) + 1UL : (ulong)integer;
            for (int i = 0; i < 9; i++)
            {
                int low = (int)(magnitude % 10);
                magnitude /= 10;
                int high = (int)(magnitude % 10);
                magnitude /= 10;
                bytes[i] = (byte)(low | (high << 4));
            }

            bytes[9] = negative ? (byte)0x80 : (byte)0;
        }

        WriteMemoryBytes(address, bytes);
    }

    private void SaveFpuEnvironment(uint address, int operandSize)
    {
        bool wide = operandSize != 16;
        Span<byte> buffer = stackalloc byte[28];
        buffer.Clear();
        Endian.Write16(buffer, Fpu.ControlWord);
        Endian.Write16(buffer.Slice(wide ? 4 : 2, 2), Fpu.StatusWord);
        Endian.Write16(buffer.Slice(wide ? 8 : 4, 2), Fpu.TagWord);
        WriteMemoryBytes(address, buffer[..(wide ? 28 : 14)]);
    }

    private void LoadFpuEnvironment(uint address, int operandSize)
    {
        bool wide = operandSize != 16;
        Span<byte> buffer = stackalloc byte[28];
        ReadMemoryBytes(address, buffer[..(wide ? 28 : 14)]);
        Fpu.ControlWord = Endian.Read16(buffer);
        Fpu.StatusWord = Endian.Read16(buffer.Slice(wide ? 4 : 2, 2));
        Fpu.TagWord = Endian.Read16(buffer.Slice(wide ? 8 : 4, 2));
    }

    private void SaveFpuState(uint address, int operandSize)
    {
        bool wide = operandSize != 16;
        int environmentSize = wide ? 28 : 14;
        Span<byte> buffer = stackalloc byte[108];
        buffer.Clear();
        Endian.Write16(buffer, Fpu.ControlWord);
        Endian.Write16(buffer.Slice(wide ? 4 : 2, 2), Fpu.StatusWord);
        Endian.Write16(buffer.Slice(wide ? 8 : 4, 2), Fpu.TagWord);

        int offset = environmentSize;
        for (int i = 0; i < 8; i++)
        {
            Fpu80 register = Fpu.GetPhysicalRegister(i);
            Endian.Write64(buffer.Slice(offset, 8), register.Significand);
            Endian.Write16(buffer.Slice(offset + 8, 2), register.SignExponent);
            offset += 10;
        }

        WriteMemoryBytes(address, buffer[..(environmentSize + 80)]);
        Fpu.Init();
    }

    private void RestoreFpuState(uint address, int operandSize)
    {
        bool wide = operandSize != 16;
        int environmentSize = wide ? 28 : 14;
        Span<byte> buffer = stackalloc byte[108];
        ReadMemoryBytes(address, buffer[..(environmentSize + 80)]);
        Fpu.ControlWord = Endian.Read16(buffer);
        Fpu.StatusWord = Endian.Read16(buffer.Slice(wide ? 4 : 2, 2));
        Fpu.TagWord = Endian.Read16(buffer.Slice(wide ? 8 : 4, 2));

        int offset = environmentSize;
        for (int i = 0; i < 8; i++)
        {
            ulong significand = Endian.Read64(buffer.Slice(offset, 8));
            ushort signExponent = Endian.Read16(buffer.Slice(offset + 8, 2));
            Fpu.SetPhysicalRegister(i, new Fpu80(signExponent, significand));
            offset += 10;
        }
    }

    private void ExecuteMmx(in DecodedInstruction instruction)
    {
        switch (instruction.Mnemonic)
        {
            case Mnemonic.Emms:
                Fpu.EmptyAll();
                break;

            case Mnemonic.Movd:
                if (instruction.Operand1.Size == 64)
                {
                    Context.WriteMmx(instruction.Operand1.RegisterIndex, ReadGprOrMemory(instruction.Operand2));
                }
                else
                {
                    WriteGprOrMemory(instruction.Operand1, (uint)Context.ReadMmx(instruction.Operand2.RegisterIndex));
                }

                break;

            case Mnemonic.Movq:
                if (instruction.Operand1.Size == 64 && instruction.Operand1.Kind == OperandKind.Register)
                {
                    Context.WriteMmx(instruction.Operand1.RegisterIndex, ReadMmxOperand(instruction.Operand2));
                }
                else
                {
                    WriteMmxOperand(instruction.Operand1, Context.ReadMmx(instruction.Operand2.RegisterIndex));
                }

                break;

            case Mnemonic.Pshufw:
                {
                    ulong source = ReadMmxOperand(instruction.Operand2);
                    uint control = instruction.Operand3.Immediate;
                    ulong result = 0;
                    for (int i = 0; i < 4; i++)
                    {
                        int lane = (int)((control >> (2 * i)) & 3);
                        result |= (ulong)(ushort)(source >> (16 * lane)) << (16 * i);
                    }

                    Context.WriteMmx(instruction.Operand1.RegisterIndex, result);
                    break;
                }

            case Mnemonic.Pinsrw:
                {
                    ulong destination = Context.ReadMmx(instruction.Operand1.RegisterIndex);
                    int lane = (int)(instruction.Operand3.Immediate & 3);
                    ulong mask = 0xFFFFUL << (16 * lane);
                    destination = (destination & ~mask) |
                                  (((ulong)ReadGprOrMemory(instruction.Operand2) & 0xFFFF) << (16 * lane));
                    Context.WriteMmx(instruction.Operand1.RegisterIndex, destination);
                    break;
                }

            case Mnemonic.Pextrw:
                {
                    ulong source = Context.ReadMmx(instruction.Operand2.RegisterIndex);
                    int lane = (int)(instruction.Operand3.Immediate & 3);
                    Context.WriteRegister((Gpr)instruction.Operand1.RegisterIndex,
                        (uint)((source >> (16 * lane)) & 0xFFFF));
                    break;
                }

            case Mnemonic.Pmovmskb:
                {
                    ulong source = Context.ReadMmx(instruction.Operand2.RegisterIndex);
                    uint result = 0;
                    for (int i = 0; i < 8; i++)
                    {
                        if (((source >> (8 * i)) & 0x80) != 0)
                        {
                            result |= 1u << i;
                        }
                    }

                    Context.WriteRegister((Gpr)instruction.Operand1.RegisterIndex, result);
                    break;
                }

            case Mnemonic.Movntq:
                WriteMemory64(ComputeAddress(instruction.Operand1.Memory),
                    Context.ReadMmx(instruction.Operand2.RegisterIndex));
                break;

            case Mnemonic.Maskmovq:
                {
                    ulong data = Context.ReadMmx(instruction.Operand1.RegisterIndex);
                    ulong mask = Context.ReadMmx(instruction.Operand2.RegisterIndex);
                    uint address = Context.GetSegmentBase(SegmentRegister.Es) + Context.Edi;
                    for (int i = 0; i < 8; i++)
                    {
                        if (((mask >> (8 * i)) & 0x80) != 0)
                        {
                            WriteMemory(address + (uint)i, 8, (uint)(data >> (8 * i)) & 0xFFu);
                        }
                    }

                    break;
                }

            case Mnemonic.Psllw:
            case Mnemonic.Pslld:
            case Mnemonic.Psllq:
            case Mnemonic.Psrlw:
            case Mnemonic.Psrld:
            case Mnemonic.Psrlq:
            case Mnemonic.Psraw:
            case Mnemonic.Psrad:
                {
                    ulong value = Context.ReadMmx(instruction.Operand1.RegisterIndex);
                    int count = instruction.Operand2.Kind == OperandKind.Immediate
                        ? (int)instruction.Operand2.Immediate
                        : (int)(ReadMmxOperand(instruction.Operand2) & 0xFF);
                    Context.WriteMmx(instruction.Operand1.RegisterIndex, ShiftMmx(instruction.Mnemonic, value, count));
                    break;
                }

            default:
                {
                    ulong left = Context.ReadMmx(instruction.Operand1.RegisterIndex);
                    ulong right = ReadMmxOperand(instruction.Operand2);
                    Context.WriteMmx(instruction.Operand1.RegisterIndex, ComputeMmx(instruction.Mnemonic, left, right));
                    break;
                }
        }
    }

    private ulong ReadMmxOperand(in Operand operand)
    {
        return operand.Kind == OperandKind.Register
            ? Context.ReadMmx(operand.RegisterIndex)
            : ReadMemory64(ComputeAddress(operand.Memory));
    }

    private void WriteMmxOperand(in Operand operand, ulong value)
    {
        if (operand.Kind == OperandKind.Register)
        {
            Context.WriteMmx(operand.RegisterIndex, value);
        }
        else
        {
            WriteMemory64(ComputeAddress(operand.Memory), value);
        }
    }

    private static ulong ComputeMmx(Mnemonic mnemonic, ulong left, ulong right)
    {
        switch (mnemonic)
        {
            case Mnemonic.Paddb:
                return MapBytes(left, right, static (x, y) => (byte)(x + y));
            case Mnemonic.Paddw:
                return MapWords(left, right, static (x, y) => (ushort)(x + y));
            case Mnemonic.Paddd:
                return MapDwords(left, right, static (x, y) => x + y);
            case Mnemonic.Paddsb:
                return MapBytes(left, right, static (x, y) => SaturateSByte((sbyte)x + (sbyte)y));
            case Mnemonic.Paddsw:
                return MapWords(left, right, static (x, y) => SaturateInt16((short)x + (short)y));
            case Mnemonic.Paddusb:
                return MapBytes(left, right, static (x, y) => SaturateByte(x + y));
            case Mnemonic.Paddusw:
                return MapWords(left, right, static (x, y) => SaturateUInt16(x + y));
            case Mnemonic.Psubb:
                return MapBytes(left, right, static (x, y) => (byte)(x - y));
            case Mnemonic.Psubw:
                return MapWords(left, right, static (x, y) => (ushort)(x - y));
            case Mnemonic.Psubd:
                return MapDwords(left, right, static (x, y) => x - y);
            case Mnemonic.Psubsb:
                return MapBytes(left, right, static (x, y) => SaturateSByte((sbyte)x - (sbyte)y));
            case Mnemonic.Psubsw:
                return MapWords(left, right, static (x, y) => SaturateInt16((short)x - (short)y));
            case Mnemonic.Psubusb:
                return MapBytes(left, right, static (x, y) => SaturateByte(x - y));
            case Mnemonic.Psubusw:
                return MapWords(left, right, static (x, y) => SaturateUInt16(x - y));
            case Mnemonic.Pmullw:
                return MapWords(left, right, static (x, y) => (ushort)(x * y));
            case Mnemonic.Pmulhw:
                return MapWords(left, right, static (x, y) => (ushort)(((short)x * (short)y) >> 16));
            case Mnemonic.Pmulhuw:
                return MapWords(left, right, static (x, y) => (ushort)(((uint)x * y) >> 16));
            case Mnemonic.Pmaddwd:
                {
                    ulong result = 0;
                    for (int i = 0; i < 4; i++)
                    {
                        int p0 = (short)(left >> (16 * (2 * i))) * (short)(right >> (16 * (2 * i)));
                        int p1 = (short)(left >> (16 * ((2 * i) + 1))) * (short)(right >> (16 * ((2 * i) + 1)));
                        result |= (ulong)(uint)(p0 + p1) << (32 * i);
                    }

                    return result;
                }

            case Mnemonic.Pcmpeqb:
                return MapBytes(left, right, static (x, y) => x == y ? (byte)0xFF : (byte)0);
            case Mnemonic.Pcmpeqw:
                return MapWords(left, right, static (x, y) => x == y ? (ushort)0xFFFF : (ushort)0);
            case Mnemonic.Pcmpeqd:
                return MapDwords(left, right, static (x, y) => x == y ? 0xFFFFFFFFu : 0u);
            case Mnemonic.Pcmpgtb:
                return MapBytes(left, right, static (x, y) => (sbyte)x > (sbyte)y ? (byte)0xFF : (byte)0);
            case Mnemonic.Pcmpgtw:
                return MapWords(left, right, static (x, y) => (short)x > (short)y ? (ushort)0xFFFF : (ushort)0);
            case Mnemonic.Pcmpgtd:
                return MapDwords(left, right, static (x, y) => (int)x > (int)y ? 0xFFFFFFFFu : 0u);
            case Mnemonic.Pand:
                return left & right;
            case Mnemonic.Pandn:
                return ~left & right;
            case Mnemonic.Por:
                return left | right;
            case Mnemonic.Pxor:
                return left ^ right;
            case Mnemonic.Pavgb:
                return MapBytes(left, right, static (x, y) => (byte)((x + y + 1) >> 1));
            case Mnemonic.Pavgw:
                return MapWords(left, right, static (x, y) => (ushort)((x + y + 1) >> 1));
            case Mnemonic.Pminub:
                return MapBytes(left, right, static (x, y) => Math.Min(x, y));
            case Mnemonic.Pmaxub:
                return MapBytes(left, right, static (x, y) => Math.Max(x, y));
            case Mnemonic.Pminuw:
                return MapWords(left, right, static (x, y) => Math.Min(x, y));
            case Mnemonic.Pmaxuw:
                return MapWords(left, right, static (x, y) => Math.Max(x, y));
            case Mnemonic.Psadbw:
                {
                    int sum = 0;
                    for (int i = 0; i < 8; i++)
                    {
                        sum += Math.Abs((int)((left >> (8 * i)) & 0xFF) - (int)((right >> (8 * i)) & 0xFF));
                    }

                    return (ulong)(ushort)sum;
                }

            case Mnemonic.Packsswb:
                return PackWordsToBytes(left, right, static v => SaturateSByte(v));
            case Mnemonic.Packuswb:
                return PackWordsToBytes(left, right, static v => SaturateByte(v));
            case Mnemonic.Packssdw:
                return PackDwordsToWords(left, right);
            case Mnemonic.Punpcklbw:
                return InterleaveBytes(left, right, 0);
            case Mnemonic.Punpckhbw:
                return InterleaveBytes(left, right, 4);
            case Mnemonic.Punpcklwd:
                return InterleaveWords(left, right, 0);
            case Mnemonic.Punpckhwd:
                return InterleaveWords(left, right, 2);
            case Mnemonic.Punpckldq:
                return (left & 0xFFFFFFFFUL) | ((right & 0xFFFFFFFFUL) << 32);
            case Mnemonic.Punpckhdq:
                return ((left >> 32) & 0xFFFFFFFFUL) | ((right >> 32) << 32);
            default:
                throw new InvalidOperationException($"Unsupported MMX operation {mnemonic}.");
        }
    }

    private static ulong ShiftMmx(Mnemonic mnemonic, ulong value, int count)
    {
        switch (mnemonic)
        {
            case Mnemonic.Psllw:
                if (count > 15)
                {
                    return 0;
                }

                return MapWords(value, 0, (x, _) => (ushort)(x << count));

            case Mnemonic.Psrlw:
                if (count > 15)
                {
                    return 0;
                }

                return MapWords(value, 0, (x, _) => (ushort)(x >> count));

            case Mnemonic.Psraw:
                if (count > 15)
                {
                    return (short)(value & 0xFFFF) < 0 ? 0xFFFFFFFFFFFFFFFFUL : 0;
                }

                return MapWords(value, 0, (x, _) => (ushort)((short)x >> count));

            case Mnemonic.Pslld:
                if (count > 31)
                {
                    return 0;
                }

                return MapDwords(value, 0, (x, _) => x << count);

            case Mnemonic.Psrld:
                if (count > 31)
                {
                    return 0;
                }

                return MapDwords(value, 0, (x, _) => x >> count);

            case Mnemonic.Psrad:
                if (count > 31)
                {
                    return (int)value < 0 ? 0xFFFFFFFFFFFFFFFFUL : 0;
                }

                return MapDwords(value, 0, (x, _) => (uint)((int)x >> count));

            case Mnemonic.Psllq:
                return count > 63 ? 0 : value << count;

            default:
                return count > 63 ? 0 : value >> count;
        }
    }

    private static ulong MapBytes(ulong left, ulong right, Func<byte, byte, byte> operation)
    {
        ulong result = 0;
        for (int i = 0; i < 8; i++)
        {
            result |= (ulong)operation((byte)(left >> (8 * i)), (byte)(right >> (8 * i))) << (8 * i);
        }

        return result;
    }

    private static ulong MapWords(ulong left, ulong right, Func<ushort, ushort, ushort> operation)
    {
        ulong result = 0;
        for (int i = 0; i < 4; i++)
        {
            result |= (ulong)operation((ushort)(left >> (16 * i)), (ushort)(right >> (16 * i))) << (16 * i);
        }

        return result;
    }

    private static ulong MapDwords(ulong left, ulong right, Func<uint, uint, uint> operation)
    {
        ulong result = 0;
        for (int i = 0; i < 2; i++)
        {
            result |= (ulong)operation((uint)(left >> (32 * i)), (uint)(right >> (32 * i))) << (32 * i);
        }

        return result;
    }

    private static byte SaturateSByte(int value)
    {
        return value < sbyte.MinValue ? (byte)0x80 : value > sbyte.MaxValue ? (byte)0x7F : (byte)value;
    }

    private static byte SaturateByte(int value)
    {
        return value < 0 ? (byte)0 : value > 0xFF ? (byte)0xFF : (byte)value;
    }

    private static ushort SaturateInt16(int value)
    {
        return value < short.MinValue ? (ushort)0x8000 : value > short.MaxValue ? (ushort)0x7FFF : (ushort)value;
    }

    private static ushort SaturateUInt16(int value)
    {
        return value < 0 ? (ushort)0 : value > 0xFFFF ? (ushort)0xFFFF : (ushort)value;
    }

    private static ulong PackWordsToBytes(ulong left, ulong right, Func<short, byte> saturate)
    {
        ulong result = 0;
        for (int i = 0; i < 4; i++)
        {
            result |= (ulong)saturate((short)(left >> (16 * i))) << (8 * i);
            result |= (ulong)saturate((short)(right >> (16 * i))) << (8 * (i + 4));
        }

        return result;
    }

    private static ulong PackDwordsToWords(ulong left, ulong right)
    {
        ulong result = 0;
        for (int i = 0; i < 2; i++)
        {
            result |= (ulong)SaturateInt16((int)(uint)(left >> (32 * i))) << (16 * i);
            result |= (ulong)SaturateInt16((int)(uint)(right >> (32 * i))) << (16 * (i + 2));
        }

        return result;
    }

    private static ulong InterleaveBytes(ulong left, ulong right, int start)
    {
        ulong result = 0;
        for (int i = 0; i < 4; i++)
        {
            result |= (ulong)(byte)(left >> (8 * (start + i))) << (16 * i);
            result |= (ulong)(byte)(right >> (8 * (start + i))) << ((16 * i) + 8);
        }

        return result;
    }

    private static ulong InterleaveWords(ulong left, ulong right, int start)
    {
        ulong result = 0;
        for (int i = 0; i < 2; i++)
        {
            result |= (ulong)(ushort)(left >> (16 * (start + i))) << (32 * i);
            result |= (ulong)(ushort)(right >> (16 * (start + i))) << ((32 * i) + 16);
        }

        return result;
    }

    private void ExecuteSse(in DecodedInstruction instruction)
    {
        switch (instruction.Mnemonic)
        {
            case Mnemonic.Prefetch:
            case Mnemonic.Sfence:
            case Mnemonic.Lfence:
            case Mnemonic.Mfence:
                break;

            case Mnemonic.Movaps:
            case Mnemonic.Movups:
                {
                    (ulong low, ulong high) = ReadXmmOperand(instruction.Operand2);
                    WriteXmmOperand(instruction.Operand1, low, high);
                    break;
                }

            case Mnemonic.Movss:
                {
                    if (instruction.Operand1.Kind == OperandKind.Register)
                    {
                        (ulong low, ulong high) = ReadXmmOperand(instruction.Operand2);
                        Context.WriteXmmElement(instruction.Operand1.RegisterIndex, 0, ElementBits((low, high), 0));
                    }
                    else
                    {
                        uint source = Context.ReadXmmElement(instruction.Operand2.RegisterIndex, 0);
                        WriteMemory(ComputeAddress(instruction.Operand1.Memory), 32, source);
                    }

                    break;
                }

            case Mnemonic.Movlps:
                {
                    if (instruction.Operand1.Kind == OperandKind.Memory)
                    {
                        WriteMemory64(ComputeAddress(instruction.Operand1.Memory),
                            Context.ReadXmm64(instruction.Operand2.RegisterIndex, 0));
                    }
                    else if (instruction.Operand2.Kind == OperandKind.Memory)
                    {
                        ulong value = ReadMemory64(ComputeAddress(instruction.Operand2.Memory));
                        Context.WriteXmm64(instruction.Operand1.RegisterIndex, 0, value);
                    }

                    break;
                }

            case Mnemonic.Movhps:
                {
                    if (instruction.Operand1.Kind == OperandKind.Memory)
                    {
                        WriteMemory64(ComputeAddress(instruction.Operand1.Memory),
                            Context.ReadXmm64(instruction.Operand2.RegisterIndex, 1));
                    }
                    else if (instruction.Operand2.Kind == OperandKind.Memory)
                    {
                        ulong value = ReadMemory64(ComputeAddress(instruction.Operand2.Memory));
                        Context.WriteXmm64(instruction.Operand1.RegisterIndex, 1, value);
                    }

                    break;
                }

            case Mnemonic.Movhlps:
                {
                    ulong source = Context.ReadXmm64(instruction.Operand2.RegisterIndex, 1);
                    Context.WriteXmm64(instruction.Operand1.RegisterIndex, 0, source);
                    break;
                }

            case Mnemonic.Movlhps:
                {
                    ulong source = Context.ReadXmm64(instruction.Operand2.RegisterIndex, 0);
                    Context.WriteXmm64(instruction.Operand1.RegisterIndex, 1, source);
                    break;
                }

            case Mnemonic.Movmskps:
                {
                    uint result = 0;
                    for (int i = 0; i < 4; i++)
                    {
                        if ((Context.ReadXmmElement(instruction.Operand2.RegisterIndex, i) & 0x80000000u) != 0)
                        {
                            result |= 1u << i;
                        }
                    }

                    Context.WriteRegister((Gpr)instruction.Operand1.RegisterIndex, result);
                    break;
                }

            case Mnemonic.Addps:
            case Mnemonic.Addss:
            case Mnemonic.Subps:
            case Mnemonic.Subss:
            case Mnemonic.Mulps:
            case Mnemonic.Mulss:
            case Mnemonic.Divps:
            case Mnemonic.Divss:
            case Mnemonic.Minps:
            case Mnemonic.Minss:
            case Mnemonic.Maxps:
            case Mnemonic.Maxss:
                ExecuteSseArithmetic(instruction);
                break;

            case Mnemonic.Sqrtps:
            case Mnemonic.Sqrtss:
            case Mnemonic.Rsqrtps:
            case Mnemonic.Rsqrtss:
            case Mnemonic.Rcpps:
            case Mnemonic.Rcpss:
                ExecuteSseUnary(instruction);
                break;

            case Mnemonic.Andps:
            case Mnemonic.Andnps:
            case Mnemonic.Orps:
            case Mnemonic.Xorps:
                ExecuteSseLogical(instruction);
                break;

            case Mnemonic.Cmpps:
            case Mnemonic.Cmpss:
                ExecuteSseCompare(instruction);
                break;

            case Mnemonic.Comiss:
            case Mnemonic.Ucomiss:
                ExecuteSseCompareScalar(instruction);
                break;

            case Mnemonic.Shufps:
                ExecuteShufps(instruction);
                break;

            case Mnemonic.Unpcklps:
            case Mnemonic.Unpckhps:
                {
                    (ulong dlow, ulong dhigh) = Context.ReadXmm128(instruction.Operand1.RegisterIndex);
                    (ulong slow, ulong shigh) = ReadXmmOperand(instruction.Operand2);
                    ulong rlow;
                    ulong rhigh;
                    if (instruction.Mnemonic == Mnemonic.Unpcklps)
                    {
                        rlow = (dlow & 0xFFFFFFFFUL) | ((slow & 0xFFFFFFFFUL) << 32);
                        rhigh = ((dlow >> 32) & 0xFFFFFFFFUL) | ((slow >> 32) << 32);
                    }
                    else
                    {
                        rlow = (dhigh & 0xFFFFFFFFUL) | ((shigh & 0xFFFFFFFFUL) << 32);
                        rhigh = ((dhigh >> 32) & 0xFFFFFFFFUL) | ((shigh >> 32) << 32);
                    }

                    Context.WriteXmm128(instruction.Operand1.RegisterIndex, rlow, rhigh);
                    break;
                }

            case Mnemonic.Cvtsi2Ss:
                {
                    int value = (int)ReadGprOrMemory(instruction.Operand2);
                    Context.WriteXmmElement(instruction.Operand1.RegisterIndex, 0, BitConverter.SingleToUInt32Bits(value));
                    break;
                }

            case Mnemonic.Cvtss2Si:
            case Mnemonic.Cvttss2Si:
                {
                    (ulong low, ulong high) = ReadXmmOperand(instruction.Operand2);
                    float value = BitConverter.UInt32BitsToSingle(ElementBits((low, high), 0));
                    int result = ConvertFloatToInt32(value, instruction.Mnemonic == Mnemonic.Cvttss2Si);
                    Context.WriteRegister((Gpr)instruction.Operand1.RegisterIndex, (uint)result);
                    break;
                }

            case Mnemonic.Cvtps2Pi:
            case Mnemonic.Cvttps2Pi:
                {
                    (ulong low, ulong high) = ReadXmmOperand(instruction.Operand2);
                    bool truncate = instruction.Mnemonic == Mnemonic.Cvttps2Pi;
                    ulong result = 0;
                    for (int i = 0; i < 2; i++)
                    {
                        float value = BitConverter.UInt32BitsToSingle(ElementBits((low, high), i));
                        result |= (ulong)(uint)ConvertFloatToInt32(value, truncate) << (32 * i);
                    }

                    Context.WriteMmx(instruction.Operand1.RegisterIndex, result);
                    break;
                }

            case Mnemonic.Cvtpi2Ps:
                {
                    ulong source = ReadMmxOperand(instruction.Operand2);
                    ulong result = 0;
                    for (int i = 0; i < 2; i++)
                    {
                        int value = (int)(uint)(source >> (32 * i));
                        result |= (ulong)BitConverter.SingleToUInt32Bits(value) << (32 * i);
                    }

                    Context.WriteXmm64(instruction.Operand1.RegisterIndex, 0, result);
                    break;
                }

            case Mnemonic.Ldmxcsr:
                Context.Mxcsr = ReadMemory(ComputeAddress(instruction.Operand1.Memory), 32);
                break;

            case Mnemonic.Stmxcsr:
                WriteMemory(ComputeAddress(instruction.Operand1.Memory), 32, Context.Mxcsr);
                break;

            case Mnemonic.Fxsave:
                ExecuteFxsave(ComputeAddress(instruction.Operand1.Memory));
                break;

            case Mnemonic.Fxrstor:
                ExecuteFxrstor(ComputeAddress(instruction.Operand1.Memory));
                break;

            case Mnemonic.Movntps:
                {
                    (ulong low, ulong high) = Context.ReadXmm128(instruction.Operand2.RegisterIndex);
                    WriteXmmOperand(instruction.Operand1, low, high);
                    break;
                }

            default:
                throw new UnsupportedInstructionException(Context.Eip, instruction.Opcode);
        }
    }

    private void ExecuteSseArithmetic(in DecodedInstruction instruction)
    {
        bool scalar = instruction.Mnemonic is Mnemonic.Addss or Mnemonic.Subss or Mnemonic.Mulss
            or Mnemonic.Divss or Mnemonic.Minss or Mnemonic.Maxss;
        (ulong slow, ulong shigh) = ReadXmmOperand(instruction.Operand2);

        if (scalar)
        {
            float a = BitConverter.UInt32BitsToSingle(Context.ReadXmmElement(instruction.Operand1.RegisterIndex, 0));
            float b = BitConverter.UInt32BitsToSingle(ElementBits((slow, shigh), 0));
            Context.WriteXmmElement(instruction.Operand1.RegisterIndex, 0,
                BitConverter.SingleToUInt32Bits(ApplySseArithmetic(instruction.Mnemonic, a, b)));
            return;
        }

        ulong rlow = 0;
        ulong rhigh = 0;
        for (int i = 0; i < 4; i++)
        {
            float a = BitConverter.UInt32BitsToSingle(Context.ReadXmmElement(instruction.Operand1.RegisterIndex, i));
            float b = BitConverter.UInt32BitsToSingle(ElementBits((slow, shigh), i));
            uint bits = BitConverter.SingleToUInt32Bits(ApplySseArithmetic(instruction.Mnemonic, a, b));
            if (i < 2)
            {
                rlow |= (ulong)bits << (32 * i);
            }
            else
            {
                rhigh |= (ulong)bits << (32 * (i - 2));
            }
        }

        Context.WriteXmm128(instruction.Operand1.RegisterIndex, rlow, rhigh);
    }

    private static float ApplySseArithmetic(Mnemonic mnemonic, float left, float right)
    {
        switch (mnemonic)
        {
            case Mnemonic.Addps:
            case Mnemonic.Addss:
                return left + right;
            case Mnemonic.Subps:
            case Mnemonic.Subss:
                return left - right;
            case Mnemonic.Mulps:
            case Mnemonic.Mulss:
                return left * right;
            case Mnemonic.Divps:
            case Mnemonic.Divss:
                return left / right;
            case Mnemonic.Minps:
            case Mnemonic.Minss:
                return float.IsNaN(left) || float.IsNaN(right) ? right : MathF.Min(left, right);
            default:
                return float.IsNaN(left) || float.IsNaN(right) ? right : MathF.Max(left, right);
        }
    }

    private void ExecuteSseUnary(in DecodedInstruction instruction)
    {
        bool scalar = instruction.Mnemonic is Mnemonic.Sqrtss or Mnemonic.Rsqrtss or Mnemonic.Rcpss;
        (ulong slow, ulong shigh) = ReadXmmOperand(instruction.Operand2);

        if (scalar)
        {
            float a = BitConverter.UInt32BitsToSingle(ElementBits((slow, shigh), 0));
            Context.WriteXmmElement(instruction.Operand1.RegisterIndex, 0,
                BitConverter.SingleToUInt32Bits(ApplySseUnary(instruction.Mnemonic, a)));
            return;
        }

        ulong rlow = 0;
        ulong rhigh = 0;
        for (int i = 0; i < 4; i++)
        {
            float a = BitConverter.UInt32BitsToSingle(ElementBits((slow, shigh), i));
            uint bits = BitConverter.SingleToUInt32Bits(ApplySseUnary(instruction.Mnemonic, a));
            if (i < 2)
            {
                rlow |= (ulong)bits << (32 * i);
            }
            else
            {
                rhigh |= (ulong)bits << (32 * (i - 2));
            }
        }

        Context.WriteXmm128(instruction.Operand1.RegisterIndex, rlow, rhigh);
    }

    private static float ApplySseUnary(Mnemonic mnemonic, float value)
    {
        return mnemonic switch
        {
            Mnemonic.Sqrtps or Mnemonic.Sqrtss => MathF.Sqrt(value),
            Mnemonic.Rsqrtps or Mnemonic.Rsqrtss => 1f / MathF.Sqrt(value),
            _ => 1f / value,
        };
    }

    private void ExecuteSseLogical(in DecodedInstruction instruction)
    {
        (ulong alow, ulong ahigh) = Context.ReadXmm128(instruction.Operand1.RegisterIndex);
        (ulong blow, ulong bhigh) = ReadXmmOperand(instruction.Operand2);

        (ulong low, ulong high) = instruction.Mnemonic switch
        {
            Mnemonic.Andps => (alow & blow, ahigh & bhigh),
            Mnemonic.Andnps => (~alow & blow, ~ahigh & bhigh),
            Mnemonic.Orps => (alow | blow, ahigh | bhigh),
            _ => (alow ^ blow, ahigh ^ bhigh),
        };

        Context.WriteXmm128(instruction.Operand1.RegisterIndex, low, high);
    }

    private void ExecuteSseCompare(in DecodedInstruction instruction)
    {
        bool scalar = instruction.Mnemonic == Mnemonic.Cmpss;
        int predicate = (int)(instruction.Operand3.Immediate & 7);
        (ulong slow, ulong shigh) = ReadXmmOperand(instruction.Operand2);

        if (scalar)
        {
            float a = BitConverter.UInt32BitsToSingle(Context.ReadXmmElement(instruction.Operand1.RegisterIndex, 0));
            float b = BitConverter.UInt32BitsToSingle(ElementBits((slow, shigh), 0));
            uint mask = EvaluateSseCompare(predicate, a, b) ? 0xFFFFFFFFu : 0u;
            Context.WriteXmmElement(instruction.Operand1.RegisterIndex, 0, mask);
            return;
        }

        ulong rlow = 0;
        ulong rhigh = 0;
        for (int i = 0; i < 4; i++)
        {
            float a = BitConverter.UInt32BitsToSingle(Context.ReadXmmElement(instruction.Operand1.RegisterIndex, i));
            float b = BitConverter.UInt32BitsToSingle(ElementBits((slow, shigh), i));
            uint mask = EvaluateSseCompare(predicate, a, b) ? 0xFFFFFFFFu : 0u;
            if (i < 2)
            {
                rlow |= (ulong)mask << (32 * i);
            }
            else
            {
                rhigh |= (ulong)mask << (32 * (i - 2));
            }
        }

        Context.WriteXmm128(instruction.Operand1.RegisterIndex, rlow, rhigh);
    }

    private static bool EvaluateSseCompare(int predicate, float left, float right)
    {
        bool unordered = float.IsNaN(left) || float.IsNaN(right);
        return predicate switch
        {
            0 => left == right,
            1 => left < right,
            2 => left <= right,
            3 => unordered,
            4 => left != right,
            5 => !(left < right),
            6 => !(left <= right),
            _ => !unordered,
        };
    }

    private void ExecuteSseCompareScalar(in DecodedInstruction instruction)
    {
        float left = BitConverter.UInt32BitsToSingle(Context.ReadXmmElement(instruction.Operand1.RegisterIndex, 0));
        (ulong low, ulong high) = ReadXmmOperand(instruction.Operand2);
        float right = BitConverter.UInt32BitsToSingle(ElementBits((low, high), 0));

        uint flags = Context.Eflags &
                     ~(FlagBits.Zf | FlagBits.Pf | FlagBits.Cf | FlagBits.Of | FlagBits.Sf | FlagBits.Af);
        if (float.IsNaN(left) || float.IsNaN(right))
        {
            flags |= FlagBits.Zf | FlagBits.Pf | FlagBits.Cf;
        }
        else if (left < right)
        {
            flags |= FlagBits.Cf;
        }
        else if (left == right)
        {
            flags |= FlagBits.Zf;
        }

        Context.Eflags = flags;
    }

    private void ExecuteShufps(in DecodedInstruction instruction)
    {
        (ulong dlow, ulong dhigh) = Context.ReadXmm128(instruction.Operand1.RegisterIndex);
        (ulong slow, ulong shigh) = ReadXmmOperand(instruction.Operand2);
        uint control = instruction.Operand3.Immediate;

        uint[] destination =
        {
            ElementBits((dlow, dhigh), 0),
            ElementBits((dlow, dhigh), 1),
            ElementBits((dlow, dhigh), 2),
            ElementBits((dlow, dhigh), 3),
        };
        uint[] source =
        {
            ElementBits((slow, shigh), 0),
            ElementBits((slow, shigh), 1),
            ElementBits((slow, shigh), 2),
            ElementBits((slow, shigh), 3),
        };

        ulong rlow = destination[control & 3] | ((ulong)destination[(control >> 2) & 3] << 32);
        ulong rhigh = source[(control >> 4) & 3] | ((ulong)source[(control >> 6) & 3] << 32);
        Context.WriteXmm128(instruction.Operand1.RegisterIndex, rlow, rhigh);
    }

    private (ulong Low, ulong High) ReadXmmOperand(in Operand operand)
    {
        if (operand.Kind == OperandKind.Register)
        {
            return Context.ReadXmm128(operand.RegisterIndex);
        }

        uint address = ComputeAddress(operand.Memory);
        // Slice 1: single-translate 128-bit RAM read (legacy: two
        // ReadMemory64 = two translates + four bus dispatches). Same
        // one-page + observer gating as the FPU80 fusion.
        PhysicalMemory? ram = _ramDirect;
        if (FastWideAllowed && ram is not null
            && (address & 0xFFFu) + 16 <= 0x1000u && (ulong)address + 16 <= _ramSize)
        {
            uint physical = TranslateLinear(address, isWrite: false, isInstruction: false);
            if ((ulong)physical + 16 <= _ramSize)
            {
                return (ram.Read64(physical), ram.Read64(physical + 8));
            }
        }

        return (ReadMemory64(address), ReadMemory64(address + 8));
    }

    private void WriteXmmOperand(in Operand operand, ulong low, ulong high)
    {
        if (operand.Kind == OperandKind.Register)
        {
            Context.WriteXmm128(operand.RegisterIndex, low, high);
            return;
        }

        uint address = ComputeAddress(operand.Memory);
        PhysicalMemory? ram = _ramDirect;
        if (FastWideAllowed && ram is not null
            && (address & 0xFFFu) + 16 <= 0x1000u && (ulong)address + 16 <= _ramSize)
        {
            uint physical = TranslateLinear(address, isWrite: true, isInstruction: false);
            if ((ulong)physical + 16 <= _ramSize && !IsWatched(physical, 16))
            {
                NoteStorePhysical(physical, 128);
                ram.Write64(physical, low);
                ram.Write64(physical + 8, high);
                return;
            }
        }

        WriteMemory64(address, low);
        WriteMemory64(address + 8, high);
    }

    private static uint ElementBits((ulong Low, ulong High) value, int index)
    {
        return index < 2
            ? (uint)(value.Low >> (32 * index))
            : (uint)(value.High >> (32 * (index - 2)));
    }

    private int ConvertFloatToInt32(float value, bool truncate)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
        {
            return unchecked((int)0x80000000u);
        }

        float rounded = truncate ? MathF.Truncate(value) : RoundFloat(value);
        if (rounded is < -2147483648f or >= 2147483648f)
        {
            return unchecked((int)0x80000000u);
        }

        return (int)rounded;
    }

    private float RoundFloat(float value)
    {
        return ((Context.Mxcsr >> 13) & 3) switch
        {
            1 => MathF.Floor(value),
            2 => MathF.Ceiling(value),
            3 => MathF.Truncate(value),
            _ => MathF.Round(value, MidpointRounding.ToEven),
        };
    }

    private void ExecuteFxsave(uint address)
    {
        Span<byte> buffer = stackalloc byte[512];
        buffer.Clear();
        Endian.Write16(buffer, Fpu.ControlWord);
        Endian.Write16(buffer.Slice(2, 2), Fpu.StatusWord);
        buffer[4] = AbridgedTagWord();
        Endian.Write32(buffer.Slice(24, 4), Context.Mxcsr);
        Endian.Write32(buffer.Slice(28, 4), 0x0000FFBF);

        for (int i = 0; i < 8; i++)
        {
            Fpu80 register = Fpu.GetPhysicalRegister(i);
            Endian.Write64(buffer.Slice(32 + (i * 16), 8), register.Significand);
            Endian.Write16(buffer.Slice(32 + (i * 16) + 8, 2), register.SignExponent);
        }

        for (int i = 0; i < 8; i++)
        {
            Endian.Write64(buffer.Slice(160 + (i * 16), 8), Context.ReadXmm64(i, 0));
            Endian.Write64(buffer.Slice(160 + (i * 16) + 8, 8), Context.ReadXmm64(i, 1));
        }

        WriteMemoryBytes(address, buffer);
    }

    private void ExecuteFxrstor(uint address)
    {
        Span<byte> buffer = stackalloc byte[512];
        ReadMemoryBytes(address, buffer);
        Fpu.ControlWord = Endian.Read16(buffer);
        Fpu.StatusWord = Endian.Read16(buffer.Slice(2, 2));

        ushort tag = 0;
        byte abridged = buffer[4];
        for (int i = 0; i < 8; i++)
        {
            int value = ((abridged >> i) & 1) != 0 ? FpuState.TagEmpty : FpuState.TagValid;
            tag |= (ushort)(value << (i * 2));
        }

        Fpu.TagWord = tag;
        Context.Mxcsr = Endian.Read32(buffer.Slice(24, 4));

        for (int i = 0; i < 8; i++)
        {
            ulong significand = Endian.Read64(buffer.Slice(32 + (i * 16), 8));
            ushort signExponent = Endian.Read16(buffer.Slice(32 + (i * 16) + 8, 2));
            Fpu.SetPhysicalRegister(i, new Fpu80(signExponent, significand));
        }

        for (int i = 0; i < 8; i++)
        {
            Context.WriteXmm64(i, 0, Endian.Read64(buffer.Slice(160 + (i * 16), 8)));
            Context.WriteXmm64(i, 1, Endian.Read64(buffer.Slice(160 + (i * 16) + 8, 8)));
        }
    }

    private byte AbridgedTagWord()
    {
        byte result = 0;
        for (int i = 0; i < 8; i++)
        {
            int tag = (Fpu.TagWord >> (i * 2)) & 3;
            if (tag is FpuState.TagSpecial or FpuState.TagEmpty)
            {
                result |= (byte)(1 << i);
            }
        }

        return result;
    }

    private bool EvaluateCondition(ConditionCode condition)
    {
        uint flags = Context.Eflags;
        bool cf = (flags & FlagBits.Cf) != 0;
        bool zf = (flags & FlagBits.Zf) != 0;
        bool sf = (flags & FlagBits.Sf) != 0;
        bool of = (flags & FlagBits.Of) != 0;
        bool pf = (flags & FlagBits.Pf) != 0;

        return condition switch
        {
            ConditionCode.Overflow => of,
            ConditionCode.NotOverflow => !of,
            ConditionCode.Below => cf,
            ConditionCode.AboveOrEqual => !cf,
            ConditionCode.Equal => zf,
            ConditionCode.NotEqual => !zf,
            ConditionCode.BelowOrEqual => cf || zf,
            ConditionCode.Above => !cf && !zf,
            ConditionCode.Sign => sf,
            ConditionCode.NotSign => !sf,
            ConditionCode.Parity => pf,
            ConditionCode.NotParity => !pf,
            ConditionCode.Less => sf != of,
            ConditionCode.GreaterOrEqual => sf == of,
            ConditionCode.LessOrEqual => zf || sf != of,
            _ => !zf && sf == of,
        };
    }
}
