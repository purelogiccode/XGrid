using System.Runtime.InteropServices;

namespace XGrid.Cpu;

/// <summary>
/// JIT semspec slice (§139): pre-specialized per-shape straight-line handlers
/// plus the form-time shape table. No emitter, no codegen: each handler is the
/// exact interpreter case body for one (mnemonic, operand kinds, size) shape
/// with the mnemonic/operand-kind/size switches resolved at block-formation
/// time. GPR indices, immediates, displacements, EFLAGS, segments and
/// addresses stay data read from the block's cached member, exactly as the
/// interpreter reads them. Control, privileged, mode-changing, REP/string,
/// I/O, FPU and invalid shapes never resolve: they keep the interpreter.
/// </summary>
public sealed partial class XboxCpu
{
    /// <summary>
    /// One pre-specialized straight-line body. Advances
    /// <see cref="XboxContext.Eip"/> only after successful completion, exactly
    /// like <see cref="ExecuteInstruction"/>: a fault leaves EIP at the member
    /// start for precise fault reporting. Never touches live state beyond what
    /// the mirrored interpreter body touches.
    /// </summary>
    /// <param name="cpu">The CPU to execute on.</param>
    /// <param name="instruction">The block's cached member (shared lifetime).</param>
    public delegate void SpecHandler(XboxCpu cpu, in DecodedInstruction instruction);

    /// <summary>
    /// Shape key baked per member at formation time (never live state):
    /// mnemonic, operand kinds, operand size, Jcc condition and the
    /// segment-override-or-none. GPR indices, immediates, displacements,
    /// EFLAGS, segments and addresses stay data. The segment override
    /// participates in key identity; the initial handlers consume it as data
    /// through <see cref="ComputeAddress"/>.
    /// </summary>
    [StructLayout(LayoutKind.Auto)]
    public readonly record struct SpecShape(
        Mnemonic Mnemonic,
        OperandKind Op1Kind,
        OperandKind Op2Kind,
        int Size,
        ConditionCode Condition,
        SegmentRegister? SegmentOverride)
    {
        /// <summary>Builds the shape key for one decoded member.</summary>
        /// <param name="instruction">The member to key.</param>
        /// <returns>The form-time shape key.</returns>
        public static SpecShape From(in DecodedInstruction instruction)
        {
            return new SpecShape(
                instruction.Mnemonic,
                instruction.Operand1.Kind,
                instruction.Operand2.Kind,
                instruction.Operand1.Size,
                instruction.Condition,
                instruction.Operand1.Memory.SegmentOverride
                ?? instruction.Operand2.Memory.SegmentOverride);
        }
    }

    /// <summary>
    /// Resolves a member to its pre-specialized handler, or null when the
    /// shape is outside the initial cover (the member keeps the interpreter).
    /// Allocation-free: returns cached delegate instances.
    /// </summary>
    /// <param name="instruction">The member to resolve.</param>
    /// <returns>The handler, or null for fallback shapes.</returns>
    public static SpecHandler? ResolveSpec(in DecodedInstruction instruction)
    {
        if (!instruction.IsValid || instruction.Rep != RepPrefix.None)
        {
            return null;
        }

        OperandKind first = instruction.Operand1.Kind;
        OperandKind second = instruction.Operand2.Kind;
        int size = instruction.Operand1.Size;

        switch (instruction.Mnemonic)
        {
            case Mnemonic.Nop:
                return SpecNopHandler;
            case Mnemonic.Mov:
                return ResolveSpecMov(first, second, size);
            case Mnemonic.Add:
            case Mnemonic.Sub:
            case Mnemonic.And:
            case Mnemonic.Or:
            case Mnemonic.Xor:
                return ResolveSpecAlu(instruction.Mnemonic, first, second, size);
            case Mnemonic.Cmp:
            case Mnemonic.Test:
                return ResolveSpecTest(instruction.Mnemonic, first, second, size);
            case Mnemonic.Inc:
                return first == OperandKind.Register && second == OperandKind.None
                    ? ResolveSpecIncDec(isInc: true, size)
                    : null;
            case Mnemonic.Dec:
                return first == OperandKind.Register && second == OperandKind.None
                    ? ResolveSpecIncDec(isInc: false, size)
                    : null;
            case Mnemonic.Lea:
                return first == OperandKind.Register && second == OperandKind.Memory
                    ? ResolveSpecLea(size)
                    : null;
            case Mnemonic.Push:
                return second == OperandKind.None
                    ? ResolveSpecPush(first, size)
                    : null;
            case Mnemonic.Pop:
                return first == OperandKind.Register && second == OperandKind.None
                    ? ResolveSpecPop(size)
                    : null;
            default:
                return null;
        }
    }

    private static SpecHandler? ResolveSpecMov(OperandKind first, OperandKind second, int size)
    {
        if (first == OperandKind.Register && second == OperandKind.Register)
        {
            return size switch
            {
                8 => SpecMovRegReg8Handler,
                16 => SpecMovRegReg16Handler,
                _ => size == 32 ? SpecMovRegReg32Handler : null,
            };
        }

        if (first == OperandKind.Register && second == OperandKind.Immediate)
        {
            return size switch
            {
                8 => SpecMovRegImm8Handler,
                16 => SpecMovRegImm16Handler,
                _ => size == 32 ? SpecMovRegImm32Handler : null,
            };
        }

        if (first == OperandKind.Register && second == OperandKind.Memory)
        {
            return size switch
            {
                8 => SpecMovRegMem8Handler,
                16 => SpecMovRegMem16Handler,
                _ => size == 32 ? SpecMovRegMem32Handler : null,
            };
        }

        if (first == OperandKind.Memory && second == OperandKind.Register)
        {
            return size switch
            {
                8 => SpecMovMemReg8Handler,
                16 => SpecMovMemReg16Handler,
                _ => size == 32 ? SpecMovMemReg32Handler : null,
            };
        }

        return null;
    }

