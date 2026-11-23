using System;
using System.IO;
using System.Runtime.InteropServices;
using Iced.Intel;

namespace XGrid.Cpu;

/// <summary>
/// What one native emission produced (D8 census): the host instruction count of
/// each region of the emitted function and the two formation timings. The
/// regions are straight-line, so their instruction counts times the
/// entry/edge counts are the engine's exact host-instruction census.
/// </summary>
[StructLayout(LayoutKind.Auto)]
internal readonly struct RawBuildStatics
{
    /// <summary>Gets the emitted instructions of the external entry prologue.</summary>
    public int PrologueInstructions { get; init; }

    /// <summary>Gets the emitted instructions of the in-frame chain entry (0 when not chainable).</summary>
    public int ChainEntryInstructions { get; init; }

    /// <summary>Gets the emitted instructions of the member bodies.</summary>
    public int MemberInstructions { get; init; }

    /// <summary>Gets the emitted instructions of the exit edge (publish + chain attempt + return).</summary>
    public int ExitInstructions { get; init; }

    /// <summary>Gets the ticks spent inside Iced's <c>Assembler.Assemble</c> (0 unless attributed).</summary>
    public long AssembleTicks { get; init; }

    /// <summary>Gets the ticks spent in the emitter's own instruction building (0 unless attributed).</summary>
    public long EmitTicks { get; init; }
}

/// <summary>
/// C3 raw emitter: one native x86-64 function per block (Windows x64 only).
///
/// <para>Guest state lives in fixed host registers across the whole block -
/// EAX/ECX/EDX/EBX for guest eax/ecx/edx/ebx (kept in the legacy registers so
/// AH/CH/DH/BH stay addressable) and R12D/R13D/R14D/R15D for esp/ebp/esi/edi -
/// so an ordinary member costs its own instruction plus one EIP store and one
/// served increment. The guest arithmetic flags live in the host FLAGS register
/// and are materialized into a frame slot only when something else needs the
/// host flags (memory probes, helper calls) or at an exit; the non-arithmetic
/// bits (IF/DF/IOPL/...) ride in a separate frame slot updated by
/// CLI/STI/CLD/STD.</para>
///
/// <para>Memory members inline the same translate-cache probe the IL emitter
/// uses, against pinned pointers: tag compare, physical page, RAM bound, then a
/// direct load/store through the pinned RAM base. Any miss, watched range or
/// non-RAM physical exits to the exact helper (state synced first, so fault
/// precision is unchanged). Members whose shape the raw path cannot encode
/// (x87/SSE, string ops, calls, indirect jumps, awkward 8-bit forms) run
/// through the same spec-handler/interpreter helper the IL emitter uses, so
/// every block is emittable.</para>
///
/// <para>Blocks are formed and invalidated by the existing block cache; the raw
/// runner simply replaces the IL runner when it builds. No per-member budget
/// check is emitted: <c>RunJitBlock</c> only calls a raw block when the
/// remaining budget covers every member (otherwise it yields), which is what
/// keeps the host flags live between members.</para>
/// </summary>
internal static unsafe class RawBlockEmitter
{

    // Frame layout. The first 32 bytes are the Win64 shadow space every call
    // site must reserve for its callee - the helper trampolines write their
    // register arguments there, so no live slot may live below 0x20.
    private const int ShadowSpace = 0x20;
    private const int F_CPU = ShadowSpace + 0x00;
    private const int F_ENTRIES = ShadowSpace + 0x08;
    private const int F_STATE = ShadowSpace + 0x10;
    private const int F_BUDGET = ShadowSpace + 0x18;
    private const int F_SERVED = ShadowSpace + 0x1C;
    private const int F_CTRL = ShadowSpace + 0x20;
    private const int F_EFLAGS = ShadowSpace + 0x24;
    private const int F_EIP = ShadowSpace + 0x28;
    private const int F_TSC = ShadowSpace + 0x30;
    private const int F_TSC_NOW = ShadowSpace + 0x58;
    private const int F_VALUE = ShadowSpace + 0x38;
    private const int F_PHYS = ShadowSpace + 0x40;
    private const int F_VALUE64 = ShadowSpace + 0x48;
    private const int F_FPU = ShadowSpace + 0x50;

    /// <summary>
    /// Park slot for the inline store guard (guest-write stamp and the note
    /// path): it must not share F_VALUE/F_VALUE64, which push [mem] and
    /// movq [mem], mm hold the stored value in across the probe.
    /// </summary>
    private const int F_STORE_SCRATCH = ShadowSpace + 0x60;

    // 8 callee-saved pushes (64) + this size keeps rsp 16-byte aligned at a call.
    private const int FrameSize = ShadowSpace + 0x108;

    /// <summary>Arithmetic flags the host FLAGS register carries for the guest (plus reserved bit 1).</summary>
    private const uint ArithmeticMask = 0x8D5;

    private const uint ControlMask = ~ArithmeticMask;

    private static readonly int OffRegisters = (int)Marshal.OffsetOf<RawState>(nameof(RawState.Registers));
    private static readonly int OffXlatTag = (int)Marshal.OffsetOf<RawState>(nameof(RawState.XlatTag));
    private static readonly int OffXlatPhys = (int)Marshal.OffsetOf<RawState>(nameof(RawState.XlatPhys));
    private static readonly int OffXlatWritable = (int)Marshal.OffsetOf<RawState>(nameof(RawState.XlatWritable));
    private static readonly int OffActiveBits = (int)Marshal.OffsetOf<RawState>(nameof(RawState.ActiveBits));
    private static readonly int OffPtBits = (int)Marshal.OffsetOf<RawState>(nameof(RawState.PtBits));
    private static readonly int OffWatchArmed = (int)Marshal.OffsetOf<RawState>(nameof(RawState.WatchArmed));
    private static readonly int OffWatchStart = (int)Marshal.OffsetOf<RawState>(nameof(RawState.WatchStart));
    private static readonly int OffWatchEnd = (int)Marshal.OffsetOf<RawState>(nameof(RawState.WatchEnd));
    private static readonly int OffCr0Wp = (int)Marshal.OffsetOf<RawState>(nameof(RawState.Cr0Wp));
    private static readonly int OffStoreGuardBail = (int)Marshal.OffsetOf<RawState>(nameof(RawState.StoreGuardBail));
    private static readonly int OffWriteStamp = (int)Marshal.OffsetOf<RawState>(nameof(RawState.WriteStamp));
    private static readonly int OffWriteStampPages = (int)Marshal.OffsetOf<RawState>(nameof(RawState.WriteStampPages));
    private static readonly int OffRam = (int)Marshal.OffsetOf<RawState>(nameof(RawState.Ram));
    private static readonly int OffRamSize = (int)Marshal.OffsetOf<RawState>(nameof(RawState.RamSize));
    private static readonly int OffEip = (int)Marshal.OffsetOf<RawState>(nameof(RawState.Eip));
    private static readonly int OffEflags = (int)Marshal.OffsetOf<RawState>(nameof(RawState.Eflags));
    private static readonly int OffTsc = (int)Marshal.OffsetOf<RawState>(nameof(RawState.Tsc));
    private static readonly int OffProgress = (int)Marshal.OffsetOf<RawState>(nameof(RawState.Progress));
    private static readonly int OffChainTable = (int)Marshal.OffsetOf<RawState>(nameof(RawState.ChainTable));
    private static readonly int OffGeneration = (int)Marshal.OffsetOf<RawState>(nameof(RawState.Generation));
    private static readonly int OffIrqArmed = (int)Marshal.OffsetOf<RawState>(nameof(RawState.IrqArmed));
    private static readonly int OffIrqPending = (int)Marshal.OffsetOf<RawState>(nameof(RawState.IrqPending));
    private static readonly int OffChainServed = (int)Marshal.OffsetOf<RawState>(nameof(RawState.ChainServed));
    private static readonly int OffChainCount = (int)Marshal.OffsetOf<RawState>(nameof(RawState.ChainCount));
    private static readonly int OffSelfLoopEdges = (int)Marshal.OffsetOf<RawState>(nameof(RawState.SelfLoopEdges));
    private static readonly int OffFault = (int)Marshal.OffsetOf<RawState>(nameof(RawState.Fault));
    private static readonly int OffSegments = (int)Marshal.OffsetOf<RawState>(nameof(RawState.SegmentBases));

    private static readonly Register[] Gpr32 =
    [
        Register.EAX, Register.ECX, Register.EDX, Register.EBX,
        Register.R12D, Register.R13D, Register.R14D, Register.R15D,
    ];

    private static readonly Register[] Gpr64 =
    [
        Register.RAX, Register.RCX, Register.RDX, Register.RBX,
        Register.R12, Register.R13, Register.R14, Register.R15,
    ];

    private static readonly Register[] Gpr16 =
    [
        Register.AX, Register.CX, Register.DX, Register.BX,
        Register.R12W, Register.R13W, Register.R14W, Register.R15W,
    ];

    private static readonly Register[] Gpr8 =
    [
        Register.AL, Register.CL, Register.DL, Register.BL,
        Register.R12L, Register.R13L, Register.R14L, Register.R15L,
    ];

    private static readonly Register[] Gpr8High =
    [
        Register.AH, Register.CH, Register.DH, Register.BH,
        Register.AH, Register.CH, Register.DH, Register.BH,
    ];

    /// <summary>
    /// Tries to build a raw runner for the planned members. Returns 0 when the
    /// host cannot run raw code. <paramref name="chainEntry"/> receives the
    /// in-frame chain target (0 when the block is not chainable or the address
    /// could not be resolved), which is what the chain table publishes: a chain
    /// jump enters it with the source's frame in place, so the target skips the
    /// whole prologue.
    /// </summary>
    public static nint TryBuild(
        uint headEip,
        ReadOnlySpan<JitMemberPlan> plans,
        bool mmioFast,
        bool chainable,
        out nint chainEntry,
        out RawBuildStatics statics)
    {
        chainEntry = 0;
        statics = default;
        if (!RawCodeMemory.IsSupported || plans.Length == 0)
        {
            return 0;
        }

        try
        {
            // TEMPORARY diagnostic: XGRID_RAW_SKIP_HEADS=hex[,hex...] forces the
            // listed heads to the IL runner (bisecting raw-block divergences).
            string? skip = Environment.GetEnvironmentVariable("XGRID_RAW_SKIP_HEADS");
            if (!string.IsNullOrEmpty(skip))
            {
                foreach (string part in skip.Split(','))
                {
                    if (uint.TryParse(part, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out uint skipped)
                        && skipped == headEip)
                    {
                        if (XboxCpu.RawTraceEnabled)
                        {
                            XboxCpu.RawTrace($"  skip head={headEip:X5} (XGRID_RAW_SKIP_HEADS)");
                        }
                        return 0;
                    }
                }
            }

            var ctx = new EmitContext(headEip, plans.ToArray(), mmioFast, chainable);
            nint entry = ctx.Build();
            if (entry != 0)
            {
                chainEntry = ctx.ChainEntry;
                statics = ctx.Statics;
            }

            return entry;
        }
        catch (Exception ex)
        {
            // A shape the assembler cannot encode: keep the IL runner for this
            // block (correctness first, the raw path is an optimization).
            XboxCpu.NoteRawBuildFailure();
            if (XboxCpu.RawTraceEnabled)
            {
                XboxCpu.RawTrace($"  build failed head={headEip:X5}: {ex.GetType().Name}: {ex.Message}");
            }

            return 0;
        }
    }

    private sealed class EmitContext(uint headEip, JitMemberPlan[] plans, bool mmioFast, bool chainable)
    {
        private readonly Assembler _a = new(64);
        private Label _exit;
        private Label _ret;
        private Label _leave;
        private Label _faultRet;
        private Label _chainIn;
        private Label _body;
        private bool _flagsLive;
        private bool _memberHelper;

        // Static per-member accounting (§239). A block is straight-line, so the
        // guest EIP, the TSC tick count and the served count of every point in
        // it are compile-time constants. The emitter therefore never writes
        // them per member: an *edge* (an exit, a helper call, or the linear
        // fall-through) publishes them once through EmitPublishAccounting, and
        // F_TSC stays the TSC of the block entry (the base) for the whole
        // block. That removes 11 emitted instructions per member (two EIP
        // stores, three TSC accesses and three served accesses) from every
        // guest instruction the raw runner serves.
        private uint _memberEip;
        private int _membersDone;

        /// <summary>Gets the in-frame chain target resolved by <see cref="Build"/> (0 when not chainable).</summary>
        public nint ChainEntry { get; private set; }

        /// <summary>Gets the emitted-region census and the Assemble/emit split (D8).</summary>
        public RawBuildStatics Statics { get; private set; }

        public nint Build()
        {
            long buildStart = System.Diagnostics.Stopwatch.GetTimestamp();
            _exit = _a.CreateLabel(string.Empty);
            _ret = _a.CreateLabel(string.Empty);
            _leave = _a.CreateLabel(string.Empty);
            _faultRet = _a.CreateLabel(string.Empty);
            _chainIn = _a.CreateLabel(string.Empty);
            _body = _a.CreateLabel(string.Empty);
            EmitPrologue();
            int prologueEnd = _a.Instructions.Count;
            if (chainable)
            {
                // The external entry falls through into the body; a chain jump
                // enters at _chainIn, which skips the prologue entirely (the
                // source's frame, state slots and guest registers are live).
                _a.jmp(_body);
                _a.Label(ref _chainIn);
                EmitChainEntry();
                _a.Label(ref _body);
            }

            int bodyStart = _a.Instructions.Count;
            uint eip = headEip;
            for (int i = 0; i < plans.Length; i++)
            {
                JitMemberPlan plan = plans[i];
                EmitMember(plan, eip, i);
                if (!plan.IsTerminator)
                {
                    eip += (uint)plan.Length;
                }
            }

            int membersEnd = _a.Instructions.Count;

            // The linear fall-through edge: every member ran, so the block's
            // own accounting is published here (every other edge publishes its
            // own before jumping to _exit).
            EmitPublishAccounting(plans.Length, plans.Length, eip);
            EmitEpilogue();

            int instructionsGenerated = _a.Instructions.Count;
            long assembleStart = System.Diagnostics.Stopwatch.GetTimestamp();

            byte* memory = RawCodeMemory.Allocate(64 * 1024);
            if (memory is null)
            {
                return 0;
            }



            var writer = new BufferCodeWriter(memory, 64 * 1024);

            AssemblerResult result = _a.Assemble(
                writer,
                (ulong)memory,
                BlockEncoderOptions.ReturnNewInstructionOffsets);

            long assembleEnd = System.Diagnostics.Stopwatch.GetTimestamp();

            // D8 census: the regions are straight-line, so counting the
            // assembled instructions at each label boundary gives the exact
            // host instruction cost of an entry, a chained edge, a member and
            // an exit without a counter in the emitted code (one integer read
            // per boundary at formation).
            Statics = new RawBuildStatics
            {
                PrologueInstructions = prologueEnd,
                ChainEntryInstructions = bodyStart - prologueEnd,
                MemberInstructions = membersEnd - bodyStart,
                ExitInstructions = instructionsGenerated - membersEnd,
                AssembleTicks = assembleEnd - assembleStart,
                EmitTicks = assembleStart - buildStart,
            };
            if (chainable)
            {
                // Resolve the in-frame chain target. GetLabelRIP returns an
                // absolute RIP; a value outside this block's own code means the
                // API contract changed, in which case the block simply is not
                // published as a chain target (the safe degradation).
                ulong chainRip = result.GetLabelRIP(in _chainIn);
                if (chainRip >= (ulong)memory && chainRip < (ulong)memory + (ulong)writer.Position)
                {
                    ChainEntry = (nint)chainRip;
                }
            }

            if (string.Equals(Environment.GetEnvironmentVariable("XGRID_RAW_DUMP"), "1", StringComparison.Ordinal))
            {
                Dump(headEip: headEip, memory, writer.Position);
            }

            return (nint)memory;
        }

        private static void Dump(uint headEip, byte* memory, int length)
        {
            try
            {
                var bytes = new byte[length];
                Marshal.Copy((IntPtr)memory, bytes, 0, length);
                var codeReader = new ByteArrayCodeReader(bytes);
                var decoder = Iced.Intel.Decoder.Create(64, codeReader);
                decoder.IP = (ulong)memory;
                var output = new System.Text.StringBuilder();
                output.Append("head=0x").Append(headEip.ToString("X8")).Append(" len=").Append(length).Append('\n');
                while (decoder.IP < (ulong)memory + (ulong)length)
                {
                    Instruction instruction = decoder.Decode();
                    output.Append(instruction.IP.ToString("X16")).Append(": ").Append(instruction.ToString()).Append('\n');
                }

                string path = Path.Combine(Path.GetTempPath(), "xgrid-raw-" + headEip.ToString("X8") + ".txt");
                File.WriteAllText(path, output.ToString());
            }
            catch (IOException)
            {
                // Diagnostics only.
            }
        }

        private static AssemblerRegister64 R64(Register register) => new(register);

        private static AssemblerRegister32 R32(Register register) => new(register);

        private static AssemblerMemoryOperand MemOp(Register? baseRegister, Register? indexRegister, int scale, long displacement)
        {
            AssemblerMemoryOperand operand;
            if (baseRegister is not null && indexRegister is not null)
            {
                operand = R64(baseRegister.Value) + (R64(indexRegister.Value) * scale);
            }
            else
            {
                operand = baseRegister is not null
                    ? R64(baseRegister.Value) + 0L
                    : indexRegister is not null
                    ? R64(indexRegister.Value) * scale
                    : throw new InvalidOperationException("raw: memory operand without base or index");
            }

            return displacement == 0 ? operand : operand + displacement;
        }

