namespace XGrid.Cpu;

/// <summary>
/// C2 slice 1: per-member guest-register read/write masks for the block
/// emitter's lazy state sync.
///
/// A block loads the <em>union</em> of its members' read and write masks at
/// the prologue and stores only the block's write mask. Registers a block
/// never touches cost nothing (no load, no store). Because every register in
/// the write mask is loaded at the prologue, an over-store at any sync point
/// (a member that has not run yet) writes that register's unchanged context
/// value back - safe by construction, so the mid-block fault/interpreter sync
/// paths need no cumulative bookkeeping.
///
/// 8-bit encodings 4..7 are AH/CH/DH/BH and fold onto the low four registers
/// (<c>index &amp; 3</c>), exactly as the emitter's <c>EmitReadReg</c> /
/// <c>EmitWriteRegFromStack</c> do; the masks must fold them the same way.
///
/// Unknown or interpreter-backed kinds (<see cref="JitKind.CallSpec"/>,
/// <see cref="JitKind.CallInterpreter"/>, <see cref="JitKind.Invlpg"/>) map to
/// all eight registers: their bodies read and write the context directly.
/// </summary>
internal static class JitStateMask
{
    /// <summary>All eight general-purpose registers.</summary>
    public const uint All = 0xFF;

    /// <summary>ESP index in the context register array (eax,ecx,edx,ebx,esp,ebp,esi,edi).</summary>
    private const int Esp = 4;

    /// <summary>Gets the registers a member consumes.</summary>
    public static uint ReadMask(in JitMemberPlan plan)
    {
        uint address = AddressMask(plan);
        switch (plan.Kind)
        {
            case JitKind.MovRR:
                return RegBit(plan, plan.SecondRegIndex);
            case JitKind.MovRI:
                return 0;
            case JitKind.MovLoad:
                return address;
            case JitKind.FpuLoadReal:
                return address;
            case JitKind.MovStore:
                return RegBit(plan, plan.RegIndex) | address;
            case JitKind.MovStoreImm:
                return address;
            case JitKind.AluRR:
                return RegBit(plan, plan.RegIndex) | RegBit(plan, plan.SecondRegIndex);
            case JitKind.AluRI:
                return RegBit(plan, plan.RegIndex);
            case JitKind.AluRM:
                return RegBit(plan, plan.RegIndex) | address;
            case JitKind.AluMR:
                return RegBit(plan, plan.RegIndex) | address;
            case JitKind.AluMI:
                return address;
            case JitKind.TestRR:
                return RegBit(plan, plan.RegIndex) | RegBit(plan, plan.SecondRegIndex);
            case JitKind.TestRI:
                return RegBit(plan, plan.RegIndex);
            case JitKind.TestMI:
                return address;
            case JitKind.IncR:
            case JitKind.DecR:
                return RegBit(plan, plan.RegIndex);
            case JitKind.IncM:
            case JitKind.DecM:
                return address;
            case JitKind.Lea:
                return address;
            case JitKind.MovExtend:
                return plan.HasMemory ? address : ExtendSourceBit(plan, plan.SecondRegIndex);
            case JitKind.ShiftRI:
                return RegBit(plan, plan.RegIndex);
            case JitKind.PushR:
                return RegBit(plan, plan.RegIndex) | Bit(Esp);
            case JitKind.PushI:
                return Bit(Esp);
            case JitKind.PopR:
                return Bit(Esp);
            case JitKind.CallRel:
                return Bit(Esp);
            case JitKind.CallReg:
                return RegBit(plan, plan.RegIndex) | Bit(Esp);
            case JitKind.CallMem:
                return address | Bit(Esp);
            case JitKind.RetNear:
                return Bit(Esp);
            case JitKind.JmpReg:
                return RegBit(plan, plan.RegIndex);
            case JitKind.JmpMem:
                return address;
            case JitKind.JmpRel:
            case JitKind.JccRel:
            case JitKind.Cli:
            case JitKind.Sti:
            case JitKind.Cld:
            case JitKind.Std:
            case JitKind.Nop:
                return 0;
            default:
                return All;
        }
    }

    /// <summary>Gets the registers a member produces.</summary>
    public static uint WriteMask(in JitMemberPlan plan)
    {
        switch (plan.Kind)
        {
            case JitKind.MovRR:
            case JitKind.MovRI:
            case JitKind.MovLoad:
            case JitKind.AluRR:
            case JitKind.AluRI:
            case JitKind.AluRM:
            case JitKind.IncR:
            case JitKind.DecR:
            case JitKind.Lea:
            case JitKind.MovExtend:
            case JitKind.ShiftRI:
                return RegBit(plan, plan.RegIndex);
            case JitKind.PushR:
            case JitKind.PushI:
            case JitKind.CallRel:
            case JitKind.CallReg:
            case JitKind.CallMem:
            case JitKind.RetNear:
                return Bit(Esp);
            case JitKind.PopR:
                return RegBit(plan, plan.RegIndex) | Bit(Esp);
            case JitKind.MovStore:
            case JitKind.MovStoreImm:
            case JitKind.FpuStoreReal:
            case JitKind.FpuStoreRealPop:
            case JitKind.AluMR:
            case JitKind.AluMI:
            case JitKind.TestRR:
            case JitKind.TestRI:
            case JitKind.TestMI:
            case JitKind.IncM:
            case JitKind.DecM:
            case JitKind.JmpReg:
            case JitKind.JmpMem:
            case JitKind.JmpRel:
            case JitKind.JccRel:
            case JitKind.Cli:
            case JitKind.Sti:
            case JitKind.Cld:
            case JitKind.Std:
            case JitKind.Nop:
                return 0;
            default:
                return All;
        }
    }

    private static uint AddressMask(in JitMemberPlan plan)
    {
        if (!plan.HasMemory)
        {
            return 0;
        }

        uint mask = 0;
        if (plan.HasBase)
        {
            mask |= Bit(plan.BaseRegister);
        }

        if (plan.HasIndex)
        {
            mask |= Bit(plan.IndexRegister);
        }

        return mask;
    }

    /// <summary>Register bit for an operand index at the member's operation size.</summary>
    private static uint RegBit(in JitMemberPlan plan, int index) =>
        Bit(plan.Size == 8 ? index & 3 : index);

    /// <summary>Register bit for a Movzx/Movsx source at the source size.</summary>
    private static uint ExtendSourceBit(in JitMemberPlan plan, int index) =>
        Bit(plan.SourceSize == 8 ? index & 3 : index);

    private static uint Bit(int index) => index is >= 0 and < 8 ? 1u << index : 0;
}
