using System;
using XGrid.Memory;

namespace XGrid.Cpu;

/// <summary>Table-driven x86 decoder for the implemented instruction set (32-bit protected and 16-bit real mode).</summary>
public static class Decoder
{
    /// <summary>Maximum x86 instruction length in bytes.</summary>
    public const int MaxInstructionLength = 15;

    /// <summary>Decodes one instruction from the given code bytes.</summary>
    public static DecodedInstruction Decode(
        ReadOnlySpan<byte> code,
        int defaultOperandSize = 32,
        int defaultAddressSize = 32)
    {
        int position = 0;
        bool operandOverride = false;
        bool addressOverride = false;
        SegmentRegister? segmentOverride = null;
        RepPrefix repPrefix = RepPrefix.None;
        byte mandatoryPrefix = 0;

        while (position < code.Length && position < MaxInstructionLength)
        {
            byte prefix = code[position];
            switch (prefix)
            {
                case 0x66:
                    operandOverride = true;
                    mandatoryPrefix = 0x66;
                    position++;
                    continue;
                case 0x67:
                    addressOverride = true;
                    position++;
                    continue;
                case 0x26:
                    segmentOverride = SegmentRegister.Es;
                    position++;
                    continue;
                case 0x2E:
                    segmentOverride = SegmentRegister.Cs;
                    position++;
                    continue;
                case 0x36:
                    segmentOverride = SegmentRegister.Ss;
                    position++;
                    continue;
                case 0x3E:
                    segmentOverride = SegmentRegister.Ds;
                    position++;
                    continue;
                case 0x64:
                    segmentOverride = SegmentRegister.Fs;
                    position++;
                    continue;
                case 0x65:
                    segmentOverride = SegmentRegister.Gs;
                    position++;
                    continue;
                case 0xF0:
                    position++;
                    continue;
                case 0xF2:
                    repPrefix = RepPrefix.Repne;
                    mandatoryPrefix = 0xF2;
                    position++;
                    continue;
                case 0xF3:
                    repPrefix = RepPrefix.Rep;
                    mandatoryPrefix = 0xF3;
                    position++;
                    continue;
            }

            break;
        }

        if (position >= code.Length || position >= MaxInstructionLength)
        {
            return Unsupported(position, 0, defaultOperandSize, defaultAddressSize);
        }

        int operandSize = operandOverride ? (defaultOperandSize == 32 ? 16 : 32) : defaultOperandSize;
        int addressSize = addressOverride ? (defaultAddressSize == 32 ? 16 : 32) : defaultAddressSize;

        byte opcode = code[position++];
        DecodedInstruction instruction = opcode == 0x0F
            ? DecodeTwoByte(code, ref position, operandSize, addressSize, segmentOverride, mandatoryPrefix)
            : DecodeOneByte(code, ref position, opcode, operandSize, addressSize, segmentOverride);

        return repPrefix == RepPrefix.None ? instruction : instruction with { Rep = repPrefix };
    }

