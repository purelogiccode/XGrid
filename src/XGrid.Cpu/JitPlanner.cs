using System.Reflection;

namespace XGrid.Cpu;

/// <summary>Native member shapes the fast-mode emitter can bake (J1b slice).</summary>
internal enum JitKind : byte
{
    None = 0,
    MovRR,
    MovRI,
    MovLoad,
    MovStore,
    MovStoreImm,
    AluRR,
    AluRI,
    AluRM,
    AluMR,
    AluMI,
    TestRR,
    TestRI,
    TestMI,
    FpuLoadReal,
    FpuStoreReal,
    FpuStoreRealPop,
    FpuLoadRegister,
    FpuStoreRegister,
    FpuStoreRegisterPop,
    FpuLoadInteger,
    FpuStoreInteger,
    FpuStoreIntegerPop,
    FpuArithReg,
    FpuArithPop,
    FpuArithMem,
    FpuExchange,
    FpuPushOne,
    FpuChangeSign,
    FpuCompareReg,
    FpuCompareMem,
    MmxLoad,
    MmxStore,
    SseArithReg,
    SseArithMem,
    SseUnaryReg,
    SseMovssReg,
    SseMovssMem,
    SseMovssStore,
    SseCvtReg,
    SseCvtMem,
    NegR,
    NotR,
    Bswap,
    XchgRR,
    Leave,
    PushM,
    Cdq,
    Setcc,
    IncR,
    DecR,
    IncM,
    DecM,
    Lea,
    MovExtend,
    ShiftRI,
    ShiftRC,
    PushR,
    PushI,
    PopR,
    CallRel,
    CallReg,
    CallMem,
    RetNear,
    JmpReg,
    JmpMem,
    JmpRel,
    JccRel,
    Cli,
    Sti,
    Cld,
    Std,
    CallSpec,
    CallInterpreter,
    Invlpg,
    Nop,
}

/// <summary>ALU operation selector baked into a plan (carry-free and carry-aware).</summary>
internal enum JitAluOp : byte
{
    None = 0,
    Add,
    Sub,
    And,
    Or,
    Xor,
    Cmp,
    Adc,
    Sbb,
}

/// <summary>Shift operation selector baked into a plan.</summary>
internal enum JitShiftOp : byte
{
    None = 0,
    Shl,
    Shr,
    Sar,
    Rol,
    Ror,
    Rcl,
    Rcr,
}

/// <summary>One planned block member: everything the emitter needs, baked.</summary>
internal readonly struct JitMemberPlan
{
    public JitKind Kind { get; init; }

    public int Length { get; init; }

    /// <summary>Operation size in bits (8/16/32).</summary>
    public int Size { get; init; }

    /// <summary>Source size for Movzx/Movsx (8 or 16).</summary>
    public int SourceSize { get; init; }

    /// <summary>Primary register (destination for loads/algebra, source for stores/push).</summary>
    public int RegIndex { get; init; }

    /// <summary>Second source register (register/register forms, Movzx/Movsx register source).</summary>
    public int SecondRegIndex { get; init; }

    /// <summary>Immediate operand (or branch displacement / shift count, as raw uint bits).</summary>
    public uint Immediate { get; init; }

    /// <summary>True when the member can enable interrupts (POPF) and must be followed by an IRQ poll.</summary>
    public bool PollAfter { get; init; }

    public JitAluOp AluOp { get; init; }

    public JitShiftOp ShiftOp { get; init; }

    public bool SignExtend { get; init; }

    public ConditionCode Condition { get; init; }

    /// <summary>Member index in the block's entry array (CallSpec fallback).</summary>
    public int EntryIndex { get; init; }

    /// <summary>
    /// C2 slice 1: the block's total guest-register write mask, stamped onto
    /// every plan at formation. The emitter stores this set (and only this
    /// set) at the block epilogue and at every mid-block sync; every register
    /// in it is loaded at the prologue, so an over-store writes back an
    /// unchanged value.
    /// </summary>
    public uint BlockWriteMask { get; init; }

    /// <summary>Resolved static spec handler (CallSpec fallback).</summary>
    public MethodInfo? SpecHandler { get; init; }

    public bool HasMemory { get; init; }

    public int BaseRegister { get; init; }

    public bool HasBase { get; init; }

    public int IndexRegister { get; init; }

    public bool HasIndex { get; init; }

    public int Scale { get; init; }

    public uint Displacement { get; init; }

    public SegmentRegister Segment { get; init; }

    public bool IsTerminator { get; init; }

    /// <summary>Source mnemonic (diagnostics: the slow-member tally).</summary>
    public Mnemonic Mnemonic { get; init; }
}

/// <summary>
/// Fast-mode plan resolver (J1b): maps a decoded instruction to a fully baked
/// native shape, or <see cref="JitKind.None"/> to end the block before it.
/// Covers 8/16/32-bit register/immediate/memory forms of the carry-free and
/// carry-aware ALU, test, inc/dec, mov/mov-extend, lea, immediate shifts, and
/// the near stack/control set (push/pop/call/ret/jmp). 16-bit addressing and
/// segment overrides keep the member path, as does everything unlisted.
/// Semantics mirror the shapes in <c>XboxCpuSpecExec</c>/<c>XboxCpu</c>
/// exactly (AluOps, ShiftOps, Push/Pop order, mov-extend truncation).
/// </summary>
internal static class JitPlanner
{

    /// <summary>
    /// True when the member runs a spec handler (or interpreter entry) that
    /// advances EIP itself; the runners must not advance it again.
    /// </summary>
    public static bool HandlerOwnsEip(JitKind kind) => kind is
        JitKind.CallSpec or
        JitKind.FpuLoadRegister or
        JitKind.FpuStoreRegister or
        JitKind.FpuStoreRegisterPop or
        JitKind.FpuLoadInteger or
        JitKind.FpuStoreInteger or
        JitKind.FpuStoreIntegerPop or
        JitKind.FpuArithReg or
        JitKind.FpuArithPop or
        JitKind.FpuArithMem or
        JitKind.FpuExchange or
        JitKind.FpuPushOne or
        JitKind.FpuChangeSign or
        JitKind.FpuCompareReg or
        JitKind.FpuCompareMem or
        JitKind.PushM or
        JitKind.Cdq or
        JitKind.Setcc or
        JitKind.MmxLoad or
        JitKind.MmxStore or
        JitKind.SseArithReg or
        JitKind.SseArithMem or
        JitKind.SseUnaryReg or
        JitKind.SseMovssReg or
        JitKind.SseMovssMem or
        JitKind.SseMovssStore or
        JitKind.SseCvtReg or
        JitKind.SseCvtMem or
        JitKind.NegR or
        JitKind.NotR or
        JitKind.Bswap or
        JitKind.XchgRR or
        JitKind.Leave;