        private static AssemblerMemoryOperand Slot(int offset) => MemOp(Register.RSP, null, 1, offset);

        private static AssemblerMemoryOperand Field(Register baseRegister, int offset) =>
            MemOp(baseRegister, null, 1, offset);

        private static AssemblerMemoryOperand Indexed(Register baseRegister, Register indexRegister, int scale, long displacement) =>
            MemOp(baseRegister, indexRegister, scale, displacement);

        private void EmitPrologue()
        {
            Assembler a = _a;
            a.push(R64(Register.RBX));
            a.push(R64(Register.RBP));
            a.push(R64(Register.RSI));
            a.push(R64(Register.RDI));
            a.push(R64(Register.R12));
            a.push(R64(Register.R13));
            a.push(R64(Register.R14));
            a.push(R64(Register.R15));
            a.sub(R64(Register.RSP), FrameSize);

            // Arguments: rcx = cpu, rdx = RawState*, r8 = entries, r9d = budget.
            a.mov(Slot(F_CPU), R64(Register.RCX));
            a.mov(Slot(F_STATE), R64(Register.RDX));
            a.mov(Slot(F_ENTRIES), R64(Register.R8));
            a.mov(Slot(F_BUDGET), R32(Register.R9D));
            a.mov(R32(Register.EAX), 0);
            a.mov(Slot(F_SERVED), R32(Register.EAX));
            a.mov(R64(Register.R11), R64(Register.RDX));

            a.mov(R64(Register.R9), Field(Register.R11, OffEflags));
            a.mov(R32(Register.EAX), MemOp(Register.R9, null, 1, 0));
            a.mov(Slot(F_EFLAGS), R32(Register.EAX));
            a.mov(R32(Register.ECX), R32(Register.EAX));
            a.and(R32(Register.ECX), unchecked((int)ControlMask));
            a.mov(Slot(F_CTRL), R32(Register.ECX));

            // popfq must never see IOPL/TF/NT (they fault in user mode): only
            // the arithmetic flags and DF are loaded into the host FLAGS.
            a.and(R32(Register.EAX), unchecked((int)ArithmeticMask));
            a.push(R64(Register.RAX));
            a.popfq();

            a.mov(R64(Register.R9), Field(Register.R11, OffTsc));
            a.mov(R64(Register.RAX), MemOp(Register.R9, null, 1, 0));
            a.mov(Slot(F_TSC), R64(Register.RAX));
            a.mov(Slot(F_TSC_NOW), R64(Register.RAX));

            a.mov(R64(Register.R9), Field(Register.R11, OffEip));
            a.mov(R32(Register.EAX), MemOp(Register.R9, null, 1, 0));
            a.mov(Slot(F_EIP), R32(Register.EAX));

            a.mov(R64(Register.R9), Field(Register.R11, OffRegisters));
            LoadGuestRegisters(a, Register.R9);

            // The host FLAGS register now carries the guest's arithmetic flags
            // (plus DF): every following member may rely on them staying live
            // until something materializes them.
            _flagsLive = true;
        }

        /// <summary>
        /// In-frame chain entry (C5 slice 1): a chain jump from another raw
        /// block arrives here with that block's frame in place - same
        /// <see cref="FrameSize"/>, same slot layout - and the guest registers
        /// still live in the host registers. The source has already advanced
        /// the frame EIP to this block's head, published F_EFLAGS/F_CTRL, set
        /// F_BUDGET to the remaining budget, published this block's entries
        /// and zeroed the frame's served counter. Only the host FLAGS need
        /// reloading: the source's chain probe clobbered them, and F_EFLAGS is
        /// authoritative on every edge (the source materialized it first).
        /// </summary>
        private void EmitChainEntry()
        {
            Assembler a = _a;
            a.mov(R64(Register.R11), Slot(F_STATE));

            // Compile-time liveness must be forced: the prologue just set it
            // true, but this path's FLAGS were clobbered by the source's probe.
            _flagsLive = false;
            EmitLoadFlags();

            // The source published the guest registers before its chain probe,
            // so reload them exactly as the prologue does.
            a.mov(R64(Register.R9), Field(Register.R11, OffRegisters));
            LoadGuestRegisters(a, Register.R9);
        }

        private static void LoadGuestRegisters(Assembler a, Register baseRegister)
        {
            a.mov(R32(Register.EAX), Field(baseRegister, 0));
            a.mov(R32(Register.ECX), Field(baseRegister, 4));
            a.mov(R32(Register.EDX), Field(baseRegister, 8));
            a.mov(R32(Register.EBX), Field(baseRegister, 12));
            a.mov(R32(Register.R12D), Field(baseRegister, 16));
            a.mov(R32(Register.R13D), Field(baseRegister, 20));
            a.mov(R32(Register.R14D), Field(baseRegister, 24));
            a.mov(R32(Register.R15D), Field(baseRegister, 28));
        }

        private static void StoreGuestRegisters(Assembler a, Register baseRegister)
        {
            a.mov(Field(baseRegister, 0), R32(Register.EAX));
            a.mov(Field(baseRegister, 4), R32(Register.ECX));
            a.mov(Field(baseRegister, 8), R32(Register.EDX));
            a.mov(Field(baseRegister, 12), R32(Register.EBX));
            a.mov(Field(baseRegister, 16), R32(Register.R12D));
            a.mov(Field(baseRegister, 20), R32(Register.R13D));
            a.mov(Field(baseRegister, 24), R32(Register.R14D));
            a.mov(Field(baseRegister, 28), R32(Register.R15D));
        }

        private void EmitEpilogue()
        {
            Assembler a = _a;

            // The linear fall-through (every member completed normally)
            // normalizes the frame flags here, because `_exit` is also a jump
            // target of edges that cannot know this emission point's
            // compile-time liveness (the Jcc arms, the terminator jumps) —
            // each of those normalizes on its own edge. After this point
            // F_EFLAGS is authoritative on every path, so the epilogue must
            // not re-materialize from the host FLAGS.
            EmitMaterializeFlags();

            a.Label(ref _exit);

            // Publish the guest state on every return path (a chained target
            // reloads the registers from it at its in-frame entry, exactly as
            // the prologue would).
            EmitStoreGuestState();
            if (chainable)
            {
                EmitChainAttempt();
            }

            a.Label(ref _ret);

            // Return the total served (this block plus every chained block)
            // and clear the carry for the next runner invocation.
            a.mov(R64(Register.R11), Slot(F_STATE));
            a.mov(R64(Register.R9), Field(Register.R11, OffChainServed));
            a.mov(R32(Register.EAX), Slot(F_SERVED));
            a.add(R32(Register.EAX), MemOp(Register.R9, null, 1, 0));
            a.xor(R32(Register.R10D), R32(Register.R10D));
            a.mov(MemOp(Register.R9, null, 1, 0), R32(Register.R10D));

            a.Label(ref _leave);
            a.add(R64(Register.RSP), FrameSize);
            a.pop(R64(Register.R15));
            a.pop(R64(Register.R14));
            a.pop(R64(Register.R13));
            a.pop(R64(Register.R12));
            a.pop(R64(Register.RDI));
            a.pop(R64(Register.RSI));
            a.pop(R64(Register.RBP));
            a.pop(R64(Register.RBX));
            a.ret();

            // Fault exit: a helper caught an exception (RawCaptureFault) instead
            // of unwinding through this frame, which CoreCLR cannot walk. The
            // failing call site published the guest state and the progress slot
            // before the call, so only the carry needs clearing; the sentinel
            // makes RunJitBlock rethrow the captured exception in managed code.
            a.Label(ref _faultRet);
            a.mov(R64(Register.R11), Slot(F_STATE));
            a.mov(R64(Register.R9), Field(Register.R11, OffChainServed));
            a.xor(R32(Register.R10D), R32(Register.R10D));
            a.mov(MemOp(Register.R9, null, 1, 0), R32(Register.R10D));
            a.mov(R32(Register.EAX), -1);
            a.jmp(_leave);
        }

        /// <summary>
        /// Tests the fault word a helper sets when it captured an exception and
        /// branches to the fault exit. Emitted after every helper call: a
        /// managed exception must never unwind through this frame (it has no
        /// unwind data, so CoreCLR cannot walk it and the fault would abort the
        /// process instead of reaching <c>RunJitBlock</c>'s catch).
        /// </summary>
        private void EmitFaultCheck()
        {
            Assembler a = _a;
            a.mov(R64(Register.R11), Slot(F_STATE));
            a.mov(R64(Register.R9), Field(Register.R11, OffFault));
            a.mov(R32(Register.R10D), MemOp(Register.R9, null, 1, 0));
            a.test(R32(Register.R10D), R32(Register.R10D));
            a.jne(_faultRet);
        }

        /// <summary>
        /// Direct block chaining: a matching table entry lets the block jump to
        /// the target's in-frame chain entry, which continues in this same
        /// frame (identical layout) instead of tearing it down and running the
        /// target's prologue. The target's epilogue returns to the original
        /// <c>RunJitBlock</c> caller; the served carry keeps the accounting
        /// exact across the chain. Any failed check falls through to the normal
        /// return path (the caller serves the next block itself).
        ///
        /// <para>Every register used here is host scratch (R8-R11, RDI, RSI,
        /// RBP): the guest GPRs are already published by the epilogue's
        /// <c>EmitStoreGuestState</c>, and the target's chain entry reloads them
        /// from there exactly as the prologue would.</para>
        ///
        /// <para>The entry poll contract is preserved: with IF set the block
        /// only chains when the interrupt source exposes a cheap 0/1 pending
        /// word and that word is clear. Otherwise it returns so
        /// <c>RunJitBlock</c> acknowledges (and delivers) at the boundary.
        /// Store-bearing blocks are never chainable (they can invalidate the
        /// table and the generation mid-path).</para>
        /// </summary>
        private void EmitChainAttempt()
        {
            Assembler a = _a;
            a.mov(R64(Register.R11), Slot(F_STATE));

            Label ifClear = a.CreateLabel(string.Empty);
            a.mov(R32(Register.R10D), Slot(F_CTRL));
            a.test(R32(Register.R10D), (int)FlagBits.If);
            a.je(ifClear);
            a.mov(R32(Register.R10D), Field(Register.R11, OffIrqArmed));
            a.test(R32(Register.R10D), R32(Register.R10D));
            a.je(_ret);
            a.mov(R64(Register.R9), Field(Register.R11, OffIrqPending));
            a.mov(R32(Register.R10D), MemOp(Register.R9, null, 1, 0));
            a.test(R32(Register.R10D), R32(Register.R10D));
            a.jne(_ret);

            a.Label(ref ifClear);

            // Direct-mapped lookup by the next guest EIP.
            a.mov(R32(Register.R10D), Slot(F_EIP));
            a.mov(R32(Register.R8D), R32(Register.R10D));
            a.imul(R32(Register.R8D), R32(Register.R8D), unchecked((int)RawChainHash.Multiplier));
            a.shr(R32(Register.R8D), RawChainHash.IndexShift);
            a.shl(R32(Register.R8D), 5); // sizeof(RawChainEntry) = 32
            a.mov(R64(Register.RDI), Field(Register.R11, OffChainTable));
            a.add(R64(Register.RDI), R64(Register.R8));

            a.mov(R32(Register.R8D), MemOp(Register.RDI, null, 1, 0)); // Valid
            a.test(R32(Register.R8D), R32(Register.R8D));
            a.je(_ret);
            a.cmp(MemOp(Register.RDI, null, 1, 4), R32(Register.R10D)); // Eip
            a.jne(_ret);
            a.mov(R64(Register.R9), Field(Register.R11, OffGeneration)); // live generation pointer
            a.test(R64(Register.R9), R64(Register.R9));
            a.je(_ret);
            a.mov(R32(Register.R8D), MemOp(Register.R9, null, 1, 0));
            a.cmp(R32(Register.R8D), MemOp(Register.RDI, null, 1, 8)); // entry Generation
            a.jne(_ret);

            if (XboxCpu.SelfLoopProbeEnabled)
            {
                // D8 self-loop probe: the resolved target head is the next EIP;
                // when it equals this block's own head the edge is a self-loop.
                // R10D/R9 are host scratch here and every later check reloads
                // what it needs, so the compare's flags cannot leak.
                Label notSelf = a.CreateLabel(string.Empty);
                a.cmp(R32(Register.R10D), unchecked((int)headEip));
                a.jne(notSelf);
                a.mov(R64(Register.R9), Field(Register.R11, OffSelfLoopEdges));
                a.mov(R32(Register.R8D), 1);
                a.add(MemOp(Register.R9, null, 1, 0), R32(Register.R8D));
                a.Label(ref notSelf);
            }

            // remaining = F_BUDGET - F_SERVED (the budget is already net of
            // every prior chained block; the served carry is accounting only).
            // Signed compare: a negative remainder must return, not read as a
            // huge unsigned budget. RSI is host scratch.
            a.mov(R32(Register.ESI), Slot(F_BUDGET));
            a.sub(R32(Register.ESI), Slot(F_SERVED));
            a.cmp(R32(Register.ESI), MemOp(Register.RDI, null, 1, 12)); // Members
            a.jl(_ret);

            // The source's served members move into the carry.
            a.mov(R64(Register.R9), Field(Register.R11, OffChainServed));
            a.mov(R32(Register.R8D), Slot(F_SERVED));
            a.add(MemOp(Register.R9, null, 1, 0), R32(Register.R8D));

            // Diagnostic: count the chain jump.
            a.mov(R64(Register.R9), Field(Register.R11, OffChainCount));
            a.mov(R32(Register.R10D), MemOp(Register.R9, null, 1, 0));
            a.lea(R32(Register.R10D), MemOp(Register.R10, null, 1, 1));
            a.mov(MemOp(Register.R9, null, 1, 0), R32(Register.R10D));

            // Target slots for the in-frame chain entry. The frame is not torn
            // down: the target continues in this same frame (identical layout)
            // at its own chain entry, which reloads the guest state and flags.
            // EIP is already the target's head (the probe matched it) and the
            // carry now holds this block's served count.
            //
            // §239: the target's accounting is relative to its own entry, so
            // the published TSC becomes the target's base and the served slot
            // restarts at zero.
            a.mov(R64(Register.R8), Slot(F_TSC_NOW));
            a.mov(Slot(F_TSC), R64(Register.R8));
            a.mov(Slot(F_BUDGET), R32(Register.ESI));
            a.mov(R64(Register.R8), MemOp(Register.RDI, null, 1, 24)); // Entries
            a.mov(Slot(F_ENTRIES), R64(Register.R8));
            a.mov(R64(Register.R10), MemOp(Register.RDI, null, 1, 16)); // Target (chain entry)
            a.xor(R32(Register.R8D), R32(Register.R8D));
            a.mov(Slot(F_SERVED), R32(Register.R8D));
            a.jmp(R64(Register.R10));
        }

        /// <summary>
        /// Merges the authoritative flag sources into the frame's EFLAGS slot:
        /// the host FLAGS register when the arithmetic bits are live,
        /// otherwise the slot's own (already merged) value, plus the control
        /// bits (IF/DF/...) tracked in <see cref="F_CTRL"/>. Always runs, so a
        /// CLI/STI/CLD/STD member followed by a flag-consuming one sees the
        /// updated control bits even when no arithmetic flag was written.
        /// </summary>
        private void EmitMaterializeFlags()
        {
            // R10D, never a guest register: the guest GPRs must survive until
            // the epilogue stores them.
            Assembler a = _a;
            if (_flagsLive)
            {
                a.pushfq();
                a.pop(R64(Register.R10));
                a.and(R32(Register.R10D), unchecked((int)ArithmeticMask));
            }
            else
            {
                a.mov(R32(Register.R10D), Slot(F_EFLAGS));
                a.and(R32(Register.R10D), unchecked((int)ArithmeticMask));
            }

            a.or(R32(Register.R10D), Slot(F_CTRL));
            a.mov(Slot(F_EFLAGS), R32(Register.R10D));
            _flagsLive = false;
        }

        private void EmitLoadFlags()
        {
            if (_flagsLive)
            {
                return;
            }

            // R10D, never a guest register: the guest GPRs must survive.
            Assembler a = _a;
            a.mov(R32(Register.R10D), Slot(F_EFLAGS));
            a.and(R32(Register.R10D), unchecked((int)ArithmeticMask));
            a.push(R64(Register.R10));
            a.popfq();
            _flagsLive = true;
        }

        /// <summary>
        /// Reloads the host FLAGS register when the arithmetic flags are dead
        /// (a probe, a poll or a control-flag update clobbered them) and the
        /// next operation only partially rewrites them: Inc/Dec preserve CF,
        /// ADC/SBB read it. Full-setters (add/sub/and/or/xor/cmp/test) compute
        /// every arithmetic bit and need no reload.
        /// </summary>
        private void EmitEnsureHostFlags()
        {
            if (!_flagsLive)
            {
                EmitLoadFlags();
            }
        }

