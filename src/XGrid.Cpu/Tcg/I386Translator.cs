using System;

namespace XGrid.Cpu.Tcg;

/// <summary>
/// The i386 translator for the P1S3 slice: 8/16/32-bit register, immediate and
/// memory MOV and ALU shapes, INC/DEC, NOP, JMP and Jcc. Every unhandled shape
/// (16-bit addressing, stack, string, FPU/SSE) returns false so the caller keeps
/// the exact interpreter; the coverage is extended by measured fallback gaps.
/// </summary>
public static class I386Translator
{
    /// <summary>Translates one decoded instruction into managed TCG IR.</summary>
    /// <param name="instruction">The decoded guest instruction.</param>
    /// <param name="eip">The guest EIP of the instruction.</param>
    /// <param name="builder">The program builder.</param>
    /// <param name="terminatesBlock">True when the instruction ends the block (JMP/Jcc).</param>
    /// <param name="reason">The unsupported shape when the translation returns false.</param>
    /// <returns>True when the instruction was translated.</returns>
    public static bool TryTranslate(
        DecodedInstruction instruction,
        uint eip,
        TcgBuilder builder,
        out bool terminatesBlock,
        out string? reason)
    {
        ArgumentNullException.ThrowIfNull(builder);
        terminatesBlock = false;
        reason = null;

        if (!instruction.IsValid)
        {
            reason = "invalid instruction";
            return false;
        }

        int size = OperandSize(instruction);
        if (size == 0)
        {
            reason = $"operand size {instruction.OperandSize}";
            return false;
        }

        builder.NoteLength(instruction.Length);

        switch (instruction.Mnemonic)
        {
            case Mnemonic.Nop:
                return true;
            case Mnemonic.Mov:
                return TryMov(instruction, size, builder, out reason);
            case Mnemonic.Add:
            case Mnemonic.Sub:
            case Mnemonic.And:
            case Mnemonic.Or:
            case Mnemonic.Xor:
            case Mnemonic.Cmp:
                return TryAlu(instruction, size, builder, out reason);
            case Mnemonic.Inc:
            case Mnemonic.Dec:
                return TryIncDec(instruction, size, builder, out reason);
            case Mnemonic.Shl:
            case Mnemonic.Shr:
            case Mnemonic.Sar:
            case Mnemonic.Rol:
            case Mnemonic.Ror:
            case Mnemonic.Rcl:
            case Mnemonic.Rcr:
                return TryShift(instruction, size, builder, out reason);
            case Mnemonic.Shld:
            case Mnemonic.Shrd:
                return TryDoubleShift(instruction, size, builder, out reason);
            case Mnemonic.Neg:
            case Mnemonic.Not:
                return TryUnary(instruction, size, builder, out reason);
            case Mnemonic.Movzx:
            case Mnemonic.Movsx:
                return TryMovExtend(instruction, size, builder, out reason);
            case Mnemonic.Lea:
                return TryLea(instruction, size, builder, out reason);
            case Mnemonic.Xchg:
                return TryXchg(instruction, size, builder, out reason);
            case Mnemonic.Sahf:
                builder.Sahf();
                return true;
            case Mnemonic.Lahf:
                builder.Lahf();
                return true;
            case Mnemonic.Mul:
            case Mnemonic.Imul:
            case Mnemonic.Div:
            case Mnemonic.Idiv:
                return TryMulDiv(instruction, builder, out reason);
            case Mnemonic.Push:
            case Mnemonic.Pop:
                return TryPushPop(instruction, builder, out reason);
            case Mnemonic.Bt:
            case Mnemonic.Bts:
            case Mnemonic.Btr:
            case Mnemonic.Btc:
                return TryBitTest(instruction, size, builder, out reason);
            case Mnemonic.Cmpxchg:
                return TryCmpxchg(instruction, size, builder, out reason);
            case Mnemonic.Cmpxchg8B:
                return TryCmpxchg8B(instruction, builder, out reason);
            case Mnemonic.Xadd:
                return TryXadd(instruction, size, builder, out reason);
            case Mnemonic.Jmp:
                if (instruction.Operand1.Kind != OperandKind.Immediate)
                {
                    reason = "indirect jump";
                    return false;
                }

                builder.SetEip(eip + (uint)instruction.Length + instruction.Operand1.Immediate);
                builder.Exit();
                terminatesBlock = true;
                return true;
            case Mnemonic.Jcc:
                if (instruction.Operand1.Kind != OperandKind.Immediate)
                {
                    reason = "indirect conditional jump";
                    return false;
                }

                builder.ConditionalSetEip(
                    eip + (uint)instruction.Length + instruction.Operand1.Immediate,
                    instruction.Condition);
                builder.SetEip(eip + (uint)instruction.Length);
                builder.Exit();
                terminatesBlock = true;
                return true;
            default:
                reason = instruction.Mnemonic.ToString();
                return false;
        }
    }

