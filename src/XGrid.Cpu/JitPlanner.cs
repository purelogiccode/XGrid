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
    IncR,
    DecR,
    IncM,
    DecM,
    Lea,
    MovExtend,
    ShiftRI,
    PushR,
    PushI,
    PopR,
    CallRel,
    RetNear,
    JmpReg,
    JmpRel,
    JccRel,
    Cli,
    Sti,
    CallSpec,
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

    public JitAluOp AluOp { get; init; }

    public JitShiftOp ShiftOp { get; init; }

    public bool SignExtend { get; init; }

    public ConditionCode Condition { get; init; }

    /// <summary>Member index in the block's entry array (CallSpec fallback).</summary>
    public int EntryIndex { get; init; }

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
        };
    }

    private static bool IsControlTransfer(Mnemonic mnemonic)
    {
        // Control transfers and CS-affecting ops must end the block (they
        // re-target execution or change the fetch basis), and POPF is
        // boundary-sensitive: it can turn interrupts on mid-block with no
        // poll behind it, so it stays a block-ender (the entry poll that
        // follows it then sees the restored IF, exactly like the
        // interpreter's next-instruction poll).
        return mnemonic switch
        {
            Mnemonic.Jcc or Mnemonic.Jmp or Mnemonic.JmpFar or Mnemonic.Call or Mnemonic.CallFar
                or Mnemonic.Ret or Mnemonic.Retf or Mnemonic.Iret or Mnemonic.Loop or Mnemonic.LoopE
                or Mnemonic.LoopNe or Mnemonic.Jecxz or Mnemonic.Int3 or Mnemonic.Interrupt
                or Mnemonic.Hlt or Mnemonic.MovSreg or Mnemonic.PopSegment or Mnemonic.Popf => true,
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
            Mnemonic.Nop => new JitMemberPlan { Kind = JitKind.Nop, Length = instruction.Length },
            _ => default,
        };
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

    private static JitMemberPlan PlanCall(in DecodedInstruction instruction)
    {
        if (instruction.AddressSize == 32 && instruction.OperandSize == 32
            && instruction.Operand1.Kind == OperandKind.Immediate)
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