        private void EmitStoreGuestState()
        {
            Assembler a = _a;
            a.mov(R64(Register.R9), Field(Register.R11, OffRegisters));
            StoreGuestRegisters(a, Register.R9);

            a.mov(R32(Register.EAX), Slot(F_EFLAGS));
            a.mov(R64(Register.R9), Field(Register.R11, OffEflags));
            a.mov(MemOp(Register.R9, null, 1, 0), R32(Register.EAX));

            a.mov(R32(Register.EAX), Slot(F_EIP));
            a.mov(R64(Register.R9), Field(Register.R11, OffEip));
            a.mov(MemOp(Register.R9, null, 1, 0), R32(Register.EAX));

            // §239: the per-edge publish already folded this block's ticks into
            // F_TSC_NOW (base + members completed), so the exact interpreter
            // value is one load away.
            a.mov(R64(Register.R10), Slot(F_TSC_NOW));
            a.mov(R64(Register.R9), Field(Register.R11, OffTsc));
            a.mov(MemOp(Register.R9, null, 1, 0), R64(Register.R10));
        }

        private void EmitSyncForHelper()
        {
            EmitMaterializeFlags();
            EmitSyncGuestStateForHelper();
        }

        /// <summary>
        /// Guest-state publish + progress accounting for a helper call whose
        /// site already materialized the flags. The memory probe materializes
        /// F_EFLAGS before it runs and then clobbers the host FLAGS with its
        /// own comparisons, so a device-path call must not materialize again:
        /// doing so would overwrite the correct guest flags with the probe's
        /// last <c>cmp</c>/<c>and</c> result, and the reload after the helper
        /// would hand those to the next flag consumer (found by the
        /// <c>--jit-mmio</c> boot divergence at 25.1M steps, decisions 202).
        ///
        /// <para>§239: the helper is an edge, so the static accounting is
        /// published first - the in-progress member is TSC-ticked but not
        /// served, and the guest EIP is the member's own.</para>
        /// </summary>
        private void EmitSyncGuestStateForHelper()
        {
            EmitPublishAccounting(_membersDone + 1, _membersDone, _memberEip);
            EmitStoreGuestState();
            Assembler a = _a;
            // Fault progress includes every block already chained into this
            // runner call; RunJitBlock adds the faulting member itself.
            a.mov(R64(Register.R10), Field(Register.R11, OffChainServed));
            a.mov(R32(Register.EAX), Slot(F_SERVED));
            a.add(R32(Register.EAX), MemOp(Register.R10, null, 1, 0));
            a.mov(R64(Register.R9), Field(Register.R11, OffProgress));
            a.mov(MemOp(Register.R9, null, 1, 0), R32(Register.EAX));
        }

        private void EmitReloadAfterHelper()
        {
            Assembler a = _a;
            a.mov(R64(Register.R11), Slot(F_STATE));
            a.mov(R64(Register.R9), Field(Register.R11, OffEflags));
            a.mov(R32(Register.EAX), MemOp(Register.R9, null, 1, 0));
            a.mov(Slot(F_EFLAGS), R32(Register.EAX));
            a.mov(R32(Register.ECX), R32(Register.EAX));
            a.and(R32(Register.ECX), unchecked((int)ControlMask));
            a.mov(Slot(F_CTRL), R32(Register.ECX));

            // popfq must never see IOPL/TF/NT (they fault in user mode): only
            // the arithmetic flags and DF are loaded into the host FLAGS.
            a.and(R32(Register.EAX), unchecked((int)ArithmeticMask));
            a.push(R64(Register.RAX));
            a.popfq();
            _flagsLive = true;

            a.mov(R64(Register.R9), Field(Register.R11, OffEip));
            a.mov(R32(Register.EAX), MemOp(Register.R9, null, 1, 0));
            a.mov(Slot(F_EIP), R32(Register.EAX));

            // Diagnostic: keep the frame TSC (the helper does not tick it).

            a.mov(R64(Register.R9), Field(Register.R11, OffRegisters));
            LoadGuestRegisters(a, Register.R9);
        }

        /// <summary>
        /// Publishes the block's static accounting at an edge: the TSC value,
        /// the served count and (when the edge's EIP is static) the guest EIP.
        /// A block is straight-line, so <paramref name="tscTicks"/> is the
        /// number of TSC ticks since the block entry - the in-progress member
        /// is already counted, because the interpreter ticks TSC before a
        /// member's body - and <paramref name="served"/> is the number of
        /// members that have *completed*. F_TSC keeps the entry TSC (the base)
        /// for the whole block; the published value lands in F_TSC_NOW, which
        /// <see cref="EmitStoreGuestState"/> copies into the context.
        ///
        /// <para>R10 only: never a guest register, and the moves/lea leave the
        /// host arithmetic flags intact (an edge publishes while they are still
        /// live, before the edge's own materialize).</para>
        /// </summary>
        private void EmitPublishAccounting(int tscTicks, int served, uint? eip)
        {
            Assembler a = _a;
            a.mov(R64(Register.R10), Slot(F_TSC));
            a.lea(R64(Register.R10), MemOp(Register.R10, null, 1, tscTicks));
            a.mov(Slot(F_TSC_NOW), R64(Register.R10));
            a.mov(R32(Register.R10D), served);
            a.mov(Slot(F_SERVED), R32(Register.R10D));
            if (eip is { } value)
            {
                a.mov(R32(Register.R10D), unchecked((int)value));
                a.mov(Slot(F_EIP), R32(Register.R10D));
            }
        }

        /// <summary>
        /// Updates the control-flag slot (CLI/STI/CLD/STD). Materializes the
        /// arithmetic flags first (the register moves below clobber the host
        /// FLAGS) and uses R10D, never a guest register.
        /// </summary>
        private void EmitUpdateControl(int andMask, int orMask)
        {
            EmitMaterializeFlags();
            Assembler a = _a;
            a.mov(R32(Register.R10D), Slot(F_CTRL));
            if (andMask != -1)
            {
                a.and(R32(Register.R10D), andMask);
            }

            if (orMask != 0)
            {
                a.or(R32(Register.R10D), orMask);
            }

            a.mov(Slot(F_CTRL), R32(Register.R10D));
        }

        /// <summary>
        /// Fast-mode post-member interrupt poll (STI/POPF/strings/port I/O),
        /// mirroring the IL runner's <c>EmitPostPoll</c>: when IF is set,
        /// acknowledge once; a delivered vector is recorded on the CPU, the
        /// member counts as served, the delivery boundary ticks TSC once more,
        /// and the block exits at the next instruction's EIP for
        /// <c>RunJitBlock</c> to transfer (it adds the delivery's own step).
        /// </summary>
        private void EmitPostPoll(int length, bool advanceEip)
        {
            Assembler a = _a;
            EmitMaterializeFlags();
            Label done = a.CreateLabel(string.Empty);
            a.mov(R32(Register.R10D), Slot(F_CTRL));
            a.test(R32(Register.R10D), (int)FlagBits.If);
            a.je(done);

            // The helper is a managed call: it clobbers the caller-saved
            // registers, which hold guest eax/ecx/edx/ebx. Park them and keep
            // the Win64 shadow space clear of the parked values (the callee
            // may spill its arguments there), and keep RSP 16-byte aligned at
            // the call; R8/R9/R10/R11/RDI/RSI are raw scratch and may die.
            a.mov(R64(Register.RSI), RawHelpers.PollInterruptAddress);
            a.mov(R32(Register.R8D), Slot(F_CTRL));
            a.mov(R64(Register.R9), Slot(F_STATE));
            a.mov(R64(Register.R10), Slot(F_CPU));
            a.push(R64(Register.RAX));
            a.push(R64(Register.RCX));
            a.push(R64(Register.RDX));
            a.push(R64(Register.RBX));
            a.sub(R64(Register.RSP), 0x20);
            a.mov(R64(Register.RCX), R64(Register.R10));
            a.mov(R64(Register.RDX), R64(Register.R9));
            a.call(R64(Register.RSI));
            a.add(R64(Register.RSP), 0x20);
            a.mov(R32(Register.R8D), R32(Register.EAX));
            a.pop(R64(Register.RBX));
            a.pop(R64(Register.RDX));
            a.pop(R64(Register.RCX));
            a.pop(R64(Register.RAX));
            EmitFaultCheck();
            a.test(R32(Register.R8D), R32(Register.R8D));
            a.je(done);

            if (advanceEip)
            {
                // The delivery boundary ticks TSC once more (the member's own
                // tick is counted, plus one for the interrupt step RunJitBlock
                // adds), exactly as the per-member accounting did.
                EmitPublishAccounting(
                    _membersDone + 2,
                    _membersDone + 1,
                    unchecked(_memberEip + (uint)length));
            }
            else
            {
                // The helper already advanced the context EIP; the published
                // value (reloaded by EmitReloadAfterHelper) is authoritative.
                EmitPublishAccounting(_membersDone + 2, _membersDone + 1, null);
            }

            // A pending delivery must return to RunJitBlock (it adds the
            // delivery's own step); publish the boundary state and skip the
            // chain attempt.
            EmitMaterializeFlags();
            EmitStoreGuestState();
            a.jmp(_ret);
            a.Label(ref done);
        }

        private void EmitMember(in JitMemberPlan plan, uint eip, int index)
        {
            _memberHelper = false;
            _memberEip = eip;
            _membersDone = index;

            switch (plan.Kind)
            {
                case JitKind.Nop:
                    break;

                case JitKind.MovRR:
                    EmitMovReg(plan);
                    break;

                case JitKind.MovRI:
                    EmitMovImmediate(plan);
                    break;

                case JitKind.AluRR:
                    EmitAluReg(plan);
                    break;

                case JitKind.AluRI:
                    EmitAluImmediate(plan);
                    break;

                case JitKind.TestRR:
                    EmitTestReg(plan);
                    break;

                case JitKind.TestRI:
                    EmitTestImmediate(plan);
                    break;

                case JitKind.IncR:
                case JitKind.DecR:
                    EmitIncDecReg(plan);
                    break;

                case JitKind.Lea:
                    _a.lea(R32(Gpr32[plan.RegIndex]), AddressOf(plan));
                    break;

                case JitKind.MovExtend:
                    EmitMovExtend(plan, index);
                    break;

                case JitKind.ShiftRI:
                    if (plan.ShiftOp is JitShiftOp.Rcl or JitShiftOp.Rcr)
                    {
                        // Carry-through rotates keep the exact ShiftOps
                        // contract as the member.
                        EmitHelperMember(index);
                    }
                    else
                    {
                        EmitShift(plan);
                    }

                    break;

                case JitKind.ShiftRC:
                    if (plan.ShiftOp is JitShiftOp.Rcl or JitShiftOp.Rcr)
                    {
                        EmitHelperMember(index);
                    }
                    else
                    {
                        EmitShiftByCl(plan);
                    }

                    break;

                case JitKind.Cli:
                    EmitUpdateControl(unchecked((int)~FlagBits.If), 0);
                    break;

                case JitKind.Sti:
                    EmitUpdateControl(-1, (int)FlagBits.If);
                    break;

                case JitKind.Cld:
                    // DF lives only in the control slot: the host DF must stay
                    // clear (the Win64 ABI requires DF=0 at every call, and a
                    // helper may use string instructions internally).
                    EmitUpdateControl(unchecked((int)~FlagBits.Df), 0);
                    break;

                case JitKind.Std:
                    EmitUpdateControl(-1, (int)FlagBits.Df);
                    break;

                case JitKind.MovLoad:
                    EmitLoad(plan, index);
                    break;

                case JitKind.MovStore:
                case JitKind.MovStoreImm:
                    EmitStore(plan, index);
                    break;

                case JitKind.FpuLoadReal:
                    EmitFpuMemoryLoad(plan, index, RawHelpers.FpuPushSingleAddress);
                    break;

                case JitKind.FpuStoreReal:
                case JitKind.FpuStoreRealPop:
                    EmitFpuMemoryStore(plan, index, pop: plan.Kind == JitKind.FpuStoreRealPop);
                    break;

                case JitKind.FpuLoadInteger:
                    EmitFpuMemoryLoad(plan, index, RawHelpers.FpuPushIntegerAddress);
                    break;

                case JitKind.FpuStoreInteger:
                case JitKind.FpuStoreIntegerPop:
                    EmitFpuIntegerMemoryStore(plan, index, pop: plan.Kind == JitKind.FpuStoreIntegerPop);
                    break;

                case JitKind.FpuLoadRegister:
                case JitKind.FpuStoreRegister:
                case JitKind.FpuStoreRegisterPop:
                    EmitFpuRegister(plan);
                    break;

                case JitKind.FpuArithReg:
                case JitKind.FpuArithPop:
                case JitKind.FpuArithMem:
                    EmitFpuArithmetic(plan, index);
                    break;

                case JitKind.FpuExchange:
                    EmitFpuExchange(plan);
                    break;

                case JitKind.FpuPushOne:
                    EmitParkedHelper(
                        RawHelpers.FpuPushOneAddress,
                        () => _a.mov(R64(Register.RCX), R64(Register.R10)));
                    _flagsLive = false;
                    break;

                case JitKind.FpuChangeSign:
                    EmitParkedHelper(
                        RawHelpers.FpuChangeSignAddress,
                        () => _a.mov(R64(Register.RCX), R64(Register.R10)));
                    _flagsLive = false;
                    break;

                case JitKind.FpuCompareReg:
                    EmitFpuCompareReg(plan);
                    break;

                case JitKind.FpuCompareMem:
                    EmitFpuCompareMem(plan, index);
                    break;

                case JitKind.PushM:
                    EmitPushMemory(plan, index);
                    break;

                case JitKind.Cdq:
                    _a.cdq();
                    break;

                case JitKind.Setcc:
                    EmitSetcc(plan);
                    break;

                case JitKind.JmpMem:
                    EmitJmpMem(plan, index);
                    return;

                case JitKind.CallMem:
                    EmitCallMem(plan, eip, index);
                    return;

                case JitKind.SseArithReg:
                case JitKind.SseArithMem:
                    EmitSseArithmetic(plan, index);
                    break;

                case JitKind.SseUnaryReg:
                    EmitSseUnary(plan);
                    break;

                case JitKind.SseMovssReg:
                case JitKind.SseMovssMem:
                case JitKind.SseMovssStore:
                    EmitSseMovss(plan, index);
                    break;

                case JitKind.SseCvtReg:
                case JitKind.SseCvtMem:
                    EmitSseCvt(plan, index);
                    break;

                case JitKind.NegR:
                    EmitEnsureHostFlags();
                    if (plan.Size == 8)
                    {
                        _a.neg(R8(plan.RegIndex));
                    }
                    else if (plan.Size == 16)
                    {
                        _a.neg(R16(plan.RegIndex));
                    }
                    else
                    {
                        _a.neg(R32(Gpr32[plan.RegIndex]));
                    }

                    _flagsLive = true;
                    break;

                case JitKind.NotR:
                    if (plan.Size == 8)
                    {
                        _a.not(R8(plan.RegIndex));
                    }
                    else if (plan.Size == 16)
                    {
                        _a.not(R16(plan.RegIndex));
                    }
                    else
                    {
                        _a.not(R32(Gpr32[plan.RegIndex]));
                    }

                    break;

                case JitKind.Bswap:
                    _a.bswap(R32(Gpr32[plan.RegIndex]));
                    break;

                case JitKind.XchgRR:
                    if (plan.RegIndex != plan.SecondRegIndex)
                    {
                        _a.xchg(R32(Gpr32[plan.RegIndex]), R32(Gpr32[plan.SecondRegIndex]));
                    }

                    break;

                case JitKind.Leave:
                    EmitLeave(index);
                    break;

                case JitKind.MmxLoad:
                    EmitMmxLoad(plan, index);
                    break;

                case JitKind.MmxStore:
                    EmitMmxStore(plan, index);
                    break;

                case JitKind.AluRM:
                    EmitAluMemorySource(plan);
                    break;

                case JitKind.AluMR:
                case JitKind.AluMI:
                    EmitAluMemoryDestination(plan, index);
                    break;

                case JitKind.TestMI:
                    EmitTestMemory(plan, index);
                    break;

                case JitKind.IncM:
                case JitKind.DecM:
                    EmitIncDecMemory(plan, index);
                    break;

                case JitKind.JmpRel:
                    EmitPublishAccounting(
                        _membersDone + 1,
                        _membersDone + 1,
                        unchecked(eip + (uint)plan.Length + plan.Immediate));
                    // Normalize the frame flags on this exit edge (the
                    // epilogue does not re-materialize).
                    EmitMaterializeFlags();
                    _a.jmp(_exit);
                    return;

                case JitKind.CallRel:
                    EmitCallRel(plan, eip, index);
                    return;

                case JitKind.RetNear:
                    EmitRet(plan, index);
                    return;

                case JitKind.PushR:
                case JitKind.PushI:
                    EmitPush(plan, index);
                    break;

                case JitKind.PopR:
                    EmitPop(plan, index);
                    break;

                case JitKind.JccRel:
                    EmitJcc(plan, eip);
                    return;

                default:
                    EmitHelperMember(index);
                    if (plan.IsTerminator)
                    {
                        // The handler owns the exit EIP (null keeps whatever
                        // EmitReloadAfterHelper published).
                        EmitPublishAccounting(_membersDone + 1, _membersDone + 1, null);
                        EmitMaterializeFlags();
                        _a.jmp(_exit);
                        return;
                    }

                    break;
            }

            // Fast-mode post-member IRQ poll: STI (and POPF/string/port
            // members) can leave the block with interrupts newly enabled, and
            // the interpreter would deliver a pending vector at the very next
            // instruction boundary — the per-block entry poll alone starves
            // that window (the §193 kernel-idle shape). Helper members
            // advance EIP themselves; a native STI takes its boundary advance
            // on the pending path.
            if (plan.Kind == JitKind.Sti || plan.PollAfter)
            {
                EmitPostPoll(plan.Length, advanceEip: !_memberHelper);
            }
        }