    private static int OperandSize(DecodedInstruction instruction)
    {
        int bits = instruction.Operand1.Size is 8 or 16 or 32
            ? instruction.Operand1.Size
            : instruction.OperandSize;
        return bits / 8;
    }

    private static bool TryMov(
        DecodedInstruction instruction,
        int size,
        TcgBuilder builder,
        out string? reason)
    {
        Operand destination = instruction.Operand1;
        if (destination.Kind == OperandKind.Register)
        {
            if (!TryReadSource(instruction.Operand2, size, builder, out int value, out reason))
            {
                return false;
            }

            builder.WriteRegister(destination.RegisterIndex, value, size);
            return true;
        }

        if (destination.Kind == OperandKind.Memory)
        {
            if (!TryAddress(destination.Memory, builder, out int address, out reason))
            {
                return false;
            }

            if (!TryReadSource(instruction.Operand2, size, builder, out int value, out reason))
            {
                return false;
            }

            builder.Store(address, value, size);
            return true;
        }

        reason = "mov destination";
        return false;
    }

    private static bool TryAlu(
        DecodedInstruction instruction,
        int size,
        TcgBuilder builder,
        out string? reason)
    {
        Operand destination = instruction.Operand1;
        int address = 0;
        int left;
        if (destination.Kind == OperandKind.Register)
        {
            left = builder.NewTemp();
            builder.ReadRegister(left, destination.RegisterIndex, size);
        }
        else if (destination.Kind == OperandKind.Memory)
        {
            if (!TryAddress(destination.Memory, builder, out address, out reason))
            {
                return false;
            }

            left = builder.NewTemp();
            builder.Load(left, address, size);
        }
        else
        {
            reason = "alu destination";
            return false;
        }

        if (!TryReadSource(instruction.Operand2, size, builder, out int right, out reason))
        {
            return false;
        }

        if (instruction.Mnemonic == Mnemonic.Cmp)
        {
            builder.Cmp(left, right, size);
            return true;
        }

        TcgOpKind kind = instruction.Mnemonic switch
        {
            Mnemonic.Add => TcgOpKind.Add,
            Mnemonic.Sub => TcgOpKind.Sub,
            Mnemonic.And => TcgOpKind.And,
            Mnemonic.Or => TcgOpKind.Or,
            _ => TcgOpKind.Xor,
        };

        int result = builder.NewTemp();
        builder.Alu(kind, result, left, right, size);
        if (destination.Kind == OperandKind.Register)
        {
            builder.WriteRegister(destination.RegisterIndex, result, size);
        }
        else
        {
            builder.Store(address, result, size);
        }

        return true;
    }

    private static bool TryIncDec(
        DecodedInstruction instruction,
        int size,
        TcgBuilder builder,
        out string? reason)
    {
        reason = null;
        Operand destination = instruction.Operand1;
        bool increment = instruction.Mnemonic == Mnemonic.Inc;

        if (destination.Kind == OperandKind.Register)
        {
            int value = builder.NewTemp();
            builder.ReadRegister(value, destination.RegisterIndex, size);
            int result = builder.NewTemp();
            if (increment)
            {
                builder.Inc(result, value, size);
            }
            else
            {
                builder.Dec(result, value, size);
            }

            builder.WriteRegister(destination.RegisterIndex, result, size);
            return true;
        }

        if (destination.Kind == OperandKind.Memory)
        {
            if (!TryAddress(destination.Memory, builder, out int address, out reason))
            {
                return false;
            }

            int value = builder.NewTemp();
            builder.Load(value, address, size);
            int result = builder.NewTemp();
            if (increment)
            {
                builder.Inc(result, value, size);
            }
            else
            {
                builder.Dec(result, value, size);
            }

            builder.Store(address, result, size);
            return true;
        }

        reason = "inc/dec destination";
        return false;
    }