    private static DecodedInstruction DecodeOneByte(
        ReadOnlySpan<byte> code,
        ref int position,
        byte opcode,
        int operandSize,
        int addressSize,
        SegmentRegister? segmentOverride)
    {
        // 0x00-0x3F: ADD/OR/ADC/SBB/AND/SUB/XOR/CMP groups.
        if (opcode <= 0x3F && (opcode & 0x07) <= 5)
        {
            return DecodeAluGroup(code, ref position, opcode, operandSize, addressSize, segmentOverride);
        }

        switch (opcode)
        {
            case 0x06 or 0x0E or 0x16 or 0x1E:
                return Make(
                    Mnemonic.PushSegment,
                    Operand.ImmediateOperand(SegmentIndex(opcode), 16),
                    Operand.None,
                    position,
                    operandSize,
                    addressSize,
                    opcode);

            case 0x07 or 0x17 or 0x1F:
                return Make(
                    Mnemonic.PopSegment,
                    Operand.ImmediateOperand(SegmentIndex(opcode), 16),
                    Operand.None,
                    position,
                    operandSize,
                    addressSize,
                    opcode);

            case 0x40 or 0x41 or 0x42 or 0x43 or 0x44 or 0x45 or 0x46 or 0x47:
                return Make(Mnemonic.Inc, Operand.RegisterOperand(opcode & 7, operandSize), Operand.None, position,
                    operandSize, addressSize, opcode);

            case 0x48 or 0x49 or 0x4A or 0x4B or 0x4C or 0x4D or 0x4E or 0x4F:
                return Make(Mnemonic.Dec, Operand.RegisterOperand(opcode & 7, operandSize), Operand.None, position,
                    operandSize, addressSize, opcode);

            case 0x50 or 0x51 or 0x52 or 0x53 or 0x54 or 0x55 or 0x56 or 0x57:
                return Make(Mnemonic.Push, Operand.RegisterOperand(opcode & 7, operandSize), Operand.None, position,
                    operandSize, addressSize, opcode);

            case 0x58 or 0x59 or 0x5A or 0x5B or 0x5C or 0x5D or 0x5E or 0x5F:
                return Make(Mnemonic.Pop, Operand.RegisterOperand(opcode & 7, operandSize), Operand.None, position,
                    operandSize, addressSize, opcode);

            case 0x60:
                return Make(Mnemonic.Pusha, Operand.None, Operand.None, position, operandSize, addressSize, opcode);

            case 0x61:
                return Make(Mnemonic.Popa, Operand.None, Operand.None, position, operandSize, addressSize, opcode);

            case 0x63:
                if (!TryParseModRm(code, ref position, 16, addressSize, segmentOverride, out int arplSource,
                        out Operand arplDestination))
                {
                    return Unsupported(position, opcode, operandSize, addressSize);
                }

                return Make(
                    Mnemonic.Arpl,
                    arplDestination,
                    Operand.RegisterOperand(arplSource, 16),
                    position,
                    operandSize,
                    addressSize,
                    opcode);

            case 0x68:
                if (!TryReadImmediate(code, ref position, operandSize, out uint immOp))
                {
                    return Unsupported(position, opcode, operandSize, addressSize);
                }

                return Make(Mnemonic.Push, Operand.ImmediateOperand(immOp, operandSize), Operand.None, position,
                    operandSize, addressSize, opcode);

            case 0x6A:
                if (!TryReadImmediate(code, ref position, 8, out uint imm8Push))
                {
                    return Unsupported(position, opcode, operandSize, addressSize);
                }

                return Make(Mnemonic.Push, Operand.ImmediateOperand(SignExtend8(imm8Push), operandSize), Operand.None,
                    position, operandSize, addressSize, opcode);

            case >= 0x70 and <= 0x7F:
                if (!TryReadImmediate(code, ref position, 8, out uint rel8Jcc))
                {
                    return Unsupported(position, opcode, operandSize, addressSize);
                }

                return Make(
                    Mnemonic.Jcc,
                    Operand.ImmediateOperand(SignExtend8(rel8Jcc), operandSize),
                    Operand.None,
                    position,
                    operandSize,
                    addressSize,
                    opcode,
                    (ConditionCode)(opcode & 0x0F));

            case 0x80 or 0x81 or 0x83:
                return DecodeGroup1(code, ref position, opcode, operandSize, addressSize, segmentOverride);

            case 0xC0 or 0xC1 or 0xD0 or 0xD1 or 0xD2 or 0xD3:
                return DecodeGroup2(code, ref position, opcode, operandSize, addressSize, segmentOverride);

            case 0x69 or 0x6B:
                return DecodeImulImmediate(code, ref position, opcode, operandSize, addressSize, segmentOverride);

            case 0xCC:
                return Make(Mnemonic.Int3, Operand.None, Operand.None, position, operandSize, addressSize, opcode);

            case 0xCD:
                {
                    if (!TryReadImmediate(code, ref position, 8, out uint vector))
                    {
                        return Unsupported(position, opcode, operandSize, addressSize);
                    }

                    return Make(Mnemonic.Interrupt, Operand.ImmediateOperand(vector, 8), Operand.None, position,
                        operandSize, addressSize, opcode);
                }

            case 0xCE:
                return Make(Mnemonic.Into, Operand.None, Operand.None, position, operandSize, addressSize, opcode);

            case 0xCF:
                return Make(Mnemonic.Iret, Operand.None, Operand.None, position, operandSize, addressSize, opcode);

            case 0xD4 or 0xD5:
                {
                    if (!TryReadImmediate(code, ref position, 8, out uint bcdBase))
                    {
                        return Unsupported(position, opcode, operandSize, addressSize);
                    }

                    Mnemonic mnemonic = opcode == 0xD4 ? Mnemonic.Aam : Mnemonic.Aad;
                    return Make(mnemonic, Operand.ImmediateOperand(bcdBase, 8), Operand.None, position, operandSize,
                        addressSize, opcode);
                }

            case 0x84 or 0x85:
                {
                    int testSize = opcode == 0x84 ? 8 : operandSize;
                    if (!TryParseModRm(code, ref position, testSize, addressSize, segmentOverride, out int testReg,
                            out Operand testRm))
                    {
                        return Unsupported(position, opcode, operandSize, addressSize);
                    }

                    return Make(Mnemonic.Test, testRm, Operand.RegisterOperand(testReg, testSize), position, operandSize,
                        addressSize, opcode);
                }

            case 0x86 or 0x87:
                {
                    int xchgSize = opcode == 0x86 ? 8 : operandSize;
                    if (!TryParseModRm(code, ref position, xchgSize, addressSize, segmentOverride, out int xchgReg,
                            out Operand xchgRm))
                    {
                        return Unsupported(position, opcode, operandSize, addressSize);
                    }

                    return Make(Mnemonic.Xchg, xchgRm, Operand.RegisterOperand(xchgReg, xchgSize), position, operandSize,
                        addressSize, opcode);
                }

            case 0x88 or 0x89:
                {
                    int movSize = opcode == 0x88 ? 8 : operandSize;
                    if (!TryParseModRm(code, ref position, movSize, addressSize, segmentOverride, out int movReg,
                            out Operand movRm))
                    {
                        return Unsupported(position, opcode, operandSize, addressSize);
                    }

                    return Make(Mnemonic.Mov, movRm, Operand.RegisterOperand(movReg, movSize), position, operandSize,
                        addressSize, opcode);
                }

            case 0x8A or 0x8B:
                {
                    int movSize = opcode == 0x8A ? 8 : operandSize;
                    if (!TryParseModRm(code, ref position, movSize, addressSize, segmentOverride, out int movReg,
                            out Operand movRm))
                    {
                        return Unsupported(position, opcode, operandSize, addressSize);
                    }

                    return Make(Mnemonic.Mov, Operand.RegisterOperand(movReg, movSize), movRm, position, operandSize,
                        addressSize, opcode);
                }

            case 0x8D:
                {
                    if (!TryParseModRm(code, ref position, operandSize, addressSize, segmentOverride, out int leaReg,
                            out Operand leaRm)
                        || leaRm.Kind != OperandKind.Memory)
                    {
                        return Unsupported(position, opcode, operandSize, addressSize);
                    }

                    return Make(Mnemonic.Lea, Operand.RegisterOperand(leaReg, operandSize), leaRm, position, operandSize,
                        addressSize, opcode);
                }

            case 0x8C or 0x8E:
                {
                    if (!TryParseModRm(code, ref position, 16, addressSize, segmentOverride, out int segment,
                            out Operand rm)
                        || segment > 5)
                    {
                        return Unsupported(position, opcode, operandSize, addressSize);
                    }

                    return opcode == 0x8E
                        ? Make(Mnemonic.MovSreg, Operand.ImmediateOperand((uint)segment, 8), rm, position, operandSize,
                            addressSize, opcode)
                        : Make(Mnemonic.MovSregRead, rm, Operand.ImmediateOperand((uint)segment, 8), position, operandSize,
                            addressSize, opcode);
                }

            case 0x90:
                return Make(Mnemonic.Nop, Operand.None, Operand.None, position, operandSize, addressSize, opcode);

            case 0x9A:
                {
                    if (!TryReadImmediate(code, ref position, operandSize, out uint farOffset)
                        || !TryReadImmediate(code, ref position, 16, out uint farSelector))
                    {
                        return Unsupported(position, opcode, operandSize, addressSize);
                    }

                    return new DecodedInstruction
                    {
                        Mnemonic = Mnemonic.CallFar,
                        Operand1 = Operand.ImmediateOperand(farOffset, operandSize),
                        Operand2 = Operand.ImmediateOperand(farSelector, 16),
                        Length = position,
                        OperandSize = operandSize,
                        AddressSize = addressSize,
                        Opcode = opcode,
                    };
                }

            case 0xCA or 0xCB:
                {
                    if (opcode == 0xCB)
                    {
                        return Make(Mnemonic.Retf, Operand.None, Operand.None, position, operandSize, addressSize, opcode);
                    }

                    if (!TryReadImmediate(code, ref position, 16, out uint popBytes))
                    {
                        return Unsupported(position, opcode, operandSize, addressSize);
                    }

                    return Make(Mnemonic.Retf, Operand.ImmediateOperand(popBytes, 16), Operand.None, position, operandSize,
                        addressSize, opcode);
                }

            case 0xEA:
                {
                    if (!TryReadImmediate(code, ref position, operandSize, out uint farOffset)
                        || !TryReadImmediate(code, ref position, 16, out uint farSelector))
                    {
                        return Unsupported(position, opcode, operandSize, addressSize);
                    }

                    return new DecodedInstruction
                    {
                        Mnemonic = Mnemonic.JmpFar,
                        Operand1 = Operand.ImmediateOperand(farOffset, operandSize),
                        Operand2 = Operand.ImmediateOperand(farSelector, 16),
                        Length = position,
                        OperandSize = operandSize,
                        AddressSize = addressSize,
                        Opcode = opcode,
                    };
                }

            case >= 0x91 and <= 0x97:
                return Make(
                    Mnemonic.Xchg,
                    Operand.RegisterOperand(0, operandSize),
                    Operand.RegisterOperand(opcode & 7, operandSize),
                    position,
                    operandSize,
                    addressSize,
                    opcode);

            case 0x98:
                return Make(Mnemonic.Cbw, Operand.None, Operand.None, position, operandSize, addressSize, opcode);

            case 0x99:
                return Make(Mnemonic.Cdq, Operand.None, Operand.None, position, operandSize, addressSize, opcode);

            case 0x9C:
                return Make(Mnemonic.Pushf, Operand.None, Operand.None, position, operandSize, addressSize, opcode);
            case 0x9D:
                return Make(Mnemonic.Popf, Operand.None, Operand.None, position, operandSize, addressSize, opcode);

            case 0xA0 or 0xA1 or 0xA2 or 0xA3:
                {
                    int moffsSize = opcode is 0xA0 or 0xA2 ? 8 : operandSize;
                    if (!TryReadImmediate(code, ref position, addressSize, out uint offset))
                    {
                        return Unsupported(position, opcode, operandSize, addressSize);
                    }

                    var memory = new MemoryOperand
                    {
                        Displacement = offset,
                        SegmentOverride = segmentOverride,
                        Is16Bit = addressSize == 16,
                    };
                    Operand memoryOperand = Operand.MemoryOperandValue(memory, moffsSize);
                    Operand accumulator = Operand.RegisterOperand(0, moffsSize);

                    return opcode is 0xA0 or 0xA1
                        ? Make(Mnemonic.Mov, accumulator, memoryOperand, position, operandSize, addressSize, opcode)
                        : Make(Mnemonic.Mov, memoryOperand, accumulator, position, operandSize, addressSize, opcode);
                }

            case 0x6C or 0x6D or 0x6E or 0x6F:
            case 0xA4 or 0xA5 or 0xA6 or 0xA7 or 0xAA or 0xAB or 0xAC or 0xAD or 0xAE or 0xAF:
                return DecodeString(opcode, position, operandSize, addressSize);

            case 0xA8 or 0xA9:
                {
                    int testSize = opcode == 0xA8 ? 8 : operandSize;
                    if (!TryReadImmediate(code, ref position, testSize, out uint testImm))
                    {
                        return Unsupported(position, opcode, operandSize, addressSize);
                    }

                    return Make(
                        Mnemonic.Test,
                        Operand.RegisterOperand(0, testSize),
                        Operand.ImmediateOperand(testImm, testSize),
                        position,
                        operandSize,
                        addressSize,
                        opcode);
                }

            case >= 0xB0 and <= 0xB7:
                {
                    if (!TryReadImmediate(code, ref position, 8, out uint immByte))
                    {
                        return Unsupported(position, opcode, operandSize, addressSize);
                    }

                    return Make(
                        Mnemonic.Mov,
                        Operand.RegisterOperand(opcode & 7, 8),
                        Operand.ImmediateOperand(immByte, 8),
                        position,
                        operandSize,
                        addressSize,
                        opcode);
                }

            case >= 0xB8 and <= 0xBF:
                {
                    if (!TryReadImmediate(code, ref position, operandSize, out uint immValue))
                    {
                        return Unsupported(position, opcode, operandSize, addressSize);
                    }

                    return Make(
                        Mnemonic.Mov,
                        Operand.RegisterOperand(opcode & 7, operandSize),
                        Operand.ImmediateOperand(immValue, operandSize),
                        position,
                        operandSize,
                        addressSize,
                        opcode);
                }

            case 0xC2:
                if (!TryReadImmediate(code, ref position, 16, out uint retImm))
                {
                    return Unsupported(position, opcode, operandSize, addressSize);
                }

                return Make(Mnemonic.Ret, Operand.ImmediateOperand(retImm, 16), Operand.None, position, operandSize,
                    addressSize, opcode);

            case 0xC3:
                return Make(Mnemonic.Ret, Operand.None, Operand.None, position, operandSize, addressSize, opcode);

            case 0xC9:
                return Make(Mnemonic.Leave, Operand.None, Operand.None, position, operandSize, addressSize, opcode);

            case 0x8F:
                {
                    if (!TryParseModRm(code, ref position, operandSize, addressSize, segmentOverride, out int popReg,
                            out Operand popRm)
                        || popReg != 0)
                    {
                        return Unsupported(position, opcode, operandSize, addressSize);
                    }

                    return Make(Mnemonic.Pop, popRm, Operand.None, position, operandSize, addressSize, opcode);
                }

            case 0xC8:
                {
                    if (!TryReadImmediate(code, ref position, 16, out uint enterAllocation)
                        || !TryReadImmediate(code, ref position, 8, out uint enterNesting))
                    {
                        return Unsupported(position, opcode, operandSize, addressSize);
                    }

                    return Make(
                        Mnemonic.Enter,
                        Operand.ImmediateOperand(enterAllocation, 16),
                        Operand.ImmediateOperand(enterNesting, 8),
                        position,
                        operandSize,
                        addressSize,
                        opcode);
                }

            case 0xC6 or 0xC7:
                {
                    int movSize = opcode == 0xC6 ? 8 : operandSize;
                    if (!TryParseModRm(code, ref position, movSize, addressSize, segmentOverride, out _, out Operand movRm)
                        || !TryReadImmediate(code, ref position, movSize, out uint movImm))
                    {
                        return Unsupported(position, opcode, operandSize, addressSize);
                    }

                    return Make(Mnemonic.Mov, movRm, Operand.ImmediateOperand(movImm, movSize), position, operandSize,
                        addressSize, opcode);
                }

            case 0xE4 or 0xE5 or 0xE6 or 0xE7:
                return DecodeInOutImmediate(code, ref position, opcode, operandSize, addressSize);

            case 0xEC or 0xED or 0xEE or 0xEF:
                return DecodeInOutDx(opcode, position, operandSize, addressSize);

            case 0xE0 or 0xE1 or 0xE2 or 0xE3:
                {
                    if (!TryReadImmediate(code, ref position, 8, out uint rel8Loop))
                    {
                        return Unsupported(position, opcode, operandSize, addressSize);
                    }

                    Mnemonic loopMnemonic = opcode switch
                    {
                        0xE0 => Mnemonic.LoopNe,
                        0xE1 => Mnemonic.LoopE,
                        0xE2 => Mnemonic.Loop,
                        _ => Mnemonic.Jecxz,
                    };

                    return Make(loopMnemonic, Operand.ImmediateOperand(SignExtend8(rel8Loop), operandSize), Operand.None,
                        position, operandSize, addressSize, opcode);
                }

            case 0xE8 or 0xE9:
                {
                    if (!TryReadImmediate(code, ref position, operandSize, out uint rel))
                    {
                        return Unsupported(position, opcode, operandSize, addressSize);
                    }

                    return Make(
                        opcode == 0xE8 ? Mnemonic.Call : Mnemonic.Jmp,
                        Operand.ImmediateOperand(rel, operandSize),
                        Operand.None,
                        position,
                        operandSize,
                        addressSize,
                        opcode);
                }

            case 0xEB:
                if (!TryReadImmediate(code, ref position, 8, out uint rel8Jmp))
                {
                    return Unsupported(position, opcode, operandSize, addressSize);
                }

                return Make(Mnemonic.Jmp, Operand.ImmediateOperand(SignExtend8(rel8Jmp), operandSize), Operand.None,
                    position, operandSize, addressSize, opcode);

            case 0x9B:
                return Make(Mnemonic.Fwait, Operand.None, Operand.None, position, operandSize, addressSize, opcode);

            case 0xF4:
                return Make(Mnemonic.Hlt, Operand.None, Operand.None, position, operandSize, addressSize, opcode);

            case 0xF5:
                return Make(Mnemonic.Cmc, Operand.None, Operand.None, position, operandSize, addressSize, opcode);

            case 0xD7:
                return Make(
                    Mnemonic.Xlat,
                    Operand.MemoryOperandValue(new MemoryOperand
                    {
                        Base = Gpr.Ebx,
                        HasBase = true,
                        SegmentOverride = segmentOverride,
                    }, 8),
                    Operand.None,
                    position,
                    operandSize,
                    addressSize,
                    opcode);

            case >= 0xD8 and <= 0xDF:
                return DecodeFpu(code, ref position, opcode, operandSize, addressSize, segmentOverride);

            case 0xF6 or 0xF7:
                return DecodeGroup3(code, ref position, opcode, operandSize, addressSize, segmentOverride);

            case 0xF8:
                return Make(Mnemonic.Clc, Operand.None, Operand.None, position, operandSize, addressSize, opcode);

            case 0xF9:
                return Make(Mnemonic.Stc, Operand.None, Operand.None, position, operandSize, addressSize, opcode);

            case 0xFA:
                return Make(Mnemonic.Cli, Operand.None, Operand.None, position, operandSize, addressSize, opcode);

            case 0xFB:
                return Make(Mnemonic.Sti, Operand.None, Operand.None, position, operandSize, addressSize, opcode);

            case 0xFC:
                return Make(Mnemonic.Cld, Operand.None, Operand.None, position, operandSize, addressSize, opcode);

            case 0xFD:
                return Make(Mnemonic.Std, Operand.None, Operand.None, position, operandSize, addressSize, opcode);

            case 0xFE:
                return DecodeGroup4(code, ref position, opcode, addressSize, segmentOverride);

            case 0xFF:
                return DecodeGroup5(code, ref position, opcode, operandSize, addressSize, segmentOverride);

            default:
                return Unsupported(position, opcode, operandSize, addressSize);
        }
    }

