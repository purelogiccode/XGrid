using System;
using System.IO;
using System.Runtime.InteropServices;
using Iced.Intel;

namespace XGrid.Cpu;

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
    private const int F_VALUE = ShadowSpace + 0x38;

    // 8 callee-saved pushes (64) + this size keeps rsp 16-byte aligned at a call.
    private const int FrameSize = ShadowSpace + 0x108;

    /// <summary>Arithmetic flags the host FLAGS register carries for the guest (plus reserved bit 1).</summary>
    private const uint ArithmeticMask = 0x8D5;

    private const uint ControlMask = ~ArithmeticMask;

    private static readonly int OffRegisters = (int)Marshal.OffsetOf<RawState>(nameof(RawState.Registers));
    private static readonly int OffXlatTag = (int)Marshal.OffsetOf<RawState>(nameof(RawState.XlatTag));
    private static readonly int OffXlatPhys = (int)Marshal.OffsetOf<RawState>(nameof(RawState.XlatPhys));
    private static readonly int OffXlatWritable = (int)Marshal.OffsetOf<RawState>(nameof(RawState.XlatWritable));
    private static readonly int OffRam = (int)Marshal.OffsetOf<RawState>(nameof(RawState.Ram));
    private static readonly int OffRamSize = (int)Marshal.OffsetOf<RawState>(nameof(RawState.RamSize));
    private static readonly int OffEip = (int)Marshal.OffsetOf<RawState>(nameof(RawState.Eip));
    private static readonly int OffEflags = (int)Marshal.OffsetOf<RawState>(nameof(RawState.Eflags));
    private static readonly int OffTsc = (int)Marshal.OffsetOf<RawState>(nameof(RawState.Tsc));
    private static readonly int OffProgress = (int)Marshal.OffsetOf<RawState>(nameof(RawState.Progress));
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
    /// host cannot run raw code.
    /// </summary>
    public static nint TryBuild(uint headEip, ReadOnlySpan<JitMemberPlan> plans)
    {
        if (!RawCodeMemory.IsSupported || plans.Length == 0)
        {
            return 0;
        }

        try
        {
            var ctx = new EmitContext(headEip, plans.ToArray());
            return ctx.Build();
        }
        catch (Exception)
        {
            // A shape the assembler cannot encode: keep the IL runner for this
            // block (correctness first, the raw path is an optimization).
            return 0;
        }
    }

    private sealed class EmitContext
    {
        private readonly Assembler _a = new(64);
        private readonly uint _headEip;
        private readonly JitMemberPlan[] _plans;
        private Label _exit;
        private bool _flagsLive;

        public EmitContext(uint headEip, JitMemberPlan[] plans)
        {
            _headEip = headEip;
            _plans = plans;
        }

        public nint Build()
        {
            _exit = _a.CreateLabel(string.Empty);
            EmitPrologue();

            uint eip = _headEip;
            for (int i = 0; i < _plans.Length; i++)
            {
                JitMemberPlan plan = _plans[i];
                EmitMember(plan, eip, i);
                if (!plan.IsTerminator)
                {
                    eip += (uint)plan.Length;
                }
            }

            EmitEpilogue();

            byte* memory = RawCodeMemory.Allocate(64 * 1024);
            if (memory is null)
            {
                return 0;
            }

            var writer = new BufferCodeWriter(memory, 64 * 1024);
            _a.Assemble(writer, (ulong)memory, BlockEncoderOptions.None);
            if (string.Equals(Environment.GetEnvironmentVariable("XGRID_RAW_DUMP"), "1", StringComparison.Ordinal))
            {
                Dump(headEip: _headEip, memory, writer.Position);
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

                string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "xgrid-raw-" + headEip.ToString("X8") + ".txt");
                System.IO.File.WriteAllText(path, output.ToString());
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
            else if (baseRegister is not null)
            {
                operand = R64(baseRegister.Value) + 0L;
            }
            else if (indexRegister is not null)
            {
                operand = R64(indexRegister.Value) * scale;
            }
            else
            {
                throw new InvalidOperationException("raw: memory operand without base or index");
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
            a.and(R32(Register.EAX), unchecked((int)(ArithmeticMask | 0x400u)));
            a.push(R64(Register.RAX));
            a.popfq();

            a.mov(R64(Register.R9), Field(Register.R11, OffTsc));
            a.mov(R64(Register.RAX), MemOp(Register.R9, null, 1, 0));
            a.mov(Slot(F_TSC), R64(Register.RAX));

            a.mov(R64(Register.R9), Field(Register.R11, OffEip));
            a.mov(R32(Register.EAX), MemOp(Register.R9, null, 1, 0));
            a.mov(Slot(F_EIP), R32(Register.EAX));

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
            a.Label(ref _exit);
            EmitMaterializeFlags();
            EmitStoreGuestState();

            a.mov(R32(Register.EAX), Slot(F_SERVED));
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
        }

        private void EmitMaterializeFlags()
        {
            if (!_flagsLive)
            {
                return;
            }

            // R10D, never a guest register: the guest GPRs must survive until
            // the epilogue stores them.
            Assembler a = _a;
            a.pushfq();
            a.pop(R64(Register.R10));
            a.and(R32(Register.R10D), unchecked((int)ArithmeticMask));
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

            Assembler a = _a;
            a.mov(R32(Register.EAX), Slot(F_EFLAGS));
            a.and(R32(Register.EAX), unchecked((int)(ArithmeticMask | 0x400u)));
            a.push(R64(Register.RAX));
            a.popfq();
            _flagsLive = true;
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

            // TSC is ticked once per member before its body (the interpreter's rule),
            // so the frame slot already holds the exact value.
            a.mov(R64(Register.R10), Slot(F_TSC));
            a.mov(R64(Register.R9), Field(Register.R11, OffTsc));
            a.mov(MemOp(Register.R9, null, 1, 0), R64(Register.R10));
        }

        private void EmitSyncForHelper()
        {
            EmitMaterializeFlags();
            EmitStoreGuestState();
            Assembler a = _a;
            a.mov(R32(Register.EAX), Slot(F_SERVED));
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
            a.and(R32(Register.EAX), unchecked((int)(ArithmeticMask | 0x400u)));
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

        /// <summary>One TSC tick per member, before the body (interpreter parity).</summary>
        private void EmitTscTick()
        {
            _a.mov(R64(Register.R10), Slot(F_TSC));
            _a.lea(R64(Register.R10), MemOp(Register.R10, null, 1, 1));
            _a.mov(Slot(F_TSC), R64(Register.R10));
        }

        private void EmitServedIncrement()
        {
            // Memory increment has no inferable size; go through R10D (free
            // between members: the EIP lives in its frame slot).
            // lea keeps the live host arithmetic flags intact between a
            // flag-setting member and the branch that consumes them.
            _a.mov(R32(Register.R10D), Slot(F_SERVED));
            _a.lea(R32(Register.R10D), MemOp(Register.R10, null, 1, 1));
            _a.mov(Slot(F_SERVED), R32(Register.R10D));
        }

        private void EmitSetEip(uint eip)
        {
            _a.mov(R32(Register.R10D), unchecked((int)eip));
            _a.mov(Slot(F_EIP), R32(Register.R10D));
        }

        /// <summary>Updates the control-flag slot (memory immediates need a register source).</summary>
        private void EmitUpdateControl(int andMask, int orMask)
        {
            Assembler a = _a;
            a.mov(R32(Register.EAX), Slot(F_CTRL));
            if (andMask != -1)
            {
                a.and(R32(Register.EAX), andMask);
            }

            if (orMask != 0)
            {
                a.or(R32(Register.EAX), orMask);
            }

            a.mov(Slot(F_CTRL), R32(Register.EAX));
        }

        private void EmitMember(in JitMemberPlan plan, uint eip, int index)
        {
            EmitSetEip(eip);
            EmitTscTick();

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
                    EmitMovExtend(plan);
                    break;

                case JitKind.ShiftRI:
                    EmitShift(plan);
                    break;

                case JitKind.Cli:
                    EmitUpdateControl(unchecked((int)~FlagBits.If), 0);
                    break;

                case JitKind.Sti:
                    EmitUpdateControl(-1, (int)FlagBits.If);
                    break;

                case JitKind.Cld:
                    EmitUpdateControl(unchecked((int)~FlagBits.Df), 0);
                    _a.cld();
                    break;

                case JitKind.Std:
                    EmitUpdateControl(-1, (int)FlagBits.Df);
                    _a.std();
                    break;

                case JitKind.MovLoad:
                    EmitLoad(plan, index);
                    break;

                case JitKind.MovStore:
                case JitKind.MovStoreImm:
                    EmitStore(plan, index);
                    break;

                case JitKind.AluRM:
                    EmitAluMemorySource(plan, index);
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
                    EmitSetEip(unchecked(eip + (uint)plan.Length + (uint)plan.Immediate));
                    EmitServedIncrement();
                    _a.jmp(_exit);
                    return;

                case JitKind.JccRel:
                    EmitJcc(plan, eip);
                    return;

                default:
                    EmitHelperMember(index);
                    if (plan.IsTerminator)
                    {
                        EmitServedIncrement();
                        _a.jmp(_exit);
                        return;
                    }

                    break;
            }

            EmitServedIncrement();
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

        private void EmitMovExtend(in JitMemberPlan plan)
        {
            Register source = plan.SourceSize == 8 && plan.SecondRegIndex >= 4
                ? Gpr8High[plan.SecondRegIndex]
                : plan.SourceSize == 8 ? Gpr8[plan.SecondRegIndex] : Gpr16[plan.SecondRegIndex];
            if (IsHighByte(source))
            {
                EmitHelperMember(plan.EntryIndex);
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

        private void EmitShift(in JitMemberPlan plan)
        {
            byte count = (byte)(plan.Immediate & 0x1F);
            switch (plan.Size)
            {
                case 8:
                    EmitShift8(R8(plan.RegIndex), count, plan.ShiftOp);
                    break;
                case 16:
                    EmitShift16(R16(plan.RegIndex), count, plan.ShiftOp);
                    break;
                default:
                    EmitShift32(R32(Gpr32[plan.RegIndex]), count, plan.ShiftOp);
                    break;
            }

            if (count != 0)
            {
                _flagsLive = true;
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
            uint target = unchecked(eip + (uint)plan.Length + (uint)plan.Immediate);
            uint fallThrough = unchecked(eip + (uint)plan.Length);
            Label taken = a.CreateLabel(string.Empty);
            EmitJccOp(plan.Condition, taken);
            EmitSetEip(fallThrough);
            EmitServedIncrement();
            a.jmp(_exit);
            a.Label(ref taken);
            EmitSetEip(target);
            EmitServedIncrement();
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
            Assembler a = _a;
            EmitSyncForHelper();
            a.mov(R64(Register.RCX), Slot(F_CPU));
            a.mov(R64(Register.RDX), Slot(F_ENTRIES));
            a.mov(R32(Register.R8D), index);
            a.mov(R64(Register.R9), R64(Register.R11));
            a.mov(R64(Register.RSI), RawHelpers.ExecuteSpecAddress);
            a.call(R64(Register.RSI));
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
                a.lea(R32(Register.R9D), MemOp(baseRegister, indexRegister, plan.Scale, plan.Displacement));
            }
            else if (baseRegister is not null)
            {
                a.lea(R32(Register.R9D), MemOp(baseRegister, null, 1, plan.Displacement));
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
        /// pinned RAM base, so the access is <c>[rbp+rdi]</c>.
        ///
        /// <para>Only non-guest scratch registers may be touched here (R8, R10,
        /// RDI, RBP, plus R9/R11): EAX/ECX/EDX/EBX/R12D-R15D hold guest state
        /// that the slow path must be able to sync intact. An earlier version
        /// used RAX/RCX/RDX and silently corrupted the guest on every probe
        /// miss.</para>
        /// </summary>
        private void EmitProbe(int sizeBytes, bool isStore, Label slow)
        {
            Assembler a = _a;
            a.mov(R32(Register.R8D), R32(Register.R9D));
            a.shr(R32(Register.R8D), 12);
            a.mov(R64(Register.RDI), Field(Register.R11, OffXlatTag));
            a.mov(R32(Register.EDI), Indexed(Register.RDI, Register.R8, 4, 0));
            a.cmp(R32(Register.EDI), R32(Register.R8D));
            a.jne(slow);

            if (isStore)
            {
                a.mov(R64(Register.RBP), Field(Register.R11, OffXlatWritable));
                a.mov(new AssemblerRegister8(Register.DIL), Indexed(Register.RBP, Register.R8, 1, 0));
                a.test(new AssemblerRegister8(Register.DIL), new AssemblerRegister8(Register.DIL));
                a.je(slow);
            }

            a.mov(R64(Register.RDI), Field(Register.R11, OffXlatPhys));
            a.mov(R32(Register.EDI), Indexed(Register.RDI, Register.R8, 4, 0));
            a.mov(R32(Register.R10D), R32(Register.R9D));
            a.and(R32(Register.R10D), 0xFFF);
            a.add(R32(Register.EDI), R32(Register.R10D));

            a.mov(R32(Register.R10D), Field(Register.R11, OffRamSize));
            a.lea(R32(Register.EBP), MemOp(Register.RDI, null, 1, sizeBytes));
            a.cmp(R32(Register.EBP), R32(Register.R10D));
            a.ja(slow);
            a.mov(R64(Register.RBP), Field(Register.R11, OffRam));
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
            EmitAddress(plan);
            EmitMaterializeFlags();
            Label slow = a.CreateLabel(string.Empty);
            Label done = a.CreateLabel(string.Empty);
            EmitProbe(plan.Size / 8, isStore: false, slow);
            EmitMoveFromMemory(plan.Size, plan.RegIndex, RamAt());
            a.jmp(done);

            a.Label(ref slow);
            EmitMemoryHelper(plan, index, isStore: false);
            EmitMoveFromScratch(plan.Size, plan.RegIndex);

            a.Label(ref done);
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
            EmitAddress(plan);
            if (immediate)
            {
                EmitImmediateToValueScratch(plan.Size, unchecked((int)plan.Immediate));
            }
            else
            {
                EmitMoveToValueScratch(plan.Size, plan.RegIndex);
            }

            EmitMaterializeFlags();
            Label slow = a.CreateLabel(string.Empty);
            Label done = a.CreateLabel(string.Empty);
            EmitProbe(plan.Size / 8, isStore: true, slow);
            EmitMoveValueScratchToMemory(plan.Size, RamAt());
            a.jmp(done);

            a.Label(ref slow);
            EmitMemoryHelper(plan, index, isStore: true);

            a.Label(ref done);
        }

        /// <summary>Read-modify-write source form: <c>op reg, [mem]</c>.</summary>
        private void EmitAluMemorySource(in JitMemberPlan plan, int index)
        {
            Assembler a = _a;
            EmitAddress(plan);
            EmitMaterializeFlags();
            Label slow = a.CreateLabel(string.Empty);
            Label done = a.CreateLabel(string.Empty);
            EmitProbe(plan.Size / 8, isStore: false, slow);
            EmitMoveFromMemory(plan.Size, 0, RamAt(), scratchOnly: true);
            EmitAluScratch(plan);
            a.jmp(done);

            a.Label(ref slow);
            EmitMemoryHelper(plan, index, isStore: false);
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

            EmitMaterializeFlags();
            Label slow = a.CreateLabel(string.Empty);
            Label done = a.CreateLabel(string.Empty);
            EmitProbe(plan.Size / 8, isStore: false, slow);
            EmitMoveFromMemory(plan.Size, 0, RamAt(), scratchOnly: true);
            EmitCmpScratch(plan);
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

            EmitMaterializeFlags();
            Label slow = a.CreateLabel(string.Empty);
            Label done = a.CreateLabel(string.Empty);
            EmitProbe(plan.Size / 8, isStore: true, slow);
            EmitAluMemoryOp(plan, RamAt());
            a.jmp(done);

            a.Label(ref slow);
            EmitHelperMember(index);

            a.Label(ref done);
        }

        private void EmitTestMemory(in JitMemberPlan plan, int index)
        {
            Assembler a = _a;
            EmitAddress(plan);
            EmitMaterializeFlags();
            Label slow = a.CreateLabel(string.Empty);
            Label done = a.CreateLabel(string.Empty);
            EmitProbe(plan.Size / 8, isStore: false, slow);
            EmitMoveFromMemory(plan.Size, 0, RamAt(), scratchOnly: true);
            EmitTestScratch(plan);
            a.jmp(done);

            a.Label(ref slow);
            EmitHelperMember(index);

            a.Label(ref done);
        }

        private void EmitIncDecMemory(in JitMemberPlan plan, int index)
        {
            Assembler a = _a;
            EmitAddress(plan);
            EmitMaterializeFlags();
            Label slow = a.CreateLabel(string.Empty);
            Label done = a.CreateLabel(string.Empty);
            EmitProbe(plan.Size / 8, isStore: true, slow);
            EmitIncDecMemoryOp(plan, RamAt());
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
        private void EmitMemoryHelper(in JitMemberPlan plan, int index, bool isStore)
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
            return MemOp(baseRegister, indexRegister, plan.Scale, plan.Displacement);
        }
    }

    private sealed class BufferCodeWriter(byte* buffer, int capacity) : CodeWriter
    {
        private readonly byte* _buffer = buffer;
        private readonly int _capacity = capacity;
        private int _position;

        public int Position => _position;

        public override void WriteByte(byte value)
        {
            if (_position >= _capacity)
            {
                throw new InvalidOperationException("raw block exceeded its code buffer");
            }

            _buffer[_position++] = value;
        }
    }
}