    private static bool TryShift(
        DecodedInstruction instruction,
        int size,
        TcgBuilder builder,
        out string? reason)
    {
        reason = null;
        Operand destination = instruction.Operand1;
        int address = 0;
        int value;
        if (destination.Kind == OperandKind.Register)
        {
            value = builder.NewTemp();
            builder.ReadRegister(value, destination.RegisterIndex, size);
        }
        else if (destination.Kind == OperandKind.Memory)
        {
            if (!TryAddress(destination.Memory, builder, out address, out reason))
            {
                return false;
            }

            value = builder.NewTemp();
            builder.Load(value, address, size);
        }
        else
        {
            reason = "shift destination";
            return false;
        }

        if (!TryReadCount(instruction.Operand2, builder, out int count, out reason))
        {
            return false;
        }

        TcgOpKind kind = instruction.Mnemonic switch
        {
            Mnemonic.Shl => TcgOpKind.Shl,
            Mnemonic.Shr => TcgOpKind.Shr,
            Mnemonic.Sar => TcgOpKind.Sar,
            Mnemonic.Rol => TcgOpKind.Rol,
            Mnemonic.Ror => TcgOpKind.Ror,
            Mnemonic.Rcl => TcgOpKind.Rcl,
            _ => TcgOpKind.Rcr,
        };

        int result = builder.NewTemp();
        builder.Shift(kind, result, value, count, size);
        if (destination.Kind == OperandKind.Register)
        {
            builder.WriteRegister(destination.RegisterIndex, result, size);
        }
        else
        {
            builder.Store(address, result, size);
        }

        return true;
    }

    private static bool TryDoubleShift(
        DecodedInstruction instruction,
        int size,
        TcgBuilder builder,
        out string? reason)
    {
        reason = null;
        if (instruction.Operand3.Kind != OperandKind.Immediate)
        {
            reason = "double-shift count";
            return false;
        }

        Operand destination = instruction.Operand1;
        int address = 0;
        int value;
        if (destination.Kind == OperandKind.Register)
        {
            value = builder.NewTemp();
            builder.ReadRegister(value, destination.RegisterIndex, size);
        }
        else if (destination.Kind == OperandKind.Memory)
        {
            if (!TryAddress(destination.Memory, builder, out address, out reason))
            {
                return false;
            }

            value = builder.NewTemp();
            builder.Load(value, address, size);
        }
        else
        {
            reason = "double-shift destination";
            return false;
        }

        if (!TryReadSource(instruction.Operand2, size, builder, out int source, out reason))
        {
            return false;
        }

        int result = builder.NewTemp();
        builder.DoubleShift(
            instruction.Mnemonic == Mnemonic.Shld ? TcgOpKind.Shld : TcgOpKind.Shrd,
            result,
            value,
            source,
            instruction.Operand3.Immediate,
            size);

        if (destination.Kind == OperandKind.Register)
        {
            builder.WriteRegister(destination.RegisterIndex, result, size);
        }
        else
        {
            builder.Store(address, result, size);
        }

        return true;
    }

    private static bool TryUnary(
        DecodedInstruction instruction,
        int size,
        TcgBuilder builder,
        out string? reason)
    {
        reason = null;
        Operand destination = instruction.Operand1;
        int address = 0;
        int value;
        if (destination.Kind == OperandKind.Register)
        {
            value = builder.NewTemp();
            builder.ReadRegister(value, destination.RegisterIndex, size);
        }
        else if (destination.Kind == OperandKind.Memory)
        {
            if (!TryAddress(destination.Memory, builder, out address, out reason))
            {
                return false;
            }

            value = builder.NewTemp();
            builder.Load(value, address, size);
        }
        else
        {
            reason = "unary destination";
            return false;
        }

        int result = builder.NewTemp();
        if (instruction.Mnemonic == Mnemonic.Neg)
        {
            builder.Neg(result, value, size);
        }
        else
        {
            builder.Complement(result, value, size);
        }

        if (destination.Kind == OperandKind.Register)
        {
            builder.WriteRegister(destination.RegisterIndex, result, size);
        }
        else
        {
            builder.Store(address, result, size);
        }

        return true;
    }