    private static DecodedInstruction DecodeTwoByte(
        ReadOnlySpan<byte> code,
        ref int position,
        int operandSize,
        int addressSize,
        SegmentRegister? segmentOverride,
        byte mandatoryPrefix)
    {
        if (position >= code.Length)
        {
            return Unsupported(position, 0x0F, operandSize, addressSize);
        }

        byte opcode = code[position++];

        if (opcode is 0x08 or 0x09)
        {
            return Make(
                opcode == 0x08 ? Mnemonic.Invd : Mnemonic.Wbinvd,
                Operand.None,
                Operand.None,
                position,
                operandSize,
                addressSize,
                opcode);
        }

        if (opcode == 0x00)
        {
            if (!TryParseModRm(code, ref position, 16, addressSize, segmentOverride, out int systemReg,
                    out Operand systemRm))
            {
                return Unsupported(position, opcode, operandSize, addressSize);
            }

            Mnemonic systemMnemonic = systemReg switch
            {
                0 => Mnemonic.Sldt,
                1 => Mnemonic.Str,
                2 => Mnemonic.Lldt,
                3 => Mnemonic.Ltr,
                4 => Mnemonic.Verr,
                5 => Mnemonic.Verw,
                _ => Mnemonic.Unsupported,
            };

            return systemMnemonic == Mnemonic.Unsupported
                ? Unsupported(position, opcode, operandSize, addressSize)
                : Make(systemMnemonic, systemRm, Operand.None, position, operandSize, addressSize, opcode);
        }

        if (opcode is 0xA3 or 0xAB or 0xB3 or 0xBB)
        {
            if (!TryParseModRm(code, ref position, operandSize, addressSize, segmentOverride, out int bitReg,
                    out Operand bitDestination))
            {
                return Unsupported(position, opcode, operandSize, addressSize);
            }

            Mnemonic bitMnemonic = opcode switch
            {
                0xA3 => Mnemonic.Bt,
                0xAB => Mnemonic.Bts,
                0xB3 => Mnemonic.Btr,
                _ => Mnemonic.Btc,
            };

            return Make(
                bitMnemonic,
                bitDestination,
                Operand.RegisterOperand(bitReg, operandSize),
                position,
                operandSize,
                addressSize,
                opcode);
        }

        if (opcode == 0xBA)
        {
            // Group 8: BT/BTS/BTR/BTC r/m, imm8.
            if (!TryParseModRm(code, ref position, operandSize, addressSize, segmentOverride, out int bitOp,
                    out Operand bitDestination) || bitOp < 4)
            {
                return Unsupported(position, opcode, operandSize, addressSize);
            }

            if (!TryReadImmediate(code, ref position, 8, out uint bitImmediate))
            {
                return Unsupported(position, opcode, operandSize, addressSize);
            }

            Mnemonic bitMnemonic = bitOp switch
            {
                4 => Mnemonic.Bt,
                5 => Mnemonic.Bts,
                6 => Mnemonic.Btr,
                _ => Mnemonic.Btc,
            };

            return Make(
                bitMnemonic,
                bitDestination,
                Operand.ImmediateOperand(bitImmediate, 8),
                position,
                operandSize,
                addressSize,
                opcode);
        }

        if (opcode is 0xA4 or 0xA5 or 0xAC or 0xAD)
        {
            if (!TryParseModRm(code, ref position, operandSize, addressSize, segmentOverride, out int shiftReg,
                    out Operand shiftRm))
            {
                return Unsupported(position, opcode, operandSize, addressSize);
            }

            Operand shiftCount;
            if (opcode is 0xA4 or 0xAC)
            {
                if (!TryReadImmediate(code, ref position, 8, out uint shiftImmediate))
                {
                    return Unsupported(position, opcode, operandSize, addressSize);
                }

                shiftCount = Operand.ImmediateOperand(shiftImmediate, 8);
            }
            else
            {
                shiftCount = Operand.RegisterOperand(1, 8);
            }

            return new DecodedInstruction
            {
                Mnemonic = opcode is 0xA4 or 0xA5 ? Mnemonic.Shld : Mnemonic.Shrd,
                Operand1 = shiftRm,
                Operand2 = Operand.RegisterOperand(shiftReg, operandSize),
                Operand3 = shiftCount,
                Length = position,
                OperandSize = operandSize,
                AddressSize = addressSize,
                Opcode = opcode,
            };
        }

        if (opcode is 0xBC or 0xBD)
        {
            if (!TryParseModRm(code, ref position, operandSize, addressSize, segmentOverride, out int destinationReg,
                    out Operand source))
            {
                return Unsupported(position, opcode, operandSize, addressSize);
            }

            return Make(
                opcode == 0xBC ? Mnemonic.Bsf : Mnemonic.Bsr,
                Operand.RegisterOperand(destinationReg, operandSize),
                source,
                position,
                operandSize,
                addressSize,
                opcode);
        }

        if (opcode is 0xB0 or 0xB1 or 0xC0 or 0xC1)
        {
            bool byteForm = opcode is 0xB0 or 0xC0;
            int size = byteForm ? 8 : operandSize;
            if (!TryParseModRm(code, ref position, size, addressSize, segmentOverride, out int sourceReg,
                    out Operand destination))
            {
                return Unsupported(position, opcode, operandSize, addressSize);
            }

            Mnemonic atomicMnemonic = opcode is 0xB0 or 0xB1 ? Mnemonic.Cmpxchg : Mnemonic.Xadd;
            return Make(
                atomicMnemonic,
                destination,
                Operand.RegisterOperand(sourceReg, size),
                position,
                operandSize,
                addressSize,
                opcode);
        }

        if (opcode is 0xC7)
        {
            if (!TryParseModRm(code, ref position, 64, addressSize, segmentOverride, out int groupReg,
                    out Operand groupRm)
                || groupReg != 1
                || groupRm.Kind != OperandKind.Memory)
            {
                return Unsupported(position, opcode, operandSize, addressSize);
            }

            return Make(
                Mnemonic.Cmpxchg8B,
                groupRm,
                Operand.None,
                position,
                operandSize,
                addressSize,
                opcode);
        }

        if (opcode is >= 0xC8 and <= 0xCF)
        {
            return Make(
                Mnemonic.Bswap,
                Operand.RegisterOperand(opcode & 7, 32),
                Operand.None,
                position,
                operandSize,
                addressSize,
                opcode);
        }

        if (opcode is >= 0x80 and <= 0x8F)
        {
            if (!TryReadImmediate(code, ref position, operandSize, out uint rel))
            {
                return Unsupported(position, opcode, operandSize, addressSize);
            }

            return Make(
                Mnemonic.Jcc,
                Operand.ImmediateOperand(rel, operandSize),
                Operand.None,
                position,
                operandSize,
                addressSize,
                opcode,
                (ConditionCode)(opcode & 0x0F));
        }

        if (opcode == 0x01)
        {
            if (!TryParseModRm(code, ref position, operandSize, addressSize, segmentOverride, out int reg,
                    out Operand rm)
                || rm.Kind != OperandKind.Memory)
            {
                return Unsupported(position, opcode, operandSize, addressSize);
            }

            Mnemonic descriptor = reg switch
            {
                0 => Mnemonic.Sgdt,
                1 => Mnemonic.Sidt,
                2 => Mnemonic.Lgdt,
                3 => Mnemonic.Lidt,
                7 => Mnemonic.Invlpg,
                _ => Mnemonic.Unsupported,
            };

            return descriptor == Mnemonic.Unsupported || rm.Kind != OperandKind.Memory
                ? Unsupported(position, opcode, operandSize, addressSize)
                : Make(descriptor, rm, Operand.None, position, operandSize, addressSize, opcode);
        }

        if (opcode is 0x20 or 0x21 or 0x22 or 0x23)
        {
            if (position >= code.Length)
            {
                return Unsupported(position, opcode, operandSize, addressSize);
            }

            byte modrm = code[position++];
            if (((modrm >> 6) & 3) != 3)
            {
                return Unsupported(position, opcode, operandSize, addressSize);
            }

            int controlRegister = (modrm >> 3) & 7;
            int generalRegister = modrm & 7;

            Mnemonic mnemonic = opcode switch
            {
                0x20 => Mnemonic.MovCrRead,
                0x21 => Mnemonic.MovDrRead,
                0x22 => Mnemonic.MovCrWrite,
                _ => Mnemonic.MovDrWrite,
            };

            return Make(
                mnemonic,
                Operand.RegisterOperand(generalRegister, 32),
                Operand.ImmediateOperand((uint)controlRegister, 32),
                position,
                operandSize,
                addressSize,
                opcode);
        }

        if (opcode == 0xAF)
        {
            if (!TryParseModRm(code, ref position, operandSize, addressSize, segmentOverride, out int reg,
                    out Operand rm))
            {
                return Unsupported(position, opcode, operandSize, addressSize);
            }

            return Make(Mnemonic.Imul, Operand.RegisterOperand(reg, operandSize), rm, position, operandSize,
                addressSize, opcode);
        }

        if (opcode is 0x30 or 0x31 or 0x32 or 0xA2)
        {
            Mnemonic system = opcode switch
            {
                0x30 => Mnemonic.Wrmsr,
                0x31 => Mnemonic.Rdtsc,
                0x32 => Mnemonic.Rdmsr,
                _ => Mnemonic.Cpuid,
            };

            return Make(system, Operand.None, Operand.None, position, operandSize, addressSize, opcode);
        }

        if (opcode is 0xA0 or 0xA1 or 0xA8 or 0xA9)
        {
            Mnemonic pushPop = opcode is 0xA0 or 0xA8 ? Mnemonic.PushSegment : Mnemonic.PopSegment;
            uint segment = opcode is 0xA0 or 0xA1 ? 4u : 5u;
            return Make(pushPop, Operand.ImmediateOperand(segment, 16), Operand.None, position, operandSize,
                addressSize, opcode);
        }

        if (opcode is >= 0x40 and <= 0x4F)
        {
            if (!TryParseModRm(code, ref position, operandSize, addressSize, segmentOverride, out int reg,
                    out Operand rm))
            {
                return Unsupported(position, opcode, operandSize, addressSize);
            }

            return Make(
                Mnemonic.Cmovcc,
                Operand.RegisterOperand(reg, operandSize),
                rm,
                position,
                operandSize,
                addressSize,
                opcode,
                (ConditionCode)(opcode & 0x0F));
        }

        if (opcode is >= 0x90 and <= 0x9F)
        {
            if (!TryParseModRm(code, ref position, 8, addressSize, segmentOverride, out _, out Operand rm))
            {
                return Unsupported(position, opcode, operandSize, addressSize);
            }

            return Make(
                Mnemonic.Setcc,
                rm,
                Operand.None,
                position,
                operandSize,
                addressSize,
                opcode,
                (ConditionCode)(opcode & 0x0F));
        }

        if (opcode is 0xB6 or 0xB7 or 0xBE or 0xBF)
        {
            int sourceSize = opcode is 0xB6 or 0xBE ? 8 : 16;
            if (!TryParseModRm(code, ref position, sourceSize, addressSize, segmentOverride, out int reg,
                    out Operand rm))
            {
                return Unsupported(position, opcode, operandSize, addressSize);
            }

            Mnemonic extend = opcode is 0xB6 or 0xB7 ? Mnemonic.Movzx : Mnemonic.Movsx;
            return Make(extend, Operand.RegisterOperand(reg, operandSize), rm, position, operandSize, addressSize,
                opcode);
        }

        return DecodeMmxOrSse(code, ref position, opcode, operandSize, addressSize, segmentOverride, mandatoryPrefix);
    }

