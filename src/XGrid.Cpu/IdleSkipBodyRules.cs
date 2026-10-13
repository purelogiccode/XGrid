using System.Collections.Generic;

namespace XGrid.Cpu;

/// <summary>
/// Fixed-point safety analysis over the instruction stream a candidate idle loop
/// actually executed: the body may only be rejected/approved from what really
/// ran (forward conditional sub-path skips make linear re-decode unsound).
/// A pass proves the body maps (registers, unchanged memory, IF) to itself, so
/// the single backward exit cannot occur without an external change (RAM watch
/// or IRQ).
/// </summary>
internal static class IdleSkipBodyRules
{
    /// <summary>
    /// Validates the recorded body stream; returns null when every skip-safety
    /// rule holds, or a short reason string when the candidate must be rejected.
    /// <paramref name="raisesIf"/> is set when the body executes STI, i.e. an
    /// IRQ becomes deliverable mid-body regardless of the IF flag at the head.
    /// </summary>
    public static string? Validate(List<DecodedInstruction> stream, out bool raisesIf)
    {
        raisesIf = false;
        if (stream.Count == 0)
        {
            return "empty";
        }

        if (stream.Count > IdleSkipDetector.MaxBodyInstructions)
        {
            return "too-long";
        }

        for (int i = 0; i < stream.Count; i++)
        {
            DecodedInstruction instruction = stream[i];
            bool isBranch = instruction.Mnemonic is Mnemonic.Jmp or Mnemonic.Jcc;
            bool last = i == stream.Count - 1;
            if (isBranch)
            {
                if (instruction.Operand1.Kind != OperandKind.Immediate)
                {
                    return "indirect-branch";
                }

                if (!last && instruction.Mnemonic == Mnemonic.Jmp)
                {
                    return "stray-jmp";
                }
            }
            else if (last)
            {
                return "missing-branch";
            }

            if (!IsAllowed(instruction.Mnemonic))
            {
                return "mnemonic";
            }

            if (instruction.Mnemonic == Mnemonic.Sti)
            {
                raisesIf = true;
            }
        }

        uint written = 0;
        for (int i = 0; i < stream.Count; i++)
        {
            written |= DestinationMask(stream[i]);
        }

        uint defined = 0;
        for (int i = 0; i < stream.Count; i++)
        {
            string? error = WalkInstruction(stream[i], written, ref defined);
            if (error is not null)
            {
                return error;
            }
        }

        return null;
    }

    /// <summary>Allow-list of mnemonics whose execution is side-effect free for skip purposes.</summary>
    public static bool IsAllowed(Mnemonic mnemonic)
    {
        return mnemonic is Mnemonic.Nop
            or Mnemonic.Mov or Mnemonic.Movzx or Mnemonic.Movsx or Mnemonic.Lea
            or Mnemonic.Test or Mnemonic.Cmp
            or Mnemonic.And or Mnemonic.Or or Mnemonic.Xor or Mnemonic.Add or Mnemonic.Sub
            or Mnemonic.Inc or Mnemonic.Dec or Mnemonic.Neg or Mnemonic.Not
            or Mnemonic.Shl or Mnemonic.Shr or Mnemonic.Sar
            or Mnemonic.Rol or Mnemonic.Ror or Mnemonic.Rcl or Mnemonic.Rcr
            or Mnemonic.Sti or Mnemonic.Cli
            or Mnemonic.Jmp or Mnemonic.Jcc;
    }

    private static uint DestinationMask(in DecodedInstruction instruction)
    {
        return instruction.Mnemonic switch
        {
            Mnemonic.Mov or Mnemonic.Movzx or Mnemonic.Movsx or Mnemonic.Lea
                or Mnemonic.And or Mnemonic.Or or Mnemonic.Xor or Mnemonic.Add or Mnemonic.Sub
                or Mnemonic.Inc or Mnemonic.Dec or Mnemonic.Neg or Mnemonic.Not
                or Mnemonic.Shl or Mnemonic.Shr or Mnemonic.Sar
                or Mnemonic.Rol or Mnemonic.Ror or Mnemonic.Rcl or Mnemonic.Rcr
                when instruction.Operand1.Kind == OperandKind.Register
                => 1u << instruction.Operand1.RegisterIndex,
            _ => 0u,
        };
    }

