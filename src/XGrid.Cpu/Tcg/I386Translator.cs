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