    private static SpecHandler? ResolveSpecAlu(Mnemonic mnemonic, OperandKind first, OperandKind second, int size)
    {
        bool regReg = first == OperandKind.Register && second == OperandKind.Register;
        bool regImm = first == OperandKind.Register && second == OperandKind.Immediate;
        if (!regReg && !regImm)
        {
            return null;
        }

        return (mnemonic, regReg, size) switch
        {
            (Mnemonic.Add, true, 8) => SpecAddRegReg8Handler,
            (Mnemonic.Add, true, 16) => SpecAddRegReg16Handler,
            (Mnemonic.Add, true, 32) => SpecAddRegReg32Handler,
            (Mnemonic.Add, false, 8) => SpecAddRegImm8Handler,
            (Mnemonic.Add, false, 16) => SpecAddRegImm16Handler,
            (Mnemonic.Add, false, 32) => SpecAddRegImm32Handler,
            (Mnemonic.Sub, true, 8) => SpecSubRegReg8Handler,
            (Mnemonic.Sub, true, 16) => SpecSubRegReg16Handler,
            (Mnemonic.Sub, true, 32) => SpecSubRegReg32Handler,
            (Mnemonic.Sub, false, 8) => SpecSubRegImm8Handler,
            (Mnemonic.Sub, false, 16) => SpecSubRegImm16Handler,
            (Mnemonic.Sub, false, 32) => SpecSubRegImm32Handler,
            (Mnemonic.And, true, 8) => SpecAndRegReg8Handler,
            (Mnemonic.And, true, 16) => SpecAndRegReg16Handler,
            (Mnemonic.And, true, 32) => SpecAndRegReg32Handler,
            (Mnemonic.And, false, 8) => SpecAndRegImm8Handler,
            (Mnemonic.And, false, 16) => SpecAndRegImm16Handler,
            (Mnemonic.And, false, 32) => SpecAndRegImm32Handler,
            (Mnemonic.Or, true, 8) => SpecOrRegReg8Handler,
            (Mnemonic.Or, true, 16) => SpecOrRegReg16Handler,
            (Mnemonic.Or, true, 32) => SpecOrRegReg32Handler,
            (Mnemonic.Or, false, 8) => SpecOrRegImm8Handler,
            (Mnemonic.Or, false, 16) => SpecOrRegImm16Handler,
            (Mnemonic.Or, false, 32) => SpecOrRegImm32Handler,
            (Mnemonic.Xor, true, 8) => SpecXorRegReg8Handler,
            (Mnemonic.Xor, true, 16) => SpecXorRegReg16Handler,
            (Mnemonic.Xor, true, 32) => SpecXorRegReg32Handler,
            (Mnemonic.Xor, false, 8) => SpecXorRegImm8Handler,
            (Mnemonic.Xor, false, 16) => SpecXorRegImm16Handler,
            (Mnemonic.Xor, false, 32) => SpecXorRegImm32Handler,
            _ => null,
        };
    }

    private static SpecHandler? ResolveSpecTest(Mnemonic mnemonic, OperandKind first, OperandKind second, int size)
    {
        bool regReg = first == OperandKind.Register && second == OperandKind.Register;
        bool regImm = first == OperandKind.Register && second == OperandKind.Immediate;
        if (!regReg && !regImm)
        {
            return null;
        }

        return (mnemonic, regReg, size) switch
        {
            (Mnemonic.Cmp, true, 8) => SpecCmpRegReg8Handler,
            (Mnemonic.Cmp, true, 16) => SpecCmpRegReg16Handler,
            (Mnemonic.Cmp, true, 32) => SpecCmpRegReg32Handler,
            (Mnemonic.Cmp, false, 8) => SpecCmpRegImm8Handler,
            (Mnemonic.Cmp, false, 16) => SpecCmpRegImm16Handler,
            (Mnemonic.Cmp, false, 32) => SpecCmpRegImm32Handler,
            (Mnemonic.Test, true, 8) => SpecTestRegReg8Handler,
            (Mnemonic.Test, true, 16) => SpecTestRegReg16Handler,
            (Mnemonic.Test, true, 32) => SpecTestRegReg32Handler,
            (Mnemonic.Test, false, 8) => SpecTestRegImm8Handler,
            (Mnemonic.Test, false, 16) => SpecTestRegImm16Handler,
            (Mnemonic.Test, false, 32) => SpecTestRegImm32Handler,
            _ => null,
        };
    }

    private static SpecHandler? ResolveSpecIncDec(bool isInc, int size)
    {
        return (isInc, size) switch
        {
            (true, 8) => SpecIncReg8Handler,
            (true, 16) => SpecIncReg16Handler,
            (true, 32) => SpecIncReg32Handler,
            (false, 8) => SpecDecReg8Handler,
            (false, 16) => SpecDecReg16Handler,
            (false, 32) => SpecDecReg32Handler,
            _ => null,
        };
    }