    public static JitMemberPlan Plan(in DecodedInstruction instruction)
    {
        JitMemberPlan plan = PlanNative(instruction);
        if (plan.Kind != JitKind.None)
        {
            return plan;
        }

        return PlanSpecFallback(instruction);
    }

    /// <summary>
    /// Fallback member: any valid non-control-transfer instruction with a
    /// static spec handler runs through that handler inside the runner after
    /// a full context sync (and a state reload after), so blocks are not
    /// shattered by FP/SSE/exotic data ops. Control transfers (including
    /// everything that can change CS or halt) are deliberately excluded —
    /// they must end the block, not become members.
    /// </summary>
    private static JitMemberPlan PlanSpecFallback(in DecodedInstruction instruction)
    {
        if (!instruction.IsValid || IsControlTransfer(instruction.Mnemonic))
        {
            return default;
        }

        XboxCpu.SpecHandler? handler = XboxCpu.ResolveSpec(instruction);
        if (handler is null || handler.Target is not null)
        {
            return default;
        }

        MethodInfo method = handler.Method;
        if (!method.IsStatic || method.ReturnType != typeof(void) || method.GetParameters().Length != 2)
        {
            return default;
        }

        return new JitMemberPlan
        {
            Kind = JitKind.CallSpec,
            Length = instruction.Length,
            SpecHandler = method,

            // POPF can turn interrupts on; the emitted member is followed by
            // the post-member IRQ poll (mirroring the interpreter's next
            // instruction boundary).
            PollAfter = instruction.Mnemonic == Mnemonic.Popf,
        };
    }

    private static bool IsControlTransfer(Mnemonic mnemonic)
    {
        // Control transfers and CS-affecting ops must end the block (they
        // re-target execution or change the fetch basis).
        return mnemonic switch
        {
            Mnemonic.Jcc or Mnemonic.Jmp or Mnemonic.JmpFar or Mnemonic.Call or Mnemonic.CallFar
                or Mnemonic.Ret or Mnemonic.Retf or Mnemonic.Iret or Mnemonic.Loop or Mnemonic.LoopE
                or Mnemonic.LoopNe or Mnemonic.Jecxz or Mnemonic.Int3 or Mnemonic.Interrupt
                or Mnemonic.Hlt or Mnemonic.MovSreg or Mnemonic.PopSegment => true,
            _ => false,
        };
    }

    private static JitMemberPlan PlanNative(in DecodedInstruction instruction)
    {
        return instruction.Mnemonic switch
        {
            Mnemonic.Mov => PlanMov(instruction),
            Mnemonic.Add => PlanAlu(instruction, JitAluOp.Add),
            Mnemonic.Sub => PlanAlu(instruction, JitAluOp.Sub),
            Mnemonic.And => PlanAlu(instruction, JitAluOp.And),
            Mnemonic.Or => PlanAlu(instruction, JitAluOp.Or),
            Mnemonic.Xor => PlanAlu(instruction, JitAluOp.Xor),
            Mnemonic.Cmp => PlanAlu(instruction, JitAluOp.Cmp),
            Mnemonic.Adc => PlanAlu(instruction, JitAluOp.Adc),
            Mnemonic.Sbb => PlanAlu(instruction, JitAluOp.Sbb),
            Mnemonic.Test => PlanTest(instruction),
            Mnemonic.Fld => PlanFpuReal(instruction, JitKind.FpuLoadReal),
            Mnemonic.Fst => PlanFpuReal(instruction, JitKind.FpuStoreReal),
            Mnemonic.Fstp => PlanFpuReal(instruction, JitKind.FpuStoreRealPop),
            Mnemonic.Fild => PlanFpuInteger(instruction, JitKind.FpuLoadInteger),
            Mnemonic.Fist => PlanFpuInteger(instruction, JitKind.FpuStoreInteger),
            Mnemonic.Fistp => PlanFpuInteger(instruction, JitKind.FpuStoreIntegerPop),
            Mnemonic.Fadd => PlanFpuArithmetic(instruction),
            Mnemonic.Fmul => PlanFpuArithmetic(instruction),
            Mnemonic.Fsub => PlanFpuArithmetic(instruction),
            Mnemonic.Fsubr => PlanFpuArithmetic(instruction),
            Mnemonic.Fdiv => PlanFpuArithmetic(instruction),
            Mnemonic.Fdivr => PlanFpuArithmetic(instruction),
            Mnemonic.Faddp => PlanFpuArithmeticPop(instruction),
            Mnemonic.Fmulp => PlanFpuArithmeticPop(instruction),
            Mnemonic.Fsubp => PlanFpuArithmeticPop(instruction),
            Mnemonic.Fsubrp => PlanFpuArithmeticPop(instruction),
            Mnemonic.Fdivp => PlanFpuArithmeticPop(instruction),
            Mnemonic.Fdivrp => PlanFpuArithmeticPop(instruction),
            Mnemonic.Movq => PlanMmx(instruction, store: false),
            Mnemonic.Movntq => PlanMmx(instruction, store: true),
            Mnemonic.Fxch => PlanFpuExchange(instruction),
            Mnemonic.Fld1 => PlanFpuPushOne(instruction),
            Mnemonic.Fchs => PlanFpuChangeSign(instruction),
            Mnemonic.Fcom => PlanFpuCompare(instruction, pop: false),
            Mnemonic.Fucom => PlanFpuCompare(instruction, pop: false),
            Mnemonic.Fcomp => PlanFpuCompare(instruction, pop: true),
            Mnemonic.Fucomp => PlanFpuCompare(instruction, pop: true),
            Mnemonic.Fiadd => PlanFpuIntegerArithmetic(instruction),
            Mnemonic.Fimul => PlanFpuIntegerArithmetic(instruction),
            Mnemonic.Fisub => PlanFpuIntegerArithmetic(instruction),
            Mnemonic.Fisubr => PlanFpuIntegerArithmetic(instruction),
            Mnemonic.Fidiv => PlanFpuIntegerArithmetic(instruction),
            Mnemonic.Fidivr => PlanFpuIntegerArithmetic(instruction),
            Mnemonic.Addss => PlanSseArithmetic(instruction, 0),
            Mnemonic.Subss => PlanSseArithmetic(instruction, 1),
            Mnemonic.Mulss => PlanSseArithmetic(instruction, 2),
            Mnemonic.Divss => PlanSseArithmetic(instruction, 3),
            Mnemonic.Minss => PlanSseArithmetic(instruction, 4),
            Mnemonic.Maxss => PlanSseArithmetic(instruction, 5),
            Mnemonic.Rsqrtss => PlanSseUnary(instruction),
            Mnemonic.Movss => PlanSseMovss(instruction),
            Mnemonic.Cvttss2Si => PlanSseCvt(instruction),
            Mnemonic.Neg => PlanUnary(instruction, JitKind.NegR),
            Mnemonic.Not => PlanUnary(instruction, JitKind.NotR),
            Mnemonic.Bswap => PlanUnary(instruction, JitKind.Bswap),
            Mnemonic.Xchg => PlanXchg(instruction),
            Mnemonic.Leave => WithSpecHandler(
                new JitMemberPlan { Kind = JitKind.Leave, Length = instruction.Length },
                instruction),
            Mnemonic.Cdq => instruction.OperandSize == 32
                ? WithSpecHandler(
                    new JitMemberPlan { Kind = JitKind.Cdq, Length = instruction.Length, Size = 32 },
                    instruction)
                : default,
            Mnemonic.Setcc => PlanSetcc(instruction),
            Mnemonic.Inc => PlanIncDec(instruction, inc: true),
            Mnemonic.Dec => PlanIncDec(instruction, inc: false),
            Mnemonic.Lea => PlanLea(instruction),
            Mnemonic.Movzx => PlanExtend(instruction, signExtend: false),
            Mnemonic.Movsx => PlanExtend(instruction, signExtend: true),
            Mnemonic.Shl => PlanShift(instruction, JitShiftOp.Shl),
            Mnemonic.Shr => PlanShift(instruction, JitShiftOp.Shr),
            Mnemonic.Sar => PlanShift(instruction, JitShiftOp.Sar),
            Mnemonic.Rol => PlanShift(instruction, JitShiftOp.Rol),
            Mnemonic.Ror => PlanShift(instruction, JitShiftOp.Ror),
            Mnemonic.Rcl => PlanShift(instruction, JitShiftOp.Rcl),
            Mnemonic.Rcr => PlanShift(instruction, JitShiftOp.Rcr),
            Mnemonic.Push => PlanPush(instruction),
            Mnemonic.Pop => PlanPop(instruction),
            Mnemonic.Call => PlanCall(instruction),
            Mnemonic.Ret => PlanRet(instruction),
            Mnemonic.Jmp => PlanJmp(instruction),
            Mnemonic.Jcc => PlanJcc(instruction),
            Mnemonic.Cli => new JitMemberPlan { Kind = JitKind.Cli, Length = instruction.Length },
            Mnemonic.Sti => new JitMemberPlan { Kind = JitKind.Sti, Length = instruction.Length },
            Mnemonic.Cld => new JitMemberPlan { Kind = JitKind.Cld, Length = instruction.Length },
            Mnemonic.Std => new JitMemberPlan { Kind = JitKind.Std, Length = instruction.Length },
            Mnemonic.Movs => PlanString(instruction, JitKind.CallInterpreter),
            Mnemonic.Stos => PlanString(instruction, JitKind.CallInterpreter),
            Mnemonic.Cmps => PlanString(instruction, JitKind.CallInterpreter),
            Mnemonic.Scas => PlanString(instruction, JitKind.CallInterpreter),
            Mnemonic.Lods => PlanString(instruction, JitKind.CallInterpreter),
            Mnemonic.Ins => PlanString(instruction, JitKind.CallInterpreter),
            Mnemonic.Outs => PlanString(instruction, JitKind.CallInterpreter),
            Mnemonic.In => PlanPortIo(instruction, ExecuteInMethod),
            Mnemonic.Out => PlanPortIo(instruction, ExecuteOutMethod),
            Mnemonic.Invlpg => PlanInvlpg(instruction),
            Mnemonic.Nop => new JitMemberPlan { Kind = JitKind.Nop, Length = instruction.Length },
            // Prefetch is architecturally a hint: the interpreter's ExecuteSse
            // treats it as a no-op (no fault, no memory access). Baking it as
            // Nop keeps the strong memory ordering the guest could rely on
            // (none - it is a hint) and removes the last common
            // fence/prefetch helper member from the dashboard's copy loops.
            Mnemonic.Prefetch => new JitMemberPlan { Kind = JitKind.Nop, Length = instruction.Length },
            _ => default,
        };
    }

