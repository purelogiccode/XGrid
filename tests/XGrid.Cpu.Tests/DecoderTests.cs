using Xunit;

namespace XGrid.Cpu.Tests;

public sealed class DecoderTests
{
    private static DecodedInstruction Decode(params byte[] code)
    {
        return Decoder.Decode(code);
    }

    [Fact]
    public void Add_RegisterRegister()
    {
        DecodedInstruction instruction = Decode(0x01, 0xD8);

        Assert.Equal(Mnemonic.Add, instruction.Mnemonic);
        Assert.Equal(2, instruction.Length);
        Assert.Equal(OperandKind.Register, instruction.Operand1.Kind);
        Assert.Equal(0, instruction.Operand1.RegisterIndex);
        Assert.Equal(32, instruction.Operand1.Size);
        Assert.Equal(3, instruction.Operand2.RegisterIndex);
    }

    [Fact]
    public void Mov_Immediate32()
    {
        DecodedInstruction instruction = Decode(0xB8, 0x78, 0x56, 0x34, 0x12);

        Assert.Equal(Mnemonic.Mov, instruction.Mnemonic);
        Assert.Equal(5, instruction.Length);
        Assert.Equal(OperandKind.Immediate, instruction.Operand2.Kind);
        Assert.Equal(0x12345678u, instruction.Operand2.Immediate);
    }

    [Fact]
    public void OperandSizeOverride_Uses16Bit()
    {
        DecodedInstruction instruction = Decode(0x66, 0xB8, 0x34, 0x12);

        Assert.Equal(Mnemonic.Mov, instruction.Mnemonic);
        Assert.Equal(16, instruction.OperandSize);
        Assert.Equal(16, instruction.Operand1.Size);
        Assert.Equal(0x1234u, instruction.Operand2.Immediate);
        Assert.Equal(4, instruction.Length);
    }

    [Fact]
    public void Sib_Addressing()
    {
        DecodedInstruction instruction = Decode(0x8B, 0x44, 0x8B, 0x08);

        Assert.Equal(Mnemonic.Mov, instruction.Mnemonic);
        Assert.Equal(OperandKind.Memory, instruction.Operand2.Kind);
        MemoryOperand memory = instruction.Operand2.Memory;
        Assert.True(memory.HasBase);
        Assert.Equal(Gpr.Ebx, memory.Base);
        Assert.True(memory.HasIndex);
        Assert.Equal(Gpr.Ecx, memory.Index);
        Assert.Equal(4, memory.Scale);
        Assert.Equal(8u, memory.Displacement);
        Assert.False(memory.Is16Bit);
        Assert.Equal(4, instruction.Length);
    }

    [Fact]
    public void AddressSizeOverride_Uses16BitAddressing()
    {
        DecodedInstruction instruction = Decode(0x67, 0x8B, 0x00);

        Assert.Equal(16, instruction.AddressSize);
        MemoryOperand memory = instruction.Operand2.Memory;
        Assert.True(memory.Is16Bit);
        Assert.True(memory.HasBase);
        Assert.Equal(Gpr.Ebx, memory.Base);
        Assert.True(memory.HasIndex);
        Assert.Equal(Gpr.Esi, memory.Index);
        Assert.Equal(3, instruction.Length);
    }

    // The index-only 16-bit forms [SI]/[DI] carry no displacement; only [BP]
    // with mod=00 does. Reading two displacement bytes for them shifted the
    // instruction stream (found by the CPU oracle, decisions 220).
    [Fact]
    public void AddressSizeOverride_SiForm_HasNoDisplacement()
    {
        DecodedInstruction instruction = Decode(0x67, 0x8D, 0x04);

        MemoryOperand memory = instruction.Operand2.Memory;
        Assert.True(memory.Is16Bit);
        Assert.False(memory.HasBase);
        Assert.True(memory.HasIndex);
        Assert.Equal(Gpr.Esi, memory.Index);
        Assert.Equal(0u, memory.Displacement);
        Assert.Equal(3, instruction.Length);
    }

    [Fact]
    public void AddressSizeOverride_DiForm_HasNoDisplacement()
    {
        DecodedInstruction instruction = Decode(0x67, 0x8D, 0x05);

        MemoryOperand memory = instruction.Operand2.Memory;
        Assert.False(memory.HasBase);
        Assert.Equal(Gpr.Edi, memory.Index);
        Assert.Equal(0u, memory.Displacement);
        Assert.Equal(3, instruction.Length);
    }