    private static DecodedInstruction DecodeMmxOrSse(
        ReadOnlySpan<byte> code,
        ref int position,
        byte opcode,
        int operandSize,
        int addressSize,
        SegmentRegister? segmentOverride,
        byte mandatoryPrefix)
    {
        if (mandatoryPrefix != 0 && !IsSsePrefixedOpcode(opcode))
        {
            return Unsupported(position, opcode, operandSize, addressSize);
        }

        switch (opcode)
        {
            case 0x18:
                if (!TryParseModRm(code, ref position, 8, addressSize, segmentOverride, out int prefetchReg,
                        out Operand prefetchRm)
                    || prefetchRm.Kind != OperandKind.Memory)
                {
                    return Unsupported(position, opcode, operandSize, addressSize);
                }

                return prefetchReg <= 3
                    ? Make(Mnemonic.Prefetch, prefetchRm, Operand.None, position, operandSize, addressSize, opcode)
                    : Make(Mnemonic.Nop, Operand.None, Operand.None, position, operandSize, addressSize, opcode);

            case 0x60 or 0x61 or 0x62 or 0x63 or 0x64 or 0x65 or 0x66 or 0x67
                or 0x68 or 0x69 or 0x6A or 0x6B:
                return DecodeMmxBinary(code, ref position, opcode, operandSize, addressSize, segmentOverride);

            case 0x6E or 0x7E:
                {
                    int direction = opcode == 0x6E ? 0 : 1;
                    if (!TryParseModRm(code, ref position, 32, addressSize, segmentOverride, out int mmReg,
                            out Operand source))
                    {
                        return Unsupported(position, opcode, operandSize, addressSize);
                    }

                    return direction == 0
                        ? Make(Mnemonic.Movd, Operand.RegisterOperand(mmReg, 64), source, position, operandSize,
                            addressSize, opcode)
                        : Make(Mnemonic.Movd, source, Operand.RegisterOperand(mmReg, 64), position, operandSize,
                            addressSize, opcode);
                }

            case 0x6F or 0x7F:
                {
                    if (!TryParseModRm(code, ref position, 64, addressSize, segmentOverride, out int mmReg,
                            out Operand source))
                    {
                        return Unsupported(position, opcode, operandSize, addressSize);
                    }

                    return opcode == 0x6F
                        ? Make(Mnemonic.Movq, Operand.RegisterOperand(mmReg, 64), source, position, operandSize,
                            addressSize, opcode)
                        : Make(Mnemonic.Movq, source, Operand.RegisterOperand(mmReg, 64), position, operandSize,
                            addressSize, opcode);
                }

            case 0x70:
                {
                    if (!TryParseModRm(code, ref position, 64, addressSize, segmentOverride, out int mmReg,
                            out Operand source)
                        || !TryReadImmediate(code, ref position, 8, out uint shuffle))
                    {
                        return Unsupported(position, opcode, operandSize, addressSize);
                    }

                    return new DecodedInstruction
                    {
                        Mnemonic = Mnemonic.Pshufw,
                        Operand1 = Operand.RegisterOperand(mmReg, 64),
                        Operand2 = source,
                        Operand3 = Operand.ImmediateOperand(shuffle, 8),
                        Length = position,
                        OperandSize = operandSize,
                        AddressSize = addressSize,
                        Opcode = opcode,
                    };
                }

            case 0x71 or 0x72 or 0x73:
                return DecodeMmxShiftImmediate(code, ref position, opcode, operandSize, addressSize);

            case 0x74 or 0x75 or 0x76:
                return DecodeMmxBinary(code, ref position, opcode, operandSize, addressSize, segmentOverride);

            case 0x77:
                return Make(Mnemonic.Emms, Operand.None, Operand.None, position, operandSize, addressSize, opcode);

            case 0xC4:
                {
                    if (!TryParseModRm(code, ref position, 32, addressSize, segmentOverride, out int mmReg,
                            out Operand source))
                    {
                        return Unsupported(position, opcode, operandSize, addressSize);
                    }

                    if (source.Kind == OperandKind.Memory)
                    {
                        source = Operand.MemoryOperandValue(source.Memory, 16);
                    }

                    if (!TryReadImmediate(code, ref position, 8, out uint lane))
                    {
                        return Unsupported(position, opcode, operandSize, addressSize);
                    }

                    return new DecodedInstruction
                    {
                        Mnemonic = Mnemonic.Pinsrw,
                        Operand1 = Operand.RegisterOperand(mmReg, 64),
                        Operand2 = source,
                        Operand3 = Operand.ImmediateOperand(lane, 8),
                        Length = position,
                        OperandSize = operandSize,
                        AddressSize = addressSize,
                        Opcode = opcode,
                    };
                }

            case 0xC5:
                {
                    if (!TryParseModRm(code, ref position, 64, addressSize, segmentOverride, out int mmReg,
                            out Operand source)
                        || source.Kind != OperandKind.Register
                        || !TryReadImmediate(code, ref position, 8, out uint lane))
                    {
                        return Unsupported(position, opcode, operandSize, addressSize);
                    }

                    return new DecodedInstruction
                    {
                        Mnemonic = Mnemonic.Pextrw,
                        Operand1 = Operand.RegisterOperand(mmReg, 32),
                        Operand2 = source,
                        Operand3 = Operand.ImmediateOperand(lane, 8),
                        Length = position,
                        OperandSize = operandSize,
                        AddressSize = addressSize,
                        Opcode = opcode,
                    };
                }

            case 0xC6:
                {
                    if (!TryParseModRm(code, ref position, 128, addressSize, segmentOverride, out int xmmReg,
                            out Operand source)
                        || !TryReadImmediate(code, ref position, 8, out uint control))
                    {
                        return Unsupported(position, opcode, operandSize, addressSize);
                    }

                    return new DecodedInstruction
                    {
                        Mnemonic = Mnemonic.Shufps,
                        Operand1 = Operand.RegisterOperand(xmmReg, 128),
                        Operand2 = source,
                        Operand3 = Operand.ImmediateOperand(control, 8),
                        Length = position,
                        OperandSize = operandSize,
                        AddressSize = addressSize,
                        Opcode = opcode,
                    };
                }

            case 0xD1 or 0xD2 or 0xD3:

            case 0xD5:
                return DecodeMmxBinary(code, ref position, opcode, operandSize, addressSize, segmentOverride);

            case 0xD7:
                {
                    if (!TryParseModRm(code, ref position, 64, addressSize, segmentOverride, out int mmReg,
                            out Operand source)
                        || source.Kind != OperandKind.Register)
                    {
                        return Unsupported(position, opcode, operandSize, addressSize);
                    }

                    return Make(Mnemonic.Pmovmskb, Operand.RegisterOperand(mmReg, 32), source, position, operandSize,
                        addressSize, opcode);
                }

            case >= 0xD8 and <= 0xDF:
                return DecodeMmxBinary(code, ref position, opcode, operandSize, addressSize, segmentOverride);

            case 0xE7:
                {
                    if (!TryParseModRm(code, ref position, 64, addressSize, segmentOverride, out int mmReg,
                            out Operand destination)
                        || destination.Kind != OperandKind.Memory)
                    {
                        return Unsupported(position, opcode, operandSize, addressSize);
                    }

                    return Make(Mnemonic.Movntq, destination, Operand.RegisterOperand(mmReg, 64), position, operandSize,
                        addressSize, opcode);
                }

            case >= 0xE0 and <= 0xEF:
                return DecodeMmxBinary(code, ref position, opcode, operandSize, addressSize, segmentOverride);

            case 0xF7:
                {
                    if (!TryParseModRm(code, ref position, 64, addressSize, segmentOverride, out int mmReg,
                            out Operand source)
                        || source.Kind != OperandKind.Register)
                    {
                        return Unsupported(position, opcode, operandSize, addressSize);
                    }

                    return Make(Mnemonic.Maskmovq, Operand.RegisterOperand(mmReg, 64), source, position, operandSize,
                        addressSize, opcode);
                }

            case >= 0xF1 and <= 0xFF:
                return DecodeMmxBinary(code, ref position, opcode, operandSize, addressSize, segmentOverride);

            case 0x10 or 0x11:
                return DecodeSseMove(code, ref position, opcode, operandSize, addressSize, segmentOverride,
                    mandatoryPrefix);

            case 0x12 or 0x13 or 0x16 or 0x17:
                return DecodeSseHighLow(code, ref position, opcode, operandSize, addressSize, segmentOverride);

            case 0x14 or 0x15:
                {
                    if (!TryParseModRm(code, ref position, 128, addressSize, segmentOverride, out int xmmReg,
                            out Operand source))
                    {
                        return Unsupported(position, opcode, operandSize, addressSize);
                    }

                    Mnemonic mnemonic = opcode == 0x14 ? Mnemonic.Unpcklps : Mnemonic.Unpckhps;
                    return Make(mnemonic, Operand.RegisterOperand(xmmReg, 128), source, position, operandSize, addressSize,
                        opcode);
                }

            case 0x28 or 0x29:
                {
                    if (!TryParseModRm(code, ref position, 128, addressSize, segmentOverride, out int xmmReg,
                            out Operand source))
                    {
                        return Unsupported(position, opcode, operandSize, addressSize);
                    }

                    return opcode == 0x28
                        ? Make(Mnemonic.Movaps, Operand.RegisterOperand(xmmReg, 128), source, position, operandSize,
                            addressSize, opcode)
                        : Make(Mnemonic.Movaps, source, Operand.RegisterOperand(xmmReg, 128), position, operandSize,
                            addressSize, opcode);
                }

            case 0x2A:
                return DecodeSseConvertTo(code, ref position, opcode, operandSize, addressSize, segmentOverride,
                    mandatoryPrefix);

            case 0x2B:
                {
                    if (!TryParseModRm(code, ref position, 128, addressSize, segmentOverride, out int xmmReg,
                            out Operand destination)
                        || destination.Kind != OperandKind.Memory)
                    {
                        return Unsupported(position, opcode, operandSize, addressSize);
                    }

                    return Make(Mnemonic.Movntps, destination, Operand.RegisterOperand(xmmReg, 128), position, operandSize,
                        addressSize, opcode);
                }

            case 0x2C or 0x2D:
                return DecodeSseConvertFrom(code, ref position, opcode, operandSize, addressSize, segmentOverride,
                    mandatoryPrefix);

            case 0x2E or 0x2F:
                {
                    if (!TryParseModRm(code, ref position, 32, addressSize, segmentOverride, out int xmmReg,
                            out Operand source))
                    {
                        return Unsupported(position, opcode, operandSize, addressSize);
                    }

                    Mnemonic mnemonic = opcode == 0x2E ? Mnemonic.Ucomiss : Mnemonic.Comiss;
                    return Make(mnemonic, Operand.RegisterOperand(xmmReg, 128), source, position, operandSize, addressSize,
                        opcode);
                }

            case 0x50:
                {
                    if (!TryParseModRm(code, ref position, 128, addressSize, segmentOverride, out int xmmReg,
                            out Operand source)
                        || source.Kind != OperandKind.Register)
                    {
                        return Unsupported(position, opcode, operandSize, addressSize);
                    }

                    return Make(Mnemonic.Movmskps, Operand.RegisterOperand(xmmReg, 32), source, position, operandSize,
                        addressSize, opcode);
                }

            case 0x51 or 0x52 or 0x53:
                return DecodeSseScalarPacked(code, ref position, opcode, operandSize, addressSize, segmentOverride,
                    mandatoryPrefix);

            case >= 0x54 and <= 0x57:
                return DecodeSseLogical(code, ref position, opcode, operandSize, addressSize, segmentOverride);

            case 0x58 or 0x59 or 0x5C or 0x5D or 0x5E or 0x5F:
                return DecodeSseArithmetic(code, ref position, opcode, operandSize, addressSize, segmentOverride,
                    mandatoryPrefix);

            case 0xAE:
                return DecodeSseState(code, ref position, opcode, operandSize, addressSize, segmentOverride);

            case 0xC2:
                return DecodeSseCompare(code, ref position, opcode, operandSize, addressSize, segmentOverride,
                    mandatoryPrefix);

            default:
                return Unsupported(position, opcode, operandSize, addressSize);
        }
    }

    private static bool IsSsePrefixedOpcode(byte opcode)
    {
        return opcode is 0x10 or 0x11 or 0x2A or 0x2C or 0x2D or 0x51 or 0x52 or 0x53
            or 0x58 or 0x59 or 0x5C or 0x5D or 0x5E or 0x5F or 0xC2;
    }