    private static SpecHandler? ResolveSpecLea(int size)
    {
        return size switch
        {
            16 => SpecLeaRegMem16Handler,
            32 => SpecLeaRegMem32Handler,
            _ => null,
        };
    }

    private static SpecHandler? ResolveSpecPush(OperandKind first, int size)
    {
        return (first, size) switch
        {
            (OperandKind.Register, 16) => SpecPushReg16Handler,
            (OperandKind.Register, 32) => SpecPushReg32Handler,
            (OperandKind.Immediate, 16) => SpecPushImm16Handler,
            (OperandKind.Immediate, 32) => SpecPushImm32Handler,
            _ => null,
        };
    }

    private static SpecHandler? ResolveSpecPop(int size)
    {
        return size switch
        {
            16 => SpecPopReg16Handler,
            32 => SpecPopReg32Handler,
            _ => null,
        };
    }

    private static readonly SpecHandler SpecNopHandler = SpecNop;
    private static readonly SpecHandler SpecMovRegReg8Handler = SpecMovRegReg8;
    private static readonly SpecHandler SpecMovRegReg16Handler = SpecMovRegReg16;
    private static readonly SpecHandler SpecMovRegReg32Handler = SpecMovRegReg32;
    private static readonly SpecHandler SpecMovRegImm8Handler = SpecMovRegImm8;
    private static readonly SpecHandler SpecMovRegImm16Handler = SpecMovRegImm16;
    private static readonly SpecHandler SpecMovRegImm32Handler = SpecMovRegImm32;
    private static readonly SpecHandler SpecMovRegMem8Handler = SpecMovRegMem8;
    private static readonly SpecHandler SpecMovRegMem16Handler = SpecMovRegMem16;
    private static readonly SpecHandler SpecMovRegMem32Handler = SpecMovRegMem32;
    private static readonly SpecHandler SpecMovMemReg8Handler = SpecMovMemReg8;
    private static readonly SpecHandler SpecMovMemReg16Handler = SpecMovMemReg16;
    private static readonly SpecHandler SpecMovMemReg32Handler = SpecMovMemReg32;
    private static readonly SpecHandler SpecAddRegReg8Handler = SpecAddRegReg8;
    private static readonly SpecHandler SpecAddRegReg16Handler = SpecAddRegReg16;
    private static readonly SpecHandler SpecAddRegReg32Handler = SpecAddRegReg32;
    private static readonly SpecHandler SpecAddRegImm8Handler = SpecAddRegImm8;
    private static readonly SpecHandler SpecAddRegImm16Handler = SpecAddRegImm16;
    private static readonly SpecHandler SpecAddRegImm32Handler = SpecAddRegImm32;
    private static readonly SpecHandler SpecSubRegReg8Handler = SpecSubRegReg8;
    private static readonly SpecHandler SpecSubRegReg16Handler = SpecSubRegReg16;
    private static readonly SpecHandler SpecSubRegReg32Handler = SpecSubRegReg32;
    private static readonly SpecHandler SpecSubRegImm8Handler = SpecSubRegImm8;
    private static readonly SpecHandler SpecSubRegImm16Handler = SpecSubRegImm16;
    private static readonly SpecHandler SpecSubRegImm32Handler = SpecSubRegImm32;
    private static readonly SpecHandler SpecAndRegReg8Handler = SpecAndRegReg8;
    private static readonly SpecHandler SpecAndRegReg16Handler = SpecAndRegReg16;
    private static readonly SpecHandler SpecAndRegReg32Handler = SpecAndRegReg32;
    private static readonly SpecHandler SpecAndRegImm8Handler = SpecAndRegImm8;
    private static readonly SpecHandler SpecAndRegImm16Handler = SpecAndRegImm16;
    private static readonly SpecHandler SpecAndRegImm32Handler = SpecAndRegImm32;
    private static readonly SpecHandler SpecOrRegReg8Handler = SpecOrRegReg8;
    private static readonly SpecHandler SpecOrRegReg16Handler = SpecOrRegReg16;
    private static readonly SpecHandler SpecOrRegReg32Handler = SpecOrRegReg32;
    private static readonly SpecHandler SpecOrRegImm8Handler = SpecOrRegImm8;
    private static readonly SpecHandler SpecOrRegImm16Handler = SpecOrRegImm16;
    private static readonly SpecHandler SpecOrRegImm32Handler = SpecOrRegImm32;
    private static readonly SpecHandler SpecXorRegReg8Handler = SpecXorRegReg8;
    private static readonly SpecHandler SpecXorRegReg16Handler = SpecXorRegReg16;
    private static readonly SpecHandler SpecXorRegReg32Handler = SpecXorRegReg32;
    private static readonly SpecHandler SpecXorRegImm8Handler = SpecXorRegImm8;
    private static readonly SpecHandler SpecXorRegImm16Handler = SpecXorRegImm16;
    private static readonly SpecHandler SpecXorRegImm32Handler = SpecXorRegImm32;
    private static readonly SpecHandler SpecCmpRegReg8Handler = SpecCmpRegReg8;
    private static readonly SpecHandler SpecCmpRegReg16Handler = SpecCmpRegReg16;
    private static readonly SpecHandler SpecCmpRegReg32Handler = SpecCmpRegReg32;
    private static readonly SpecHandler SpecCmpRegImm8Handler = SpecCmpRegImm8;
    private static readonly SpecHandler SpecCmpRegImm16Handler = SpecCmpRegImm16;
    private static readonly SpecHandler SpecCmpRegImm32Handler = SpecCmpRegImm32;
    private static readonly SpecHandler SpecTestRegReg8Handler = SpecTestRegReg8;
    private static readonly SpecHandler SpecTestRegReg16Handler = SpecTestRegReg16;
    private static readonly SpecHandler SpecTestRegReg32Handler = SpecTestRegReg32;
    private static readonly SpecHandler SpecTestRegImm8Handler = SpecTestRegImm8;
    private static readonly SpecHandler SpecTestRegImm16Handler = SpecTestRegImm16;
    private static readonly SpecHandler SpecTestRegImm32Handler = SpecTestRegImm32;
    private static readonly SpecHandler SpecIncReg8Handler = SpecIncReg8;
    private static readonly SpecHandler SpecIncReg16Handler = SpecIncReg16;
    private static readonly SpecHandler SpecIncReg32Handler = SpecIncReg32;
    private static readonly SpecHandler SpecDecReg8Handler = SpecDecReg8;
    private static readonly SpecHandler SpecDecReg16Handler = SpecDecReg16;
    private static readonly SpecHandler SpecDecReg32Handler = SpecDecReg32;
    private static readonly SpecHandler SpecLeaRegMem16Handler = SpecLeaRegMem16;
    private static readonly SpecHandler SpecLeaRegMem32Handler = SpecLeaRegMem32;
    private static readonly SpecHandler SpecPushReg16Handler = SpecPushReg16;
    private static readonly SpecHandler SpecPushReg32Handler = SpecPushReg32;
    private static readonly SpecHandler SpecPushImm16Handler = SpecPushImm16;
    private static readonly SpecHandler SpecPushImm32Handler = SpecPushImm32;
    private static readonly SpecHandler SpecPopReg16Handler = SpecPopReg16;
    private static readonly SpecHandler SpecPopReg32Handler = SpecPopReg32;