        private void EmitMovReg(in JitMemberPlan plan)
        {
            switch (plan.Size)
            {
                case 8:
                    _a.mov(R8(plan.RegIndex), R8(plan.SecondRegIndex));
                    break;
                case 16:
                    _a.mov(R16(plan.RegIndex), R16(plan.SecondRegIndex));
                    break;
                default:
                    _a.mov(R32(Gpr32[plan.RegIndex]), R32(Gpr32[plan.SecondRegIndex]));
                    break;
            }
        }

        private void EmitMovImmediate(in JitMemberPlan plan)
        {
            int immediate = unchecked((int)plan.Immediate);
            switch (plan.Size)
            {
                case 8:
                    _a.mov(R8(plan.RegIndex), (byte)immediate);
                    break;
                case 16:
                    _a.mov(R16(plan.RegIndex), (ushort)immediate);
                    break;
                default:
                    _a.mov(R32(Gpr32[plan.RegIndex]), immediate);
                    break;
            }
        }

        private void EmitAluReg(in JitMemberPlan plan)
        {
            if (plan.AluOp is JitAluOp.Adc or JitAluOp.Sbb)
            {
                EmitEnsureHostFlags();
            }

            switch (plan.Size)
            {
                case 8:
                    EmitAlu8(R8(plan.RegIndex), R8(plan.SecondRegIndex), plan.AluOp);
                    break;
                case 16:
                    EmitAlu16(R16(plan.RegIndex), R16(plan.SecondRegIndex), plan.AluOp);
                    break;
                default:
                    EmitAlu32(R32(Gpr32[plan.RegIndex]), R32(Gpr32[plan.SecondRegIndex]), plan.AluOp);
                    break;
            }

            _flagsLive = true;
        }

        private void EmitAluImmediate(in JitMemberPlan plan)
        {
            if (plan.AluOp is JitAluOp.Adc or JitAluOp.Sbb)
            {
                EmitEnsureHostFlags();
            }

            int immediate = unchecked((int)plan.Immediate);
            switch (plan.Size)
            {
                case 8:
                    EmitAluImm8(R8(plan.RegIndex), (byte)immediate, plan.AluOp);
                    break;
                case 16:
                    EmitAluImm16(R16(plan.RegIndex), (ushort)immediate, plan.AluOp);
                    break;
                default:
                    EmitAluImm32(R32(Gpr32[plan.RegIndex]), immediate, plan.AluOp);
                    break;
            }

            _flagsLive = true;
        }

        private void EmitAlu8(AssemblerRegister8 dst, AssemblerRegister8 src, JitAluOp op)
        {
            switch (op)
            {
                case JitAluOp.Add: _a.add(dst, src); break;
                case JitAluOp.Sub: _a.sub(dst, src); break;
                case JitAluOp.And: _a.and(dst, src); break;
                case JitAluOp.Or: _a.or(dst, src); break;
                case JitAluOp.Xor: _a.xor(dst, src); break;
                case JitAluOp.Cmp: _a.cmp(dst, src); break;
                case JitAluOp.Adc: _a.adc(dst, src); break;
                case JitAluOp.Sbb: _a.sbb(dst, src); break;
                default: throw new InvalidOperationException("raw: unknown ALU op");
            }
        }

        private void EmitAlu16(AssemblerRegister16 dst, AssemblerRegister16 src, JitAluOp op)
        {
            switch (op)
            {
                case JitAluOp.Add: _a.add(dst, src); break;
                case JitAluOp.Sub: _a.sub(dst, src); break;
                case JitAluOp.And: _a.and(dst, src); break;
                case JitAluOp.Or: _a.or(dst, src); break;
                case JitAluOp.Xor: _a.xor(dst, src); break;
                case JitAluOp.Cmp: _a.cmp(dst, src); break;
                case JitAluOp.Adc: _a.adc(dst, src); break;
                case JitAluOp.Sbb: _a.sbb(dst, src); break;
                default: throw new InvalidOperationException("raw: unknown ALU op");
            }
        }

        private void EmitAlu32(AssemblerRegister32 dst, AssemblerRegister32 src, JitAluOp op)
        {
            switch (op)
            {
                case JitAluOp.Add: _a.add(dst, src); break;
                case JitAluOp.Sub: _a.sub(dst, src); break;
                case JitAluOp.And: _a.and(dst, src); break;
                case JitAluOp.Or: _a.or(dst, src); break;
                case JitAluOp.Xor: _a.xor(dst, src); break;
                case JitAluOp.Cmp: _a.cmp(dst, src); break;
                case JitAluOp.Adc: _a.adc(dst, src); break;
                case JitAluOp.Sbb: _a.sbb(dst, src); break;
                default: throw new InvalidOperationException("raw: unknown ALU op");
            }
        }

        private void EmitAluImm8(AssemblerRegister8 dst, byte immediate, JitAluOp op)
        {
            switch (op)
            {
                case JitAluOp.Add: _a.add(dst, immediate); break;
                case JitAluOp.Sub: _a.sub(dst, immediate); break;
                case JitAluOp.And: _a.and(dst, immediate); break;
                case JitAluOp.Or: _a.or(dst, immediate); break;
                case JitAluOp.Xor: _a.xor(dst, immediate); break;
                case JitAluOp.Cmp: _a.cmp(dst, immediate); break;
                case JitAluOp.Adc: _a.adc(dst, immediate); break;
                case JitAluOp.Sbb: _a.sbb(dst, immediate); break;
                default: throw new InvalidOperationException("raw: unknown ALU op");
            }
        }

        private void EmitAluImm16(AssemblerRegister16 dst, ushort immediate, JitAluOp op)
        {
            switch (op)
            {
                case JitAluOp.Add: _a.add(dst, immediate); break;
                case JitAluOp.Sub: _a.sub(dst, immediate); break;
                case JitAluOp.And: _a.and(dst, immediate); break;
                case JitAluOp.Or: _a.or(dst, immediate); break;
                case JitAluOp.Xor: _a.xor(dst, immediate); break;
                case JitAluOp.Cmp: _a.cmp(dst, immediate); break;
                case JitAluOp.Adc: _a.adc(dst, immediate); break;
                case JitAluOp.Sbb: _a.sbb(dst, immediate); break;
                default: throw new InvalidOperationException("raw: unknown ALU op");
            }
        }

        private void EmitAluImm32(AssemblerRegister32 dst, int immediate, JitAluOp op)
        {
            switch (op)
            {
                case JitAluOp.Add: _a.add(dst, immediate); break;
                case JitAluOp.Sub: _a.sub(dst, immediate); break;
                case JitAluOp.And: _a.and(dst, immediate); break;
                case JitAluOp.Or: _a.or(dst, immediate); break;
                case JitAluOp.Xor: _a.xor(dst, immediate); break;
                case JitAluOp.Cmp: _a.cmp(dst, immediate); break;
                case JitAluOp.Adc: _a.adc(dst, immediate); break;
                case JitAluOp.Sbb: _a.sbb(dst, immediate); break;
                default: throw new InvalidOperationException("raw: unknown ALU op");
            }
        }

        private void EmitTestReg(in JitMemberPlan plan)
        {
            switch (plan.Size)
            {
                case 8:
                    _a.test(R8(plan.RegIndex), R8(plan.SecondRegIndex));
                    break;
                case 16:
                    _a.test(R16(plan.RegIndex), R16(plan.SecondRegIndex));
                    break;
                default:
                    _a.test(R32(Gpr32[plan.RegIndex]), R32(Gpr32[plan.SecondRegIndex]));
                    break;
            }

            _flagsLive = true;
        }

        private void EmitTestImmediate(in JitMemberPlan plan)
        {
            int immediate = unchecked((int)plan.Immediate);
            switch (plan.Size)
            {
                case 8:
                    _a.test(R8(plan.RegIndex), (byte)immediate);
                    break;
                case 16:
                    _a.test(R16(plan.RegIndex), (ushort)immediate);
                    break;
                default:
                    _a.test(R32(Gpr32[plan.RegIndex]), immediate);
                    break;
            }

            _flagsLive = true;
        }

        private void EmitIncDecReg(in JitMemberPlan plan)
        {
            EmitEnsureHostFlags();
            bool inc = plan.Kind == JitKind.IncR;
            switch (plan.Size)
            {
                case 8:
                    if (inc) { _a.inc(R8(plan.RegIndex)); } else { _a.dec(R8(plan.RegIndex)); }
                    break;
                case 16:
                    if (inc) { _a.inc(R16(plan.RegIndex)); } else { _a.dec(R16(plan.RegIndex)); }
                    break;
                default:
                    if (inc) { _a.inc(R32(Gpr32[plan.RegIndex])); } else { _a.dec(R32(Gpr32[plan.RegIndex])); }
                    break;
            }

            _flagsLive = true;
        }

        private void EmitMovExtend(in JitMemberPlan plan, int index)
        {
            if (plan.HasMemory)
            {
                // Memory-source movzx/movsx: the same RAM probe as an integer
                // load, then the extend in a scratch byte/word register. The
                // slow path keeps the exact spec handler (fault precision is
                // unchanged; the handler owns the EIP advance).
                Assembler a = _a;
                EmitMaterializeFlags();
                EmitAddress(plan);
                Label slow = a.CreateLabel(string.Empty);
                Label done = a.CreateLabel(string.Empty);
                EmitProbe(plan.SourceSize / 8, isStore: false, slow, null);
                if (plan.SourceSize == 8)
                {
                    a.mov(new AssemblerRegister8(Register.R8L), RamAt());
                    if (plan.SignExtend)
                    {
                        a.movsx(R32(Gpr32[plan.RegIndex]), new AssemblerRegister8(Register.R8L));
                    }
                    else
                    {
                        a.movzx(R32(Gpr32[plan.RegIndex]), new AssemblerRegister8(Register.R8L));
                    }
                }
                else
                {
                    a.mov(new AssemblerRegister16(Register.R8W), RamAt());
                    if (plan.SignExtend)
                    {
                        a.movsx(R32(Gpr32[plan.RegIndex]), new AssemblerRegister16(Register.R8W));
                    }
                    else
                    {
                        a.movzx(R32(Gpr32[plan.RegIndex]), new AssemblerRegister16(Register.R8W));
                    }
                }

                // The fast path owns the EIP advance (the slow path's helper
                // advances it; EmitHelperMember set _memberHelper, so the
                // outer member epilogue skips it for both paths).
                a.jmp(done);

                a.Label(ref slow);
                EmitHelperMember(index);

                // F_EFLAGS is authoritative on both paths.
                _flagsLive = false;
                a.Label(ref done);
                return;
            }

            Register source = plan.SourceSize == 8 && plan.SecondRegIndex >= 4
                ? Gpr8High[plan.SecondRegIndex]
                : plan.SourceSize == 8 ? Gpr8[plan.SecondRegIndex] : Gpr16[plan.SecondRegIndex];
            if (IsHighByte(source))
            {
                EmitHelperMember(index);
                return;
            }

            if (plan.SourceSize == 8)
            {
                AssemblerRegister8 src8 = new(source);
                if (plan.Size == 16)
                {
                    if (plan.SignExtend) { _a.movsx(new AssemblerRegister16(Gpr16[plan.RegIndex]), src8); }
                    else { _a.movzx(new AssemblerRegister16(Gpr16[plan.RegIndex]), src8); }
                }
                else
                {
                    if (plan.SignExtend) { _a.movsx(R32(Gpr32[plan.RegIndex]), src8); }
                    else { _a.movzx(R32(Gpr32[plan.RegIndex]), src8); }
                }
            }
            else
            {
                AssemblerRegister16 src16 = new(source);
                if (plan.SignExtend) { _a.movsx(R32(Gpr32[plan.RegIndex]), src16); }
                else { _a.movzx(R32(Gpr32[plan.RegIndex]), src16); }
            }
        }

        /// <summary>
        /// <c>push r32/imm32</c>: the IL runner's exact push (SS base + ESP-4,
        /// 4-byte store), with the stack write behind the same inline probe.
        /// Any probe miss runs the exact spec handler; ESP only moves on the
        /// fast path after the store succeeded, so a faulting push leaves the
        /// architectural state exactly at the member's boundary.
        /// </summary>
        private void EmitPush(in JitMemberPlan plan, int index)
        {
            Assembler a = _a;
            EmitMaterializeFlags();
            a.mov(R32(Register.R9D), R32(Gpr32[(int)Gpr.Esp]));
            a.sub(R32(Register.R9D), 4);
            a.add(R32(Register.R9D), Field(Register.R11, OffSegments + ((int)SegmentRegister.Ss * 4)));

            Label slow = a.CreateLabel(string.Empty);
            Label done = a.CreateLabel(string.Empty);
            EmitProbe(4, isStore: true, slow, null);
            if (plan.Kind == JitKind.PushR)
            {
                a.mov(RamAt(), R32(Gpr32[plan.RegIndex]));
            }
            else
            {
                // Iced cannot infer an immediate store's width from the
                // memory operand; stage it through a scratch register.
                a.mov(R32(Register.R8D), unchecked((int)plan.Immediate));
                a.mov(RamAt(), R32(Register.R8D));
            }

            // ESP is untouched by the probe; the decrement is independent of
            // the SS base. lea keeps every host-flag consumer irrelevant here
            // (F_EFLAGS is authoritative after the materialize above).
            a.lea(R32(Gpr32[(int)Gpr.Esp]), MemOp(Gpr64[(int)Gpr.Esp], null, 1, -4));
            a.jmp(done);

            a.Label(ref slow);
            EmitHelperMember(index);

            _flagsLive = false;
            a.Label(ref done);
        }

        /// <summary>
        /// <c>pop r32</c>: probe-read the 4-byte stack slot, advance ESP, then
        /// write the destination (the order matters for <c>pop esp</c>: the
        /// loaded value wins, matching the interpreter).
        /// </summary>
        private void EmitPop(in JitMemberPlan plan, int index)
        {
            Assembler a = _a;
            EmitMaterializeFlags();
            a.mov(R32(Register.R9D), R32(Gpr32[(int)Gpr.Esp]));
            a.add(R32(Register.R9D), Field(Register.R11, OffSegments + ((int)SegmentRegister.Ss * 4)));

            Label slow = a.CreateLabel(string.Empty);
            Label done = a.CreateLabel(string.Empty);
            EmitProbe(4, isStore: false, slow, null);
            a.mov(R32(Register.R8D), RamAt());
            a.lea(R32(Gpr32[(int)Gpr.Esp]), MemOp(Gpr64[(int)Gpr.Esp], null, 1, 4));
            a.mov(R32(Gpr32[plan.RegIndex]), R32(Register.R8D));
            a.jmp(done);

            a.Label(ref slow);
            EmitHelperMember(index);

            _flagsLive = false;
            a.Label(ref done);
        }

        /// <summary>
        /// <c>call rel32</c> (terminator): push the return address, then exit at
        /// the compile-time target. The slow path runs the exact handler. The
        /// block cannot chain (it stores), so the target is served by the next
        /// RunJitBlock iteration - still far cheaper than a full state sync.
        /// </summary>
        private void EmitCallRel(in JitMemberPlan plan, uint eip, int index)
        {
            Assembler a = _a;
            EmitMaterializeFlags();
            a.mov(R32(Register.R9D), R32(Gpr32[(int)Gpr.Esp]));
            a.sub(R32(Register.R9D), 4);
            a.add(R32(Register.R9D), Field(Register.R11, OffSegments + ((int)SegmentRegister.Ss * 4)));

            Label slow = a.CreateLabel(string.Empty);
            EmitProbe(4, isStore: true, slow, null);
            a.mov(R32(Register.R8D), unchecked((int)(eip + (uint)plan.Length)));
            a.mov(RamAt(), R32(Register.R8D));
            a.lea(R32(Gpr32[(int)Gpr.Esp]), MemOp(Gpr64[(int)Gpr.Esp], null, 1, -4));
            EmitPublishAccounting(
                _membersDone + 1,
                _membersDone + 1,
                unchecked(eip + (uint)plan.Length + plan.Immediate));
            EmitMaterializeFlags();
            a.jmp(_exit);

            a.Label(ref slow);
            EmitHelperMember(index);
            EmitPublishAccounting(_membersDone + 1, _membersDone + 1, null);
            EmitMaterializeFlags();
            a.jmp(_exit);
        }

        /// <summary>
        /// <c>ret [imm16]</c> (terminator): probe-read the return address, pop
        /// it (plus the immediate release), exit at the loaded EIP.
        /// </summary>
        private void EmitRet(in JitMemberPlan plan, int index)
        {
            Assembler a = _a;
            EmitMaterializeFlags();
            a.mov(R32(Register.R9D), R32(Gpr32[(int)Gpr.Esp]));
            a.add(R32(Register.R9D), Field(Register.R11, OffSegments + ((int)SegmentRegister.Ss * 4)));

            Label slow = a.CreateLabel(string.Empty);
            EmitProbe(4, isStore: false, slow, null);
            a.mov(R32(Register.R8D), RamAt());
            a.lea(R32(Gpr32[(int)Gpr.Esp]), MemOp(Gpr64[(int)Gpr.Esp], null, 1, 4 + unchecked((int)plan.Immediate)));
            a.mov(Slot(F_EIP), R32(Register.R8D));
            EmitPublishAccounting(_membersDone + 1, _membersDone + 1, null);
            EmitMaterializeFlags();
            a.jmp(_exit);

            a.Label(ref slow);
            EmitHelperMember(index);
            EmitPublishAccounting(_membersDone + 1, _membersDone + 1, null);
            EmitMaterializeFlags();
            a.jmp(_exit);
        }