    private static bool TryMovExtend(
        DecodedInstruction instruction,
        int size,
        TcgBuilder builder,
        out string? reason)
    {
        reason = null;
        if (instruction.Operand1.Kind != OperandKind.Register)
        {
            reason = "movzx/movsx destination";
            return false;
        }

        int sourceSize = instruction.Operand2.Size / 8;
        if (sourceSize is not (1 or 2))
        {
            reason = "movzx/movsx source size";
            return false;
        }

        if (!TryReadSource(instruction.Operand2, sourceSize, builder, out int value, out reason))
        {
            return false;
        }

        int result = builder.NewTemp();
        builder.MovExtend(result, value, sourceSize, instruction.Mnemonic == Mnemonic.Movsx);
        builder.WriteRegister(instruction.Operand1.RegisterIndex, result, size);
        return true;
    }

    private static bool TryLea(
        DecodedInstruction instruction,
        int size,
        TcgBuilder builder,
        out string? reason)
    {
        reason = null;
        if (instruction.Operand1.Kind != OperandKind.Register || instruction.Operand2.Kind != OperandKind.Memory)
        {
            reason = "lea operands";
            return false;
        }

        if (!TryAddress(instruction.Operand2.Memory, builder, out int address, out reason))
        {
            return false;
        }

        builder.WriteRegister(instruction.Operand1.RegisterIndex, address, size);
        return true;
    }

    private static bool TryXchg(
        DecodedInstruction instruction,
        int size,
        TcgBuilder builder,
        out string? reason)
    {
        reason = null;
        Operand first = instruction.Operand1;
        Operand second = instruction.Operand2;

        if (first.Kind == OperandKind.Register && second.Kind == OperandKind.Register)
        {
            int firstValue = builder.NewTemp();
            builder.ReadRegister(firstValue, first.RegisterIndex, size);
            int secondValue = builder.NewTemp();
            builder.ReadRegister(secondValue, second.RegisterIndex, size);
            builder.WriteRegister(first.RegisterIndex, secondValue, size);
            builder.WriteRegister(second.RegisterIndex, firstValue, size);
            return true;
        }

        if (first.Kind == OperandKind.Register && second.Kind == OperandKind.Memory)
        {
            int registerValue = builder.NewTemp();
            builder.ReadRegister(registerValue, first.RegisterIndex, size);
            if (!TryAddress(second.Memory, builder, out int address, out reason))
            {
                return false;
            }

            int memoryValue = builder.NewTemp();
            builder.Load(memoryValue, address, size);
            builder.Store(address, registerValue, size);
            builder.WriteRegister(first.RegisterIndex, memoryValue, size);
            return true;
        }

        if (first.Kind == OperandKind.Memory && second.Kind == OperandKind.Register)
        {
            int registerValue = builder.NewTemp();
            builder.ReadRegister(registerValue, second.RegisterIndex, size);
            if (!TryAddress(first.Memory, builder, out int address, out reason))
            {
                return false;
            }

            int memoryValue = builder.NewTemp();
            builder.Load(memoryValue, address, size);
            builder.Store(address, registerValue, size);
            builder.WriteRegister(second.RegisterIndex, memoryValue, size);
            return true;
        }

        reason = "xchg operands";
        return false;
    }

    private static bool TryBitTest(
        DecodedInstruction instruction,
        int size,
        TcgBuilder builder,
        out string? reason)
    {
        reason = null;
        Operand destination = instruction.Operand1;
        if (destination.Kind is not (OperandKind.Register or OperandKind.Memory))
        {
            reason = "bt destination";
            return false;
        }

        int indexSize = instruction.Operand2.Size / 8;
        if (indexSize is not (1 or 2 or 4))
        {
            indexSize = 4;
        }

        if (!TryReadSource(instruction.Operand2, indexSize, builder, out int bitIndex, out reason))
        {
            return false;
        }

        int operation = instruction.Mnemonic switch
        {
            Mnemonic.Bts => 1,
            Mnemonic.Btr => 2,
            Mnemonic.Btc => 3,
            _ => 0,
        };

        if (destination.Kind == OperandKind.Memory)
        {
            if (!TryAddress(destination.Memory, builder, out int address, out reason))
            {
                return false;
            }

            builder.BitTest(0, address, bitIndex, operation, size, isMemory: true);
            return true;
        }

        int value = builder.NewTemp();
        builder.ReadRegister(value, destination.RegisterIndex, size);
        int result = builder.NewTemp();
        builder.BitTest(result, value, bitIndex, operation, size, isMemory: false);
        builder.WriteRegister(destination.RegisterIndex, result, size);
        return true;
    }