    /// <summary>
    /// C2 slice 3: single-precision memory real forms (Fld/Fst/Fstp m32) run
    /// natively through the same RAM probe as the integer loads/stores; the
    /// FPU side is one helper call that mirrors ReadFpuReal/WriteFpuReal
    /// exactly (the demo/dashboard float math is 32-bit). Wider 64/80-bit and
    /// register forms keep the spec handler.
    /// </summary>
    private static JitMemberPlan PlanFpuReal(in DecodedInstruction instruction, JitKind kind)
    {
        Operand first = instruction.Operand1;
        if (first.Kind == OperandKind.Register && first.Size == 80)
        {
            // C3-x: register forms (Fld/Fst/Fstp st(i)) are one tiny FPU
            // helper call each - no memory probe, no full context sync.
            JitKind registerKind = kind switch
            {
                JitKind.FpuLoadReal => JitKind.FpuLoadRegister,
                JitKind.FpuStoreReal => JitKind.FpuStoreRegister,
                JitKind.FpuStoreRealPop => JitKind.FpuStoreRegisterPop,
                _ => JitKind.None,
            };
            if (registerKind == JitKind.None)
            {
                return default;
            }

            return WithSpecHandler(
                new JitMemberPlan
                {
                    Kind = registerKind,
                    Length = instruction.Length,
                    Size = 80,
                    RegIndex = first.RegisterIndex,
                },
                instruction);
        }

        if (first.Kind != OperandKind.Memory || first.Size != 32)
        {
            return default;
        }

        MemoryOperand memory = first.Memory;
        JitMemberPlan plan = WithMemory(
            new JitMemberPlan { Kind = kind, Length = instruction.Length, Size = 32 },
            in memory);
        return plan.HasMemory ? WithSpecHandler(plan, instruction) : default;
    }

    /// <summary>
    /// C3-x: <c>Fild/Fist/Fistp m32</c> - the same RAM probe as a 32-bit load
    /// or store; the FPU conversion is one tiny helper (the IL runner keeps
    /// the exact spec handler, so both engines share one semantic source).
    /// </summary>
    private static JitMemberPlan PlanFpuInteger(in DecodedInstruction instruction, JitKind kind)
    {
        Operand first = instruction.Operand1;
        if (first.Kind != OperandKind.Memory || first.Size != 32)
        {
            return default;
        }

        JitMemberPlan plan = WithMemory(
            new JitMemberPlan { Kind = kind, Length = instruction.Length, Size = 32 },
            first.Memory);
        return plan.HasMemory ? WithSpecHandler(plan, instruction) : default;
    }