        /// <summary>
        /// Calls a tiny unmanaged FPU/MMX entry with the CPU handle while the
        /// guest registers parked in caller-saved host registers are preserved.
        /// <paramref name="setArgs"/> runs after the park and may only read
        /// R8/R9 (scratch values prepared by the caller) and R10 (CPU); it must
        /// write the Win64 argument registers. <paramref name="resultReg"/>,
        /// when given, captures RAX before the parked registers are restored.
        /// </summary>
        private void EmitParkedHelper(ulong target, Action setArgs, Register? resultReg = null)
        {
            Assembler a = _a;
            // §239: an edge - the parked helpers are host-side state mutations
            // that cannot fault architecturally, but a captured host fault must
            // still report this member's boundary. R10 only; R8/R9 (argument
            // scratch prepared by the caller) survive.
            EmitPublishAccounting(_membersDone + 1, _membersDone, _memberEip);
            a.mov(R64(Register.R10), Slot(F_CPU));
            a.mov(R64(Register.RSI), target);
            a.push(R64(Register.RAX));
            a.push(R64(Register.RCX));
            a.push(R64(Register.RDX));
            a.push(R64(Register.RBX));
            a.sub(R64(Register.RSP), 0x20);
            setArgs();
            a.call(R64(Register.RSI));
            a.add(R64(Register.RSP), 0x20);
            if (resultReg is { } result)
            {
                a.mov(R64(result), R64(Register.RAX));
            }

            a.pop(R64(Register.RBX));
            a.pop(R64(Register.RDX));
            a.pop(R64(Register.RCX));
            a.pop(R64(Register.RAX));
            EmitFaultCheck();
        }

        /// <summary>
        /// <c>Fld m32</c> / <c>Fild m32</c>: probe the 4-byte source, then push
        /// it through the shared FPU conversion helper. The slow path is the
        /// exact spec handler; F_EFLAGS is authoritative on both paths.
        /// </summary>
        private void EmitFpuMemoryLoad(in JitMemberPlan plan, int index, ulong pushAddress)
        {
            Assembler a = _a;
            EmitMaterializeFlags();
            EmitAddress(plan);
            Label slow = a.CreateLabel(string.Empty);
            Label done = a.CreateLabel(string.Empty);
            EmitProbe(4, isStore: false, slow, null);
            a.mov(R32(Register.R9D), RamAt());
            EmitParkedHelper(pushAddress, () =>
            {
                a.mov(R64(Register.RCX), R64(Register.R10));
                a.mov(R32(Register.EDX), R32(Register.R9D));
            });
            a.jmp(done);

            a.Label(ref slow);
            EmitHelperMember(index);

            _flagsLive = false;
            a.Label(ref done);
        }

        /// <summary><c>Fst/Fstp m32</c>: convert ST(0) first (exact conversion), then probe-store the bits.</summary>
        private void EmitFpuMemoryStore(in JitMemberPlan plan, int index, bool pop)
        {
            Assembler a = _a;
            EmitMaterializeFlags();
            EmitParkedHelper(
                RawHelpers.FpuSingleBitsAddress,
                () => a.mov(R64(Register.RCX), R64(Register.R10)),
                resultReg: Register.R8);
            a.mov(Slot(F_FPU), R32(Register.R8D));

            EmitAddress(plan);
            Label slow = a.CreateLabel(string.Empty);
            Label done = a.CreateLabel(string.Empty);
            EmitProbe(4, isStore: true, slow, null);
            a.mov(R32(Register.R8D), Slot(F_FPU));
            a.mov(RamAt(), R32(Register.R8D));
            if (pop)
            {
                EmitParkedHelper(
                    RawHelpers.FpuPopAddress,
                    () => a.mov(R64(Register.RCX), R64(Register.R10)));
            }

            a.jmp(done);

            a.Label(ref slow);
            EmitHelperMember(index);

            _flagsLive = false;
            a.Label(ref done);
        }

        /// <summary><c>Fist/Fistp m32</c>: integer conversion first (exact rounding/exception rule), then the probe-store.</summary>
        private void EmitFpuIntegerMemoryStore(in JitMemberPlan plan, int index, bool pop)
        {
            Assembler a = _a;
            EmitMaterializeFlags();
            EmitParkedHelper(
                RawHelpers.FpuIntegerBitsAddress,
                () => a.mov(R64(Register.RCX), R64(Register.R10)),
                resultReg: Register.R8);
            a.mov(Slot(F_FPU), R32(Register.R8D));

            EmitAddress(plan);
            Label slow = a.CreateLabel(string.Empty);
            Label done = a.CreateLabel(string.Empty);
            EmitProbe(4, isStore: true, slow, null);
            a.mov(R32(Register.R8D), Slot(F_FPU));
            a.mov(RamAt(), R32(Register.R8D));
            if (pop)
            {
                EmitParkedHelper(
                    RawHelpers.FpuPopAddress,
                    () => a.mov(R64(Register.RCX), R64(Register.R10)));
            }

            a.jmp(done);

            a.Label(ref slow);
            EmitHelperMember(index);

            _flagsLive = false;
            a.Label(ref done);
        }

        /// <summary><c>Fld/Fst/Fstp st(i)</c>: one tiny FPU register helper call, no memory probe.</summary>
        private void EmitFpuRegister(in JitMemberPlan plan)
        {
            Assembler a = _a;
            EmitMaterializeFlags();
            ulong target = plan.Kind switch
            {
                JitKind.FpuLoadRegister => RawHelpers.FpuPushRegisterAddress,
                JitKind.FpuStoreRegister => RawHelpers.FpuStoreRegisterAddress,
                _ => RawHelpers.FpuStoreRegisterPopAddress,
            };
            a.mov(R32(Register.R9D), plan.RegIndex);
            EmitParkedHelper(target, () =>
            {
                a.mov(R64(Register.RCX), R64(Register.R10));
                a.mov(R32(Register.EDX), R32(Register.R9D));
            });
            _flagsLive = false;
        }

        /// <summary>
        /// x87 arithmetic: register-register and 32/64-bit memory forms. The
        /// operation descriptor packs the operation, the destination ST index
        /// and the memory width; the arithmetic itself stays in the shared
        /// managed Fpu80 implementation.
        /// </summary>
        private void EmitFpuArithmetic(in JitMemberPlan plan, int index)
        {
            Assembler a = _a;
            EmitMaterializeFlags();
            uint descriptor = FpuArithDescriptor(plan);
            if (plan.Kind == JitKind.FpuArithReg)
            {
                a.mov(R32(Register.R9D), unchecked((int)descriptor));
                a.mov(R32(Register.R8D), plan.SecondRegIndex);
                EmitParkedHelper(RawHelpers.FpuArithRegAddress, () =>
                {
                    a.mov(R64(Register.RCX), R64(Register.R10));
                    a.mov(R32(Register.EDX), R32(Register.R9D));
                });
                _flagsLive = false;
                return;
            }

            if (plan.Kind == JitKind.FpuArithPop)
            {
                a.mov(R32(Register.R9D), unchecked((int)descriptor));
                EmitParkedHelper(RawHelpers.FpuArithPopAddress, () =>
                {
                    a.mov(R64(Register.RCX), R64(Register.R10));
                    a.mov(R32(Register.EDX), R32(Register.R9D));
                });
                _flagsLive = false;
                return;
            }

            EmitAddress(plan);
            Label slow = a.CreateLabel(string.Empty);
            Label done = a.CreateLabel(string.Empty);
            EmitProbe(plan.SourceSize / 8, isStore: false, slow, null);
            if (plan.SourceSize == 64)
            {
                a.mov(R64(Register.R8), RamAt());
            }
            else if (plan.SourceSize == 16)
            {
                a.mov(new AssemblerRegister16(Register.R8W), RamAt());
            }
            else
            {
                a.mov(R32(Register.R8D), RamAt());
            }

            a.mov(R32(Register.R9D), unchecked((int)descriptor));
            EmitParkedHelper(RawHelpers.FpuArithMemAddress, () =>
            {
                a.mov(R64(Register.RCX), R64(Register.R10));
                a.mov(R32(Register.EDX), R32(Register.R9D));
            });
            a.jmp(done);

            a.Label(ref slow);
            EmitHelperMember(index);

            _flagsLive = false;
            a.Label(ref done);
        }

        /// <summary>Packs the x87 operation, destination ST index and source kind/width.</summary>
        private static uint FpuArithDescriptor(in JitMemberPlan plan)
        {
            int code = plan.Mnemonic switch
            {
                Mnemonic.Fadd or Mnemonic.Faddp or Mnemonic.Fiadd => 0,
                Mnemonic.Fmul or Mnemonic.Fmulp or Mnemonic.Fimul => 1,
                Mnemonic.Fsub or Mnemonic.Fsubp or Mnemonic.Fisub => 2,
                Mnemonic.Fsubr or Mnemonic.Fsubrp or Mnemonic.Fisubr => 3,
                Mnemonic.Fdiv or Mnemonic.Fdivp or Mnemonic.Fidiv => 4,
                _ => 5,
            };
            bool integer = plan.Mnemonic is
                Mnemonic.Fiadd or Mnemonic.Fimul or Mnemonic.Fisub
                or Mnemonic.Fisubr or Mnemonic.Fidiv or Mnemonic.Fidivr;
            uint descriptor = (uint)code | ((uint)plan.RegIndex << 8);
            if (integer)
            {
                descriptor |= 1u << 17;
                if (plan.SourceSize == 16)
                {
                    descriptor |= 1u << 16;
                }
            }
            else if (plan.Kind == JitKind.FpuArithMem && plan.SourceSize == 64)
            {
                descriptor |= 1u << 16;
            }

            return descriptor;
        }

        /// <summary><c>Fxch st(i)</c>: one exchange helper call.</summary>
        private void EmitFpuExchange(in JitMemberPlan plan)
        {
            Assembler a = _a;
            EmitMaterializeFlags();
            a.mov(R32(Register.R9D), plan.RegIndex);
            EmitParkedHelper(RawHelpers.FpuExchangeAddress, () =>
            {
                a.mov(R64(Register.RCX), R64(Register.R10));
                a.mov(R32(Register.EDX), R32(Register.R9D));
            });
            _flagsLive = false;
        }

        /// <summary>Scalar SSE arithmetic: register and m32 forms.</summary>
        private void EmitSseArithmetic(in JitMemberPlan plan, int index)
        {
            Assembler a = _a;
            EmitMaterializeFlags();
            uint descriptor = (plan.Immediate & 0xFF) | ((uint)plan.RegIndex << 8);
            if (plan.Kind == JitKind.SseArithReg)
            {
                a.mov(R32(Register.R9D), unchecked((int)descriptor));
                a.mov(R32(Register.R8D), plan.SecondRegIndex);
                EmitParkedHelper(RawHelpers.SseArithRegAddress, () =>
                {
                    a.mov(R64(Register.RCX), R64(Register.R10));
                    a.mov(R32(Register.EDX), R32(Register.R9D));
                });
                _flagsLive = false;
                return;
            }

            EmitAddress(plan);
            Label slow = a.CreateLabel(string.Empty);
            Label done = a.CreateLabel(string.Empty);
            EmitProbe(4, isStore: false, slow, null);
            a.mov(R32(Register.R8D), RamAt());
            a.mov(R32(Register.R9D), unchecked((int)descriptor));
            EmitParkedHelper(RawHelpers.SseArithMemAddress, () =>
            {
                a.mov(R64(Register.RCX), R64(Register.R10));
                a.mov(R32(Register.EDX), R32(Register.R9D));
            });
            a.jmp(done);

            a.Label(ref slow);
            EmitHelperMember(index);

            _flagsLive = false;
            a.Label(ref done);
        }

        /// <summary>Scalar SSE unary (<c>Sqrtss/Rsqrtss/Rcpss</c>), register source.</summary>
        private void EmitSseUnary(in JitMemberPlan plan)
        {
            Assembler a = _a;
            EmitMaterializeFlags();
            uint descriptor = (plan.Immediate & 0xFF) | ((uint)plan.RegIndex << 8);
            a.mov(R32(Register.R9D), unchecked((int)descriptor));
            a.mov(R32(Register.R8D), plan.SecondRegIndex);
            EmitParkedHelper(RawHelpers.SseUnaryRegAddress, () =>
            {
                a.mov(R64(Register.RCX), R64(Register.R10));
                a.mov(R32(Register.EDX), R32(Register.R9D));
            });
            _flagsLive = false;
        }

        /// <summary><c>Movss</c> register/load/store forms (element 0 only).</summary>
        private void EmitSseMovss(in JitMemberPlan plan, int index)
        {
            Assembler a = _a;
            EmitMaterializeFlags();
            if (plan.Kind == JitKind.SseMovssReg)
            {
                a.mov(R32(Register.R9D), plan.RegIndex);
                a.mov(R32(Register.R8D), plan.SecondRegIndex);
                EmitParkedHelper(RawHelpers.SseMovssRegAddress, () =>
                {
                    a.mov(R64(Register.RCX), R64(Register.R10));
                    a.mov(R32(Register.EDX), R32(Register.R9D));
                });
                _flagsLive = false;
                return;
            }

            if (plan.Kind == JitKind.SseMovssMem)
            {
                EmitAddress(plan);
                Label slow = a.CreateLabel(string.Empty);
                Label done = a.CreateLabel(string.Empty);
                EmitProbe(4, isStore: false, slow, null);
                a.mov(R32(Register.R8D), RamAt());
                a.mov(R32(Register.R9D), plan.RegIndex);
                EmitParkedHelper(RawHelpers.SseMovssMemAddress, () =>
                {
                    a.mov(R64(Register.RCX), R64(Register.R10));
                    a.mov(R32(Register.EDX), R32(Register.R9D));
                });
                a.jmp(done);

                a.Label(ref slow);
                EmitHelperMember(index);

                _flagsLive = false;
                a.Label(ref done);
                return;
            }

            a.mov(R32(Register.R9D), plan.RegIndex);
            EmitParkedHelper(
                RawHelpers.SseMovssStoreAddress,
                () =>
                {
                    a.mov(R64(Register.RCX), R64(Register.R10));
                    a.mov(R32(Register.EDX), R32(Register.R9D));
                },
                resultReg: Register.R8);
            a.mov(Slot(F_FPU), R32(Register.R8D));

            EmitAddress(plan);
            Label slowStore = a.CreateLabel(string.Empty);
            Label doneStore = a.CreateLabel(string.Empty);
            EmitProbe(4, isStore: true, slowStore, null);
            a.mov(R32(Register.R8D), Slot(F_FPU));
            a.mov(RamAt(), R32(Register.R8D));
            a.jmp(doneStore);

            a.Label(ref slowStore);
            EmitHelperMember(index);

            _flagsLive = false;
            a.Label(ref doneStore);
        }

        /// <summary><c>Cvttss2Si r32, xmm/m32</c>: tiny conversion helper, result into the guest GPR.</summary>
        private void EmitSseCvt(in JitMemberPlan plan, int index)
        {
            Assembler a = _a;
            EmitMaterializeFlags();
            if (plan.Kind == JitKind.SseCvtReg)
            {
                a.mov(R32(Register.R9D), plan.SecondRegIndex);
                EmitParkedHelper(
                    RawHelpers.SseCvtRegAddress,
                    () =>
                    {
                        a.mov(R64(Register.RCX), R64(Register.R10));
                        a.mov(R32(Register.EDX), R32(Register.R9D));
                    },
                    resultReg: Register.R8);
                a.mov(R32(Gpr32[plan.RegIndex]), R32(Register.R8D));
                _flagsLive = false;
                return;
            }

            EmitAddress(plan);
            Label slow = a.CreateLabel(string.Empty);
            Label done = a.CreateLabel(string.Empty);
            EmitProbe(4, isStore: false, slow, null);
            a.mov(R32(Register.R9D), RamAt());
            EmitParkedHelper(
                RawHelpers.SseCvtAddress,
                () =>
                {
                    a.mov(R64(Register.RCX), R64(Register.R10));
                    a.mov(R32(Register.EDX), R32(Register.R9D));
                },
                resultReg: Register.R8);
            a.mov(R32(Gpr32[plan.RegIndex]), R32(Register.R8D));
            a.jmp(done);

            a.Label(ref slow);
            EmitHelperMember(index);

            _flagsLive = false;
            a.Label(ref done);
        }

        /// <summary><c>Leave</c>: ESP = EBP + 4; EBP = [SS:EBP].</summary>
        private void EmitLeave(int index)
        {
            Assembler a = _a;
            EmitMaterializeFlags();
            a.mov(R32(Register.R9D), R32(Gpr32[(int)Gpr.Ebp]));
            a.add(R32(Register.R9D), Field(Register.R11, OffSegments + ((int)SegmentRegister.Ss * 4)));

            Label slow = a.CreateLabel(string.Empty);
            Label done = a.CreateLabel(string.Empty);
            EmitProbe(4, isStore: false, slow, null);
            a.mov(R32(Register.R8D), RamAt());
            a.lea(R32(Gpr32[(int)Gpr.Esp]), MemOp(Gpr64[(int)Gpr.Ebp], null, 1, 4));
            a.mov(R32(Gpr32[(int)Gpr.Ebp]), R32(Register.R8D));
            a.jmp(done);

            a.Label(ref slow);
            EmitHelperMember(index);

            _flagsLive = false;
            a.Label(ref done);
        }

