using System;
using System.Reflection;
using System.Reflection.Emit;
using XGrid.Memory;

namespace XGrid.Cpu;

/// <summary>Native member shapes the fast-mode emitter can bake (J1 slice).</summary>
internal enum JitKind : byte
{
    None = 0,
    MovRR32,
    MovRI32,
    MovLoad32,
    MovStore32,
    AluRR32,
    AluRI32,
    TestRR32,
    TestRI32,
    IncR32,
    DecR32,
    Nop,
    JmpRel,
    JccRel,
}

/// <summary>ALU operation selector baked into a plan.</summary>
internal enum JitAluOp : byte
{
    None = 0,
    Add,
    Sub,
    And,
    Or,
    Xor,
    Cmp,
}

/// <summary>One planned block member: everything the emitter needs, baked.</summary>
internal readonly struct JitMemberPlan
{
    public JitKind Kind { get; init; }

    public int Length { get; init; }

    /// <summary>Primary register (destination for loads/algebra, source for stores).</summary>
    public int RegIndex { get; init; }

    /// <summary>Second source register for register/register forms.</summary>
    public int SecondRegIndex { get; init; }

    /// <summary>Immediate operand (or branch displacement, as raw uint bits).</summary>
    public uint Immediate { get; init; }

    public JitAluOp AluOp { get; init; }

    public ConditionCode Condition { get; init; }

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
/// Fast-mode plan resolver: maps a decoded instruction to a fully baked
/// native shape, or <see cref="JitKind.None"/> to end the block before it.
/// Only 32-bit register/immediate forms, 32-bit addressing without a segment
/// override, and direct rel8/rel32 near Jmp/Jcc terminators are planned in
/// the J1 slice; everything else keeps the existing member path. Semantics
/// mirror the shapes in <c>XboxCpuSpecExec</c> exactly (inc/dec via
/// <see cref="AluOps"/>, test via <see cref="AluOps.Logical"/>, Jcc via the
/// same flag tests as <c>EvaluateCondition</c>).
/// </summary>
internal static class JitPlanner
{
    public static JitMemberPlan Plan(in DecodedInstruction instruction)
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
            Mnemonic.Test => PlanTest(instruction),
            Mnemonic.Inc => PlanIncDec(instruction, inc: true),
            Mnemonic.Dec => PlanIncDec(instruction, inc: false),
            Mnemonic.Nop => new JitMemberPlan { Kind = JitKind.Nop, Length = instruction.Length },
            Mnemonic.Jmp => PlanJmp(instruction),
            Mnemonic.Jcc => PlanJcc(instruction),
            _ => default,
        };
    }

    private static JitMemberPlan PlanMov(in DecodedInstruction instruction)
    {
        Operand first = instruction.Operand1;
        Operand second = instruction.Operand2;
        if (first.Kind == OperandKind.Register && second.Kind == OperandKind.Register && first.Size == 32)
        {
            return new JitMemberPlan
            {
                Kind = JitKind.MovRR32,
                Length = instruction.Length,
                RegIndex = first.RegisterIndex,
                SecondRegIndex = second.RegisterIndex,
            };
        }

        if (first.Kind == OperandKind.Register && second.Kind == OperandKind.Immediate && first.Size == 32)
        {
            return new JitMemberPlan
            {
                Kind = JitKind.MovRI32,
                Length = instruction.Length,
                RegIndex = first.RegisterIndex,
                Immediate = second.Immediate,
            };
        }

        if (first.Kind == OperandKind.Register && second.Kind == OperandKind.Memory && first.Size == 32)
        {
            var plan = new JitMemberPlan
            {
                Kind = JitKind.MovLoad32,
                Length = instruction.Length,
                RegIndex = first.RegisterIndex,
            };
            return WithMemory(plan, second.Memory);
        }

        if (first.Kind == OperandKind.Memory && second.Kind == OperandKind.Register && second.Size == 32)
        {
            var plan = new JitMemberPlan
            {
                Kind = JitKind.MovStore32,
                Length = instruction.Length,
                RegIndex = second.RegisterIndex,
            };
            return WithMemory(plan, first.Memory);
        }

        return default;
    }

    private static JitMemberPlan PlanAlu(in DecodedInstruction instruction, JitAluOp op)
    {
        Operand first = instruction.Operand1;
        Operand second = instruction.Operand2;
        if (first.Kind == OperandKind.Register && second.Kind == OperandKind.Register && first.Size == 32)
        {
            return new JitMemberPlan
            {
                Kind = JitKind.AluRR32,
                Length = instruction.Length,
                RegIndex = first.RegisterIndex,
                SecondRegIndex = second.RegisterIndex,
                AluOp = op,
            };
        }

        if (first.Kind == OperandKind.Register && second.Kind == OperandKind.Immediate && first.Size == 32)
        {
            return new JitMemberPlan
            {
                Kind = JitKind.AluRI32,
                Length = instruction.Length,
                RegIndex = first.RegisterIndex,
                Immediate = second.Immediate,
                AluOp = op,
            };
        }

        return default;
    }

    private static JitMemberPlan PlanTest(in DecodedInstruction instruction)
    {
        Operand first = instruction.Operand1;
        Operand second = instruction.Operand2;
        if (first.Kind == OperandKind.Register && second.Kind == OperandKind.Register && first.Size == 32)
        {
            return new JitMemberPlan
            {
                Kind = JitKind.TestRR32,
                Length = instruction.Length,
                RegIndex = first.RegisterIndex,
                SecondRegIndex = second.RegisterIndex,
            };
        }

        if (first.Kind == OperandKind.Register && second.Kind == OperandKind.Immediate && first.Size == 32)
        {
            return new JitMemberPlan
            {
                Kind = JitKind.TestRI32,
                Length = instruction.Length,
                RegIndex = first.RegisterIndex,
                Immediate = second.Immediate,
            };
        }

        return default;
    }

    private static JitMemberPlan PlanIncDec(in DecodedInstruction instruction, bool inc)
    {
        Operand first = instruction.Operand1;
        if (first.Kind == OperandKind.Register && first.Size == 32)
        {
            return new JitMemberPlan
            {
                Kind = inc ? JitKind.IncR32 : JitKind.DecR32,
                Length = instruction.Length,
                RegIndex = first.RegisterIndex,
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

/// <summary>
/// Fast-mode block emitter (J1 slice): builds one DynamicMethod per block that
/// keeps all eight GPRs, EIP, EFLAGS and TSC in IL locals across the whole
/// block and serves each member with baked native code. Memory members run an
/// inlined translate-cache probe + RAM-direct access; every non-fast case
/// (cache miss, non-RAM, cross-page, watched store, non-writable store with
/// WP set) takes the exact interpreter access path (ReadMemory/WriteMemory)
/// after syncing the locals back to the context, so faults stay precise and
/// every side effect keeps its legacy order. The only boundary relaxed
/// relative to the interpreter is the IRQ poll position: the caller polls
/// once per block (RunJitBlock), which the dual-mode contract permits; TSC,
/// EIP, faults and stores all stay per-instruction exact.
/// </summary>
internal static class JitEmitter
{
    /// <summary>Maximum members per emitted block.</summary>
    public const int MaxMembers = 32;

    private static readonly MethodInfo GetContext =
        typeof(XboxCpu).GetProperty("Context")!.GetMethod!;

    private static readonly MethodInfo GetEip =
        typeof(XboxContext).GetProperty("Eip")!.GetMethod!;

    private static readonly MethodInfo SetEip =
        typeof(XboxContext).GetProperty("Eip")!.SetMethod!;

    private static readonly MethodInfo GetEflags =
        typeof(XboxContext).GetProperty("Eflags")!.GetMethod!;

    private static readonly MethodInfo SetEflags =
        typeof(XboxContext).GetProperty("Eflags")!.SetMethod!;

    private static readonly MethodInfo GetTsc =
        typeof(XboxContext).GetProperty("Tsc")!.GetMethod!;

    private static readonly MethodInfo SetTsc =
        typeof(XboxContext).GetProperty("Tsc")!.SetMethod!;

    private static readonly MethodInfo GetCr0 =
        typeof(XboxContext).GetProperty("Cr0")!.GetMethod!;

    private static readonly MethodInfo GetSegmentBase =
        typeof(XboxContext).GetMethod("GetSegmentBase")!;

    private static readonly FieldInfo ContextRegisters =
        typeof(XboxContext).GetField("_registers", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static readonly FieldInfo DataXlatTag =
        typeof(XboxCpu).GetField("_dataXlatTag", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static readonly FieldInfo DataXlatPhys =
        typeof(XboxCpu).GetField("_dataXlatPhys", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static readonly FieldInfo DataXlatWritable =
        typeof(XboxCpu).GetField("_dataXlatWritable", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static readonly FieldInfo RamDirect =
        typeof(XboxCpu).GetField("_ramDirect", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static readonly FieldInfo RamSize =
        typeof(XboxCpu).GetField("_ramSize", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static readonly FieldInfo JitProgress =
        typeof(XboxCpu).GetField("_jitProgress", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static readonly MethodInfo IsWatched =
        typeof(XboxCpu).GetMethod("IsWatched", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static readonly MethodInfo NoteStorePhysical =
        typeof(XboxCpu).GetMethod("NoteStorePhysical", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static readonly MethodInfo ReadMemory =
        typeof(XboxCpu).GetMethod("ReadMemory", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static readonly MethodInfo WriteMemory =
        typeof(XboxCpu).GetMethod("WriteMemory", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static readonly MethodInfo RamRead32 =
        typeof(PhysicalMemory).GetMethod("Read32")!;

    private static readonly MethodInfo RamWrite32 =
        typeof(PhysicalMemory).GetMethod("Write32")!;

    private static readonly MethodInfo AluAdd =
        typeof(AluOps).GetMethod("Add")!;

    private static readonly MethodInfo AluSub =
        typeof(AluOps).GetMethod("Sub")!;

    private static readonly MethodInfo AluLogical =
        typeof(AluOps).GetMethod("Logical")!;

    private static readonly MethodInfo AluInc =
        typeof(AluOps).GetMethod("Inc")!;

    private static readonly MethodInfo AluDec =
        typeof(AluOps).GetMethod("Dec")!;

    /// <summary>
    /// Builds a runner for the given planned members, or null when the plan
    /// set is empty. The runner contract: budget &gt; 0; serves
    /// <c>min(budget, plans.Length)</c> members; syncs the full guest context
    /// on every exit and immediately before any interpreter-path access (so a
    /// thrown <c>PageFaultSignal</c> leaves the context at the faulting
    /// member's boundary).
    /// </summary>
    public static JitRun? Build(uint headLinear, ReadOnlySpan<JitMemberPlan> plans)
    {
        if (plans.Length == 0)
        {
            return null;
        }

        var method = new DynamicMethod(
            $"JitBlock_{headLinear:X8}_{plans.Length}",
            typeof(int),
            new[] { typeof(XboxCpu), typeof(int) },
            typeof(XboxCpu),
            skipVisibility: true);
        ILGenerator il = method.GetILGenerator();

        LocalBuilder context = il.DeclareLocal(typeof(XboxContext));
        var gprs = new LocalBuilder[8];
        for (int i = 0; i < gprs.Length; i++)
        {
            gprs[i] = il.DeclareLocal(typeof(uint));
        }

        LocalBuilder eip = il.DeclareLocal(typeof(uint));
        LocalBuilder flags = il.DeclareLocal(typeof(uint));
        LocalBuilder tsc = il.DeclareLocal(typeof(ulong));
        LocalBuilder served = il.DeclareLocal(typeof(int));
        LocalBuilder a1 = il.DeclareLocal(typeof(uint));
        LocalBuilder a2 = il.DeclareLocal(typeof(uint));
        LocalBuilder res = il.DeclareLocal(typeof(uint));
        LocalBuilder addr = il.DeclareLocal(typeof(uint));
        LocalBuilder vpn = il.DeclareLocal(typeof(uint));
        LocalBuilder phys = il.DeclareLocal(typeof(uint));
        LocalBuilder slot = il.DeclareLocal(typeof(int));
        LocalBuilder ram = il.DeclareLocal(typeof(PhysicalMemory));
        Label exit = il.DefineLabel();

        // Prologue: hoist the context and load the full cached register state.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, GetContext);
        il.Emit(OpCodes.Stloc, context);
        il.Emit(OpCodes.Ldloc, context);
        il.Emit(OpCodes.Ldfld, ContextRegisters);
        for (int i = 0; i < gprs.Length; i++)
        {
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4, i);
            il.Emit(OpCodes.Ldelem_I4);
            il.Emit(OpCodes.Stloc, gprs[i]);
        }

        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldloc, context);
        il.Emit(OpCodes.Callvirt, GetEip);
        il.Emit(OpCodes.Stloc, eip);
        il.Emit(OpCodes.Ldloc, context);
        il.Emit(OpCodes.Callvirt, GetEflags);
        il.Emit(OpCodes.Stloc, flags);
        il.Emit(OpCodes.Ldloc, context);
        il.Emit(OpCodes.Callvirt, GetTsc);
        il.Emit(OpCodes.Stloc, tsc);

        for (int m = 0; m < plans.Length; m++)
        {
            JitMemberPlan plan = plans[m];

            // Budget cap: served >= budget -> exit.
            il.Emit(OpCodes.Ldloc, served);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Bge, exit);

            // TSC++ precedes the member body, exactly as the member path.
            il.Emit(OpCodes.Ldloc, tsc);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Conv_I8);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Stloc, tsc);

            EmitMember(il, plan, context, gprs, eip, flags, tsc, served,
                a1, a2, res, addr, vpn, phys, slot, ram);

            il.Emit(OpCodes.Ldloc, served);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Stloc, served);
            if (plan.IsTerminator)
            {
                il.Emit(OpCodes.Br, exit);
            }
        }

        il.MarkLabel(exit);
        EmitStoreState(il, context, gprs, eip, flags, tsc);
        il.Emit(OpCodes.Ldloc, served);
        il.Emit(OpCodes.Ret);
        return (JitRun)method.CreateDelegate(typeof(JitRun));
    }

    private static void EmitMember(
        ILGenerator il,
        in JitMemberPlan plan,
        LocalBuilder context,
        LocalBuilder[] gprs,
        LocalBuilder eip,
        LocalBuilder flags,
        LocalBuilder tsc,
        LocalBuilder served,
        LocalBuilder a1,
        LocalBuilder a2,
        LocalBuilder res,
        LocalBuilder addr,
        LocalBuilder vpn,
        LocalBuilder phys,
        LocalBuilder slot,
        LocalBuilder ram)
    {
        switch (plan.Kind)
        {
            case JitKind.MovRR32:
                il.Emit(OpCodes.Ldloc, gprs[plan.SecondRegIndex]);
                il.Emit(OpCodes.Stloc, gprs[plan.RegIndex]);
                break;

            case JitKind.MovRI32:
                il.Emit(OpCodes.Ldc_I4, unchecked((int)plan.Immediate));
                il.Emit(OpCodes.Stloc, gprs[plan.RegIndex]);
                break;

            case JitKind.MovLoad32:
            case JitKind.MovStore32:
                EmitMemoryMember(il, plan, context, gprs, eip, flags, tsc, served,
                    a1, addr, vpn, phys, slot, ram);
                break;

            case JitKind.AluRR32:
            case JitKind.AluRI32:
                EmitAlu(il, plan, gprs, flags, a1, a2, res);
                break;

            case JitKind.TestRR32:
            case JitKind.TestRI32:
                EmitTest(il, plan, gprs, flags, a1, a2, res);
                break;

            case JitKind.IncR32:
            case JitKind.DecR32:
                EmitIncDec(il, plan, gprs, flags, res);
                break;

            case JitKind.Nop:
                break;

            case JitKind.JmpRel:
                il.Emit(OpCodes.Ldloc, eip);
                il.Emit(OpCodes.Ldc_I4, plan.Length);
                il.Emit(OpCodes.Add);
                il.Emit(OpCodes.Ldc_I4, unchecked((int)plan.Immediate));
                il.Emit(OpCodes.Add);
                il.Emit(OpCodes.Stloc, eip);
                break;

            case JitKind.JccRel:
                {
                    Label notTaken = il.DefineLabel();
                    Label done = il.DefineLabel();
                    EmitCondition(il, flags, plan.Condition);
                    il.Emit(OpCodes.Brfalse, notTaken);
                    il.Emit(OpCodes.Ldloc, eip);
                    il.Emit(OpCodes.Ldc_I4, plan.Length);
                    il.Emit(OpCodes.Add);
                    il.Emit(OpCodes.Ldc_I4, unchecked((int)plan.Immediate));
                    il.Emit(OpCodes.Add);
                    il.Emit(OpCodes.Stloc, eip);
                    il.Emit(OpCodes.Br, done);
                    il.MarkLabel(notTaken);
                    il.Emit(OpCodes.Ldloc, eip);
                    il.Emit(OpCodes.Ldc_I4, plan.Length);
                    il.Emit(OpCodes.Add);
                    il.Emit(OpCodes.Stloc, eip);
                    il.MarkLabel(done);
                    break;
                }

            default:
                throw new InvalidOperationException($"No emitter for kind {plan.Kind}.");
        }

        // Every non-terminator member whose EIP advance is not handled inside
        // its own emitter advances the local by the instruction length here.
        switch (plan.Kind)
        {
            case JitKind.MovLoad32:
            case JitKind.MovStore32:
            case JitKind.JmpRel:
            case JitKind.JccRel:
                break;
            default:
                il.Emit(OpCodes.Ldloc, eip);
                il.Emit(OpCodes.Ldc_I4, plan.Length);
                il.Emit(OpCodes.Add);
                il.Emit(OpCodes.Stloc, eip);
                break;
        }
    }

    private static void EmitAlu(
        ILGenerator il,
        in JitMemberPlan plan,
        LocalBuilder[] gprs,
        LocalBuilder flags,
        LocalBuilder a1,
        LocalBuilder a2,
        LocalBuilder res)
    {
        il.Emit(OpCodes.Ldloc, gprs[plan.RegIndex]);
        il.Emit(OpCodes.Stloc, a1);
        if (plan.Kind == JitKind.AluRR32)
        {
            il.Emit(OpCodes.Ldloc, gprs[plan.SecondRegIndex]);
        }
        else
        {
            il.Emit(OpCodes.Ldc_I4, unchecked((int)plan.Immediate));
        }

        il.Emit(OpCodes.Stloc, a2);
        switch (plan.AluOp)
        {
            case JitAluOp.Add:
            case JitAluOp.Sub:
            case JitAluOp.Cmp:
                EmitAluBinaryCall(
                    il,
                    plan.AluOp == JitAluOp.Add ? AluAdd : AluSub,
                    a1,
                    a2,
                    flags,
                    res);
                if (plan.AluOp != JitAluOp.Cmp)
                {
                    il.Emit(OpCodes.Ldloc, res);
                    il.Emit(OpCodes.Stloc, gprs[plan.RegIndex]);
                }

                break;

            default:
                {
                    // And/Or/Xor: compute the value, then AluOps.Logical.
                    il.Emit(OpCodes.Ldloc, a1);
                    il.Emit(OpCodes.Ldloc, a2);
                    il.Emit(plan.AluOp switch
                    {
                        JitAluOp.And => OpCodes.And,
                        JitAluOp.Or => OpCodes.Or,
                        _ => OpCodes.Xor,
                    });
                    il.Emit(OpCodes.Stloc, res);
                    EmitLogicalFlags(il, res, flags);
                    il.Emit(OpCodes.Ldloc, res);
                    il.Emit(OpCodes.Stloc, gprs[plan.RegIndex]);
                    break;
                }
        }
    }

    private static void EmitTest(
        ILGenerator il,
        in JitMemberPlan plan,
        LocalBuilder[] gprs,
        LocalBuilder flags,
        LocalBuilder a1,
        LocalBuilder a2,
        LocalBuilder res)
    {
        il.Emit(OpCodes.Ldloc, gprs[plan.RegIndex]);
        il.Emit(OpCodes.Stloc, a1);
        if (plan.Kind == JitKind.TestRR32)
        {
            il.Emit(OpCodes.Ldloc, gprs[plan.SecondRegIndex]);
        }
        else
        {
            il.Emit(OpCodes.Ldc_I4, unchecked((int)plan.Immediate));
        }

        il.Emit(OpCodes.Stloc, a2);
        il.Emit(OpCodes.Ldloc, a1);
        il.Emit(OpCodes.Ldloc, a2);
        il.Emit(OpCodes.And);
        il.Emit(OpCodes.Stloc, res);
        EmitLogicalFlags(il, res, flags);
    }

    private static void EmitIncDec(
        ILGenerator il,
        in JitMemberPlan plan,
        LocalBuilder[] gprs,
        LocalBuilder flags,
        LocalBuilder res)
    {
        il.Emit(OpCodes.Ldloc, gprs[plan.RegIndex]);
        il.Emit(OpCodes.Ldc_I4, 32);
        il.Emit(OpCodes.Ldloc, flags);
        il.Emit(OpCodes.Ldloca, res);
        il.Emit(OpCodes.Call, plan.Kind == JitKind.IncR32 ? AluInc : AluDec);
        il.Emit(OpCodes.Stloc, flags);
        il.Emit(OpCodes.Ldloc, res);
        il.Emit(OpCodes.Stloc, gprs[plan.RegIndex]);
    }

    private static void EmitAluBinaryCall(
        ILGenerator il,
        MethodInfo op,
        LocalBuilder a1,
        LocalBuilder a2,
        LocalBuilder flags,
        LocalBuilder res)
    {
        il.Emit(OpCodes.Ldloc, a1);
        il.Emit(OpCodes.Ldloc, a2);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldc_I4, 32);
        il.Emit(OpCodes.Ldloc, flags);
        il.Emit(OpCodes.Ldloca, res);
        il.Emit(OpCodes.Call, op);
        il.Emit(OpCodes.Stloc, flags);
    }

    private static void EmitLogicalFlags(ILGenerator il, LocalBuilder value, LocalBuilder flags)
    {
        il.Emit(OpCodes.Ldloc, value);
        il.Emit(OpCodes.Ldc_I4, 32);
        il.Emit(OpCodes.Ldloc, flags);
        il.Emit(OpCodes.Call, AluLogical);
        il.Emit(OpCodes.Stloc, flags);
    }

    /// <summary>
    /// Emits one 32-bit register/memory member: EIP store for fault precision,
    /// inlined translate-cache probe + RAM-direct access, and the exact
    /// interpreter access path on any non-fast case (with a full local->context
    /// sync so thrown faults see the member boundary exactly).
    /// </summary>
    private static void EmitMemoryMember(
        ILGenerator il,
        in JitMemberPlan plan,
        LocalBuilder context,
        LocalBuilder[] gprs,
        LocalBuilder eip,
        LocalBuilder flags,
        LocalBuilder tsc,
        LocalBuilder served,
        LocalBuilder a1,
        LocalBuilder addr,
        LocalBuilder vpn,
        LocalBuilder phys,
        LocalBuilder slot,
        LocalBuilder ram)
    {
        bool isStore = plan.Kind == JitKind.MovStore32;

        // Fault precision: the context carries the member EIP while its
        // access runs (handlers advance EIP only after completion).
        il.Emit(OpCodes.Ldloc, context);
        il.Emit(OpCodes.Ldloc, eip);
        il.Emit(OpCodes.Callvirt, SetEip);

        // addr = segmentBase + base + index*scale + disp (32-bit wrap).
        il.Emit(OpCodes.Ldloc, context);
        il.Emit(OpCodes.Ldc_I4, (int)plan.Segment);
        il.Emit(OpCodes.Call, GetSegmentBase);
        if (plan.HasBase)
        {
            il.Emit(OpCodes.Ldloc, gprs[plan.BaseRegister]);
            il.Emit(OpCodes.Add);
        }

        if (plan.HasIndex)
        {
            il.Emit(OpCodes.Ldloc, gprs[plan.IndexRegister]);
            if (plan.Scale != 1)
            {
                il.Emit(OpCodes.Ldc_I4, plan.Scale);
                il.Emit(OpCodes.Mul);
            }

            il.Emit(OpCodes.Add);
        }

        if (plan.Displacement != 0)
        {
            il.Emit(OpCodes.Ldc_I4, unchecked((int)plan.Displacement));
            il.Emit(OpCodes.Add);
        }

        il.Emit(OpCodes.Stloc, addr);

        Label slow = il.DefineLabel();
        Label done = il.DefineLabel();

        // Cross-page accesses keep the interpreter's per-byte order via the
        // member path (only 32-bit accesses can cross here).
        il.Emit(OpCodes.Ldloc, addr);
        il.Emit(OpCodes.Ldc_I4, 0xFFF);
        il.Emit(OpCodes.And);
        il.Emit(OpCodes.Ldc_I4, 0xFFC);
        il.Emit(OpCodes.Bgt_Un, slow);

        // vpn = addr >> 12; slot = vpn & 3; the tag must match.
        il.Emit(OpCodes.Ldloc, addr);
        il.Emit(OpCodes.Ldc_I4, 12);
        il.Emit(OpCodes.Shr_Un);
        il.Emit(OpCodes.Stloc, vpn);
        il.Emit(OpCodes.Ldloc, vpn);
        il.Emit(OpCodes.Ldc_I4_3);
        il.Emit(OpCodes.And);
        il.Emit(OpCodes.Stloc, slot);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, DataXlatTag);
        il.Emit(OpCodes.Ldloc, slot);
        il.Emit(OpCodes.Ldelem_I4);
        il.Emit(OpCodes.Ldloc, vpn);
        il.Emit(OpCodes.Bne_Un, slow);

        if (isStore)
        {
            // WP-gated unwritable stores fault through the member path
            // (TranslateLinear's cached-hit replay).
            Label writable = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, DataXlatWritable);
            il.Emit(OpCodes.Ldloc, slot);
            il.Emit(OpCodes.Ldelem_U1);
            il.Emit(OpCodes.Brtrue, writable);
            il.Emit(OpCodes.Ldloc, context);
            il.Emit(OpCodes.Callvirt, GetCr0);
            il.Emit(OpCodes.Ldc_I4, (int)ControlRegisterBits.Cr0WriteProtect);
            il.Emit(OpCodes.And);
            il.Emit(OpCodes.Brtrue, slow);
            il.MarkLabel(writable);
        }

        // phys = cached page base + page offset.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, DataXlatPhys);
        il.Emit(OpCodes.Ldloc, slot);
        il.Emit(OpCodes.Ldelem_I4);
        il.Emit(OpCodes.Ldloc, addr);
        il.Emit(OpCodes.Ldc_I4, 0xFFF);
        il.Emit(OpCodes.And);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, phys);

        // RAM-direct only: non-RAM or out-of-bounds takes the member path.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, RamDirect);
        il.Emit(OpCodes.Stloc, ram);
        il.Emit(OpCodes.Ldloc, ram);
        il.Emit(OpCodes.Brfalse, slow);
        il.Emit(OpCodes.Ldloc, phys);
        il.Emit(OpCodes.Conv_U8);
        il.Emit(OpCodes.Ldc_I4_4);
        il.Emit(OpCodes.Conv_U8);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, RamSize);
        il.Emit(OpCodes.Conv_U8);
        il.Emit(OpCodes.Bgt_Un, slow);

        if (isStore)
        {
            // Watched ranges keep the legacy bus path (CheckWatch + WatchHit).
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloc, phys);
            il.Emit(OpCodes.Ldc_I4, 4);
            il.Emit(OpCodes.Call, IsWatched);
            il.Emit(OpCodes.Brtrue, slow);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloc, phys);
            il.Emit(OpCodes.Ldc_I4, 32);
            il.Emit(OpCodes.Call, NoteStorePhysical);
            il.Emit(OpCodes.Ldloc, ram);
            il.Emit(OpCodes.Ldloc, phys);
            il.Emit(OpCodes.Ldloc, gprs[plan.RegIndex]);
            il.Emit(OpCodes.Call, RamWrite32);
        }
        else
        {
            il.Emit(OpCodes.Ldloc, ram);
            il.Emit(OpCodes.Ldloc, phys);
            il.Emit(OpCodes.Call, RamRead32);
            il.Emit(OpCodes.Stloc, gprs[plan.RegIndex]);
        }

        il.Emit(OpCodes.Ldloc, eip);
        il.Emit(OpCodes.Ldc_I4, plan.Length);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, eip);
        il.Emit(OpCodes.Br, done);

        // Slow path: sync every local back to the context, then run the exact
        // interpreter access (same helpers, same side-effect order, precise
        // faults — the thrown signal unwinds to the caller with the context
        // already at this member's boundary).
        il.MarkLabel(slow);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, served);
        il.Emit(OpCodes.Stfld, JitProgress);
        EmitStoreState(il, context, gprs, eip, flags, tsc);
        if (isStore)
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloc, addr);
            il.Emit(OpCodes.Ldc_I4, 32);
            il.Emit(OpCodes.Ldloc, gprs[plan.RegIndex]);
            il.Emit(OpCodes.Call, WriteMemory);
        }
        else
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloc, addr);
            il.Emit(OpCodes.Ldc_I4, 32);
            il.Emit(OpCodes.Call, ReadMemory);
            il.Emit(OpCodes.Stloc, gprs[plan.RegIndex]);
        }

        il.Emit(OpCodes.Ldloc, eip);
        il.Emit(OpCodes.Ldc_I4, plan.Length);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, eip);
        il.MarkLabel(done);
    }

    /// <summary>
    /// Emits the flag test for one Jcc condition as an int (0/1) on the
    /// stack, mirroring <c>XboxCpu.EvaluateCondition</c> bit for bit.
    /// </summary>
    private static void EmitCondition(ILGenerator il, LocalBuilder flags, ConditionCode condition)
    {
        switch (condition)
        {
            case ConditionCode.Overflow:
                EmitFlag(il, flags, FlagBits.Of);
                break;
            case ConditionCode.NotOverflow:
                EmitFlag(il, flags, FlagBits.Of);
                EmitNot(il);
                break;
            case ConditionCode.Below:
                EmitFlag(il, flags, FlagBits.Cf);
                break;
            case ConditionCode.AboveOrEqual:
                EmitFlag(il, flags, FlagBits.Cf);
                EmitNot(il);
                break;
            case ConditionCode.Equal:
                EmitFlag(il, flags, FlagBits.Zf);
                break;
            case ConditionCode.NotEqual:
                EmitFlag(il, flags, FlagBits.Zf);
                EmitNot(il);
                break;
            case ConditionCode.BelowOrEqual:
                EmitFlag(il, flags, FlagBits.Cf);
                EmitFlag(il, flags, FlagBits.Zf);
                il.Emit(OpCodes.Or);
                break;
            case ConditionCode.Above:
                EmitFlag(il, flags, FlagBits.Cf);
                EmitFlag(il, flags, FlagBits.Zf);
                il.Emit(OpCodes.Or);
                EmitNot(il);
                break;
            case ConditionCode.Sign:
                EmitFlag(il, flags, FlagBits.Sf);
                break;
            case ConditionCode.NotSign:
                EmitFlag(il, flags, FlagBits.Sf);
                EmitNot(il);
                break;
            case ConditionCode.Parity:
                EmitFlag(il, flags, FlagBits.Pf);
                break;
            case ConditionCode.NotParity:
                EmitFlag(il, flags, FlagBits.Pf);
                EmitNot(il);
                break;
            case ConditionCode.Less:
                EmitSignXorOverflow(il, flags);
                break;
            case ConditionCode.GreaterOrEqual:
                EmitSignXorOverflow(il, flags);
                EmitNot(il);
                break;
            case ConditionCode.LessOrEqual:
                EmitSignXorOverflow(il, flags);
                EmitFlag(il, flags, FlagBits.Zf);
                il.Emit(OpCodes.Or);
                break;
            default:
                // Greater: !zf && sf == of.
                EmitSignXorOverflow(il, flags);
                EmitFlag(il, flags, FlagBits.Zf);
                il.Emit(OpCodes.Or);
                EmitNot(il);
                break;
        }
    }

    private static void EmitSignXorOverflow(ILGenerator il, LocalBuilder flags)
    {
        EmitFlag(il, flags, FlagBits.Sf);
        EmitFlag(il, flags, FlagBits.Of);
        il.Emit(OpCodes.Xor);
    }

    private static void EmitFlag(ILGenerator il, LocalBuilder flags, uint bit)
    {
        il.Emit(OpCodes.Ldloc, flags);
        il.Emit(OpCodes.Ldc_I4, (int)bit);
        il.Emit(OpCodes.And);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Cgt_Un);
    }

    private static void EmitNot(ILGenerator il)
    {
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Xor);
    }

    private static void EmitStoreState(
        ILGenerator il,
        LocalBuilder context,
        LocalBuilder[] gprs,
        LocalBuilder eip,
        LocalBuilder flags,
        LocalBuilder tsc)
    {
        il.Emit(OpCodes.Ldloc, context);
        il.Emit(OpCodes.Ldloc, eip);
        il.Emit(OpCodes.Callvirt, SetEip);
        il.Emit(OpCodes.Ldloc, context);
        il.Emit(OpCodes.Ldloc, flags);
        il.Emit(OpCodes.Callvirt, SetEflags);
        il.Emit(OpCodes.Ldloc, context);
        il.Emit(OpCodes.Ldloc, tsc);
        il.Emit(OpCodes.Callvirt, SetTsc);
        il.Emit(OpCodes.Ldloc, context);
        il.Emit(OpCodes.Ldfld, ContextRegisters);
        for (int i = 0; i < gprs.Length; i++)
        {
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4, i);
            il.Emit(OpCodes.Ldloc, gprs[i]);
            il.Emit(OpCodes.Stelem_I4);
        }

        il.Emit(OpCodes.Pop);
    }
}