    /// <summary>
    /// C3-x: <c>movq mm, m64</c> / <c>movq m64, mm</c> / <c>movntq m64, mm</c>
    /// (MMX registers alias the x87 signature words). Register-to-register and
    /// GPR forms stay on the exact spec handler.
    /// </summary>
    private static JitMemberPlan PlanMmx(in DecodedInstruction instruction, bool store)
    {
        Operand first = instruction.Operand1;
        Operand second = instruction.Operand2;
        if (store)
        {
            if (first.Kind != OperandKind.Memory || first.Size != 64
                || second.Kind != OperandKind.Register || second.Size != 64)
            {
                return default;
            }

            JitMemberPlan stored = WithMemory(
                new JitMemberPlan
                {
                    Kind = JitKind.MmxStore,
                    Length = instruction.Length,
                    Size = 64,
                    RegIndex = second.RegisterIndex,
                },
                first.Memory);
            return stored.HasMemory ? WithSpecHandler(stored, instruction) : default;
        }

        if (first.Kind == OperandKind.Register && first.Size == 64
            && second.Kind == OperandKind.Memory && second.Size == 64)
        {
            JitMemberPlan loaded = WithMemory(
                new JitMemberPlan
                {
                    Kind = JitKind.MmxLoad,
                    Length = instruction.Length,
                    Size = 64,
                    RegIndex = first.RegisterIndex,
                },
                second.Memory);
            return loaded.HasMemory ? WithSpecHandler(loaded, instruction) : default;
        }

        if (first.Kind == OperandKind.Memory && first.Size == 64
            && second.Kind == OperandKind.Register && second.Size == 64)
        {
            JitMemberPlan stored = WithMemory(
                new JitMemberPlan
                {
                    Kind = JitKind.MmxStore,
                    Length = instruction.Length,
                    Size = 64,
                    RegIndex = second.RegisterIndex,
                },
                first.Memory);
            return stored.HasMemory ? WithSpecHandler(stored, instruction) : default;
        }

        return default;
    }

    /// <summary>
    /// Stamps the member's exact spec handler (the IL runner uses it for the
    /// shapes only the raw emitter bakes natively); a shape with no handler
    /// keeps the block boundary.
    /// </summary>
    private static JitMemberPlan WithSpecHandler(JitMemberPlan plan, in DecodedInstruction instruction)
    {
        MethodInfo? method = XboxCpu.ResolveSpec(instruction)?.Method;
        return method is null ? default : plan with { SpecHandler = method };
    }

    /// <summary>
    /// C3-x: <c>Fadd/Fmul/Fsub/Fsubr/Fdiv/Fdivr</c> in the register-register
    /// and 32/64-bit memory forms. The arithmetic is one tiny helper call with
    /// a packed operation descriptor, so the host x87 stack never leaves the
    /// managed FpuState (and every IL/raw differential compares the same
    /// bytes). Wider sources keep the exact interpreter path.
    /// </summary>
    private static JitMemberPlan PlanFpuArithmetic(in DecodedInstruction instruction)
    {
        Operand first = instruction.Operand1;
        Operand second = instruction.Operand2;
        if (first.Kind != OperandKind.Register || first.Size != 80)
        {
            return default;
        }

        if (second.Kind == OperandKind.Register && second.Size == 80)
        {
            return WithSpecHandler(
                new JitMemberPlan
                {
                    Kind = JitKind.FpuArithReg,
                    Length = instruction.Length,
                    Size = 80,
                    RegIndex = first.RegisterIndex,
                    SecondRegIndex = second.RegisterIndex,
                },
                instruction);
        }

        if (second.Kind == OperandKind.Memory && second.Size is 32 or 64)
        {
            JitMemberPlan plan = WithMemory(
                new JitMemberPlan
                {
                    Kind = JitKind.FpuArithMem,
                    Length = instruction.Length,
                    Size = 80,
                    SourceSize = second.Size,
                    RegIndex = first.RegisterIndex,
                },
                second.Memory);
            return plan.HasMemory ? WithSpecHandler(plan, instruction) : default;
        }

        return default;
    }

    /// <summary>C3-x: the popping register forms (<c>Faddp</c> family).</summary>
    private static JitMemberPlan PlanFpuArithmeticPop(in DecodedInstruction instruction)
    {
        Operand first = instruction.Operand1;
        if (first.Kind != OperandKind.Register || first.Size != 80)
        {
            return default;
        }

        return WithSpecHandler(
            new JitMemberPlan
            {
                Kind = JitKind.FpuArithPop,
                Length = instruction.Length,
                Size = 80,
                RegIndex = first.RegisterIndex,
            },
            instruction);
    }

    /// <summary>C3-x: <c>Fxch st(i)</c> - swap ST(0) with ST(i).</summary>
    private static JitMemberPlan PlanFpuExchange(in DecodedInstruction instruction)
    {
        Operand first = instruction.Operand1;
        if (first.Kind != OperandKind.Register || first.Size != 80)
        {
            return default;
        }

        return WithSpecHandler(
            new JitMemberPlan
            {
                Kind = JitKind.FpuExchange,
                Length = instruction.Length,
                Size = 80,
                RegIndex = first.RegisterIndex,
            },
            instruction);
    }

    /// <summary>C3-x: <c>Fld1</c> - push the constant 1.0.</summary>
    private static JitMemberPlan PlanFpuPushOne(in DecodedInstruction instruction)
    {
        return WithSpecHandler(
            new JitMemberPlan { Kind = JitKind.FpuPushOne, Length = instruction.Length, Size = 80 },
            instruction);
    }

    /// <summary>
    /// C3-x: scalar SSE arithmetic (<c>Addss/Subss/Mulss/Divss/Minss/Maxss</c>)
    /// in the register and m32 forms, evaluated by the same managed float
    /// expressions the interpreter uses (one tiny helper call).
    /// </summary>
    private static JitMemberPlan PlanSseArithmetic(in DecodedInstruction instruction, int op)
    {
        Operand first = instruction.Operand1;
        Operand second = instruction.Operand2;
        if (first.Kind != OperandKind.Register || first.Size != 128)
        {
            return default;
        }

        if (second.Kind == OperandKind.Register && second.Size == 32)
        {
            return WithSpecHandler(
                new JitMemberPlan
                {
                    Kind = JitKind.SseArithReg,
                    Length = instruction.Length,
                    Size = 128,
                    RegIndex = first.RegisterIndex,
                    SecondRegIndex = second.RegisterIndex,
                    Immediate = (uint)op,
                },
                instruction);
        }

        if (second.Kind == OperandKind.Memory && second.Size == 32)
        {
            JitMemberPlan plan = WithMemory(
                new JitMemberPlan
                {
                    Kind = JitKind.SseArithMem,
                    Length = instruction.Length,
                    Size = 128,
                    RegIndex = first.RegisterIndex,
                    Immediate = (uint)op,
                },
                second.Memory);
            return plan.HasMemory ? WithSpecHandler(plan, instruction) : default;
        }

        return default;
    }