    private static DecodedInstruction DecodeMmxBinary(
        ReadOnlySpan<byte> code,
        ref int position,
        byte opcode,
        int operandSize,
        int addressSize,
        SegmentRegister? segmentOverride)
    {
        if (!TryParseModRm(code, ref position, 64, addressSize, segmentOverride, out int mmReg, out Operand source))
        {
            return Unsupported(position, opcode, operandSize, addressSize);
        }

        Mnemonic mnemonic = opcode switch
        {
            0x60 => Mnemonic.Punpcklbw,
            0x61 => Mnemonic.Punpcklwd,
            0x62 => Mnemonic.Punpckldq,
            0x63 => Mnemonic.Packsswb,
            0x64 => Mnemonic.Pcmpgtb,
            0x65 => Mnemonic.Pcmpgtw,
            0x66 => Mnemonic.Pcmpgtd,
            0x67 => Mnemonic.Packuswb,
            0x68 => Mnemonic.Punpckhbw,
            0x69 => Mnemonic.Punpckhwd,
            0x6A => Mnemonic.Punpckhdq,
            0x6B => Mnemonic.Packssdw,
            0x74 => Mnemonic.Pcmpeqb,
            0x75 => Mnemonic.Pcmpeqw,
            0x76 => Mnemonic.Pcmpeqd,
            0xD1 => Mnemonic.Psrlw,
            0xD2 => Mnemonic.Psrld,
            0xD3 => Mnemonic.Psrlq,
            0xD5 => Mnemonic.Pmullw,
            0xD8 => Mnemonic.Psubusb,
            0xD9 => Mnemonic.Psubusw,
            0xDA => Mnemonic.Pminub,
            0xDB => Mnemonic.Pand,
            0xDC => Mnemonic.Paddusb,
            0xDD => Mnemonic.Paddusw,
            0xDE => Mnemonic.Pmaxub,
            0xDF => Mnemonic.Pandn,
            0xE0 => Mnemonic.Pavgb,
            0xE1 => Mnemonic.Psraw,
            0xE2 => Mnemonic.Psrad,
            0xE3 => Mnemonic.Pavgw,
            0xE4 => Mnemonic.Pmulhuw,
            0xE5 => Mnemonic.Pmulhw,
            0xE8 => Mnemonic.Psubsb,
            0xE9 => Mnemonic.Psubsw,
            0xEA => Mnemonic.Pminsw,
            0xEB => Mnemonic.Por,
            0xEC => Mnemonic.Paddsb,
            0xED => Mnemonic.Paddsw,
            0xEE => Mnemonic.Pmaxsw,
            0xEF => Mnemonic.Pxor,
            0xF1 => Mnemonic.Psllw,
            0xF2 => Mnemonic.Pslld,
            0xF3 => Mnemonic.Psllq,
            0xF5 => Mnemonic.Pmaddwd,
            0xF6 => Mnemonic.Psadbw,
            0xF8 => Mnemonic.Psubb,
            0xF9 => Mnemonic.Psubw,
            0xFA => Mnemonic.Psubd,
            0xFC => Mnemonic.Paddb,
            0xFD => Mnemonic.Paddw,
            0xFE => Mnemonic.Paddd,
            _ => Mnemonic.Unsupported,
        };

        return mnemonic == Mnemonic.Unsupported
            ? Unsupported(position, opcode, operandSize, addressSize)
            : Make(mnemonic, Operand.RegisterOperand(mmReg, 64), source, position, operandSize, addressSize, opcode);
    }

    private static DecodedInstruction DecodeMmxShiftImmediate(
        ReadOnlySpan<byte> code,
        ref int position,
        byte opcode,
        int operandSize,
        int addressSize)
    {
        if (position >= code.Length)
        {
            return Unsupported(position, opcode, operandSize, addressSize);
        }

        byte modrm = code[position];
        int mod = (modrm >> 6) & 3;
        int reg = (modrm >> 3) & 7;
        int rm = modrm & 7;

        if (mod != 3)
        {
            return Unsupported(position, opcode, operandSize, addressSize);
        }

        position++;

        Mnemonic mnemonic = (opcode, reg) switch
        {
            (0x71, 2) => Mnemonic.Psrlw,
            (0x71, 4) => Mnemonic.Psraw,
            (0x71, 6) => Mnemonic.Psllw,
            (0x72, 2) => Mnemonic.Psrld,
            (0x72, 4) => Mnemonic.Psrad,
            (0x72, 6) => Mnemonic.Pslld,
            (0x73, 2) => Mnemonic.Psrlq,
            (0x73, 6) => Mnemonic.Psllq,
            _ => Mnemonic.Unsupported,
        };

        if (mnemonic == Mnemonic.Unsupported || !TryReadImmediate(code, ref position, 8, out uint count))
        {
            return Unsupported(position, opcode, operandSize, addressSize);
        }

        return Make(
            mnemonic,
            Operand.RegisterOperand(rm, 64),
            Operand.ImmediateOperand(count, 8),
            position,
            operandSize,
            addressSize,
            opcode);
    }

    private static DecodedInstruction DecodeSseMove(
        ReadOnlySpan<byte> code,
        ref int position,
        byte opcode,
        int operandSize,
        int addressSize,
        SegmentRegister? segmentOverride,
        byte mandatoryPrefix)
    {
        if (mandatoryPrefix is not (0 or 0xF3))
        {
            return Unsupported(position, opcode, operandSize, addressSize);
        }

        bool scalar = mandatoryPrefix == 0xF3;
        int size = scalar ? 32 : 128;
        if (!TryParseModRm(code, ref position, size, addressSize, segmentOverride, out int xmmReg, out Operand source))
        {
            return Unsupported(position, opcode, operandSize, addressSize);
        }

        Mnemonic mnemonic = scalar ? Mnemonic.Movss : Mnemonic.Movups;
        return opcode == 0x10
            ? Make(mnemonic, Operand.RegisterOperand(xmmReg, 128), source, position, operandSize, addressSize, opcode)
            : Make(mnemonic, source, Operand.RegisterOperand(xmmReg, 128), position, operandSize, addressSize, opcode);
    }

    private static DecodedInstruction DecodeSseHighLow(
        ReadOnlySpan<byte> code,
        ref int position,
        byte opcode,
        int operandSize,
        int addressSize,
        SegmentRegister? segmentOverride)
    {
        if (!TryParseModRm(code, ref position, 64, addressSize, segmentOverride, out int xmmReg, out Operand source))
        {
            return Unsupported(position, opcode, operandSize, addressSize);
        }

        Mnemonic mnemonic;
        if (opcode is 0x12 or 0x16)
        {
            mnemonic = source.Kind == OperandKind.Register
                ? opcode == 0x12 ? Mnemonic.Movhlps : Mnemonic.Movlhps
                : opcode == 0x12
                    ? Mnemonic.Movlps
                    : Mnemonic.Movhps;
        }
        else
        {
            if (source.Kind != OperandKind.Memory)
            {
                return Unsupported(position, opcode, operandSize, addressSize);
            }

            mnemonic = opcode == 0x13 ? Mnemonic.Movlps : Mnemonic.Movhps;
        }

        if (opcode is 0x13 or 0x17)
        {
            return Make(mnemonic, source, Operand.RegisterOperand(xmmReg, 128), position, operandSize, addressSize,
                opcode);
        }

        if (source.Kind == OperandKind.Register)
        {
            source = Operand.RegisterOperand(source.RegisterIndex, 128);
        }

        return Make(mnemonic, Operand.RegisterOperand(xmmReg, 128), source, position, operandSize, addressSize, opcode);
    }

    private static DecodedInstruction DecodeSseConvertTo(
        ReadOnlySpan<byte> code,
        ref int position,
        byte opcode,
        int operandSize,
        int addressSize,
        SegmentRegister? segmentOverride,
        byte mandatoryPrefix)
    {
        if (mandatoryPrefix is not (0 or 0xF3))
        {
            return Unsupported(position, opcode, operandSize, addressSize);
        }

        if (mandatoryPrefix == 0xF3)
        {
            if (!TryParseModRm(code, ref position, 32, addressSize, segmentOverride, out int xmmReg,
                    out Operand source))
            {
                return Unsupported(position, opcode, operandSize, addressSize);
            }

            return Make(Mnemonic.Cvtsi2Ss, Operand.RegisterOperand(xmmReg, 128), source, position, operandSize,
                addressSize, opcode);
        }

        if (!TryParseModRm(code, ref position, 64, addressSize, segmentOverride, out int mmReg, out Operand mmSource))
        {
            return Unsupported(position, opcode, operandSize, addressSize);
        }

        return Make(Mnemonic.Cvtpi2Ps, Operand.RegisterOperand(mmReg, 128), mmSource, position, operandSize,
            addressSize, opcode);
    }

    private static DecodedInstruction DecodeSseConvertFrom(
        ReadOnlySpan<byte> code,
        ref int position,
        byte opcode,
        int operandSize,
        int addressSize,
        SegmentRegister? segmentOverride,
        byte mandatoryPrefix)
    {
        if (mandatoryPrefix is not (0 or 0xF3))
        {
            return Unsupported(position, opcode, operandSize, addressSize);
        }

        bool truncate = opcode == 0x2C;
        if (mandatoryPrefix == 0xF3)
        {
            if (!TryParseModRm(code, ref position, 32, addressSize, segmentOverride, out int gpr, out Operand source))
            {
                return Unsupported(position, opcode, operandSize, addressSize);
            }

            Mnemonic scalar = truncate ? Mnemonic.Cvttss2Si : Mnemonic.Cvtss2Si;
            return Make(scalar, Operand.RegisterOperand(gpr, 32), source, position, operandSize, addressSize, opcode);
        }

        if (!TryParseModRm(code, ref position, 64, addressSize, segmentOverride, out int mmReg, out Operand mmSource))
        {
            return Unsupported(position, opcode, operandSize, addressSize);
        }

        Mnemonic mnemonic = truncate ? Mnemonic.Cvttps2Pi : Mnemonic.Cvtps2Pi;
        return Make(mnemonic, Operand.RegisterOperand(mmReg, 64), mmSource, position, operandSize, addressSize, opcode);
    }

    private static DecodedInstruction DecodeSseScalarPacked(
        ReadOnlySpan<byte> code,
        ref int position,
        byte opcode,
        int operandSize,
        int addressSize,
        SegmentRegister? segmentOverride,
        byte mandatoryPrefix)
    {
        if (mandatoryPrefix is not (0 or 0xF3))
        {
            return Unsupported(position, opcode, operandSize, addressSize);
        }

        bool scalar = mandatoryPrefix == 0xF3;
        int size = scalar ? 32 : 128;
        if (!TryParseModRm(code, ref position, size, addressSize, segmentOverride, out int xmmReg, out Operand source))
        {
            return Unsupported(position, opcode, operandSize, addressSize);
        }

        Mnemonic mnemonic = opcode switch
        {
            0x51 => scalar ? Mnemonic.Sqrtss : Mnemonic.Sqrtps,
            0x52 => scalar ? Mnemonic.Rsqrtss : Mnemonic.Rsqrtps,
            _ => scalar ? Mnemonic.Rcpss : Mnemonic.Rcpps,
        };

        return Make(mnemonic, Operand.RegisterOperand(xmmReg, 128), source, position, operandSize, addressSize, opcode);
    }

    private static DecodedInstruction DecodeSseLogical(
        ReadOnlySpan<byte> code,
        ref int position,
        byte opcode,
        int operandSize,
        int addressSize,
        SegmentRegister? segmentOverride)
    {
        if (!TryParseModRm(code, ref position, 128, addressSize, segmentOverride, out int xmmReg, out Operand source))
        {
            return Unsupported(position, opcode, operandSize, addressSize);
        }

        Mnemonic mnemonic = opcode switch
        {
            0x54 => Mnemonic.Andps,
            0x55 => Mnemonic.Andnps,
            0x56 => Mnemonic.Orps,
            _ => Mnemonic.Xorps,
        };

        return Make(mnemonic, Operand.RegisterOperand(xmmReg, 128), source, position, operandSize, addressSize, opcode);
    }

    private static DecodedInstruction DecodeSseArithmetic(
        ReadOnlySpan<byte> code,
        ref int position,
        byte opcode,
        int operandSize,
        int addressSize,
        SegmentRegister? segmentOverride,
        byte mandatoryPrefix)
    {
        if (mandatoryPrefix is not (0 or 0xF3))
        {
            return Unsupported(position, opcode, operandSize, addressSize);
        }

        bool scalar = mandatoryPrefix == 0xF3;
        int size = scalar ? 32 : 128;
        if (!TryParseModRm(code, ref position, size, addressSize, segmentOverride, out int xmmReg, out Operand source))
        {
            return Unsupported(position, opcode, operandSize, addressSize);
        }

        Mnemonic mnemonic = opcode switch
        {
            0x58 => scalar ? Mnemonic.Addss : Mnemonic.Addps,
            0x59 => scalar ? Mnemonic.Mulss : Mnemonic.Mulps,
            0x5C => scalar ? Mnemonic.Subss : Mnemonic.Subps,
            0x5D => scalar ? Mnemonic.Minss : Mnemonic.Minps,
            0x5E => scalar ? Mnemonic.Divss : Mnemonic.Divps,
            _ => scalar ? Mnemonic.Maxss : Mnemonic.Maxps,
        };

        return Make(mnemonic, Operand.RegisterOperand(xmmReg, 128), source, position, operandSize, addressSize, opcode);
    }