        /// <summary><c>Fcom/Fcomp st(i)</c>.</summary>
        private void EmitFpuCompareReg(in JitMemberPlan plan)
        {
            Assembler a = _a;
            EmitMaterializeFlags();
            uint descriptor = plan.Immediate & 3u;
            a.mov(R32(Register.R9D), unchecked((int)descriptor));
            a.mov(R32(Register.R8D), plan.RegIndex);
            EmitParkedHelper(RawHelpers.FpuCompareRegAddress, () =>
            {
                a.mov(R64(Register.RCX), R64(Register.R10));
                a.mov(R32(Register.EDX), R32(Register.R9D));
            });
            _flagsLive = false;
        }

        /// <summary><c>Fcom/Fcomp m32/m64</c>.</summary>
        private void EmitFpuCompareMem(in JitMemberPlan plan, int index)
        {
            Assembler a = _a;
            EmitMaterializeFlags();
            EmitAddress(plan);
            Label slow = a.CreateLabel(string.Empty);
            Label done = a.CreateLabel(string.Empty);
            EmitProbe(plan.SourceSize / 8, isStore: false, slow, null);
            if (plan.SourceSize == 64)
            {
                a.mov(R64(Register.R8), RamAt());
            }
            else
            {
                a.mov(R32(Register.R8D), RamAt());
            }

            uint descriptor = plan.Immediate & 5u;
            if (plan.SourceSize == 64)
            {
                descriptor |= 2u;
            }

            a.mov(R32(Register.R9D), unchecked((int)descriptor));
            EmitParkedHelper(RawHelpers.FpuCompareMemAddress, () =>
            {
                a.mov(R64(Register.RCX), R64(Register.R10));
                a.mov(R32(Register.EDX), R32(Register.R9D));
            });
            a.jmp(done);

            a.Label(ref slow);
            EmitHelperMember(index);

            _flagsLive = false;
            a.Label(ref done);
        }

        /// <summary><c>push [mem]</c>: probe-read the 4-byte source, then the exact push store.</summary>
        private void EmitPushMemory(in JitMemberPlan plan, int index)
        {
            Assembler a = _a;
            EmitMaterializeFlags();
            EmitAddress(plan);
            Label slow = a.CreateLabel(string.Empty);
            Label done = a.CreateLabel(string.Empty);
            EmitProbe(4, isStore: false, slow, null);
            a.mov(R32(Register.R8D), RamAt());
            a.mov(Slot(F_VALUE), R32(Register.R8D));

            a.mov(R32(Register.R9D), R32(Gpr32[(int)Gpr.Esp]));
            a.sub(R32(Register.R9D), 4);
            a.add(R32(Register.R9D), Field(Register.R11, OffSegments + ((int)SegmentRegister.Ss * 4)));
            EmitProbe(4, isStore: true, slow, null);
            a.mov(R32(Register.R8D), Slot(F_VALUE));
            a.mov(RamAt(), R32(Register.R8D));
            a.lea(R32(Gpr32[(int)Gpr.Esp]), MemOp(Gpr64[(int)Gpr.Esp], null, 1, -4));
            a.jmp(done);

            a.Label(ref slow);
            EmitHelperMember(index);

            _flagsLive = false;
            a.Label(ref done);
        }

        /// <summary><c>Setcc r8</c> (high-byte destinations deposit into the host register).</summary>
        private void EmitSetcc(in JitMemberPlan plan)
        {
            Assembler a = _a;
            EmitEnsureHostFlags();
            AssemblerRegister8 scratch = new(Register.R8L);
            EmitSetccOp(a, plan.Condition, scratch);
            if (plan.RegIndex >= 4)
            {
                // AH/CH/DH/BH: deposit bits 8-15 of the host register.
                int host = plan.RegIndex - 4;
                a.movzx(R32(Register.R10D), scratch);
                a.shl(R32(Register.R10D), 8);
                a.and(R32(Gpr32[host]), unchecked((int)0xFFFF00FFu));
                a.or(R32(Gpr32[host]), R32(Register.R10D));
            }
            else
            {
                a.mov(new AssemblerRegister8(Gpr8[plan.RegIndex]), scratch);
            }

            _flagsLive = true;
        }

        private static void EmitSetccOp(Assembler a, ConditionCode condition, AssemblerRegister8 destination)
        {
            switch (condition)
            {
                case ConditionCode.Overflow: a.seto(destination); break;
                case ConditionCode.NotOverflow: a.setno(destination); break;
                case ConditionCode.Below: a.setb(destination); break;
                case ConditionCode.AboveOrEqual: a.setae(destination); break;
                case ConditionCode.Equal: a.sete(destination); break;
                case ConditionCode.NotEqual: a.setne(destination); break;
                case ConditionCode.BelowOrEqual: a.setbe(destination); break;
                case ConditionCode.Above: a.seta(destination); break;
                case ConditionCode.Sign: a.sets(destination); break;
                case ConditionCode.NotSign: a.setns(destination); break;
                case ConditionCode.Parity: a.setp(destination); break;
                case ConditionCode.NotParity: a.setnp(destination); break;
                case ConditionCode.Less: a.setl(destination); break;
                case ConditionCode.GreaterOrEqual: a.setge(destination); break;
                case ConditionCode.LessOrEqual: a.setle(destination); break;
                default: a.setg(destination); break;
            }
        }

        /// <summary><c>jmp [mem]</c> (terminator).</summary>
        private void EmitJmpMem(in JitMemberPlan plan, int index)
        {
            Assembler a = _a;
            EmitMaterializeFlags();
            EmitAddress(plan);
            Label slow = a.CreateLabel(string.Empty);
            EmitProbe(4, isStore: false, slow, null);
            a.mov(R32(Register.R8D), RamAt());
            a.mov(Slot(F_EIP), R32(Register.R8D));
            EmitPublishAccounting(_membersDone + 1, _membersDone + 1, null);
            EmitMaterializeFlags();
            a.jmp(_exit);

            a.Label(ref slow);
            EmitHelperMember(index);
            EmitPublishAccounting(_membersDone + 1, _membersDone + 1, null);
            EmitMaterializeFlags();
            a.jmp(_exit);
        }

        /// <summary><c>call [mem]</c> (terminator): read the target, push the return address, exit there.</summary>
        private void EmitCallMem(in JitMemberPlan plan, uint eip, int index)
        {
            Assembler a = _a;
            EmitMaterializeFlags();
            EmitAddress(plan);
            Label slow = a.CreateLabel(string.Empty);
            EmitProbe(4, isStore: false, slow, null);
            a.mov(R32(Register.R8D), RamAt());
            a.mov(Slot(F_VALUE), R32(Register.R8D));

            a.mov(R32(Register.R9D), R32(Gpr32[(int)Gpr.Esp]));
            a.sub(R32(Register.R9D), 4);
            a.add(R32(Register.R9D), Field(Register.R11, OffSegments + ((int)SegmentRegister.Ss * 4)));
            EmitProbe(4, isStore: true, slow, null);
            a.mov(R32(Register.R8D), unchecked((int)(eip + (uint)plan.Length)));
            a.mov(RamAt(), R32(Register.R8D));
            a.lea(R32(Gpr32[(int)Gpr.Esp]), MemOp(Gpr64[(int)Gpr.Esp], null, 1, -4));
            a.mov(R32(Register.R8D), Slot(F_VALUE));
            a.mov(Slot(F_EIP), R32(Register.R8D));
            EmitPublishAccounting(_membersDone + 1, _membersDone + 1, null);
            EmitMaterializeFlags();
            a.jmp(_exit);

            a.Label(ref slow);
            EmitHelperMember(index);
            EmitPublishAccounting(_membersDone + 1, _membersDone + 1, null);
            EmitMaterializeFlags();
            a.jmp(_exit);
        }

        /// <summary><c>movq mm, m64</c>: probe-load 8 bytes, then write the MMX register.</summary>
        private void EmitMmxLoad(in JitMemberPlan plan, int index)
        {
            Assembler a = _a;
            EmitMaterializeFlags();
            EmitAddress(plan);
            Label slow = a.CreateLabel(string.Empty);
            Label done = a.CreateLabel(string.Empty);
            EmitProbe(8, isStore: false, slow, null);
            a.mov(R64(Register.R9), RamAt());
            int mmxIndex = plan.RegIndex;
            EmitParkedHelper(RawHelpers.MmxWriteAddress, () =>
            {
                a.mov(R64(Register.RCX), R64(Register.R10));
                a.mov(R32(Register.EDX), mmxIndex);
                a.mov(R64(Register.R8), R64(Register.R9));
            });
            a.jmp(done);

            a.Label(ref slow);
            EmitHelperMember(index);

            _flagsLive = false;
            a.Label(ref done);
        }

        /// <summary><c>movq/movntq m64, mm</c>: read the MMX register, then the probe-store.</summary>
        private void EmitMmxStore(in JitMemberPlan plan, int index)
        {
            Assembler a = _a;
            EmitMaterializeFlags();
            int mmxIndex = plan.RegIndex;
            EmitParkedHelper(
                RawHelpers.MmxReadAddress,
                () =>
                {
                    a.mov(R64(Register.RCX), R64(Register.R10));
                    a.mov(R32(Register.EDX), mmxIndex);
                },
                resultReg: Register.R8);
            a.mov(Slot(F_VALUE64), R64(Register.R8));

            EmitAddress(plan);
            Label slow = a.CreateLabel(string.Empty);
            Label done = a.CreateLabel(string.Empty);
            EmitProbe(8, isStore: true, slow, null);
            a.mov(R64(Register.R8), Slot(F_VALUE64));
            a.mov(RamAt(), R64(Register.R8));
            a.jmp(done);

            a.Label(ref slow);
            EmitHelperMember(index);

            _flagsLive = false;
            a.Label(ref done);
        }

        private void EmitShift(in JitMemberPlan plan)
        {
            EmitEnsureHostFlags();
            bool rotate = plan.ShiftOp is JitShiftOp.Rol or JitShiftOp.Ror;
            byte count = (byte)(plan.Immediate & 0x1F);
            switch (plan.Size)
            {
                case 8:
                    if (rotate)
                    {
                        EmitRotate8(R8(plan.RegIndex), count, plan.ShiftOp);
                    }
                    else
                    {
                        EmitShift8(R8(plan.RegIndex), count, plan.ShiftOp);
                    }

                    break;
                case 16:
                    if (rotate)
                    {
                        EmitRotate16(R16(plan.RegIndex), count, plan.ShiftOp);
                    }
                    else
                    {
                        EmitShift16(R16(plan.RegIndex), count, plan.ShiftOp);
                    }

                    break;
                default:
                    if (rotate)
                    {
                        EmitRotate32(R32(Gpr32[plan.RegIndex]), count, plan.ShiftOp);
                    }
                    else
                    {
                        EmitShift32(R32(Gpr32[plan.RegIndex]), count, plan.ShiftOp);
                    }

                    break;
            }

            // ShiftOps clears OF for every count except 1; the host sets it for
            // any non-zero count, so the >1 cases clear it explicitly (the
            // C3-y flag contract keeps every arithmetic bit exact).
            if (plan.ShiftOp is JitShiftOp.Shl or JitShiftOp.Shr or JitShiftOp.Rol or JitShiftOp.Ror
                && count > 1)
            {
                EmitClearOverflowFlag();
            }

            _flagsLive = true;
        }

        /// <summary><c>shl/shr/sar/rol/ror reg, cl</c>: the host instruction is
        /// exact for every count except OF on count &gt; 1 (ShiftOps clears it).</summary>
        private void EmitShiftByCl(in JitMemberPlan plan)
        {
            EmitEnsureHostFlags();
            switch (plan.Size)
            {
                case 8:
                    EmitShiftCl8(R8(plan.RegIndex), plan.ShiftOp);
                    break;
                case 16:
                    EmitShiftCl16(R16(plan.RegIndex), plan.ShiftOp);
                    break;
                default:
                    EmitShiftCl32(R32(Gpr32[plan.RegIndex]), plan.ShiftOp);
                    break;
            }

            if (plan.ShiftOp is JitShiftOp.Shl or JitShiftOp.Shr or JitShiftOp.Rol or JitShiftOp.Ror)
            {
                EmitClearOverflowIfCountAboveOne();
            }

            _flagsLive = true;
        }

        /// <summary>Clears OF in the live host flags (the shift result is already there).</summary>
        private void EmitClearOverflowFlag()
        {
            Assembler a = _a;
            a.pushfq();
            a.mov(R32(Register.R10D), unchecked((int)~FlagBits.Of));
            a.and(MemOp(Register.RSP, null, 1, 0), R32(Register.R10D));
            a.popfq();
        }

        /// <summary>Clears OF in the saved shift flags only when the masked CL count exceeds one.</summary>
        private void EmitClearOverflowIfCountAboveOne()
        {
            Assembler a = _a;
            Label skip = a.CreateLabel(string.Empty);
            a.pushfq();
            a.mov(R32(Register.R10D), R32(Register.ECX));
            a.and(R32(Register.R10D), 0x1F);
            a.cmp(R32(Register.R10D), 1);
            a.jbe(skip);
            a.mov(R32(Register.R10D), unchecked((int)~FlagBits.Of));
            a.and(MemOp(Register.RSP, null, 1, 0), R32(Register.R10D));
            a.Label(ref skip);
            a.popfq();
        }

        private void EmitRotate8(AssemblerRegister8 dst, byte count, JitShiftOp op)
        {
            if (op == JitShiftOp.Rol) { _a.rol(dst, count); } else { _a.ror(dst, count); }
        }

        private void EmitRotate16(AssemblerRegister16 dst, byte count, JitShiftOp op)
        {
            if (op == JitShiftOp.Rol) { _a.rol(dst, count); } else { _a.ror(dst, count); }
        }

        private void EmitRotate32(AssemblerRegister32 dst, byte count, JitShiftOp op)
        {
            if (op == JitShiftOp.Rol) { _a.rol(dst, count); } else { _a.ror(dst, count); }
        }

        private void EmitShiftCl8(AssemblerRegister8 dst, JitShiftOp op)
        {
            AssemblerRegister8 cl = new(Register.CL);
            switch (op)
            {
                case JitShiftOp.Shl: _a.shl(dst, cl); break;
                case JitShiftOp.Shr: _a.shr(dst, cl); break;
                case JitShiftOp.Sar: _a.sar(dst, cl); break;
                case JitShiftOp.Rol: _a.rol(dst, cl); break;
                default: _a.ror(dst, cl); break;
            }
        }

        private void EmitShiftCl16(AssemblerRegister16 dst, JitShiftOp op)
        {
            AssemblerRegister8 cl = new(Register.CL);
            switch (op)
            {
                case JitShiftOp.Shl: _a.shl(dst, cl); break;
                case JitShiftOp.Shr: _a.shr(dst, cl); break;
                case JitShiftOp.Sar: _a.sar(dst, cl); break;
                case JitShiftOp.Rol: _a.rol(dst, cl); break;
                default: _a.ror(dst, cl); break;
            }
        }

        private void EmitShiftCl32(AssemblerRegister32 dst, JitShiftOp op)
        {
            AssemblerRegister8 cl = new(Register.CL);
            switch (op)
            {
                case JitShiftOp.Shl: _a.shl(dst, cl); break;
                case JitShiftOp.Shr: _a.shr(dst, cl); break;
                case JitShiftOp.Sar: _a.sar(dst, cl); break;
                case JitShiftOp.Rol: _a.rol(dst, cl); break;
                default: _a.ror(dst, cl); break;
            }
        }

        private void EmitShift8(AssemblerRegister8 dst, byte count, JitShiftOp op)
        {
            switch (op)
            {
                case JitShiftOp.Shl: _a.shl(dst, count); break;
                case JitShiftOp.Shr: _a.shr(dst, count); break;
                case JitShiftOp.Sar: _a.sar(dst, count); break;
                default: throw new InvalidOperationException("raw: unknown shift op");
            }
        }

        private void EmitShift16(AssemblerRegister16 dst, byte count, JitShiftOp op)
        {
            switch (op)
            {
                case JitShiftOp.Shl: _a.shl(dst, count); break;
                case JitShiftOp.Shr: _a.shr(dst, count); break;
                case JitShiftOp.Sar: _a.sar(dst, count); break;
                default: throw new InvalidOperationException("raw: unknown shift op");
            }
        }

        private void EmitShift32(AssemblerRegister32 dst, byte count, JitShiftOp op)
        {
            switch (op)
            {
                case JitShiftOp.Shl: _a.shl(dst, count); break;
                case JitShiftOp.Shr: _a.shr(dst, count); break;
                case JitShiftOp.Sar: _a.sar(dst, count); break;
                default: throw new InvalidOperationException("raw: unknown shift op");
            }
        }

        private void EmitJcc(in JitMemberPlan plan, uint eip)
        {
            Assembler a = _a;
            EmitLoadFlags();
            uint target = unchecked(eip + (uint)plan.Length + plan.Immediate);
            uint fallThrough = unchecked(eip + (uint)plan.Length);
            Label taken = a.CreateLabel(string.Empty);
            EmitJccOp(plan.Condition, taken);
            EmitPublishAccounting(_membersDone + 1, _membersDone + 1, fallThrough);
            // Both arms normalize the frame flags before exiting (the
            // epilogue does not re-materialize). EmitLoadFlags above left the
            // host FLAGS live with the branch condition's values, and both
            // arms enter with them live: the compile-time state must be reset
            // for the taken arm, which the fall-through arm's materialize
            // would otherwise leave "dead" (the frame slot is stale there).
            EmitMaterializeFlags();
            a.jmp(_exit);
            a.Label(ref taken);
            _flagsLive = true;
            EmitPublishAccounting(_membersDone + 1, _membersDone + 1, target);
            EmitMaterializeFlags();
            a.jmp(_exit);
        }