    /// <summary>C3-x: scalar SSE unary (<c>Rsqrtss/Rcpss/Sqrtss</c>), register form.</summary>
    private static JitMemberPlan PlanSseUnary(in DecodedInstruction instruction)
    {
        Operand first = instruction.Operand1;
        Operand second = instruction.Operand2;
        if (first.Kind != OperandKind.Register || first.Size != 128
            || second.Kind != OperandKind.Register || second.Size != 32)
        {
            return default;
        }

        int op = instruction.Mnemonic switch
        {
            Mnemonic.Sqrtss => 0,
            Mnemonic.Rsqrtss => 1,
            _ => 2,
        };
        return WithSpecHandler(
            new JitMemberPlan
            {
                Kind = JitKind.SseUnaryReg,
                Length = instruction.Length,
                Size = 128,
                RegIndex = first.RegisterIndex,
                SecondRegIndex = second.RegisterIndex,
                Immediate = (uint)op,
            },
            instruction);
    }

    /// <summary>C3-x: <c>Movss</c> register/load/store forms (element 0 only).</summary>
    private static JitMemberPlan PlanSseMovss(in DecodedInstruction instruction)
    {
        Operand first = instruction.Operand1;
        Operand second = instruction.Operand2;
        if (first.Kind == OperandKind.Register && first.Size == 128)
        {
            if (second.Kind == OperandKind.Register && second.Size == 32)
            {
                return WithSpecHandler(
                    new JitMemberPlan
                    {
                        Kind = JitKind.SseMovssReg,
                        Length = instruction.Length,
                        Size = 128,
                        RegIndex = first.RegisterIndex,
                        SecondRegIndex = second.RegisterIndex,
                    },
                    instruction);
            }

            if (second.Kind == OperandKind.Memory && second.Size == 32)
            {
                JitMemberPlan plan = WithMemory(
                    new JitMemberPlan
                    {
                        Kind = JitKind.SseMovssMem,
                        Length = instruction.Length,
                        Size = 128,
                        RegIndex = first.RegisterIndex,
                    },
                    second.Memory);
                return plan.HasMemory ? WithSpecHandler(plan, instruction) : default;
            }

            return default;
        }

        if (first.Kind == OperandKind.Memory && first.Size == 32
            && second.Kind == OperandKind.Register && second.Size == 128)
        {
            JitMemberPlan plan = WithMemory(
                new JitMemberPlan
                {
                    Kind = JitKind.SseMovssStore,
                    Length = instruction.Length,
                    Size = 32,
                    RegIndex = second.RegisterIndex,
                },
                first.Memory);
            return plan.HasMemory ? WithSpecHandler(plan, instruction) : default;
        }

        return default;
    }

    /// <summary>C3-x: <c>Cvttss2Si r32, xmm/m32</c> (truncating conversion).</summary>
    private static JitMemberPlan PlanSseCvt(in DecodedInstruction instruction)
    {
        Operand first = instruction.Operand1;
        Operand second = instruction.Operand2;
        if (first.Kind != OperandKind.Register || first.Size != 32)
        {
            return default;
        }

        if (second.Kind == OperandKind.Register && second.Size == 32)
        {
            return WithSpecHandler(
                new JitMemberPlan
                {
                    Kind = JitKind.SseCvtReg,
                    Length = instruction.Length,
                    Size = 32,
                    RegIndex = first.RegisterIndex,
                    SecondRegIndex = second.RegisterIndex,
                },
                instruction);
        }

        if (second.Kind == OperandKind.Memory && second.Size == 32)
        {
            JitMemberPlan plan = WithMemory(
                new JitMemberPlan
                {
                    Kind = JitKind.SseCvtMem,
                    Length = instruction.Length,
                    Size = 32,
                    RegIndex = first.RegisterIndex,
                },
                second.Memory);
            return plan.HasMemory ? WithSpecHandler(plan, instruction) : default;
        }

        return default;
    }

    /// <summary>C3-x: flag-free register unaries (<c>Neg/Not/Bswap</c>).</summary>
    private static JitMemberPlan PlanUnary(in DecodedInstruction instruction, JitKind kind)
    {
        Operand first = instruction.Operand1;
        if (first.Kind != OperandKind.Register)
        {
            return default;
        }

        if (kind == JitKind.Bswap)
        {
            if (first.Size != 32)
            {
                return default;
            }
        }
        else if (!IsSized(first.Size))
        {
            return default;
        }

        return new JitMemberPlan
        {
            Kind = kind,
            Length = instruction.Length,
            Size = first.Size,
            RegIndex = first.RegisterIndex,
            SpecHandler = XboxCpu.ResolveSpec(instruction)?.Method,
        };
    }

    /// <summary>C3-x: <c>Xchg r32, r32</c> (the 32-bit register form only).</summary>
    private static JitMemberPlan PlanXchg(in DecodedInstruction instruction)
    {
        Operand first = instruction.Operand1;
        Operand second = instruction.Operand2;
        if (first.Kind == OperandKind.Register && first.Size == 32
            && second.Kind == OperandKind.Register && second.Size == 32)
        {
            return WithSpecHandler(
                new JitMemberPlan
                {
                    Kind = JitKind.XchgRR,
                    Length = instruction.Length,
                    Size = 32,
                    RegIndex = first.RegisterIndex,
                    SecondRegIndex = second.RegisterIndex,
                },
                instruction);
        }

        return default;
    }

    /// <summary>C3-x: <c>Fchs</c> - negate ST(0).</summary>
    private static JitMemberPlan PlanFpuChangeSign(in DecodedInstruction instruction)
    {
        return WithSpecHandler(
            new JitMemberPlan { Kind = JitKind.FpuChangeSign, Length = instruction.Length, Size = 80 },
            instruction);
    }