    private static void SpecNop(XboxCpu cpu, in DecodedInstruction instruction)
    {
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecMovRegReg8(XboxCpu cpu, in DecodedInstruction instruction)
    {
        cpu.Context.WriteRegister8(
            instruction.Operand1.RegisterIndex,
            cpu.Context.ReadRegister8(instruction.Operand2.RegisterIndex));
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecMovRegReg16(XboxCpu cpu, in DecodedInstruction instruction)
    {
        cpu.Context.WriteRegister16(
            (Gpr)instruction.Operand1.RegisterIndex,
            cpu.Context.ReadRegister16((Gpr)instruction.Operand2.RegisterIndex));
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecMovRegReg32(XboxCpu cpu, in DecodedInstruction instruction)
    {
        cpu.Context.WriteRegister(
            (Gpr)instruction.Operand1.RegisterIndex,
            cpu.Context.ReadRegister((Gpr)instruction.Operand2.RegisterIndex));
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecMovRegImm8(XboxCpu cpu, in DecodedInstruction instruction)
    {
        cpu.Context.WriteRegister8(instruction.Operand1.RegisterIndex, (byte)instruction.Operand2.Immediate);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecMovRegImm16(XboxCpu cpu, in DecodedInstruction instruction)
    {
        cpu.Context.WriteRegister16((Gpr)instruction.Operand1.RegisterIndex, (ushort)instruction.Operand2.Immediate);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecMovRegImm32(XboxCpu cpu, in DecodedInstruction instruction)
    {
        cpu.Context.WriteRegister((Gpr)instruction.Operand1.RegisterIndex, instruction.Operand2.Immediate);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecMovRegMem8(XboxCpu cpu, in DecodedInstruction instruction)
    {
        cpu.Context.WriteRegister8(
            instruction.Operand1.RegisterIndex,
            (byte)cpu.ReadMemory(cpu.ComputeAddress(instruction.Operand2.Memory), 8));
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecMovRegMem16(XboxCpu cpu, in DecodedInstruction instruction)
    {
        cpu.Context.WriteRegister16(
            (Gpr)instruction.Operand1.RegisterIndex,
            (ushort)cpu.ReadMemory(cpu.ComputeAddress(instruction.Operand2.Memory), 16));
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecMovRegMem32(XboxCpu cpu, in DecodedInstruction instruction)
    {
        cpu.Context.WriteRegister(
            (Gpr)instruction.Operand1.RegisterIndex,
            cpu.ReadMemory(cpu.ComputeAddress(instruction.Operand2.Memory), 32));
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecMovMemReg8(XboxCpu cpu, in DecodedInstruction instruction)
    {
        cpu.WriteMemory(
            cpu.ComputeAddress(instruction.Operand1.Memory),
            8,
            cpu.Context.ReadRegister8(instruction.Operand2.RegisterIndex));
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecMovMemReg16(XboxCpu cpu, in DecodedInstruction instruction)
    {
        cpu.WriteMemory(
            cpu.ComputeAddress(instruction.Operand1.Memory),
            16,
            cpu.Context.ReadRegister16((Gpr)instruction.Operand2.RegisterIndex));
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecMovMemReg32(XboxCpu cpu, in DecodedInstruction instruction)
    {
        cpu.WriteMemory(
            cpu.ComputeAddress(instruction.Operand1.Memory),
            32,
            cpu.Context.ReadRegister((Gpr)instruction.Operand2.RegisterIndex));
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecAddRegReg8(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint first = cpu.Context.ReadRegister8(instruction.Operand1.RegisterIndex);
        uint second = cpu.Context.ReadRegister8(instruction.Operand2.RegisterIndex);
        cpu.Context.Eflags = AluOps.Add(first, second, 0, 8, cpu.Context.Eflags, out uint result);
        cpu.Context.WriteRegister8(instruction.Operand1.RegisterIndex, (byte)result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecAddRegReg16(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint first = cpu.Context.ReadRegister16((Gpr)instruction.Operand1.RegisterIndex);
        uint second = cpu.Context.ReadRegister16((Gpr)instruction.Operand2.RegisterIndex);
        cpu.Context.Eflags = AluOps.Add(first, second, 0, 16, cpu.Context.Eflags, out uint result);
        cpu.Context.WriteRegister16((Gpr)instruction.Operand1.RegisterIndex, (ushort)result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecAddRegReg32(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint first = cpu.Context.ReadRegister((Gpr)instruction.Operand1.RegisterIndex);
        uint second = cpu.Context.ReadRegister((Gpr)instruction.Operand2.RegisterIndex);
        cpu.Context.Eflags = AluOps.Add(first, second, 0, 32, cpu.Context.Eflags, out uint result);
        cpu.Context.WriteRegister((Gpr)instruction.Operand1.RegisterIndex, result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecAddRegImm8(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint first = cpu.Context.ReadRegister8(instruction.Operand1.RegisterIndex);
        cpu.Context.Eflags =
            AluOps.Add(first, instruction.Operand2.Immediate, 0, 8, cpu.Context.Eflags, out uint result);
        cpu.Context.WriteRegister8(instruction.Operand1.RegisterIndex, (byte)result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecAddRegImm16(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint first = cpu.Context.ReadRegister16((Gpr)instruction.Operand1.RegisterIndex);
        cpu.Context.Eflags =
            AluOps.Add(first, instruction.Operand2.Immediate, 0, 16, cpu.Context.Eflags, out uint result);
        cpu.Context.WriteRegister16((Gpr)instruction.Operand1.RegisterIndex, (ushort)result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecAddRegImm32(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint first = cpu.Context.ReadRegister((Gpr)instruction.Operand1.RegisterIndex);
        cpu.Context.Eflags =
            AluOps.Add(first, instruction.Operand2.Immediate, 0, 32, cpu.Context.Eflags, out uint result);
        cpu.Context.WriteRegister((Gpr)instruction.Operand1.RegisterIndex, result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecSubRegReg8(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint first = cpu.Context.ReadRegister8(instruction.Operand1.RegisterIndex);
        uint second = cpu.Context.ReadRegister8(instruction.Operand2.RegisterIndex);
        cpu.Context.Eflags = AluOps.Sub(first, second, 0, 8, cpu.Context.Eflags, out uint result);
        cpu.Context.WriteRegister8(instruction.Operand1.RegisterIndex, (byte)result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecSubRegReg16(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint first = cpu.Context.ReadRegister16((Gpr)instruction.Operand1.RegisterIndex);
        uint second = cpu.Context.ReadRegister16((Gpr)instruction.Operand2.RegisterIndex);
        cpu.Context.Eflags = AluOps.Sub(first, second, 0, 16, cpu.Context.Eflags, out uint result);
        cpu.Context.WriteRegister16((Gpr)instruction.Operand1.RegisterIndex, (ushort)result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecSubRegReg32(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint first = cpu.Context.ReadRegister((Gpr)instruction.Operand1.RegisterIndex);
        uint second = cpu.Context.ReadRegister((Gpr)instruction.Operand2.RegisterIndex);
        cpu.Context.Eflags = AluOps.Sub(first, second, 0, 32, cpu.Context.Eflags, out uint result);
        cpu.Context.WriteRegister((Gpr)instruction.Operand1.RegisterIndex, result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecSubRegImm8(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint first = cpu.Context.ReadRegister8(instruction.Operand1.RegisterIndex);
        cpu.Context.Eflags =
            AluOps.Sub(first, instruction.Operand2.Immediate, 0, 8, cpu.Context.Eflags, out uint result);
        cpu.Context.WriteRegister8(instruction.Operand1.RegisterIndex, (byte)result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecSubRegImm16(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint first = cpu.Context.ReadRegister16((Gpr)instruction.Operand1.RegisterIndex);
        cpu.Context.Eflags =
            AluOps.Sub(first, instruction.Operand2.Immediate, 0, 16, cpu.Context.Eflags, out uint result);
        cpu.Context.WriteRegister16((Gpr)instruction.Operand1.RegisterIndex, (ushort)result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecSubRegImm32(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint first = cpu.Context.ReadRegister((Gpr)instruction.Operand1.RegisterIndex);
        cpu.Context.Eflags =
            AluOps.Sub(first, instruction.Operand2.Immediate, 0, 32, cpu.Context.Eflags, out uint result);
        cpu.Context.WriteRegister((Gpr)instruction.Operand1.RegisterIndex, result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecAndRegReg8(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint first = cpu.Context.ReadRegister8(instruction.Operand1.RegisterIndex);
        uint second = cpu.Context.ReadRegister8(instruction.Operand2.RegisterIndex);
        uint result = (first & second) & AluOps.Mask(8);
        cpu.Context.Eflags = AluOps.Logical(result, 8, cpu.Context.Eflags);
        cpu.Context.WriteRegister8(instruction.Operand1.RegisterIndex, (byte)result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecAndRegReg16(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint first = cpu.Context.ReadRegister16((Gpr)instruction.Operand1.RegisterIndex);
        uint second = cpu.Context.ReadRegister16((Gpr)instruction.Operand2.RegisterIndex);
        uint result = (first & second) & AluOps.Mask(16);
        cpu.Context.Eflags = AluOps.Logical(result, 16, cpu.Context.Eflags);
        cpu.Context.WriteRegister16((Gpr)instruction.Operand1.RegisterIndex, (ushort)result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecAndRegReg32(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint result = (cpu.Context.ReadRegister((Gpr)instruction.Operand1.RegisterIndex)
                       & cpu.Context.ReadRegister((Gpr)instruction.Operand2.RegisterIndex)) & AluOps.Mask(32);
        cpu.Context.Eflags = AluOps.Logical(result, 32, cpu.Context.Eflags);
        cpu.Context.WriteRegister((Gpr)instruction.Operand1.RegisterIndex, result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecAndRegImm8(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint first = cpu.Context.ReadRegister8(instruction.Operand1.RegisterIndex);
        uint result = (first & instruction.Operand2.Immediate) & AluOps.Mask(8);
        cpu.Context.Eflags = AluOps.Logical(result, 8, cpu.Context.Eflags);
        cpu.Context.WriteRegister8(instruction.Operand1.RegisterIndex, (byte)result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecAndRegImm16(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint first = cpu.Context.ReadRegister16((Gpr)instruction.Operand1.RegisterIndex);
        uint result = (first & instruction.Operand2.Immediate) & AluOps.Mask(16);
        cpu.Context.Eflags = AluOps.Logical(result, 16, cpu.Context.Eflags);
        cpu.Context.WriteRegister16((Gpr)instruction.Operand1.RegisterIndex, (ushort)result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecAndRegImm32(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint result = (cpu.Context.ReadRegister((Gpr)instruction.Operand1.RegisterIndex)
                       & instruction.Operand2.Immediate) & AluOps.Mask(32);
        cpu.Context.Eflags = AluOps.Logical(result, 32, cpu.Context.Eflags);
        cpu.Context.WriteRegister((Gpr)instruction.Operand1.RegisterIndex, result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecOrRegReg8(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint first = cpu.Context.ReadRegister8(instruction.Operand1.RegisterIndex);
        uint second = cpu.Context.ReadRegister8(instruction.Operand2.RegisterIndex);
        uint result = (first | second) & AluOps.Mask(8);
        cpu.Context.Eflags = AluOps.Logical(result, 8, cpu.Context.Eflags);
        cpu.Context.WriteRegister8(instruction.Operand1.RegisterIndex, (byte)result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecOrRegReg16(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint first = cpu.Context.ReadRegister16((Gpr)instruction.Operand1.RegisterIndex);
        uint second = cpu.Context.ReadRegister16((Gpr)instruction.Operand2.RegisterIndex);
        uint result = (first | second) & AluOps.Mask(16);
        cpu.Context.Eflags = AluOps.Logical(result, 16, cpu.Context.Eflags);
        cpu.Context.WriteRegister16((Gpr)instruction.Operand1.RegisterIndex, (ushort)result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecOrRegReg32(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint result = (cpu.Context.ReadRegister((Gpr)instruction.Operand1.RegisterIndex)
                       | cpu.Context.ReadRegister((Gpr)instruction.Operand2.RegisterIndex)) & AluOps.Mask(32);
        cpu.Context.Eflags = AluOps.Logical(result, 32, cpu.Context.Eflags);
        cpu.Context.WriteRegister((Gpr)instruction.Operand1.RegisterIndex, result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecOrRegImm8(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint first = cpu.Context.ReadRegister8(instruction.Operand1.RegisterIndex);
        uint result = (first | instruction.Operand2.Immediate) & AluOps.Mask(8);
        cpu.Context.Eflags = AluOps.Logical(result, 8, cpu.Context.Eflags);
        cpu.Context.WriteRegister8(instruction.Operand1.RegisterIndex, (byte)result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecOrRegImm16(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint first = cpu.Context.ReadRegister16((Gpr)instruction.Operand1.RegisterIndex);
        uint result = (first | instruction.Operand2.Immediate) & AluOps.Mask(16);
        cpu.Context.Eflags = AluOps.Logical(result, 16, cpu.Context.Eflags);
        cpu.Context.WriteRegister16((Gpr)instruction.Operand1.RegisterIndex, (ushort)result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecOrRegImm32(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint result = (cpu.Context.ReadRegister((Gpr)instruction.Operand1.RegisterIndex)
                       | instruction.Operand2.Immediate) & AluOps.Mask(32);
        cpu.Context.Eflags = AluOps.Logical(result, 32, cpu.Context.Eflags);
        cpu.Context.WriteRegister((Gpr)instruction.Operand1.RegisterIndex, result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecXorRegReg8(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint first = cpu.Context.ReadRegister8(instruction.Operand1.RegisterIndex);
        uint second = cpu.Context.ReadRegister8(instruction.Operand2.RegisterIndex);
        uint result = (first ^ second) & AluOps.Mask(8);
        cpu.Context.Eflags = AluOps.Logical(result, 8, cpu.Context.Eflags);
        cpu.Context.WriteRegister8(instruction.Operand1.RegisterIndex, (byte)result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecXorRegReg16(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint first = cpu.Context.ReadRegister16((Gpr)instruction.Operand1.RegisterIndex);
        uint second = cpu.Context.ReadRegister16((Gpr)instruction.Operand2.RegisterIndex);
        uint result = (first ^ second) & AluOps.Mask(16);
        cpu.Context.Eflags = AluOps.Logical(result, 16, cpu.Context.Eflags);
        cpu.Context.WriteRegister16((Gpr)instruction.Operand1.RegisterIndex, (ushort)result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecXorRegReg32(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint result = (cpu.Context.ReadRegister((Gpr)instruction.Operand1.RegisterIndex)
                       ^ cpu.Context.ReadRegister((Gpr)instruction.Operand2.RegisterIndex)) & AluOps.Mask(32);
        cpu.Context.Eflags = AluOps.Logical(result, 32, cpu.Context.Eflags);
        cpu.Context.WriteRegister((Gpr)instruction.Operand1.RegisterIndex, result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecXorRegImm8(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint first = cpu.Context.ReadRegister8(instruction.Operand1.RegisterIndex);
        uint result = (first ^ instruction.Operand2.Immediate) & AluOps.Mask(8);
        cpu.Context.Eflags = AluOps.Logical(result, 8, cpu.Context.Eflags);
        cpu.Context.WriteRegister8(instruction.Operand1.RegisterIndex, (byte)result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecXorRegImm16(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint first = cpu.Context.ReadRegister16((Gpr)instruction.Operand1.RegisterIndex);
        uint result = (first ^ instruction.Operand2.Immediate) & AluOps.Mask(16);
        cpu.Context.Eflags = AluOps.Logical(result, 16, cpu.Context.Eflags);
        cpu.Context.WriteRegister16((Gpr)instruction.Operand1.RegisterIndex, (ushort)result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecXorRegImm32(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint result = (cpu.Context.ReadRegister((Gpr)instruction.Operand1.RegisterIndex)
                       ^ instruction.Operand2.Immediate) & AluOps.Mask(32);
        cpu.Context.Eflags = AluOps.Logical(result, 32, cpu.Context.Eflags);
        cpu.Context.WriteRegister((Gpr)instruction.Operand1.RegisterIndex, result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecCmpRegReg8(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint first = cpu.Context.ReadRegister8(instruction.Operand1.RegisterIndex);
        uint second = cpu.Context.ReadRegister8(instruction.Operand2.RegisterIndex);
        cpu.Context.Eflags = AluOps.Sub(first, second, 0, 8, cpu.Context.Eflags, out _);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecCmpRegReg16(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint first = cpu.Context.ReadRegister16((Gpr)instruction.Operand1.RegisterIndex);
        uint second = cpu.Context.ReadRegister16((Gpr)instruction.Operand2.RegisterIndex);
        cpu.Context.Eflags = AluOps.Sub(first, second, 0, 16, cpu.Context.Eflags, out _);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecCmpRegReg32(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint first = cpu.Context.ReadRegister((Gpr)instruction.Operand1.RegisterIndex);
        uint second = cpu.Context.ReadRegister((Gpr)instruction.Operand2.RegisterIndex);
        cpu.Context.Eflags = AluOps.Sub(first, second, 0, 32, cpu.Context.Eflags, out _);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecCmpRegImm8(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint first = cpu.Context.ReadRegister8(instruction.Operand1.RegisterIndex);
        cpu.Context.Eflags = AluOps.Sub(first, instruction.Operand2.Immediate, 0, 8, cpu.Context.Eflags, out _);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecCmpRegImm16(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint first = cpu.Context.ReadRegister16((Gpr)instruction.Operand1.RegisterIndex);
        cpu.Context.Eflags = AluOps.Sub(first, instruction.Operand2.Immediate, 0, 16, cpu.Context.Eflags, out _);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecCmpRegImm32(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint first = cpu.Context.ReadRegister((Gpr)instruction.Operand1.RegisterIndex);
        cpu.Context.Eflags = AluOps.Sub(first, instruction.Operand2.Immediate, 0, 32, cpu.Context.Eflags, out _);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecTestRegReg8(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint first = cpu.Context.ReadRegister8(instruction.Operand1.RegisterIndex);
        uint second = cpu.Context.ReadRegister8(instruction.Operand2.RegisterIndex);
        uint value = first & second;
        value &= AluOps.Mask(8);
        cpu.Context.Eflags = AluOps.Logical(value, 8, cpu.Context.Eflags);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecTestRegReg16(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint first = cpu.Context.ReadRegister16((Gpr)instruction.Operand1.RegisterIndex);
        uint second = cpu.Context.ReadRegister16((Gpr)instruction.Operand2.RegisterIndex);
        uint value = first & second;
        value &= AluOps.Mask(16);
        cpu.Context.Eflags = AluOps.Logical(value, 16, cpu.Context.Eflags);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecTestRegReg32(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint value = cpu.Context.ReadRegister((Gpr)instruction.Operand1.RegisterIndex)
                     & cpu.Context.ReadRegister((Gpr)instruction.Operand2.RegisterIndex);
        value &= AluOps.Mask(32);
        cpu.Context.Eflags = AluOps.Logical(value, 32, cpu.Context.Eflags);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecTestRegImm8(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint first = cpu.Context.ReadRegister8(instruction.Operand1.RegisterIndex);
        uint value = first & instruction.Operand2.Immediate;
        value &= AluOps.Mask(8);
        cpu.Context.Eflags = AluOps.Logical(value, 8, cpu.Context.Eflags);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecTestRegImm16(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint first = cpu.Context.ReadRegister16((Gpr)instruction.Operand1.RegisterIndex);
        uint value = first & instruction.Operand2.Immediate;
        value &= AluOps.Mask(16);
        cpu.Context.Eflags = AluOps.Logical(value, 16, cpu.Context.Eflags);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecTestRegImm32(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint value = cpu.Context.ReadRegister((Gpr)instruction.Operand1.RegisterIndex) & instruction.Operand2.Immediate;
        value &= AluOps.Mask(32);
        cpu.Context.Eflags = AluOps.Logical(value, 32, cpu.Context.Eflags);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecIncReg8(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint value = cpu.Context.ReadRegister8(instruction.Operand1.RegisterIndex);
        cpu.Context.Eflags = AluOps.Inc(value, 8, cpu.Context.Eflags, out uint result);
        cpu.Context.WriteRegister8(instruction.Operand1.RegisterIndex, (byte)result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecIncReg16(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint value = cpu.Context.ReadRegister16((Gpr)instruction.Operand1.RegisterIndex);
        cpu.Context.Eflags = AluOps.Inc(value, 16, cpu.Context.Eflags, out uint result);
        cpu.Context.WriteRegister16((Gpr)instruction.Operand1.RegisterIndex, (ushort)result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecIncReg32(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint value = cpu.Context.ReadRegister((Gpr)instruction.Operand1.RegisterIndex);
        cpu.Context.Eflags = AluOps.Inc(value, 32, cpu.Context.Eflags, out uint result);
        cpu.Context.WriteRegister((Gpr)instruction.Operand1.RegisterIndex, result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecDecReg8(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint value = cpu.Context.ReadRegister8(instruction.Operand1.RegisterIndex);
        cpu.Context.Eflags = AluOps.Dec(value, 8, cpu.Context.Eflags, out uint result);
        cpu.Context.WriteRegister8(instruction.Operand1.RegisterIndex, (byte)result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecDecReg16(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint value = cpu.Context.ReadRegister16((Gpr)instruction.Operand1.RegisterIndex);
        cpu.Context.Eflags = AluOps.Dec(value, 16, cpu.Context.Eflags, out uint result);
        cpu.Context.WriteRegister16((Gpr)instruction.Operand1.RegisterIndex, (ushort)result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecDecReg32(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint value = cpu.Context.ReadRegister((Gpr)instruction.Operand1.RegisterIndex);
        cpu.Context.Eflags = AluOps.Dec(value, 32, cpu.Context.Eflags, out uint result);
        cpu.Context.WriteRegister((Gpr)instruction.Operand1.RegisterIndex, result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecLeaRegMem16(XboxCpu cpu, in DecodedInstruction instruction)
    {
        cpu.Context.WriteRegister16(
            (Gpr)instruction.Operand1.RegisterIndex,
            (ushort)cpu.ComputeAddress(instruction.Operand2.Memory));
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecLeaRegMem32(XboxCpu cpu, in DecodedInstruction instruction)
    {
        cpu.Context.WriteRegister(
            (Gpr)instruction.Operand1.RegisterIndex,
            cpu.ComputeAddress(instruction.Operand2.Memory));
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecPushReg16(XboxCpu cpu, in DecodedInstruction instruction)
    {
        cpu.Push(
            cpu.Context.ReadRegister16((Gpr)instruction.Operand1.RegisterIndex),
            16,
            instruction.AddressSize);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecPushReg32(XboxCpu cpu, in DecodedInstruction instruction)
    {
        cpu.Push(
            cpu.Context.ReadRegister((Gpr)instruction.Operand1.RegisterIndex),
            32,
            instruction.AddressSize);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecPushImm16(XboxCpu cpu, in DecodedInstruction instruction)
    {
        cpu.Push(instruction.Operand1.Immediate, 16, instruction.AddressSize);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecPushImm32(XboxCpu cpu, in DecodedInstruction instruction)
    {
        cpu.Push(instruction.Operand1.Immediate, 32, instruction.AddressSize);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecPopReg16(XboxCpu cpu, in DecodedInstruction instruction)
    {
        cpu.Context.WriteRegister16(
            (Gpr)instruction.Operand1.RegisterIndex,
            (ushort)cpu.Pop(16, instruction.AddressSize));
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecPopReg32(XboxCpu cpu, in DecodedInstruction instruction)
    {
        cpu.Context.WriteRegister(
            (Gpr)instruction.Operand1.RegisterIndex,
            cpu.Pop(32, instruction.AddressSize));
        cpu.Context.Eip += (uint)instruction.Length;
    }
}