    private static string? WalkInstruction(in DecodedInstruction instruction, uint written, ref uint defined)
    {
        switch (instruction.Mnemonic)
        {
            case Mnemonic.Nop:
            case Mnemonic.Sti:
            case Mnemonic.Cli:
            case Mnemonic.Jmp:
            case Mnemonic.Jcc:
                return null;

            case Mnemonic.Test:
            case Mnemonic.Cmp:
                return CheckOperandRead(instruction.Operand1, written, defined)
                    ?? CheckOperandRead(instruction.Operand2, written, defined);

            case Mnemonic.Mov:
            case Mnemonic.Movzx:
            case Mnemonic.Movsx:
            case Mnemonic.Lea:
                return WalkDefSource(instruction, written, ref defined);

            default:
                return WalkRmw(instruction, written, ref defined);
        }
    }

    private static string? WalkDefSource(in DecodedInstruction instruction, uint written, ref uint defined)
    {
        Operand destination = instruction.Operand1;
        if (destination.Kind == OperandKind.Memory)
        {
            return "dest-mem";
        }

        if (destination.Kind != OperandKind.Register)
        {
            return "dest-form";
        }

        if (destination.Size != 32)
        {
            return "partial-def";
        }

        Operand source = instruction.Operand2;
        switch (source.Kind)
        {
            case OperandKind.Immediate:
                break;
            case OperandKind.Memory:
                string? addressError = CheckOperandRead(source, written, defined);
                if (addressError is not null)
                {
                    return addressError;
                }

                break;
            case OperandKind.Register:
                if (instruction.Mnemonic == Mnemonic.Lea)
                {
                    return "lea-form";
                }

                string? readError = CheckOperandRead(source, written, defined);
                if (readError is not null)
                {
                    return readError;
                }

                break;
            default:
                return "src-form";
        }

        if (instruction.Mnemonic == Mnemonic.Lea && source.Kind != OperandKind.Memory)
        {
            return "lea-form";
        }

        defined |= 1u << destination.RegisterIndex;
        return null;
    }

    private static string? WalkRmw(in DecodedInstruction instruction, uint written, ref uint defined)
    {
        Operand destination = instruction.Operand1;
        if (destination.Kind == OperandKind.Memory)
        {
            return "dest-mem";
        }

        if (destination.Kind != OperandKind.Register)
        {
            return "dest-form";
        }

        if (destination.Size != 32)
        {
            return "partial-def";
        }

        Operand source = instruction.Operand2;
        bool selfClearing = instruction.Mnemonic is Mnemonic.Xor or Mnemonic.Sub
            && source.Kind == OperandKind.Register
            && source.RegisterIndex == destination.RegisterIndex;
        if (!selfClearing)
        {
            string? oldDestination = CheckOperandRead(destination, written, defined);
            if (oldDestination is not null)
            {
                return oldDestination;
            }
        }

        if (!selfClearing && source.Kind is OperandKind.Register or OperandKind.Memory)
        {
            string? sourceError = CheckOperandRead(source, written, defined);
            if (sourceError is not null)
            {
                return sourceError;
            }
        }

        defined |= 1u << destination.RegisterIndex;
        return null;
    }

    private static string? CheckOperandRead(in Operand operand, uint written, uint defined)
    {
        switch (operand.Kind)
        {
            case OperandKind.Register:
                return CheckRegister(operand.RegisterIndex, written, defined);
            case OperandKind.Memory:
                MemoryOperand memory = operand.Memory;
                if (memory.HasBase)
                {
                    string? baseError = CheckRegister((int)memory.Base, written, defined);
                    if (baseError is not null)
                    {
                        return baseError;
                    }
                }

                if (memory.HasIndex)
                {
                    return CheckRegister((int)memory.Index, written, defined);
                }

                return null;
            default:
                return null;
        }
    }

    private static string? CheckRegister(int register, uint written, uint defined)
    {
        uint bit = 1u << register;
        if ((written & bit) != 0 && (defined & bit) == 0)
        {
            return "cross-read";
        }

        return null;
    }
}