    /// <summary>C3-x: <c>Fcom/Fcomp/Fucom/Fucomp</c> register and m32/m64 forms.</summary>
    private static JitMemberPlan PlanFpuCompare(in DecodedInstruction instruction, bool pop)
    {
        Operand compared = instruction.Operand2.Kind == OperandKind.None
            ? instruction.Operand1
            : instruction.Operand2;
        if (compared.Kind == OperandKind.Register && compared.Size == 80)
        {
            return WithSpecHandler(
                new JitMemberPlan
                {
                    Kind = JitKind.FpuCompareReg,
                    Length = instruction.Length,
                    Size = 80,
                    RegIndex = compared.RegisterIndex,
                    Immediate = pop ? 1u : 0u,
                },
                instruction);
        }

        if (compared.Kind == OperandKind.Memory && compared.Size is 32 or 64)
        {
            JitMemberPlan plan = WithMemory(
                new JitMemberPlan
                {
                    Kind = JitKind.FpuCompareMem,
                    Length = instruction.Length,
                    Size = 80,
                    SourceSize = compared.Size,
                    Immediate = pop ? 1u : 0u,
                },
                compared.Memory);
            return plan.HasMemory ? WithSpecHandler(plan, instruction) : default;
        }

        return default;
    }

    /// <summary>C3-x: <c>Fiadd/Fimul/Fisub/Fisubr/Fidiv/Fidivr m16/m32</c>.</summary>
    private static JitMemberPlan PlanFpuIntegerArithmetic(in DecodedInstruction instruction)
    {
        Operand first = instruction.Operand1;
        if (first.Kind != OperandKind.Memory || first.Size is not (16 or 32))
        {
            return default;
        }

        JitMemberPlan plan = WithMemory(
            new JitMemberPlan
            {
                Kind = JitKind.FpuArithMem,
                Length = instruction.Length,
                Size = 80,
                SourceSize = first.Size,
                RegIndex = 0,
            },
            first.Memory);
        return plan.HasMemory ? WithSpecHandler(plan, instruction) : default;
    }

    /// <summary>C3-x: <c>Setcc r8</c> (register destination only).</summary>
    private static JitMemberPlan PlanSetcc(in DecodedInstruction instruction)
    {
        Operand first = instruction.Operand1;
        if (first.Kind != OperandKind.Register || first.Size != 8)
        {
            return default;
        }

        return WithSpecHandler(
            new JitMemberPlan
            {
                Kind = JitKind.Setcc,
                Length = instruction.Length,
                Size = 8,
                RegIndex = first.RegisterIndex,
                Condition = instruction.Condition,
            },
            instruction);
    }

    private static JitMemberPlan PlanMov(in DecodedInstruction instruction)
    {
        Operand first = instruction.Operand1;
        Operand second = instruction.Operand2;
        if (first.Kind == OperandKind.Register && second.Kind == OperandKind.Register
            && IsSized(first.Size))
        {
            return new JitMemberPlan
            {
                Kind = JitKind.MovRR,
                Length = instruction.Length,
                Size = first.Size,
                RegIndex = first.RegisterIndex,
                SecondRegIndex = second.RegisterIndex,
            };
        }

        if (first.Kind == OperandKind.Register && second.Kind == OperandKind.Immediate
            && IsSized(first.Size))
        {
            return new JitMemberPlan
            {
                Kind = JitKind.MovRI,
                Length = instruction.Length,
                Size = first.Size,
                RegIndex = first.RegisterIndex,
                Immediate = second.Immediate,
            };
        }

        if (first.Kind == OperandKind.Register && second.Kind == OperandKind.Memory
            && IsSized(first.Size))
        {
            var plan = new JitMemberPlan
            {
                Kind = JitKind.MovLoad,
                Length = instruction.Length,
                Size = first.Size,
                RegIndex = first.RegisterIndex,
            };
            return WithMemory(plan, second.Memory);
        }

        if (first.Kind == OperandKind.Memory && second.Kind == OperandKind.Register
            && IsSized(second.Size))
        {
            var plan = new JitMemberPlan
            {
                Kind = JitKind.MovStore,
                Length = instruction.Length,
                Size = second.Size,
                RegIndex = second.RegisterIndex,
            };
            return WithMemory(plan, first.Memory);
        }

        if (first.Kind == OperandKind.Memory && second.Kind == OperandKind.Immediate
            && IsSized(first.Size))
        {
            var plan = new JitMemberPlan
            {
                Kind = JitKind.MovStoreImm,
                Length = instruction.Length,
                Size = first.Size,
                Immediate = second.Immediate,
            };
            return WithMemory(plan, first.Memory);
        }

        return default;
    }

    private static JitMemberPlan PlanAlu(in DecodedInstruction instruction, JitAluOp op)
    {
        Operand first = instruction.Operand1;
        Operand second = instruction.Operand2;
        if (first.Kind == OperandKind.Register && second.Kind == OperandKind.Register
            && IsSized(first.Size))
        {
            return new JitMemberPlan
            {
                Kind = JitKind.AluRR,
                Length = instruction.Length,
                Size = first.Size,
                RegIndex = first.RegisterIndex,
                SecondRegIndex = second.RegisterIndex,
                AluOp = op,
            };
        }

        if (first.Kind == OperandKind.Register && second.Kind == OperandKind.Immediate
            && IsSized(first.Size))
        {
            return new JitMemberPlan
            {
                Kind = JitKind.AluRI,
                Length = instruction.Length,
                Size = first.Size,
                RegIndex = first.RegisterIndex,
                Immediate = second.Immediate,
                AluOp = op,
            };
        }

        if (first.Kind == OperandKind.Register && second.Kind == OperandKind.Memory
            && IsSized(first.Size))
        {
            var plan = new JitMemberPlan
            {
                Kind = JitKind.AluRM,
                Length = instruction.Length,
                Size = first.Size,
                RegIndex = first.RegisterIndex,
                AluOp = op,
            };
            return WithMemory(plan, second.Memory);
        }

        if (first.Kind == OperandKind.Memory && second.Kind == OperandKind.Register
            && IsSized(second.Size))
        {
            var plan = new JitMemberPlan
            {
                Kind = JitKind.AluMR,
                Length = instruction.Length,
                Size = second.Size,
                RegIndex = second.RegisterIndex,
                AluOp = op,
            };
            return WithMemory(plan, first.Memory);
        }

        if (first.Kind == OperandKind.Memory && second.Kind == OperandKind.Immediate
            && IsSized(first.Size))
        {
            var plan = new JitMemberPlan
            {
                Kind = JitKind.AluMI,
                Length = instruction.Length,
                Size = first.Size,
                Immediate = second.Immediate,
                AluOp = op,
            };
            return WithMemory(plan, first.Memory);
        }

        return default;
    }