        private void EmitJccOp(ConditionCode condition, Label target)
        {
            switch (condition)
            {
                case ConditionCode.Overflow: _a.jo(target); break;
                case ConditionCode.NotOverflow: _a.jno(target); break;
                case ConditionCode.Below: _a.jb(target); break;
                case ConditionCode.AboveOrEqual: _a.jae(target); break;
                case ConditionCode.Equal: _a.je(target); break;
                case ConditionCode.NotEqual: _a.jne(target); break;
                case ConditionCode.BelowOrEqual: _a.jbe(target); break;
                case ConditionCode.Above: _a.ja(target); break;
                case ConditionCode.Sign: _a.js(target); break;
                case ConditionCode.NotSign: _a.jns(target); break;
                case ConditionCode.Parity: _a.jp(target); break;
                case ConditionCode.NotParity: _a.jnp(target); break;
                case ConditionCode.Less: _a.jl(target); break;
                case ConditionCode.GreaterOrEqual: _a.jge(target); break;
                case ConditionCode.LessOrEqual: _a.jle(target); break;
                default: _a.jg(target); break;
            }
        }

        /// <summary>
        /// Generic member fallback: sync the context, run the exact spec handler
        /// (or interpreter body), reload. Faults unwind with the context precise.
        /// </summary>
        private void EmitHelperMember(int index)
        {
            _memberHelper = true;
            Assembler a = _a;
            EmitSyncForHelper();
            a.mov(R64(Register.RCX), Slot(F_CPU));
            a.mov(R64(Register.RDX), Slot(F_ENTRIES));
            a.mov(R32(Register.R8D), index);
            a.mov(R64(Register.R9), R64(Register.R11));
            a.mov(R64(Register.RSI), RawHelpers.ExecuteSpecAddress);
            a.call(R64(Register.RSI));
            EmitFaultCheck();
            EmitReloadAfterHelper();
        }

        /// <summary>Computes the guest linear address of a memory plan into R9D.</summary>
        private void EmitAddress(in JitMemberPlan plan)
        {
            Assembler a = _a;
            Register? baseRegister = plan.HasBase ? Gpr64[plan.BaseRegister] : null;
            Register? indexRegister = plan.HasIndex ? Gpr64[plan.IndexRegister] : null;
            if (baseRegister is not null && indexRegister is not null)
            {
                a.lea(R32(Register.R9D), MemOp(baseRegister, indexRegister, plan.Scale, unchecked((int)plan.Displacement)));
            }
            else if (baseRegister is not null)
            {
                a.lea(R32(Register.R9D), MemOp(baseRegister, null, 1, unchecked((int)plan.Displacement)));
            }
            else if (indexRegister is not null)
            {
                a.mov(R32(Register.R9D), unchecked((int)plan.Displacement));
                a.lea(R32(Register.R9D), MemOp(Register.R9, indexRegister, plan.Scale, 0));
            }
            else
            {
                a.mov(R32(Register.R9D), unchecked((int)plan.Displacement));
            }

            a.add(R32(Register.R9D), Field(Register.R11, OffSegments + ((int)plan.Segment * 4)));
        }

        /// <summary>
        /// Emits the inline probe: R9D holds the guest linear address; on success
        /// RAX holds the physical offset and RDX the RAM base.
        /// </summary>
        /// <summary>
        /// Emits the inline translate-cache probe: R9D holds the guest linear
        /// address; on success RDI holds the physical byte offset and RBP the
        /// pinned RAM base, so the access is <c>[rbp+rdi]</c>. A single-page
        /// access whose physical address leaves RAM branches to
        /// <paramref name="device"/> (device fast path) when given, else to
        /// <paramref name="slow"/>.
        ///
        /// <para>Only non-guest scratch registers may be touched here (R8, R10,
        /// RDI, RBP, plus R9/R11): EAX/ECX/EDX/EBX/R12D-R15D hold guest state
        /// that the slow path must be able to sync intact, and the slow path
        /// still needs R9D as the linear address on every branch out.</para>
        ///
        /// <para>Multi-byte accesses never cross a page inline (the second
        /// page may map differently) and stores run the decode-cache guard:
        /// a store to an active code page or a tracked page-table page takes
        /// the exact interpreter path, where <c>NoteStorePhysical</c> drops or
        /// flushes the decodes; an armed bus write watch also goes slow
        /// (CheckWatch/WatchHit).</para>
        /// </summary>
        private void EmitProbe(int sizeBytes, bool isStore, Label slow, Label? device)
        {
            Assembler a = _a;
            if (sizeBytes > 1)
            {
                a.mov(R32(Register.R8D), R32(Register.R9D));
                a.and(R32(Register.R8D), 0xFFF);
                a.cmp(R32(Register.R8D), 0x1000 - sizeBytes);
                a.ja(slow);
            }

            a.mov(R32(Register.R8D), R32(Register.R9D));
            a.shr(R32(Register.R8D), 12);
            a.mov(R32(Register.R10D), R32(Register.R8D));
            a.and(R32(Register.R10D), (int)XboxCpu.DataXlatMask);
            a.mov(R64(Register.RDI), Field(Register.R11, OffXlatTag));
            a.mov(R32(Register.EDI), Indexed(Register.RDI, Register.R10, 4, 0));
            a.cmp(R32(Register.EDI), R32(Register.R8D));
            a.jne(slow);

            if (isStore)
            {
                // WP parity with the IL probe: a non-writable page only takes
                // the slow path when CR0.WP is set (otherwise the direct
                // write is legal and must not re-translate — A/D bits would
                // become observable in the guest page tables).
                Label writable = a.CreateLabel(string.Empty);
                a.mov(R64(Register.RBP), Field(Register.R11, OffXlatWritable));
                a.mov(new AssemblerRegister8(Register.DIL), Indexed(Register.RBP, Register.R10, 1, 0));
                a.test(new AssemblerRegister8(Register.DIL), new AssemblerRegister8(Register.DIL));
                a.jne(writable);
                a.mov(R32(Register.R10D), Field(Register.R11, OffCr0Wp));
                a.test(R32(Register.R10D), R32(Register.R10D));
                a.jne(slow);

                // The CR0.WP probe above clobbered R10D, which still holds the
                // translate-cache slot the physical-page load below indexes:
                // without this reload a writable-direct store to a read-only
                // page with WP clear reads the wrong slot (decisions 232).
                a.mov(R32(Register.R10D), R32(Register.R8D));
                a.and(R32(Register.R10D), (int)XboxCpu.DataXlatMask);
                a.Label(ref writable);
            }

            a.mov(R64(Register.RDI), Field(Register.R11, OffXlatPhys));
            a.mov(R32(Register.EDI), Indexed(Register.RDI, Register.R10, 4, 0));
            a.mov(R32(Register.R10D), R32(Register.R9D));
            a.and(R32(Register.R10D), 0xFFF);
            a.add(R32(Register.EDI), R32(Register.R10D));

            // phys + size <= RamSize, 64-bit (a near-4G physical must not wrap).
            a.mov(R32(Register.EBP), R32(Register.EDI));
            a.mov(R32(Register.R10D), Field(Register.R11, OffRamSize));
            a.lea(R64(Register.RBP), MemOp(Register.RBP, null, 1, sizeBytes));
            a.cmp(R64(Register.RBP), R64(Register.R10));
            a.ja(device ?? slow);
            a.mov(R64(Register.RBP), Field(Register.R11, OffRam));

            if (isStore)
            {
                EmitStoreGuard(sizeBytes * 8, slow);
            }
        }

        /// <summary>
        /// Inline decode-cache store guard: a store to an active code page or
        /// a tracked page-table page must run <c>NoteStorePhysical</c> (page
        /// drop / full flush), and an armed bus watch must run CheckWatch —
        /// both take the exact slow path. The bitset reads are single
        /// <c>bt</c> instructions against the pinned tables (page &gt;= the
        /// tracked limit can appear in neither table).
        /// </summary>
        private void EmitStoreGuard(int sizeBits, Label slow)
        {
            Assembler a = _a;
            int trackedPages = (int)(DecodeCache.MaxTrackedPhysical >> 12);
            Label noWatch = a.CreateLabel(string.Empty);

            // Guest-write stamp (XFP4 texture-content identity): every inline
            // RAM store bumps its page's generation so a content cache can
            // tell whether a guest range changed without rescanning it. A
            // slow-path store bumps through PhysicalMemory.Write*, never here,
            // so one store never bumps twice. R8D/R10 are the guard's own
            // scratch; ESI (the value scratch) is parked in F_VALUE across
            // the bump because the store itself has not run yet; R9 (the
            // linear address the slow path consumes) stays untouched.
            Label noStamp = a.CreateLabel(string.Empty);
            a.mov(Slot(F_STORE_SCRATCH), R32(Register.ESI));
            a.mov(R32(Register.R8D), R32(Register.EDI));
            a.shr(R32(Register.R8D), 12);
            a.cmp(R32(Register.R8D), Field(Register.R11, OffWriteStampPages));
            a.jae(noStamp);
            a.mov(R64(Register.R10), Field(Register.R11, OffWriteStamp));
            a.mov(R32(Register.ESI), MemOp(Register.R10, Register.R8, 4, 0));
            a.add(R32(Register.ESI), 1);
            a.mov(MemOp(Register.R10, Register.R8, 4, 0), R32(Register.ESI));
            a.Label(ref noStamp);
            a.mov(R32(Register.ESI), Slot(F_STORE_SCRATCH));
            Label armCheck = a.CreateLabel(string.Empty);
            Label needNote = a.CreateLabel(string.Empty);
            Label done = a.CreateLabel(string.Empty);

            // Bus write watch (IsWatched/CheckWatch parity): an armed watch
            // whose range overlaps [phys, phys + size) takes the exact bus
            // path. EDI holds the probe's physical address; R8D/R10D are free
            // here. This runs before the note path on purpose: the note helper
            // clobbers R9 (the linear address) and the slow path consumes it,
            // so a watched store must diverge while R9 is still intact.
            a.mov(R32(Register.R10D), Field(Register.R11, OffWatchArmed));
            a.test(R32(Register.R10D), R32(Register.R10D));
            a.je(noWatch);
            a.mov(R32(Register.R8D), R32(Register.EDI));
            a.add(R32(Register.R8D), sizeBits / 8);
            a.mov(R32(Register.R10D), Field(Register.R11, OffWatchStart));
            a.cmp(R32(Register.R10D), R32(Register.R8D));
            a.jae(noWatch);
            a.mov(R32(Register.R10D), Field(Register.R11, OffWatchEnd));
            a.cmp(R32(Register.EDI), R32(Register.R10D));
            a.jae(noWatch);
            a.jmp(slow);
            a.Label(ref noWatch);

            a.mov(R32(Register.R8D), Field(Register.R11, OffStoreGuardBail));
            a.test(R32(Register.R8D), R32(Register.R8D));
            a.jne(slow);

            // EDI holds the physical address here; R8D is free again.
            a.mov(R32(Register.R8D), R32(Register.EDI));
            a.shr(R32(Register.R8D), 12);
            a.cmp(R32(Register.R8D), trackedPages);
            a.jae(done);
            a.mov(R64(Register.R10), Field(Register.R11, OffPtBits));
            a.test(R64(Register.R10), R64(Register.R10));
            a.je(armCheck);
            a.bt(MemOp(Register.R10, null, 1, 0), R64(Register.R8));
            a.jc(needNote);
            a.Label(ref armCheck);
            a.mov(R64(Register.R10), Field(Register.R11, OffActiveBits));
            a.bt(MemOp(Register.R10, null, 1, 0), R64(Register.R8));
            a.jc(needNote);
            a.jmp(done);

            // A store to an active code page or a tracked page-table page
            // runs the decode-cache invalidation and then continues with the
            // direct write — exactly what the IL store path does (the slow
            // path would additionally re-translate, making A/D bits visible).
            // The helper call clobbers the caller-saved host registers, which
            // hold guest eax/ecx/edx/ebx: park them (with the Win64 shadow
            // space kept clear) and restore after. R9 is dead on this path:
            // nothing after the note consumes it (the watch divergence above
            // happens before the note).
            a.Label(ref needNote);
            a.mov(Slot(F_STORE_SCRATCH), R32(Register.ESI));
            a.mov(Slot(F_PHYS), R32(Register.EDI));
            a.mov(R32(Register.R8D), sizeBits);
            a.mov(R64(Register.R9), Slot(F_CPU));
            a.mov(R64(Register.R10), Slot(F_PHYS));
            a.mov(R64(Register.RSI), RawHelpers.NoteStoreAddress);
            a.push(R64(Register.RAX));
            a.push(R64(Register.RCX));
            a.push(R64(Register.RDX));
            a.push(R64(Register.RBX));
            a.sub(R64(Register.RSP), 0x20);
            a.mov(R64(Register.RCX), R64(Register.R9));
            a.mov(R64(Register.RDX), R64(Register.R10));
            a.call(R64(Register.RSI));
            a.add(R64(Register.RSP), 0x20);
            a.pop(R64(Register.RBX));
            a.pop(R64(Register.RDX));
            a.pop(R64(Register.RCX));
            a.pop(R64(Register.RAX));
            EmitFaultCheck();
            a.mov(R32(Register.ESI), Slot(F_STORE_SCRATCH));
            a.mov(R32(Register.EDI), Slot(F_PHYS));
            a.mov(R64(Register.RBP), Field(Register.R11, OffRam));

            a.Label(ref done);
        }

        /// <summary>The probed RAM access: <c>[rbp+rdi]</c> (RBP base, RDI offset).</summary>
        private static AssemblerMemoryOperand RamAt() => Indexed(Register.RBP, Register.RDI, 1, 0);

        private void EmitLoad(in JitMemberPlan plan, int index)
        {
            if (IsHighByte(Reg(plan.Size, plan.RegIndex)))
            {
                EmitHelperMember(index);
                return;
            }

            Assembler a = _a;
            EmitMaterializeFlags();
            EmitAddress(plan);
            Label slow = a.CreateLabel(string.Empty);
            Label done = a.CreateLabel(string.Empty);
            Label? device = mmioFast ? a.CreateLabel(string.Empty) : null;
            EmitProbe(plan.Size / 8, isStore: false, slow, device);
            EmitMoveFromMemory(plan.Size, plan.RegIndex, RamAt());
            a.jmp(done);

            if (device is not null)
            {
                EmitDeviceLoad(plan.Size, plan.RegIndex, device.Value, done);
            }

            // Compile-time flag state does not flow along the emitted control
            // flow: the device block above ends in EmitReloadAfterHelper,
            // which sets _flagsLive = true for the code it falls into (done),
            // but the slow block below is entered from the probe with the host
            // FLAGS already clobbered and F_EFLAGS authoritative. Leaving the
            // leaked true here made EmitSyncForHelper materialize the probe's
            // own comparison results into F_EFLAGS, so the next flag consumer
            // saw the probe's flags (the --jit-mmio boot divergence at 25.1M
            // steps, decisions 202).
            _flagsLive = false;
            a.Label(ref slow);
            EmitMemoryHelper(plan, isStore: false);
            EmitMoveFromScratch(plan.Size, plan.RegIndex);

            // Merge point: the fast path arrives with the host flags dead
            // (materialized before the probe, then clobbered), the slow path
            // with them live (the reload). F_EFLAGS is authoritative on both,
            // so the continuation must not trust the host FLAGS: reloading on
            // demand (EmitLoadFlags) is exact on either path.
            _flagsLive = false;

            a.Label(ref done);
        }

        /// <summary>
        /// Device fast path (<c>--jit-mmio</c>): the probe translated the
        /// single-page access and left the physical address in EDI; the read
        /// goes through the same <c>RawRead</c> the interpreter would reach,
        /// so no re-translate is needed. The helper call clobbers the guest
        /// registers held in host registers, so the state is synced and
        /// reloaded around it exactly like any other interpreter-path call.
        /// </summary>
        private void EmitDeviceLoad(int size, int regIndex, Label device, Label done)
        {
            Assembler a = _a;
            Label target = device;
            a.Label(ref target);
            EmitSyncGuestStateForHelper();
            a.mov(R64(Register.RCX), Slot(F_CPU));
            a.mov(R32(Register.EDX), R32(Register.EDI));
            a.mov(R32(Register.R8D), size);
            a.mov(R64(Register.RSI), RawHelpers.ReadPhysicalAddress);
            a.call(R64(Register.RSI));
            EmitFaultCheck();
            a.mov(R32(Register.R8D), R32(Register.EAX));
            EmitReloadAfterHelper();
            EmitMoveFromScratch(size, regIndex);
            a.jmp(done);
        }