    private static DecodedInstruction DecodeSseCompare(
        ReadOnlySpan<byte> code,
        ref int position,
        byte opcode,
        int operandSize,
        int addressSize,
        SegmentRegister? segmentOverride,
        byte mandatoryPrefix)
    {
        if (mandatoryPrefix is not (0 or 0xF3))
        {
            return Unsupported(position, opcode, operandSize, addressSize);
        }

        bool scalar = mandatoryPrefix == 0xF3;
        int size = scalar ? 32 : 128;
        if (!TryParseModRm(code, ref position, size, addressSize, segmentOverride, out int xmmReg, out Operand source)
            || !TryReadImmediate(code, ref position, 8, out uint predicate))
        {
            return Unsupported(position, opcode, operandSize, addressSize);
        }

        return new DecodedInstruction
        {
            Mnemonic = scalar ? Mnemonic.Cmpss : Mnemonic.Cmpps,
            Operand1 = Operand.RegisterOperand(xmmReg, 128),
            Operand2 = source,
            Operand3 = Operand.ImmediateOperand(predicate, 8),
            Length = position,
            OperandSize = operandSize,
            AddressSize = addressSize,
            Opcode = opcode,
        };
    }

    private static DecodedInstruction DecodeSseState(
        ReadOnlySpan<byte> code,
        ref int position,
        byte opcode,
        int operandSize,
        int addressSize,
        SegmentRegister? segmentOverride)
    {
        if (position >= code.Length)
        {
            return Unsupported(position, opcode, operandSize, addressSize);
        }

        byte modrm = code[position];
        int mod = (modrm >> 6) & 3;
        int reg = (modrm >> 3) & 7;
        int rm = modrm & 7;

        if (reg is 5 or 6 or 7 && mod == 3 && rm == 0)
        {
            position++;
            Mnemonic fence = reg switch
            {
                5 => Mnemonic.Lfence,
                6 => Mnemonic.Mfence,
                _ => Mnemonic.Sfence,
            };

            return Make(fence, Operand.None, Operand.None, position, operandSize, addressSize, opcode);
        }

        int size = reg switch
        {
            0 or 1 => 4096,
            2 or 3 => 32,
            _ => 0,
        };

        if (size == 0 || mod == 3)
        {
            return Unsupported(position, opcode, operandSize, addressSize);
        }

        if (!TryParseModRm(code, ref position, size, addressSize, segmentOverride, out _, out Operand memory))
        {
            return Unsupported(position, opcode, operandSize, addressSize);
        }

        Mnemonic mnemonic = reg switch
        {
            0 => Mnemonic.Fxsave,
            1 => Mnemonic.Fxrstor,
            2 => Mnemonic.Ldmxcsr,
            _ => Mnemonic.Stmxcsr,
        };

        return Make(mnemonic, memory, Operand.None, position, operandSize, addressSize, opcode);
    }

    private static DecodedInstruction DecodeFpu(
        ReadOnlySpan<byte> code,
        ref int position,
        byte opcode,
        int operandSize,
        int addressSize,
        SegmentRegister? segmentOverride)
    {
        if (position >= code.Length)
        {
            return Unsupported(position, opcode, operandSize, addressSize);
        }

        byte modrm = code[position];
        int mod = (modrm >> 6) & 3;
        int reg = (modrm >> 3) & 7;
        int rm = modrm & 7;

        if (mod == 3)
        {
            position++;
            return DecodeFpuRegister(opcode, reg, rm, position, operandSize, addressSize);
        }

        if (!TryGetFpuMemoryOperation(opcode, reg, operandSize, out Mnemonic mnemonic, out int memorySize))
        {
            return Unsupported(position, opcode, operandSize, addressSize);
        }

        if (!TryParseModRm(code, ref position, memorySize, addressSize, segmentOverride, out _, out Operand memory))
        {
            return Unsupported(position, opcode, operandSize, addressSize);
        }

        if (opcode is 0xD8 or 0xDC)
        {
            return Make(mnemonic, Operand.RegisterOperand(0, 80), memory, position, operandSize, addressSize, opcode);
        }

        return Make(mnemonic, memory, Operand.None, position, operandSize, addressSize, opcode);
    }

    private static bool TryGetFpuMemoryOperation(
        byte opcode,
        int reg,
        int operandSize,
        out Mnemonic mnemonic,
        out int memorySize)
    {
        mnemonic = Mnemonic.Unsupported;
        memorySize = 32;

        switch (opcode)
        {
            case 0xD8:
                mnemonic = reg switch
                {
                    0 => Mnemonic.Fadd,
                    1 => Mnemonic.Fmul,
                    2 => Mnemonic.Fcom,
                    3 => Mnemonic.Fcomp,
                    4 => Mnemonic.Fsub,
                    5 => Mnemonic.Fsubr,
                    6 => Mnemonic.Fdiv,
                    _ => Mnemonic.Fdivr,
                };
                return true;

            case 0xD9:
                memorySize = reg switch
                {
                    0 or 2 or 3 => 32,
                    4 or 6 => operandSize,
                    _ => 16,
                };
                mnemonic = reg switch
                {
                    0 => Mnemonic.Fld,
                    2 => Mnemonic.Fst,
                    3 => Mnemonic.Fstp,
                    4 => Mnemonic.Fldenv,
                    5 => Mnemonic.Fldcw,
                    6 => Mnemonic.Fnstenv,
                    7 => Mnemonic.Fnstcw,
                    _ => Mnemonic.Unsupported,
                };
                return mnemonic != Mnemonic.Unsupported;

            case 0xDA:
                mnemonic = reg switch
                {
                    0 => Mnemonic.Fiadd,
                    1 => Mnemonic.Fimul,
                    2 => Mnemonic.Ficom,
                    3 => Mnemonic.Ficomp,
                    4 => Mnemonic.Fisub,
                    5 => Mnemonic.Fisubr,
                    6 => Mnemonic.Fidiv,
                    _ => Mnemonic.Fidivr,
                };
                return true;

            case 0xDB:
                memorySize = reg is 0 or 2 or 3 ? 32 : 80;
                mnemonic = reg switch
                {
                    0 => Mnemonic.Fild,
                    2 => Mnemonic.Fist,
                    3 => Mnemonic.Fistp,
                    5 => Mnemonic.Fld,
                    7 => Mnemonic.Fstp,
                    _ => Mnemonic.Unsupported,
                };
                return mnemonic != Mnemonic.Unsupported;

            case 0xDC:
                memorySize = 64;
                mnemonic = reg switch
                {
                    0 => Mnemonic.Fadd,
                    1 => Mnemonic.Fmul,
                    2 => Mnemonic.Fcom,
                    3 => Mnemonic.Fcomp,
                    4 => Mnemonic.Fsub,
                    5 => Mnemonic.Fsubr,
                    6 => Mnemonic.Fdiv,
                    _ => Mnemonic.Fdivr,
                };
                return true;

            case 0xDD:
                memorySize = reg switch
                {
                    0 or 2 or 3 => 64,
                    4 or 6 => operandSize,
                    _ => 16,
                };
                mnemonic = reg switch
                {
                    0 => Mnemonic.Fld,
                    2 => Mnemonic.Fst,
                    3 => Mnemonic.Fstp,
                    4 => Mnemonic.Frstor,
                    6 => Mnemonic.Fnsave,
                    7 => Mnemonic.Fnstsw,
                    _ => Mnemonic.Unsupported,
                };
                return mnemonic != Mnemonic.Unsupported;

            case 0xDE:
                memorySize = 16;
                mnemonic = reg switch
                {
                    0 => Mnemonic.Fiadd,
                    1 => Mnemonic.Fimul,
                    2 => Mnemonic.Ficom,
                    3 => Mnemonic.Ficomp,
                    4 => Mnemonic.Fisub,
                    5 => Mnemonic.Fisubr,
                    6 => Mnemonic.Fidiv,
                    _ => Mnemonic.Fidivr,
                };
                return true;

            case 0xDF:
                memorySize = reg switch
                {
                    0 or 2 or 3 => 16,
                    4 or 6 => 80,
                    _ => 64,
                };
                mnemonic = reg switch
                {
                    0 => Mnemonic.Fild,
                    2 => Mnemonic.Fist,
                    3 => Mnemonic.Fistp,
                    4 => Mnemonic.Fbld,
                    5 => Mnemonic.Fild,
                    6 => Mnemonic.Fbstp,
                    7 => Mnemonic.Fistp,
                    _ => Mnemonic.Unsupported,
                };
                return mnemonic != Mnemonic.Unsupported;

            default:
                return false;
        }
    }