    [Fact]
    public void AddressSizeOverride_BpForm_HasDisplacement16()
    {
        // 67 8D 46 34: lea eax, [bp+0x34] (mod=01 rm=110 uses BP as the base).
        DecodedInstruction instruction = Decode(0x67, 0x8D, 0x46, 0x34);

        MemoryOperand memory = instruction.Operand2.Memory;
        Assert.True(memory.HasBase);
        Assert.Equal(Gpr.Ebp, memory.Base);
        Assert.Equal(0x34u, memory.Displacement);
        Assert.Equal(4, instruction.Length);
    }

    [Fact]
    public void AddressSizeOverride_BpFormWithoutDisplacementUsesDisp16()
    {
        // 67 8D 06 34 12: lea eax, [0x1234] - mod=00 rm=110 is the disp16 form.
        DecodedInstruction instruction = Decode(0x67, 0x8D, 0x06, 0x34, 0x12);

        MemoryOperand memory = instruction.Operand2.Memory;
        Assert.False(memory.HasBase);
        Assert.False(memory.HasIndex);
        Assert.Equal(0x1234u, memory.Displacement);
        Assert.Equal(5, instruction.Length);
    }

    [Fact]
    public void Xlat_Decodes()
    {
        DecodedInstruction instruction = Decode(0xD7);

        Assert.Equal(Mnemonic.Xlat, instruction.Mnemonic);
        Assert.Equal(OperandKind.Memory, instruction.Operand1.Kind);
        Assert.Equal(Gpr.Ebx, instruction.Operand1.Memory.Base);
        Assert.Equal(1, instruction.Length);
    }

    [Fact]
    public void Jcc_Short()
    {
        DecodedInstruction instruction = Decode(0x75, 0x05);

        Assert.Equal(Mnemonic.Jcc, instruction.Mnemonic);
        Assert.Equal(ConditionCode.NotEqual, instruction.Condition);
        Assert.Equal(5u, instruction.Operand1.Immediate);
        Assert.Equal(2, instruction.Length);
    }

    [Fact]
    public void Jcc_Near_UsesTwoByteOpcode()
    {
        DecodedInstruction instruction = Decode(0x0F, 0x85, 0x00, 0x01, 0x00, 0x00);

        Assert.Equal(Mnemonic.Jcc, instruction.Mnemonic);
        Assert.Equal(ConditionCode.NotEqual, instruction.Condition);
        Assert.Equal(0x100u, instruction.Operand1.Immediate);
        Assert.Equal(6, instruction.Length);
    }

    [Fact]
    public void Lea_DecodesMemorySource()
    {
        DecodedInstruction instruction = Decode(0x8D, 0x04, 0x8B);

        Assert.Equal(Mnemonic.Lea, instruction.Mnemonic);
        Assert.Equal(OperandKind.Register, instruction.Operand1.Kind);
        Assert.Equal(OperandKind.Memory, instruction.Operand2.Kind);
        Assert.Equal(4, instruction.Operand2.Memory.Scale);
        Assert.Equal(3, instruction.Length);
    }

    [Fact]
    public void Group1_Imm8_SignExtends()
    {
        DecodedInstruction instruction = Decode(0x83, 0xC0, 0xFF);

        Assert.Equal(Mnemonic.Add, instruction.Mnemonic);
        Assert.Equal(0xFFFFFFFFu, instruction.Operand2.Immediate);
        Assert.Equal(32, instruction.Operand2.Size);
        Assert.Equal(3, instruction.Length);
    }

    [Fact]
    public void Mov_ByteRegister_Encoding()
    {
        DecodedInstruction instruction = Decode(0xB4, 0x12);

        Assert.Equal(Mnemonic.Mov, instruction.Mnemonic);
        Assert.Equal(4, instruction.Operand1.RegisterIndex);
        Assert.Equal(8, instruction.Operand1.Size);
        Assert.Equal(0x12u, instruction.Operand2.Immediate);
    }

    [Fact]
    public void Push_Imm8_SignExtends()
    {
        DecodedInstruction instruction = Decode(0x6A, 0xFF);

        Assert.Equal(Mnemonic.Push, instruction.Mnemonic);
        Assert.Equal(0xFFFFFFFFu, instruction.Operand1.Immediate);
        Assert.Equal(32, instruction.Operand1.Size);
    }

    [Fact]
    public void SegmentOverride_IsRecorded()
    {
        DecodedInstruction instruction = Decode(0x64, 0x8B, 0x00);

        Assert.Equal(Mnemonic.Mov, instruction.Mnemonic);
        Assert.Equal(SegmentRegister.Fs, instruction.Operand2.Memory.SegmentOverride);
    }