    private static bool TryCmpxchg(
        DecodedInstruction instruction,
        int size,
        TcgBuilder builder,
        out string? reason)
    {
        reason = null;
        Operand destination = instruction.Operand1;
        Operand source = instruction.Operand2;
        if (source.Kind != OperandKind.Register || source.Size != size * 8)
        {
            reason = "cmpxchg source";
            return false;
        }

        int sourceValue = builder.NewTemp();
        builder.ReadRegister(sourceValue, source.RegisterIndex, size);

        if (destination.Kind == OperandKind.Register)
        {
            builder.Cmpxchg(destination.RegisterIndex, sourceValue, size, isMemory: false);
            return true;
        }

        if (destination.Kind == OperandKind.Memory)
        {
            if (!TryAddress(destination.Memory, builder, out int address, out reason))
            {
                return false;
            }

            builder.Cmpxchg(address, sourceValue, size, isMemory: true);
            return true;
        }

        reason = "cmpxchg destination";
        return false;
    }

    private static bool TryCmpxchg8B(DecodedInstruction instruction, TcgBuilder builder, out string? reason)
    {
        reason = null;
        if (instruction.Operand1.Kind != OperandKind.Memory)
        {
            reason = "cmpxchg8b destination";
            return false;
        }

        if (!TryAddress(instruction.Operand1.Memory, builder, out int address, out reason))
        {
            return false;
        }

        builder.Cmpxchg8B(address);
        return true;
    }

    private static bool TryXadd(
        DecodedInstruction instruction,
        int size,
        TcgBuilder builder,
        out string? reason)
    {
        reason = null;
        Operand destination = instruction.Operand1;
        Operand source = instruction.Operand2;
        if (source.Kind != OperandKind.Register || source.Size != size * 8)
        {
            reason = "xadd source";
            return false;
        }

        int sourceValue = builder.NewTemp();
        builder.ReadRegister(sourceValue, source.RegisterIndex, size);

        if (destination.Kind == OperandKind.Register)
        {
            int value = builder.NewTemp();
            builder.ReadRegister(value, destination.RegisterIndex, size);
            builder.Xadd(value, sourceValue, source.RegisterIndex, size, isMemory: false);
            builder.WriteRegister(destination.RegisterIndex, value, size);
            return true;
        }

        if (destination.Kind == OperandKind.Memory)
        {
            if (!TryAddress(destination.Memory, builder, out int address, out reason))
            {
                return false;
            }

            builder.Xadd(address, sourceValue, source.RegisterIndex, size, isMemory: true);
            return true;
        }

        reason = "xadd destination";
        return false;
    }

    private static bool TryMulDiv(
        DecodedInstruction instruction,
        TcgBuilder builder,
        out string? reason)
    {
        reason = null;
        if (instruction.Mnemonic == Mnemonic.Imul)
        {
            if (instruction.Operand3.Kind != OperandKind.None)
            {
                if (instruction.Operand1.Kind != OperandKind.Register)
                {
                    reason = "imul3 destination";
                    return false;
                }

                int destSize = instruction.Operand1.Size / 8;
                int operandSize = instruction.Operand2.Size / 8;
                if (!TryReadSource(instruction.Operand2, operandSize, builder, out int source, out reason))
                {
                    return false;
                }

                int result = builder.NewTemp();
                builder.Imul3(result, source, instruction.Operand3.Immediate, destSize);
                builder.WriteRegister(instruction.Operand1.RegisterIndex, result, destSize);
                return true;
            }

            if (instruction.Operand2.Kind != OperandKind.None)
            {
                if (instruction.Operand1.Kind != OperandKind.Register)
                {
                    reason = "imul2 destination";
                    return false;
                }

                int destSize = instruction.Operand1.Size / 8;
                int left = builder.NewTemp();
                builder.ReadRegister(left, instruction.Operand1.RegisterIndex, destSize);
                if (!TryReadSource(instruction.Operand2, destSize, builder, out int right, out reason))
                {
                    return false;
                }

                int result = builder.NewTemp();
                builder.Imul2(result, left, right, destSize);
                builder.WriteRegister(instruction.Operand1.RegisterIndex, result, destSize);
                return true;
            }

            int sourceSize = instruction.Operand1.Size / 8;
            if (!TryReadSource(instruction.Operand1, sourceSize, builder, out int implicitSource, out reason))
            {
                return false;
            }

            builder.Imul1(implicitSource, sourceSize);
            return true;
        }

        int size = instruction.Operand1.Size / 8;
        if (!TryReadSource(instruction.Operand1, size, builder, out int sourceOperand, out reason))
        {
            return false;
        }

        switch (instruction.Mnemonic)
        {
            case Mnemonic.Mul:
                builder.Mul(sourceOperand, size);
                break;
            case Mnemonic.Div:
                builder.Div(sourceOperand, size);
                break;
            default:
                builder.Idiv(sourceOperand, size);
                break;
        }

        return true;
    }