    private static DecodedInstruction DecodeFpuRegister(
        byte opcode,
        int reg,
        int rm,
        int length,
        int operandSize,
        int addressSize)
    {
        Operand st0 = Operand.RegisterOperand(0, 80);
        Operand sti = Operand.RegisterOperand(rm, 80);

        switch (opcode)
        {
            case 0xD8:
                return reg switch
                {
                    0 => Make(Mnemonic.Fadd, st0, sti, length, operandSize, addressSize, opcode),
                    1 => Make(Mnemonic.Fmul, st0, sti, length, operandSize, addressSize, opcode),
                    2 => Make(Mnemonic.Fcom, sti, Operand.None, length, operandSize, addressSize, opcode),
                    3 => Make(Mnemonic.Fcomp, sti, Operand.None, length, operandSize, addressSize, opcode),
                    4 => Make(Mnemonic.Fsub, st0, sti, length, operandSize, addressSize, opcode),
                    5 => Make(Mnemonic.Fsubr, st0, sti, length, operandSize, addressSize, opcode),
                    6 => Make(Mnemonic.Fdiv, st0, sti, length, operandSize, addressSize, opcode),
                    _ => Make(Mnemonic.Fdivr, st0, sti, length, operandSize, addressSize, opcode),
                };

            case 0xD9:
                return reg switch
                {
                    0 => Make(Mnemonic.Fld, sti, Operand.None, length, operandSize, addressSize, opcode),
                    1 => Make(Mnemonic.Fxch, sti, Operand.None, length, operandSize, addressSize, opcode),
                    2 => rm == 0
                        ? Make(Mnemonic.Fnop, Operand.None, Operand.None, length, operandSize, addressSize, opcode)
                        : Unsupported(length, opcode, operandSize, addressSize),
                    4 => rm switch
                    {
                        0 => Make(Mnemonic.Fchs, Operand.None, Operand.None, length, operandSize, addressSize, opcode),
                        1 => Make(Mnemonic.Fabs, Operand.None, Operand.None, length, operandSize, addressSize, opcode),
                        4 => Make(Mnemonic.Ftst, Operand.None, Operand.None, length, operandSize, addressSize, opcode),
                        5 => Make(Mnemonic.Fxam, Operand.None, Operand.None, length, operandSize, addressSize, opcode),
                        _ => Unsupported(length, opcode, operandSize, addressSize),
                    },
                    5 => rm switch
                    {
                        0 => Make(Mnemonic.Fld1, Operand.None, Operand.None, length, operandSize, addressSize, opcode),
                        1 => Make(Mnemonic.Fldl2T, Operand.None, Operand.None, length, operandSize, addressSize,
                            opcode),
                        2 => Make(Mnemonic.Fldl2E, Operand.None, Operand.None, length, operandSize, addressSize,
                            opcode),
                        3 => Make(Mnemonic.Fldpi, Operand.None, Operand.None, length, operandSize, addressSize, opcode),
                        4 => Make(Mnemonic.Fldlg2, Operand.None, Operand.None, length, operandSize, addressSize,
                            opcode),
                        5 => Make(Mnemonic.Fldln2, Operand.None, Operand.None, length, operandSize, addressSize,
                            opcode),
                        6 => Make(Mnemonic.Fldz, Operand.None, Operand.None, length, operandSize, addressSize, opcode),
                        _ => Unsupported(length, opcode, operandSize, addressSize),
                    },
                    6 => rm switch
                    {
                        0 => Make(Mnemonic.F2Xm1, Operand.None, Operand.None, length, operandSize, addressSize, opcode),
                        1 => Make(Mnemonic.Fyl2X, Operand.None, Operand.None, length, operandSize, addressSize, opcode),
                        2 => Make(Mnemonic.Fptan, Operand.None, Operand.None, length, operandSize, addressSize, opcode),
                        3 => Make(Mnemonic.Fpatan, Operand.None, Operand.None, length, operandSize, addressSize,
                            opcode),
                        4 => Make(Mnemonic.Fxtract, Operand.None, Operand.None, length, operandSize, addressSize,
                            opcode),
                        5 => Make(Mnemonic.Fprem1, Operand.None, Operand.None, length, operandSize, addressSize,
                            opcode),
                        6 => Make(Mnemonic.Fdecstp, Operand.None, Operand.None, length, operandSize, addressSize,
                            opcode),
                        _ => Make(Mnemonic.Fincstp, Operand.None, Operand.None, length, operandSize, addressSize,
                            opcode),
                    },
                    7 => rm switch
                    {
                        0 => Make(Mnemonic.Fprem, Operand.None, Operand.None, length, operandSize, addressSize, opcode),
                        1 => Make(Mnemonic.Fyl2Xp1, Operand.None, Operand.None, length, operandSize, addressSize,
                            opcode),
                        2 => Make(Mnemonic.Fsqrt, Operand.None, Operand.None, length, operandSize, addressSize, opcode),
                        3 => Make(Mnemonic.Fsincos, Operand.None, Operand.None, length, operandSize, addressSize,
                            opcode),
                        4 => Make(Mnemonic.Frndint, Operand.None, Operand.None, length, operandSize, addressSize,
                            opcode),
                        5 => Make(Mnemonic.Fscale, Operand.None, Operand.None, length, operandSize, addressSize,
                            opcode),
                        6 => Make(Mnemonic.Fsin, Operand.None, Operand.None, length, operandSize, addressSize, opcode),
                        _ => Make(Mnemonic.Fcos, Operand.None, Operand.None, length, operandSize, addressSize, opcode),
                    },
                    _ => Unsupported(length, opcode, operandSize, addressSize),
                };

            case 0xDA:
                return reg switch
                {
                    0 => Make(Mnemonic.Fcmovb, st0, sti, length, operandSize, addressSize, opcode),
                    1 => Make(Mnemonic.Fcmove, st0, sti, length, operandSize, addressSize, opcode),
                    2 => Make(Mnemonic.Fcmovbe, st0, sti, length, operandSize, addressSize, opcode),
                    3 => Make(Mnemonic.Fcmovu, st0, sti, length, operandSize, addressSize, opcode),
                    5 when rm == 1 => Make(Mnemonic.Fucompp, Operand.None, Operand.None, length, operandSize,
                        addressSize, opcode),
                    _ => Unsupported(length, opcode, operandSize, addressSize),
                };

            case 0xDB:
                return reg switch
                {
                    0 => Make(Mnemonic.Fcmovnb, st0, sti, length, operandSize, addressSize, opcode),
                    1 => Make(Mnemonic.Fcmovne, st0, sti, length, operandSize, addressSize, opcode),
                    2 => Make(Mnemonic.Fcmovnbe, st0, sti, length, operandSize, addressSize, opcode),
                    3 => Make(Mnemonic.Fcmovnu, st0, sti, length, operandSize, addressSize, opcode),
                    4 => rm switch
                    {
                        2 => Make(Mnemonic.Fnclex, Operand.None, Operand.None, length, operandSize, addressSize,
                            opcode),
                        3 => Make(Mnemonic.Fninit, Operand.None, Operand.None, length, operandSize, addressSize,
                            opcode),
                        _ => Unsupported(length, opcode, operandSize, addressSize),
                    },
                    5 => Make(Mnemonic.Fucomi, st0, sti, length, operandSize, addressSize, opcode),
                    6 => Make(Mnemonic.Fcomi, st0, sti, length, operandSize, addressSize, opcode),
                    _ => Unsupported(length, opcode, operandSize, addressSize),
                };

            case 0xDC:
                return reg switch
                {
                    0 => Make(Mnemonic.Fadd, sti, st0, length, operandSize, addressSize, opcode),
                    1 => Make(Mnemonic.Fmul, sti, st0, length, operandSize, addressSize, opcode),
                    4 => Make(Mnemonic.Fsubr, sti, st0, length, operandSize, addressSize, opcode),
                    5 => Make(Mnemonic.Fsub, sti, st0, length, operandSize, addressSize, opcode),
                    6 => Make(Mnemonic.Fdivr, sti, st0, length, operandSize, addressSize, opcode),
                    7 => Make(Mnemonic.Fdiv, sti, st0, length, operandSize, addressSize, opcode),
                    _ => Unsupported(length, opcode, operandSize, addressSize),
                };

            case 0xDD:
                return reg switch
                {
                    0 => Make(Mnemonic.Ffree, sti, Operand.None, length, operandSize, addressSize, opcode),
                    1 => Make(Mnemonic.Fxch, sti, Operand.None, length, operandSize, addressSize, opcode),
                    2 => Make(Mnemonic.Fst, sti, Operand.None, length, operandSize, addressSize, opcode),
                    3 => Make(Mnemonic.Fstp, sti, Operand.None, length, operandSize, addressSize, opcode),
                    4 => Make(Mnemonic.Fucom, sti, Operand.None, length, operandSize, addressSize, opcode),
                    5 => Make(Mnemonic.Fucomp, sti, Operand.None, length, operandSize, addressSize, opcode),
                    _ => Unsupported(length, opcode, operandSize, addressSize),
                };

            case 0xDE:
                return reg switch
                {
                    0 => Make(Mnemonic.Faddp, sti, st0, length, operandSize, addressSize, opcode),
                    1 => Make(Mnemonic.Fmulp, sti, st0, length, operandSize, addressSize, opcode),
                    3 when rm == 1 => Make(Mnemonic.Fcompp, Operand.None, Operand.None, length, operandSize,
                        addressSize, opcode),
                    4 => Make(Mnemonic.Fsubrp, sti, st0, length, operandSize, addressSize, opcode),
                    5 => Make(Mnemonic.Fsubp, sti, st0, length, operandSize, addressSize, opcode),
                    6 => Make(Mnemonic.Fdivrp, sti, st0, length, operandSize, addressSize, opcode),
                    7 => Make(Mnemonic.Fdivp, sti, st0, length, operandSize, addressSize, opcode),
                    _ => Unsupported(length, opcode, operandSize, addressSize),
                };

            case 0xDF:
                return reg switch
                {
                    0 => Make(Mnemonic.Ffreep, sti, Operand.None, length, operandSize, addressSize, opcode),
                    4 when rm == 0 => Make(Mnemonic.Fnstsw, Operand.None, Operand.None, length, operandSize,
                        addressSize, opcode),
                    5 => Make(Mnemonic.Fucomip, st0, sti, length, operandSize, addressSize, opcode),
                    6 => Make(Mnemonic.Fcomip, st0, sti, length, operandSize, addressSize, opcode),
                    _ => Unsupported(length, opcode, operandSize, addressSize),
                };

            default:
                return Unsupported(length, opcode, operandSize, addressSize);
        }
    }

    private static DecodedInstruction DecodeInOutImmediate(
        ReadOnlySpan<byte> code,
        ref int position,
        byte opcode,
        int operandSize,
        int addressSize)
    {
        if (!TryReadImmediate(code, ref position, 8, out uint port))
        {
            return Unsupported(position, opcode, operandSize, addressSize);
        }

        int dataSize = opcode is 0xE4 or 0xE6 ? 8 : operandSize;
        return opcode is 0xE4 or 0xE5
            ? Make(
                Mnemonic.In,
                Operand.ImmediateOperand(port, 16),
                Operand.RegisterOperand(0, dataSize),
                position,
                operandSize,
                addressSize,
                opcode)
            : Make(
                Mnemonic.Out,
                Operand.ImmediateOperand(port, 16),
                Operand.RegisterOperand(0, dataSize),
                position,
                operandSize,
                addressSize,
                opcode);
    }

    private static DecodedInstruction DecodeInOutDx(
        byte opcode,
        int position,
        int operandSize,
        int addressSize)
    {
        int dataSize = opcode is 0xEC or 0xEE ? 8 : operandSize;
        return opcode is 0xEC or 0xED
            ? Make(
                Mnemonic.In,
                Operand.RegisterOperand(2, 16),
                Operand.RegisterOperand(0, dataSize),
                position,
                operandSize,
                addressSize,
                opcode)
            : Make(
                Mnemonic.Out,
                Operand.RegisterOperand(2, 16),
                Operand.RegisterOperand(0, dataSize),
                position,
                operandSize,
                addressSize,
                opcode);
    }

    private static DecodedInstruction DecodeString(byte opcode, int position, int operandSize, int addressSize)
    {
        bool byteForm = opcode is 0x6C or 0x6E or 0xA4 or 0xA6 or 0xAA or 0xAC or 0xAE;
        int elementSize = byteForm ? 8 : operandSize;

        Mnemonic mnemonic = opcode switch
        {
            0x6C or 0x6D => Mnemonic.Ins,
            0x6E or 0x6F => Mnemonic.Outs,
            0xA4 or 0xA5 => Mnemonic.Movs,
            0xA6 or 0xA7 => Mnemonic.Cmps,
            0xAA or 0xAB => Mnemonic.Stos,
            0xAC or 0xAD => Mnemonic.Lods,
            _ => Mnemonic.Scas,
        };

        return new DecodedInstruction
        {
            Mnemonic = mnemonic,
            OperandSize = elementSize,
            AddressSize = addressSize,
            Length = position,
            Opcode = opcode,
        };
    }

    private static DecodedInstruction DecodeAluGroup(
        ReadOnlySpan<byte> code,
        ref int position,
        byte opcode,
        int operandSize,
        int addressSize,
        SegmentRegister? segmentOverride)
    {
        Mnemonic mnemonic = AluMnemonic(opcode >> 3);
        int form = opcode & 0x07;

        switch (form)
        {
            case 0 or 1:
                {
                    int size = form == 0 ? 8 : operandSize;
                    if (!TryParseModRm(code, ref position, size, addressSize, segmentOverride, out int reg, out Operand rm))
                    {
                        return Unsupported(position, opcode, operandSize, addressSize);
                    }

                    return Make(mnemonic, rm, Operand.RegisterOperand(reg, size), position, operandSize, addressSize,
                        opcode);
                }

            case 2 or 3:
                {
                    int size = form == 2 ? 8 : operandSize;
                    if (!TryParseModRm(code, ref position, size, addressSize, segmentOverride, out int reg, out Operand rm))
                    {
                        return Unsupported(position, opcode, operandSize, addressSize);
                    }

                    return Make(mnemonic, Operand.RegisterOperand(reg, size), rm, position, operandSize, addressSize,
                        opcode);
                }

            case 4 or 5:
                {
                    int size = form == 4 ? 8 : operandSize;
                    if (!TryReadImmediate(code, ref position, size, out uint immediate))
                    {
                        return Unsupported(position, opcode, operandSize, addressSize);
                    }

                    return Make(
                        mnemonic,
                        Operand.RegisterOperand(0, size),
                        Operand.ImmediateOperand(immediate, size),
                        position,
                        operandSize,
                        addressSize,
                        opcode);
                }

            default:
                return Unsupported(position, opcode, operandSize, addressSize);
        }
    }

    private static DecodedInstruction DecodeGroup1(
        ReadOnlySpan<byte> code,
        ref int position,
        byte opcode,
        int operandSize,
        int addressSize,
        SegmentRegister? segmentOverride)
    {
        int rmSize = opcode == 0x80 ? 8 : operandSize;
        if (!TryParseModRm(code, ref position, rmSize, addressSize, segmentOverride, out int reg, out Operand rm))
        {
            return Unsupported(position, opcode, operandSize, addressSize);
        }

        int immediateSize = opcode == 0x81 ? rmSize : 8;
        if (!TryReadImmediate(code, ref position, immediateSize, out uint immediate))
        {
            return Unsupported(position, opcode, operandSize, addressSize);
        }

        uint value = opcode == 0x83 ? SignExtend8(immediate) : immediate;

        return Make(
            AluMnemonic(reg),
            rm,
            Operand.ImmediateOperand(value, rmSize),
            position,
            operandSize,
            addressSize,
            opcode);
    }

    private static DecodedInstruction DecodeGroup2(
        ReadOnlySpan<byte> code,
        ref int position,
        byte opcode,
        int operandSize,
        int addressSize,
        SegmentRegister? segmentOverride)
    {
        int rmSize = opcode is 0xC0 or 0xD0 or 0xD2 ? 8 : operandSize;
        if (!TryParseModRm(code, ref position, rmSize, addressSize, segmentOverride, out int reg, out Operand rm))
        {
            return Unsupported(position, opcode, operandSize, addressSize);
        }

        Mnemonic mnemonic = reg switch
        {
            0 => Mnemonic.Rol,
            1 => Mnemonic.Ror,
            2 => Mnemonic.Rcl,
            3 => Mnemonic.Rcr,
            4 => Mnemonic.Shl,
            5 => Mnemonic.Shr,
            6 => Mnemonic.Shl,
            _ => Mnemonic.Sar,
        };

        Operand count;
        switch (opcode)
        {
            case 0xC0 or 0xC1:
                if (!TryReadImmediate(code, ref position, 8, out uint immediate))
                {
                    return Unsupported(position, opcode, operandSize, addressSize);
                }

                count = Operand.ImmediateOperand(immediate, 8);
                break;
            case 0xD0 or 0xD1:
                count = Operand.ImmediateOperand(1, 8);
                break;
            default:
                count = Operand.RegisterOperand(1, 8);
                break;
        }

        return Make(mnemonic, rm, count, position, operandSize, addressSize, opcode);
    }