    [Fact]
    public void Group3_NotAndNeg()
    {
        Assert.Equal(Mnemonic.Not, Decode(0xF7, 0xD0).Mnemonic);
        Assert.Equal(Mnemonic.Neg, Decode(0xF7, 0xD8).Mnemonic);
    }

    [Fact]
    public void Group5_IndirectJmp()
    {
        DecodedInstruction instruction = Decode(0xFF, 0xE0);

        Assert.Equal(Mnemonic.Jmp, instruction.Mnemonic);
        Assert.Equal(OperandKind.Register, instruction.Operand1.Kind);
        Assert.Equal(0, instruction.Operand1.RegisterIndex);
    }

    [Fact]
    public void Group5_PushMemory()
    {
        DecodedInstruction instruction = Decode(0xFF, 0x75, 0xE8);

        Assert.Equal(Mnemonic.Push, instruction.Mnemonic);
        Assert.Equal(OperandKind.Memory, instruction.Operand1.Kind);
        Assert.Equal(Gpr.Ebp, instruction.Operand1.Memory.Base);
        Assert.Equal(3, instruction.Length);
    }

    [Fact]
    public void Group1A_PopMemory()
    {
        DecodedInstruction instruction = Decode(0x8F, 0x05, 0x00, 0x20, 0x00, 0x00);

        Assert.Equal(Mnemonic.Pop, instruction.Mnemonic);
        Assert.Equal(OperandKind.Memory, instruction.Operand1.Kind);
        Assert.Equal(6, instruction.Length);
    }

    [Fact]
    public void Group1A_PopMemory_WithFsOverride()
    {
        DecodedInstruction instruction = Decode(0x64, 0x8F, 0x05, 0x00, 0x00, 0x00, 0x00);

        Assert.Equal(Mnemonic.Pop, instruction.Mnemonic);
        Assert.Equal(OperandKind.Memory, instruction.Operand1.Kind);
        Assert.Equal(7, instruction.Length);
    }

    [Fact]
    public void Mov_Moffs32()
    {
        DecodedInstruction instruction = Decode(0xA1, 0x00, 0x20, 0x00, 0x00);

        Assert.Equal(Mnemonic.Mov, instruction.Mnemonic);
        Assert.Equal(OperandKind.Memory, instruction.Operand2.Kind);
        Assert.Equal(0x2000u, instruction.Operand2.Memory.Displacement);
        Assert.Equal(5, instruction.Length);
    }

    [Fact]
    public void Xchg_EaxRegister()
    {
        DecodedInstruction instruction = Decode(0x93);

        Assert.Equal(Mnemonic.Xchg, instruction.Mnemonic);
        Assert.Equal(0, instruction.Operand1.RegisterIndex);
        Assert.Equal(3, instruction.Operand2.RegisterIndex);
        Assert.Equal(1, instruction.Length);
    }

    [Fact]
    public void Nop_IsOneByte()
    {
        DecodedInstruction instruction = Decode(0x90);

        Assert.Equal(Mnemonic.Nop, instruction.Mnemonic);
        Assert.Equal(1, instruction.Length);
    }

    [Fact]
    public void UnsupportedOpcode_IsReported()
    {
        DecodedInstruction instruction = Decode(0x0F, 0x0B);

        Assert.Equal(Mnemonic.Unsupported, instruction.Mnemonic);
        Assert.False(instruction.IsValid);
    }

    [Fact]
    public void BitTest_ImmediateForms()
    {
        DecodedInstruction bt = Decode(0x0F, 0xBA, 0xE0, 0x05);
        DecodedInstruction bts = Decode(0x0F, 0xBA, 0xE8, 0x05);
        DecodedInstruction btr = Decode(0x0F, 0xBA, 0xF0, 0x05);
        DecodedInstruction btc = Decode(0x0F, 0xBA, 0xF8, 0x05);
        DecodedInstruction memory = Decode(0x0F, 0xBA, 0x20, 0x05);
        DecodedInstruction reserved = Decode(0x0F, 0xBA, 0xC0, 0x05);

        Assert.Equal(Mnemonic.Bt, bt.Mnemonic);
        Assert.Equal(Mnemonic.Bts, bts.Mnemonic);
        Assert.Equal(Mnemonic.Btr, btr.Mnemonic);
        Assert.Equal(Mnemonic.Btc, btc.Mnemonic);
        Assert.Equal(4, bt.Length);
        Assert.Equal(OperandKind.Immediate, bt.Operand2.Kind);
        Assert.Equal(5u, bt.Operand2.Immediate);
        Assert.Equal(OperandKind.Memory, memory.Operand1.Kind);
        Assert.Equal(Mnemonic.Unsupported, reserved.Mnemonic);
    }
}