    private static JitMemberPlan PlanTest(in DecodedInstruction instruction)
    {
        Operand first = instruction.Operand1;
        Operand second = instruction.Operand2;
        if (first.Kind == OperandKind.Register && second.Kind == OperandKind.Register
            && IsSized(first.Size))
        {
            return new JitMemberPlan
            {
                Kind = JitKind.TestRR,
                Length = instruction.Length,
                Size = first.Size,
                RegIndex = first.RegisterIndex,
                SecondRegIndex = second.RegisterIndex,
            };
        }

        if (first.Kind == OperandKind.Register && second.Kind == OperandKind.Immediate
            && IsSized(first.Size))
        {
            return new JitMemberPlan
            {
                Kind = JitKind.TestRI,
                Length = instruction.Length,
                Size = first.Size,
                RegIndex = first.RegisterIndex,
                Immediate = second.Immediate,
            };
        }

        if (first.Kind == OperandKind.Memory && second.Kind == OperandKind.Immediate
            && IsSized(first.Size))
        {
            var plan = new JitMemberPlan
            {
                Kind = JitKind.TestMI,
                Length = instruction.Length,
                Size = first.Size,
                Immediate = second.Immediate,
            };
            return WithMemory(plan, first.Memory);
        }

        return default;
    }

    private static JitMemberPlan PlanIncDec(in DecodedInstruction instruction, bool inc)
    {
        Operand first = instruction.Operand1;
        if (first.Kind == OperandKind.Register && IsSized(first.Size))
        {
            return new JitMemberPlan
            {
                Kind = inc ? JitKind.IncR : JitKind.DecR,
                Length = instruction.Length,
                Size = first.Size,
                RegIndex = first.RegisterIndex,
            };
        }

        if (first.Kind == OperandKind.Memory && IsSized(first.Size))
        {
            var plan = new JitMemberPlan
            {
                Kind = inc ? JitKind.IncM : JitKind.DecM,
                Length = instruction.Length,
                Size = first.Size,
            };
            return WithMemory(plan, first.Memory);
        }

        return default;
    }

    private static JitMemberPlan PlanLea(in DecodedInstruction instruction)
    {
        Operand first = instruction.Operand1;
        if (first.Kind == OperandKind.Register && instruction.Operand2.Kind == OperandKind.Memory
            && (first.Size == 16 || first.Size == 32))
        {
            var plan = new JitMemberPlan
            {
                Kind = JitKind.Lea,
                Length = instruction.Length,
                Size = first.Size,
                RegIndex = first.RegisterIndex,
            };
            return WithMemory(plan, instruction.Operand2.Memory);
        }

        return default;
    }

    private static JitMemberPlan PlanExtend(in DecodedInstruction instruction, bool signExtend)
    {
        Operand first = instruction.Operand1;
        Operand second = instruction.Operand2;
        if (first.Kind != OperandKind.Register || first.Size != 32
            || second.Size is not (8 or 16))
        {
            return default;
        }

        if (second.Kind == OperandKind.Register)
        {
            return new JitMemberPlan
            {
                Kind = JitKind.MovExtend,
                Length = instruction.Length,
                Size = 32,
                SourceSize = second.Size,
                RegIndex = first.RegisterIndex,
                SecondRegIndex = second.RegisterIndex,
                SignExtend = signExtend,
            };
        }

        if (second.Kind == OperandKind.Memory)
        {
            var plan = new JitMemberPlan
            {
                Kind = JitKind.MovExtend,
                Length = instruction.Length,
                Size = 32,
                SourceSize = second.Size,
                RegIndex = first.RegisterIndex,
                SignExtend = signExtend,
            };
            return WithMemory(plan, second.Memory);
        }

        return default;
    }

    private static JitMemberPlan PlanShift(in DecodedInstruction instruction, JitShiftOp op)
    {
        Operand first = instruction.Operand1;
        Operand second = instruction.Operand2;
        if (first.Kind == OperandKind.Register && second.Kind == OperandKind.Immediate
            && IsSized(first.Size))
        {
            return new JitMemberPlan
            {
                Kind = JitKind.ShiftRI,
                Length = instruction.Length,
                Size = first.Size,
                RegIndex = first.RegisterIndex,
                ShiftOp = op,
                Immediate = second.Immediate,
            };
        }

        if (first.Kind == OperandKind.Register && second.Kind == OperandKind.Register
            && second.Size == 8 && second.RegisterIndex == (int)Gpr.Ecx && IsSized(first.Size))
        {
            return new JitMemberPlan
            {
                Kind = JitKind.ShiftRC,
                Length = instruction.Length,
                Size = first.Size,
                RegIndex = first.RegisterIndex,
                ShiftOp = op,
            };
        }

        return default;
    }

    private static JitMemberPlan PlanPush(in DecodedInstruction instruction)
    {
        if (instruction.AddressSize != 32 || instruction.OperandSize != 32)
        {
            return default;
        }

        Operand first = instruction.Operand1;
        if (first.Kind == OperandKind.Register && first.Size == 32)
        {
            return new JitMemberPlan
            {
                Kind = JitKind.PushR,
                Length = instruction.Length,
                Size = 32,
                RegIndex = first.RegisterIndex,
            };
        }

        if (first.Kind == OperandKind.Immediate && first.Size == 32)
        {
            return new JitMemberPlan
            {
                Kind = JitKind.PushI,
                Length = instruction.Length,
                Size = 32,
                Immediate = first.Immediate,
            };
        }

        if (first.Kind == OperandKind.Memory && first.Size == 32)
        {
            JitMemberPlan plan = WithMemory(
                new JitMemberPlan { Kind = JitKind.PushM, Length = instruction.Length, Size = 32 },
                first.Memory);
            return plan.HasMemory ? WithSpecHandler(plan, instruction) : default;
        }

        return default;
    }

    private static JitMemberPlan PlanPop(in DecodedInstruction instruction)
    {
        if (instruction.AddressSize != 32 || instruction.OperandSize != 32)
        {
            return default;
        }

        Operand first = instruction.Operand1;
        if (first.Kind == OperandKind.Register && first.Size == 32)
        {
            return new JitMemberPlan
            {
                Kind = JitKind.PopR,
                Length = instruction.Length,
                Size = 32,
                RegIndex = first.RegisterIndex,
            };
        }

        return default;
    }

    /// <summary>The interpreter's exact string handler (rep loop and the
    /// single-iteration path both); strings used to reject heads outright.</summary>
    private static readonly MethodInfo ExecuteStringMethod =
        typeof(XboxCpu).GetMethod(
            "ExecuteString",
            BindingFlags.NonPublic | BindingFlags.Instance)!;

    /// <summary>The interpreter's exact port-input handler.</summary>
    private static readonly MethodInfo ExecuteInMethod =
        typeof(XboxCpu).GetMethod(
            "ExecuteIn",
            BindingFlags.NonPublic | BindingFlags.Instance)!;

