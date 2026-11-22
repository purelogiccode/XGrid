using System;

namespace XGrid.Cpu.Tcg;

/// <summary>
/// The i386 translator for the first P1S3 slice: 32-bit register/immediate
/// MOV and ALU shapes, INC/DEC, NOP, JMP and Jcc. Memory operands, 8/16-bit
/// forms, stack operations and FPU/SSE are the next slices; every unhandled
/// shape returns false so the caller keeps the exact interpreter.
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

        if (instruction.OperandSize != 32)
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
                return TryMov(instruction, builder, out reason);
            case Mnemonic.Add:
            case Mnemonic.Sub:
            case Mnemonic.And:
            case Mnemonic.Or:
            case Mnemonic.Xor:
            case Mnemonic.Cmp:
                return TryAlu(instruction, builder, out reason);
            case Mnemonic.Inc:
            case Mnemonic.Dec:
                return TryIncDec(instruction, builder, out reason);
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

    private static bool TryMov(DecodedInstruction instruction, TcgBuilder builder, out string? reason)
    {
        reason = null;
        Operand destination = instruction.Operand1;
        if (destination.Kind != OperandKind.Register || destination.Size != 32)
        {
            reason = "mov destination";
            return false;
        }

        Operand source = instruction.Operand2;
        int value = builder.NewTemp();
        switch (source.Kind)
        {
            case OperandKind.Register when source.Size == 32:
                builder.ReadRegister(value, (Gpr)source.RegisterIndex);
                break;
            case OperandKind.Immediate:
                builder.MovImmediate(value, source.Immediate);
                break;
            default:
                reason = "mov source";
                return false;
        }

        builder.WriteRegister((Gpr)destination.RegisterIndex, value);
        return true;
    }

    private static bool TryAlu(DecodedInstruction instruction, TcgBuilder builder, out string? reason)
    {
        reason = null;
        Operand destination = instruction.Operand1;
        if (destination.Kind != OperandKind.Register || destination.Size != 32)
        {
            reason = "alu destination";
            return false;
        }

        if (!TryReadSource(instruction.Operand2, builder, out int right, out reason))
        {
            return false;
        }

        Gpr destinationRegister = (Gpr)destination.RegisterIndex;
        int left = builder.NewTemp();
        builder.ReadRegister(left, destinationRegister);

        if (instruction.Mnemonic == Mnemonic.Cmp)
        {
            builder.Cmp(left, right);
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
        builder.Alu(kind, result, left, right);
        builder.WriteRegister(destinationRegister, result);
        return true;
    }

    private static bool TryIncDec(DecodedInstruction instruction, TcgBuilder builder, out string? reason)
    {
        reason = null;
        Operand destination = instruction.Operand1;
        if (destination.Kind != OperandKind.Register || destination.Size != 32)
        {
            reason = "inc/dec destination";
            return false;
        }

        Gpr register = (Gpr)destination.RegisterIndex;
        int value = builder.NewTemp();
        builder.ReadRegister(value, register);
        int result = builder.NewTemp();
        if (instruction.Mnemonic == Mnemonic.Inc)
        {
            builder.Inc(result, value);
        }
        else
        {
            builder.Dec(result, value);
        }

        builder.WriteRegister(register, result);
        return true;
    }

    private static bool TryReadSource(Operand source, TcgBuilder builder, out int temp, out string? reason)
    {
        temp = 0;
        reason = null;
        switch (source.Kind)
        {
            case OperandKind.Register when source.Size == 32:
                temp = builder.NewTemp();
                builder.ReadRegister(temp, (Gpr)source.RegisterIndex);
                return true;
            case OperandKind.Immediate:
                temp = builder.NewTemp();
                builder.MovImmediate(temp, source.Immediate);
                return true;
            default:
                reason = "alu source";
                return false;
        }
    }
}