        private void EmitStore(in JitMemberPlan plan, int index)
        {
            bool immediate = plan.Kind == JitKind.MovStoreImm;
            if (!immediate && IsHighByte(Reg(plan.Size, plan.RegIndex)))
            {
                EmitHelperMember(index);
                return;
            }

            Assembler a = _a;
            EmitMaterializeFlags();
            EmitAddress(plan);
            if (immediate)
            {
                EmitImmediateToValueScratch(plan.Size, unchecked((int)plan.Immediate));
            }
            else
            {
                EmitMoveToValueScratch(plan.Size, plan.RegIndex);
            }

            Label slow = a.CreateLabel(string.Empty);
            Label done = a.CreateLabel(string.Empty);
            Label? device = mmioFast ? a.CreateLabel(string.Empty) : null;
            EmitProbe(plan.Size / 8, isStore: true, slow, device);
            EmitMoveValueScratchToMemory(plan.Size, RamAt());
            a.jmp(done);

            if (device is not null)
            {
                Label deviceLabel = device.Value;
                a.Label(ref deviceLabel);
                EmitSyncGuestStateForHelper();
                a.mov(R32(Register.R9D), R32(Register.ESI));
                a.mov(R64(Register.RCX), Slot(F_CPU));
                a.mov(R32(Register.EDX), R32(Register.EDI));
                a.mov(R32(Register.R8D), plan.Size);
                a.mov(R64(Register.RSI), RawHelpers.WritePhysicalAddress);
                a.call(R64(Register.RSI));
                EmitFaultCheck();
                EmitReloadAfterHelper();
                a.jmp(done);
            }

            // See EmitLoad: the slow block is entered from the probe with the
            // host FLAGS dead, so the emitter state must not inherit the
            // device block's reload.
            _flagsLive = false;
            a.Label(ref slow);
            EmitMemoryHelper(plan, isStore: true);

            // Merge point (see EmitLoad): F_EFLAGS is authoritative on both
            // the fast and slow paths; the host FLAGS must not be trusted.
            _flagsLive = false;

            a.Label(ref done);
        }

        /// <summary>Read-modify-write source form: <c>op reg, [mem]</c>.</summary>
        private void EmitAluMemorySource(in JitMemberPlan plan)
        {
            Assembler a = _a;
            EmitMaterializeFlags();
            EmitAddress(plan);
            Label slow = a.CreateLabel(string.Empty);
            Label done = a.CreateLabel(string.Empty);
            EmitProbe(plan.Size / 8, isStore: false, slow, null);
            EmitMoveFromMemory(plan.Size, 0, RamAt(), scratchOnly: true);
            EmitAluScratch(plan);
            a.jmp(done);

            a.Label(ref slow);
            EmitMemoryHelper(plan, isStore: false);
            EmitAluScratch(plan);

            a.Label(ref done);
        }

        /// <summary>
        /// <c>cmp [mem], reg/imm</c>: a pure read whose flags are
        /// <c>mem - value</c>, so the memory operand must stay on the left.
        /// </summary>
        private void EmitCmpMemorySource(in JitMemberPlan plan, int index)
        {
            Assembler a = _a;
            EmitMaterializeFlags();
            EmitAddress(plan);
            if (plan.Kind == JitKind.AluMI)
            {
                EmitImmediateToValueScratch(plan.Size, unchecked((int)plan.Immediate));
            }
            else if (IsHighByte(Reg(plan.Size, plan.RegIndex)))
            {
                EmitHelperMember(index);
                return;
            }
            else
            {
                EmitMoveToValueScratch(plan.Size, plan.RegIndex);
            }

            Label slow = a.CreateLabel(string.Empty);
            Label done = a.CreateLabel(string.Empty);
            EmitProbe(plan.Size / 8, isStore: false, slow, null);
            EmitMoveFromMemory(plan.Size, 0, RamAt(), scratchOnly: true);
            EmitCmpScratch(plan);
            // The slow path below runs the full spec handler, which advances
            // EIP itself; the fast path owns its own advance (without this the
            // outer EmitMember would skip it for both paths because
            // EmitHelperMember sets _memberHelper, and a member that ends the
            // block would leave EIP on its own first byte).
            a.jmp(done);

            a.Label(ref slow);
            EmitHelperMember(index);

            a.Label(ref done);
        }

        /// <summary>Read-modify-write destination form: <c>op [mem], reg/imm</c>.</summary>
        private void EmitAluMemoryDestination(in JitMemberPlan plan, int index)
        {
            if (plan.AluOp == JitAluOp.Cmp)
            {
                // Cmp never writes: same shape as the source form.
                EmitCmpMemorySource(plan, index);
                return;
            }

            Assembler a = _a;
            EmitMaterializeFlags();
            EmitAddress(plan);
            if (plan.Kind == JitKind.AluMI)
            {
                EmitImmediateToValueScratch(plan.Size, unchecked((int)plan.Immediate));
            }
            else if (IsHighByte(Reg(plan.Size, plan.RegIndex)))
            {
                EmitHelperMember(index);
                return;
            }
            else
            {
                EmitMoveToValueScratch(plan.Size, plan.RegIndex);
            }

            Label slow = a.CreateLabel(string.Empty);
            Label done = a.CreateLabel(string.Empty);
            EmitProbe(plan.Size / 8, isStore: true, slow, null);
            EmitAluMemoryOp(plan, RamAt());
            // Fast-path EIP advance (see EmitCmpMemorySource): the slow path's
            // helper owns the advance there.
            a.jmp(done);

            a.Label(ref slow);
            EmitHelperMember(index);

            a.Label(ref done);
        }

        private void EmitTestMemory(in JitMemberPlan plan, int index)
        {
            Assembler a = _a;
            EmitMaterializeFlags();
            EmitAddress(plan);
            Label slow = a.CreateLabel(string.Empty);
            Label done = a.CreateLabel(string.Empty);
            EmitProbe(plan.Size / 8, isStore: false, slow, null);
            EmitMoveFromMemory(plan.Size, 0, RamAt(), scratchOnly: true);
            EmitTestScratch(plan);
            // Fast-path EIP advance (see EmitCmpMemorySource).
            a.jmp(done);

            a.Label(ref slow);
            EmitHelperMember(index);

            a.Label(ref done);
        }

        private void EmitIncDecMemory(in JitMemberPlan plan, int index)
        {
            Assembler a = _a;
            EmitMaterializeFlags();
            EmitAddress(plan);
            Label slow = a.CreateLabel(string.Empty);
            Label done = a.CreateLabel(string.Empty);
            EmitProbe(plan.Size / 8, isStore: true, slow, null);
            EmitIncDecMemoryOp(plan, RamAt());
            // Fast-path EIP advance (see EmitCmpMemorySource).
            a.jmp(done);

            a.Label(ref slow);
            EmitHelperMember(index);

            a.Label(ref done);
        }

        private void EmitMoveFromMemory(int size, int regIndex, AssemblerMemoryOperand source, bool scratchOnly = false)
        {
            switch (size)
            {
                case 8:
                    _a.mov(scratchOnly ? new AssemblerRegister8(Register.R8L) : new AssemblerRegister8(Reg(8, regIndex)), source);
                    break;
                case 16:
                    _a.mov(scratchOnly ? new AssemblerRegister16(Register.R8W) : new AssemblerRegister16(Reg(16, regIndex)), source);
                    break;
                default:
                    _a.mov(scratchOnly ? R32(Register.R8D) : R32(Gpr32[regIndex]), source);
                    break;
            }
        }

        private void EmitMoveFromScratch(int size, int regIndex)
        {
            switch (size)
            {
                case 8:
                    _a.mov(new AssemblerRegister8(Reg(8, regIndex)), new AssemblerRegister8(Register.R8L));
                    break;
                case 16:
                    _a.mov(new AssemblerRegister16(Reg(16, regIndex)), new AssemblerRegister16(Register.R8W));
                    break;
                default:
                    _a.mov(R32(Gpr32[regIndex]), R32(Register.R8D));
                    break;
            }
        }

        private void EmitMoveToValueScratch(int size, int regIndex)
        {
            switch (size)
            {
                case 8:
                    _a.mov(new AssemblerRegister8(Register.SIL), new AssemblerRegister8(Reg(8, regIndex)));
                    break;
                case 16:
                    _a.mov(new AssemblerRegister16(Register.SI), new AssemblerRegister16(Reg(16, regIndex)));
                    break;
                default:
                    _a.mov(R32(Register.ESI), R32(Gpr32[regIndex]));
                    break;
            }
        }

        private void EmitImmediateToValueScratch(int size, int immediate)
        {
            switch (size)
            {
                case 8:
                    _a.mov(new AssemblerRegister8(Register.SIL), (byte)immediate);
                    break;
                case 16:
                    _a.mov(new AssemblerRegister16(Register.SI), (ushort)immediate);
                    break;
                default:
                    _a.mov(R32(Register.ESI), immediate);
                    break;
            }
        }

        private void EmitMoveValueScratchToMemory(int size, AssemblerMemoryOperand destination)
        {
            switch (size)
            {
                case 8:
                    _a.mov(destination, new AssemblerRegister8(Register.SIL));
                    break;
                case 16:
                    _a.mov(destination, new AssemblerRegister16(Register.SI));
                    break;
                default:
                    _a.mov(destination, R32(Register.ESI));
                    break;
            }
        }

        /// <summary><c>op reg, scratch</c> for the source form.</summary>
        private void EmitAluScratch(in JitMemberPlan plan)
        {
            if (plan.AluOp is JitAluOp.Adc or JitAluOp.Sbb)
            {
                EmitEnsureHostFlags();
            }

            switch (plan.Size)
            {
                case 8:
                    EmitAlu8(new AssemblerRegister8(Reg(8, plan.RegIndex)), new AssemblerRegister8(Register.R8L), plan.AluOp);
                    break;
                case 16:
                    EmitAlu16(new AssemblerRegister16(Reg(16, plan.RegIndex)), new AssemblerRegister16(Register.R8W), plan.AluOp);
                    break;
                default:
                    EmitAlu32(R32(Gpr32[plan.RegIndex]), R32(Register.R8D), plan.AluOp);
                    break;
            }

            _flagsLive = true;
        }

        /// <summary><c>cmp scratch, value</c> (memory operand on the left).</summary>
        private void EmitCmpScratch(in JitMemberPlan plan)
        {
            switch (plan.Size)
            {
                case 8:
                    _a.cmp(new AssemblerRegister8(Register.R8L), new AssemblerRegister8(Register.SIL));
                    break;
                case 16:
                    _a.cmp(new AssemblerRegister16(Register.R8W), new AssemblerRegister16(Register.SI));
                    break;
                default:
                    _a.cmp(R32(Register.R8D), R32(Register.ESI));
                    break;
            }

            _flagsLive = true;
        }

        /// <summary><c>test scratch, imm</c>.</summary>
        private void EmitTestScratch(in JitMemberPlan plan)
        {
            int immediate = unchecked((int)plan.Immediate);
            switch (plan.Size)
            {
                case 8:
                    _a.test(new AssemblerRegister8(Register.R8L), (byte)immediate);
                    break;
                case 16:
                    _a.test(new AssemblerRegister16(Register.R8W), (ushort)immediate);
                    break;
                default:
                    _a.test(R32(Register.R8D), immediate);
                    break;
            }

            _flagsLive = true;
        }

        private void EmitAluMemoryOp(in JitMemberPlan plan, AssemblerMemoryOperand destination)
        {
            if (plan.AluOp is JitAluOp.Adc or JitAluOp.Sbb)
            {
                EmitEnsureHostFlags();
            }

            switch (plan.Size)
            {
                case 8:
                    EmitAluMemory8(destination, new AssemblerRegister8(Register.SIL), plan.AluOp);
                    break;
                case 16:
                    EmitAluMemory16(destination, new AssemblerRegister16(Register.SI), plan.AluOp);
                    break;
                default:
                    EmitAluMemory32(destination, R32(Register.ESI), plan.AluOp);
                    break;
            }

            _flagsLive = true;
        }

        private void EmitAluMemory8(AssemblerMemoryOperand dst, AssemblerRegister8 src, JitAluOp op)
        {
            switch (op)
            {
                case JitAluOp.Add: _a.add(dst, src); break;
                case JitAluOp.Sub: _a.sub(dst, src); break;
                case JitAluOp.And: _a.and(dst, src); break;
                case JitAluOp.Or: _a.or(dst, src); break;
                case JitAluOp.Xor: _a.xor(dst, src); break;
                case JitAluOp.Cmp: _a.cmp(dst, src); break;
                case JitAluOp.Adc: _a.adc(dst, src); break;
                case JitAluOp.Sbb: _a.sbb(dst, src); break;
                default: throw new InvalidOperationException("raw: unknown ALU op");
            }
        }

        private void EmitAluMemory16(AssemblerMemoryOperand dst, AssemblerRegister16 src, JitAluOp op)
        {
            switch (op)
            {
                case JitAluOp.Add: _a.add(dst, src); break;
                case JitAluOp.Sub: _a.sub(dst, src); break;
                case JitAluOp.And: _a.and(dst, src); break;
                case JitAluOp.Or: _a.or(dst, src); break;
                case JitAluOp.Xor: _a.xor(dst, src); break;
                case JitAluOp.Cmp: _a.cmp(dst, src); break;
                case JitAluOp.Adc: _a.adc(dst, src); break;
                case JitAluOp.Sbb: _a.sbb(dst, src); break;
                default: throw new InvalidOperationException("raw: unknown ALU op");
            }
        }

        private void EmitAluMemory32(AssemblerMemoryOperand dst, AssemblerRegister32 src, JitAluOp op)
        {
            switch (op)
            {
                case JitAluOp.Add: _a.add(dst, src); break;
                case JitAluOp.Sub: _a.sub(dst, src); break;
                case JitAluOp.And: _a.and(dst, src); break;
                case JitAluOp.Or: _a.or(dst, src); break;
                case JitAluOp.Xor: _a.xor(dst, src); break;
                case JitAluOp.Cmp: _a.cmp(dst, src); break;
                case JitAluOp.Adc: _a.adc(dst, src); break;
                case JitAluOp.Sbb: _a.sbb(dst, src); break;
                default: throw new InvalidOperationException("raw: unknown ALU op");
            }
        }

        /// <summary>Read-modify-write <c>inc/dec [mem]</c> through the scratch.</summary>
        private void EmitIncDecMemoryOp(in JitMemberPlan plan, AssemblerMemoryOperand destination)
        {
            EmitEnsureHostFlags();
            bool inc = plan.Kind == JitKind.IncM;
            switch (plan.Size)
            {
                case 8:
                    _a.mov(new AssemblerRegister8(Register.R8L), destination);
                    if (inc) { _a.inc(new AssemblerRegister8(Register.R8L)); } else { _a.dec(new AssemblerRegister8(Register.R8L)); }
                    _a.mov(destination, new AssemblerRegister8(Register.R8L));
                    break;
                case 16:
                    _a.mov(new AssemblerRegister16(Register.R8W), destination);
                    if (inc) { _a.inc(new AssemblerRegister16(Register.R8W)); } else { _a.dec(new AssemblerRegister16(Register.R8W)); }
                    _a.mov(destination, new AssemblerRegister16(Register.R8W));
                    break;
                default:
                    _a.mov(R32(Register.R8D), destination);
                    if (inc) { _a.inc(R32(Register.R8D)); } else { _a.dec(R32(Register.R8D)); }
                    _a.mov(destination, R32(Register.R8D));
                    break;
            }

            _flagsLive = true;
        }

        /// <summary>
        /// Simple read/write slow path: sync the context, call the exact
        /// read/write helper (which faults precisely), reload. Leaves a load
        /// result in R8D and consumes a staged store value from RSI.
        /// </summary>
        private void EmitMemoryHelper(in JitMemberPlan plan, bool isStore)
        {
            Assembler a = _a;
            // RDI, not R10: the flag materializer and the TSC bookkeeping in
            // the sync both use R10.
            a.mov(R32(Register.EDI), R32(Register.R9D));
            if (isStore)
            {
                a.mov(Slot(F_VALUE), R32(Register.ESI));
            }

            EmitSyncForHelper();
            a.mov(R64(Register.RCX), Slot(F_CPU));
            a.mov(R32(Register.EDX), R32(Register.EDI));
            a.mov(R32(Register.R8D), plan.Size);
            if (isStore)
            {
                a.mov(R32(Register.R9D), Slot(F_VALUE));
                a.mov(R64(Register.RSI), RawHelpers.WriteMemoryAddress);
            }
            else
            {
                a.mov(R64(Register.RSI), RawHelpers.ReadMemoryAddress);
            }

            a.call(R64(Register.RSI));
            EmitFaultCheck();
            a.mov(R32(Register.R8D), R32(Register.EAX));
            EmitReloadAfterHelper();
        }

        private static AssemblerRegister8 R8(int index) =>
            new(index >= 4 ? Gpr8High[index] : Gpr8[index]);

        private static AssemblerRegister16 R16(int index) => new(Gpr16[index]);

        private static bool IsHighByte(Register register) =>
            register is Register.AH or Register.CH or Register.DH or Register.BH;

        private static Register Reg(int size, int index) => size switch
        {
            8 => index >= 4 ? Gpr8High[index] : Gpr8[index],
            16 => Gpr16[index],
            _ => Gpr32[index],
        };

        private static AssemblerMemoryOperand AddressOf(in JitMemberPlan plan)
        {
            Register? baseRegister = plan.HasBase ? Gpr64[plan.BaseRegister] : null;
            Register? indexRegister = plan.HasIndex ? Gpr64[plan.IndexRegister] : null;
            return MemOp(baseRegister, indexRegister, plan.Scale, unchecked((int)plan.Displacement));
        }
    }

    private sealed class BufferCodeWriter(byte* buffer, int capacity) : CodeWriter
    {
        private readonly int _capacity = capacity;

        public int Position { get; private set; }

        public override void WriteByte(byte value)
        {
            if (Position >= _capacity)
            {
                throw new InvalidOperationException("raw block exceeded its code buffer");
            }

            buffer[Position++] = value;
        }
    }
}