    /// <summary>The interpreter's exact port-output handler.</summary>
    private static readonly MethodInfo ExecuteOutMethod =
        typeof(XboxCpu).GetMethod(
            "ExecuteOut",
            BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static JitMemberPlan PlanString(in DecodedInstruction instruction, JitKind kind)
    {
        if (instruction.AddressSize != 32 || instruction.OperandSize is not (8 or 16 or 32))
        {
            return default;
        }

        // Poll at the member's boundary (the STI rule): a rep can touch
        // device-mapped memory (Ins/Outs do port I/O outright), and the
        // interpreter would deliver a raised vector at the very next
        // instruction boundary, not at the block's end.
        return new JitMemberPlan
        {
            Kind = kind,
            Length = instruction.Length,
            Size = instruction.OperandSize,
            SpecHandler = ExecuteStringMethod,
            PollAfter = true,
        };
    }

    /// <summary>IN/OUT (non-string port I/O): the interpreter's exact handlers.
    /// Port I/O is how the guest pokes the PIC/PIT/devices, so a device-raised
    /// IRQ is likely imminent: poll at the member's boundary (the STI rule) so
    /// delivery matches the interpreter instead of waiting for the block end.</summary>
    private static JitMemberPlan PlanPortIo(in DecodedInstruction instruction, MethodInfo handler)
    {
        if (instruction.AddressSize != 32 || instruction.OperandSize is not (8 or 16 or 32))
        {
            return default;
        }

        return new JitMemberPlan
        {
            Kind = JitKind.CallInterpreter,
            Length = instruction.Length,
            Size = instruction.OperandSize,
            SpecHandler = handler,
            PollAfter = true,
        };
    }

    /// <summary>
    /// INVLPG: compute the operand address and drive the interpreter's exact
    /// page flush (decode cache + data-xlat invalidation through the MMU
    /// hook). It does not end the block: the flush only affects caches, and
    /// the running block's baked members stay semantically valid (every
    /// memory access re-translates; the next lookup revalidates or re-forms).
    /// </summary>
    private static JitMemberPlan PlanInvlpg(in DecodedInstruction instruction)
    {
        if (instruction.AddressSize != 32 || instruction.Operand1.Kind != OperandKind.Memory)
        {
            return default;
        }

        var plan = new JitMemberPlan
        {
            Kind = JitKind.Invlpg,
            Length = instruction.Length,
        };
        return WithMemory(plan, instruction.Operand1.Memory);
    }

    private static JitMemberPlan PlanCall(in DecodedInstruction instruction)
    {
        if (instruction.AddressSize != 32 || instruction.OperandSize != 32)
        {
            return default;
        }

        if (instruction.Operand1.Kind == OperandKind.Immediate)
        {
            return new JitMemberPlan
            {
                Kind = JitKind.CallRel,
                Length = instruction.Length,
                Size = 32,
                Immediate = instruction.Operand1.Immediate,
                IsTerminator = true,
            };
        }

        if (instruction.Operand1.Kind == OperandKind.Register && instruction.Operand1.Size == 32)
        {
            return new JitMemberPlan
            {
                Kind = JitKind.CallReg,
                Length = instruction.Length,
                Size = 32,
                RegIndex = instruction.Operand1.RegisterIndex,
                IsTerminator = true,
            };
        }

        if (instruction.Operand1.Kind == OperandKind.Memory && instruction.Operand1.Size == 32)
        {
            var plan = new JitMemberPlan
            {
                Kind = JitKind.CallMem,
                Length = instruction.Length,
                Size = 32,
                IsTerminator = true,
            };
            return WithMemory(plan, instruction.Operand1.Memory);
        }

        return default;
    }

    private static JitMemberPlan PlanRet(in DecodedInstruction instruction)
    {
        if (instruction.AddressSize != 32 || instruction.OperandSize != 32)
        {
            return default;
        }

        // Near return only (no far pointer on the stack).
        if (instruction.Operand1.Kind is OperandKind.Immediate || instruction.Operand1.Kind == OperandKind.None)
        {
            return new JitMemberPlan
            {
                Kind = JitKind.RetNear,
                Length = instruction.Length,
                Size = 32,
                Immediate = instruction.Operand1.Kind == OperandKind.Immediate
                    ? instruction.Operand1.Immediate
                    : 0,
                IsTerminator = true,
            };
        }

        return default;
    }

    private static JitMemberPlan PlanJmp(in DecodedInstruction instruction)
    {
        if (instruction.Operand1.Kind == OperandKind.Immediate)
        {
            return new JitMemberPlan
            {
                Kind = JitKind.JmpRel,
                Length = instruction.Length,
                Immediate = instruction.Operand1.Immediate,
                IsTerminator = true,
            };
        }

        if (instruction.Operand1.Kind == OperandKind.Register && instruction.Operand1.Size == 32)
        {
            return new JitMemberPlan
            {
                Kind = JitKind.JmpReg,
                Length = instruction.Length,
                RegIndex = instruction.Operand1.RegisterIndex,
                IsTerminator = true,
            };
        }

        if (instruction.Operand1.Kind == OperandKind.Memory && instruction.Operand1.Size == 32)
        {
            var plan = new JitMemberPlan
            {
                Kind = JitKind.JmpMem,
                Length = instruction.Length,
                Size = 32,
                IsTerminator = true,
            };
            return WithMemory(plan, instruction.Operand1.Memory);
        }

        return default;
    }

    private static JitMemberPlan PlanJcc(in DecodedInstruction instruction)
    {
        if (instruction.Operand1.Kind == OperandKind.Immediate)
        {
            return new JitMemberPlan
            {
                Kind = JitKind.JccRel,
                Length = instruction.Length,
                Immediate = instruction.Operand1.Immediate,
                Condition = instruction.Condition,
                IsTerminator = true,
            };
        }

        return default;
    }

    private static bool IsSized(int size)
    {
        return size is 8 or 16 or 32;
    }

    /// <summary>
    /// Fills the memory facts for an address-taking member. Rejected (None)
    /// for 16-bit addressing or any segment override — those keep the member
    /// path. The default segment mirrors <c>XboxCpu.DefaultSegment</c> for
    /// 32-bit addressing (SS for ESP/EBP-based, DS otherwise).
    /// </summary>
    private static JitMemberPlan WithMemory(JitMemberPlan plan, in MemoryOperand memory)
    {
        if (memory.Is16Bit || memory.SegmentOverride is not null)
        {
            return default;
        }

        SegmentRegister segment = memory.HasBase && (memory.Base == Gpr.Esp || memory.Base == Gpr.Ebp)
            ? SegmentRegister.Ss
            : SegmentRegister.Ds;
        return plan with
        {
            HasMemory = true,
            HasBase = memory.HasBase,
            BaseRegister = (int)memory.Base,
            HasIndex = memory.HasIndex,
            IndexRegister = (int)memory.Index,
            Scale = memory.Scale == 0 ? 1 : memory.Scale,
            Displacement = memory.Displacement,
            Segment = segment,
        };
    }
}