    private static DecodedInstruction DecodeImulImmediate(
        ReadOnlySpan<byte> code,
        ref int position,
        byte opcode,
        int operandSize,
        int addressSize,
        SegmentRegister? segmentOverride)
    {
        if (!TryParseModRm(code, ref position, operandSize, addressSize, segmentOverride, out int reg, out Operand rm))
        {
            return Unsupported(position, opcode, operandSize, addressSize);
        }

        int immediateSize = opcode == 0x6B ? 8 : operandSize;
        if (!TryReadImmediate(code, ref position, immediateSize, out uint immediate))
        {
            return Unsupported(position, opcode, operandSize, addressSize);
        }

        uint value = opcode == 0x6B ? SignExtend8(immediate) : immediate;

        return new DecodedInstruction
        {
            Mnemonic = Mnemonic.Imul,
            Operand1 = Operand.RegisterOperand(reg, operandSize),
            Operand2 = rm,
            Operand3 = Operand.ImmediateOperand(value, operandSize),
            Length = position,
            OperandSize = operandSize,
            AddressSize = addressSize,
            Opcode = opcode,
        };
    }

    private static DecodedInstruction DecodeGroup3(
        ReadOnlySpan<byte> code,
        ref int position,
        byte opcode,
        int operandSize,
        int addressSize,
        SegmentRegister? segmentOverride)
    {
        int size = opcode == 0xF6 ? 8 : operandSize;
        if (!TryParseModRm(code, ref position, size, addressSize, segmentOverride, out int reg, out Operand rm))
        {
            return Unsupported(position, opcode, operandSize, addressSize);
        }

        switch (reg)
        {
            case 0:
                if (!TryReadImmediate(code, ref position, size, out uint immediate))
                {
                    return Unsupported(position, opcode, operandSize, addressSize);
                }

                return Make(Mnemonic.Test, rm, Operand.ImmediateOperand(immediate, size), position, operandSize,
                    addressSize, opcode);

            case 2:
                return Make(Mnemonic.Not, rm, Operand.None, position, operandSize, addressSize, opcode);

            case 3:
                return Make(Mnemonic.Neg, rm, Operand.None, position, operandSize, addressSize, opcode);

            case 4:
                return Make(Mnemonic.Mul, rm, Operand.None, position, operandSize, addressSize, opcode);

            case 5:
                return Make(Mnemonic.Imul, rm, Operand.None, position, operandSize, addressSize, opcode);

            case 6:
                return Make(Mnemonic.Div, rm, Operand.None, position, operandSize, addressSize, opcode);

            case 7:
                return Make(Mnemonic.Idiv, rm, Operand.None, position, operandSize, addressSize, opcode);

            default:
                return Unsupported(position, opcode, operandSize, addressSize);
        }
    }

    private static DecodedInstruction DecodeGroup4(
        ReadOnlySpan<byte> code,
        ref int position,
        byte opcode,
        int addressSize,
        SegmentRegister? segmentOverride)
    {
        if (!TryParseModRm(code, ref position, 8, addressSize, segmentOverride, out int reg, out Operand rm))
        {
            return Unsupported(position, opcode, 32, addressSize);
        }

        Mnemonic mnemonic = reg switch
        {
            0 => Mnemonic.Inc,
            1 => Mnemonic.Dec,
            _ => Mnemonic.Unsupported,
        };

        return mnemonic == Mnemonic.Unsupported
            ? Unsupported(position, opcode, 32, addressSize)
            : Make(mnemonic, rm, Operand.None, position, 32, addressSize, opcode);
    }

    private static DecodedInstruction DecodeGroup5(
        ReadOnlySpan<byte> code,
        ref int position,
        byte opcode,
        int operandSize,
        int addressSize,
        SegmentRegister? segmentOverride)
    {
        if (!TryParseModRm(code, ref position, operandSize, addressSize, segmentOverride, out int reg, out Operand rm))
        {
            return Unsupported(position, opcode, operandSize, addressSize);
        }

        Mnemonic mnemonic = reg switch
        {
            0 => Mnemonic.Inc,
            1 => Mnemonic.Dec,
            2 => Mnemonic.Call,
            3 => Mnemonic.CallFar,
            4 => Mnemonic.Jmp,
            5 => Mnemonic.JmpFar,
            6 => Mnemonic.Push,
            _ => Mnemonic.Unsupported,
        };

        if (mnemonic is Mnemonic.CallFar or Mnemonic.JmpFar && rm.Kind != OperandKind.Memory)
        {
            return Unsupported(position, opcode, operandSize, addressSize);
        }

        return mnemonic == Mnemonic.Unsupported
            ? Unsupported(position, opcode, operandSize, addressSize)
            : Make(mnemonic, rm, Operand.None, position, operandSize, addressSize, opcode);
    }

    private static bool TryParseModRm(
        ReadOnlySpan<byte> code,
        ref int position,
        int rmSize,
        int addressSize,
        SegmentRegister? segmentOverride,
        out int reg,
        out Operand rmOperand)
    {
        reg = 0;
        rmOperand = Operand.None;

        if (position >= code.Length)
        {
            return false;
        }

        byte modrm = code[position++];
        int mod = (modrm >> 6) & 3;
        reg = (modrm >> 3) & 7;
        int rm = modrm & 7;

        if (mod == 3)
        {
            rmOperand = Operand.RegisterOperand(rm, rmSize);
            return true;
        }

        bool parsed = addressSize == 16
            ? TryParseMemory16(code, ref position, mod, rm, segmentOverride, out var memory)
            : TryParseMemory32(code, ref position, mod, rm, segmentOverride, out memory);

        if (!parsed)
        {
            return false;
        }

        rmOperand = Operand.MemoryOperandValue(memory, rmSize);
        return true;
    }

    private static bool TryParseMemory32(
        ReadOnlySpan<byte> code,
        ref int position,
        int mod,
        int rm,
        SegmentRegister? segmentOverride,
        out MemoryOperand memory)
    {
        memory = default;

        bool hasBase;
        Gpr baseRegister = default;
        bool hasIndex = false;
        Gpr indexRegister = default;
        int scale = 1;

        if (rm == 4)
        {
            if (position >= code.Length)
            {
                return false;
            }

            byte sib = code[position++];
            scale = 1 << ((sib >> 6) & 3);
            int index = (sib >> 3) & 7;
            int sibBase = sib & 7;

            if (index != 4)
            {
                hasIndex = true;
                indexRegister = (Gpr)index;
            }

            if (sibBase == 5 && mod == 0)
            {
                hasBase = false;
            }
            else
            {
                hasBase = true;
                baseRegister = (Gpr)sibBase;
            }
        }
        else if (rm == 5 && mod == 0)
        {
            hasBase = false;
        }
        else
        {
            hasBase = true;
            baseRegister = (Gpr)rm;
        }

        uint displacement = 0;
        if (mod == 1)
        {
            if (position >= code.Length)
            {
                return false;
            }

            displacement = unchecked((uint)(sbyte)code[position++]);
        }
        else if (mod == 2 || (mod == 0 && !hasBase))
        {
            if (position + 4 > code.Length)
            {
                return false;
            }

            displacement = Endian.Read32(code.Slice(position, 4));
            position += 4;
        }

        memory = new MemoryOperand
        {
            HasBase = hasBase,
            Base = baseRegister,
            HasIndex = hasIndex,
            Index = indexRegister,
            Scale = scale,
            Displacement = displacement,
            SegmentOverride = segmentOverride,
            Is16Bit = false,
        };
        return true;
    }

    private static bool TryParseMemory16(
        ReadOnlySpan<byte> code,
        ref int position,
        int mod,
        int rm,
        SegmentRegister? segmentOverride,
        out MemoryOperand memory)
    {
        memory = default;

        bool hasBase = true;
        bool hasIndex = false;
        Gpr baseRegister = default;
        Gpr indexRegister = default;

        switch (rm)
        {
            case 0:
                baseRegister = Gpr.Ebx;
                hasIndex = true;
                indexRegister = Gpr.Esi;
                break;
            case 1:
                baseRegister = Gpr.Ebx;
                hasIndex = true;
                indexRegister = Gpr.Edi;
                break;
            case 2:
                baseRegister = Gpr.Ebp;
                hasIndex = true;
                indexRegister = Gpr.Esi;
                break;
            case 3:
                baseRegister = Gpr.Ebp;
                hasIndex = true;
                indexRegister = Gpr.Edi;
                break;
            case 4:
                hasBase = false;
                hasIndex = true;
                indexRegister = Gpr.Esi;
                break;
            case 5:
                hasBase = false;
                hasIndex = true;
                indexRegister = Gpr.Edi;
                break;
            case 6:
                if (mod == 0)
                {
                    hasBase = false;
                }
                else
                {
                    baseRegister = Gpr.Ebp;
                }

                break;
            default:
                baseRegister = Gpr.Ebx;
                break;
        }

        uint displacement = 0;
        if (mod == 1)
        {
            if (position >= code.Length)
            {
                return false;
            }

            displacement = unchecked((uint)(sbyte)code[position++]);
        }
        else if (mod == 2 || (mod == 0 && rm == 6))
        {
            // Only [BP] (rm = 6) carries a disp16 without a ModRM displacement
            // field; the index-only forms [SI]/[DI] (rm = 4/5) have none.
            if (position + 2 > code.Length)
            {
                return false;
            }

            displacement = Endian.Read16(code.Slice(position, 2));
            position += 2;
        }

        memory = new MemoryOperand
        {
            HasBase = hasBase,
            Base = baseRegister,
            HasIndex = hasIndex,
            Index = indexRegister,
            Scale = 1,
            Displacement = displacement,
            SegmentOverride = segmentOverride,
            Is16Bit = true,
        };
        return true;
    }

    private static bool TryReadImmediate(ReadOnlySpan<byte> code, ref int position, int size, out uint value)
    {
        value = 0;
        int bytes = size / 8;

        if (position + bytes > code.Length)
        {
            return false;
        }

        value = bytes switch
        {
            1 => code[position],
            2 => Endian.Read16(code.Slice(position, 2)),
            4 => Endian.Read32(code.Slice(position, 4)),
            _ => 0,
        };

        position += bytes;
        return true;
    }

    private static uint SignExtend8(uint value)
    {
        return unchecked((uint)(sbyte)value);
    }

    private static uint SegmentIndex(byte opcode)
    {
        return (uint)((opcode >> 3) & 3);
    }

    private static Mnemonic AluMnemonic(int group)
    {
        return group switch
        {
            0 => Mnemonic.Add,
            1 => Mnemonic.Or,
            2 => Mnemonic.Adc,
            3 => Mnemonic.Sbb,
            4 => Mnemonic.And,
            5 => Mnemonic.Sub,
            6 => Mnemonic.Xor,
            _ => Mnemonic.Cmp,
        };
    }

    private static DecodedInstruction Make(
        Mnemonic mnemonic,
        Operand operand1,
        Operand operand2,
        int length,
        int operandSize,
        int addressSize,
        byte opcode,
        ConditionCode condition = default)
    {
        return new DecodedInstruction
        {
            Mnemonic = mnemonic,
            Operand1 = operand1,
            Operand2 = operand2,
            Length = length,
            OperandSize = operandSize,
            AddressSize = addressSize,
            Condition = condition,
            Opcode = opcode,
        };
    }

    private static DecodedInstruction Unsupported(int length, byte opcode, int operandSize, int addressSize)
    {
        return new DecodedInstruction
        {
            Mnemonic = Mnemonic.Unsupported,
            Length = length < 1 ? 1 : length,
            OperandSize = operandSize,
            AddressSize = addressSize,
            Opcode = opcode,
        };
    }
}