    private static bool TryPushPop(
        DecodedInstruction instruction,
        TcgBuilder builder,
        out string? reason)
    {
        reason = null;
        int size = instruction.OperandSize / 8;
        if (size is not (2 or 4))
        {
            reason = "stack operand size";
            return false;
        }

        if (instruction.Mnemonic == Mnemonic.Push)
        {
            int value;
            if (instruction.Operand1.Kind == OperandKind.Immediate)
            {
                value = builder.NewTemp();
                builder.MovImmediate(value, instruction.Operand1.Immediate, size);
            }
            else if (!TryReadSource(instruction.Operand1, size, builder, out value, out reason))
            {
                return false;
            }

            builder.Push(value, size);
            return true;
        }

        int popped = builder.NewTemp();
        builder.Pop(popped, size);
        if (instruction.Operand1.Kind == OperandKind.Register)
        {
            builder.WriteRegister(instruction.Operand1.RegisterIndex, popped, size);
            return true;
        }

        if (instruction.Operand1.Kind == OperandKind.Memory)
        {
            if (!TryAddress(instruction.Operand1.Memory, builder, out int address, out reason))
            {
                return false;
            }

            builder.Store(address, popped, size);
            return true;
        }

        reason = "pop destination";
        return false;
    }

    private static bool TryReadCount(Operand operand, TcgBuilder builder, out int temp, out string? reason)
    {
        temp = 0;
        reason = null;
        if (operand.Kind == OperandKind.Register && operand.Size == 8)
        {
            temp = builder.NewTemp();
            builder.ReadRegister(temp, 1, 1);
            return true;
        }

        if (operand.Kind == OperandKind.Immediate)
        {
            temp = builder.NewTemp();
            builder.MovImmediate(temp, operand.Immediate, 4);
            return true;
        }

        reason = "shift count";
        return false;
    }

    private static bool TryReadSource(
        Operand source,
        int size,
        TcgBuilder builder,
        out int temp,
        out string? reason)
    {
        temp = 0;
        reason = null;
        switch (source.Kind)
        {
            case OperandKind.Register when source.Size == size * 8:
                temp = builder.NewTemp();
                builder.ReadRegister(temp, source.RegisterIndex, size);
                return true;
            case OperandKind.Immediate:
                temp = builder.NewTemp();
                builder.MovImmediate(temp, source.Immediate, size);
                return true;
            case OperandKind.Memory:
                if (!TryAddress(source.Memory, builder, out int address, out reason))
                {
                    return false;
                }

                temp = builder.NewTemp();
                builder.Load(temp, address, size);
                return true;
            default:
                reason = "alu/mov source";
                return false;
        }
    }

    private static bool TryAddress(
        in MemoryOperand memory,
        TcgBuilder builder,
        out int address,
        out string? reason)
    {
        address = 0;
        reason = null;
        if (memory.Is16Bit)
        {
            reason = "16-bit addressing";
            return false;
        }

        SegmentRegister segment = memory.SegmentOverride ?? DefaultSegment(memory);
        address = builder.NewTemp();
        builder.Lea(address, memory, segment);
        return true;
    }

    private static SegmentRegister DefaultSegment(in MemoryOperand memory)
    {
        return memory.HasBase && (memory.Base == Gpr.Esp || memory.Base == Gpr.Ebp)
            ? SegmentRegister.Ss
            : SegmentRegister.Ds;
    }
}
