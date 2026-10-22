using Xunit;

namespace XGrid.Cpu.Tests;

public sealed class DecoderTier45Tests
{
    private static DecodedInstruction Decode(params byte[] code)
    {
        return Decoder.Decode(code);
    }

    [Fact]
    public void Group2_DecodesShl()
    {
        DecodedInstruction instruction = Decode(0xD1, 0xE0);

        Assert.Equal(Mnemonic.Shl, instruction.Mnemonic);
        Assert.Equal(0, instruction.Operand1.RegisterIndex);
        Assert.Equal(32, instruction.Operand1.Size);
        Assert.Equal(OperandKind.Immediate, instruction.Operand2.Kind);
        Assert.Equal(1u, instruction.Operand2.Immediate);
    }

    [Fact]
    public void Group2_ClCount_DecodesRegister()
    {
        DecodedInstruction instruction = Decode(0xD3, 0xE0);

        Assert.Equal(Mnemonic.Shl, instruction.Mnemonic);
        Assert.Equal(OperandKind.Register, instruction.Operand2.Kind);
        Assert.Equal(1, instruction.Operand2.RegisterIndex);
        Assert.Equal(8, instruction.Operand2.Size);
    }

    [Fact]
    public void Group2_ByteForm_Uses8BitOperand()
    {
        DecodedInstruction instruction = Decode(0xD0, 0xE0);

        Assert.Equal(Mnemonic.Shl, instruction.Mnemonic);
        Assert.Equal(8, instruction.Operand1.Size);
    }

    [Fact]
    public void Group3_DecodesDiv()
    {
        DecodedInstruction instruction = Decode(0xF7, 0xF3);

        Assert.Equal(Mnemonic.Div, instruction.Mnemonic);
        Assert.Equal(3, instruction.Operand1.RegisterIndex);
        Assert.Equal(32, instruction.Operand1.Size);
    }

    [Fact]
    public void Group3_DecodesImul()
    {
        DecodedInstruction instruction = Decode(0xF7, 0xEB);

        Assert.Equal(Mnemonic.Imul, instruction.Mnemonic);
        Assert.Equal(3, instruction.Operand1.RegisterIndex);
    }

    [Fact]
    public void Imul_ThreeOperand_DecodesImmediate()
    {
        DecodedInstruction instruction = Decode(0x6B, 0xC3, 0x05);

        Assert.Equal(Mnemonic.Imul, instruction.Mnemonic);
        Assert.Equal(0, instruction.Operand1.RegisterIndex);
        Assert.Equal(3, instruction.Operand2.RegisterIndex);
        Assert.Equal(5u, instruction.Operand3.Immediate);
    }

    [Fact]
    public void Imul_TwoOperand_TwoByteOpcode()
    {
        DecodedInstruction instruction = Decode(0x0F, 0xAF, 0xC3);

        Assert.Equal(Mnemonic.Imul, instruction.Mnemonic);
        Assert.Equal(0, instruction.Operand1.RegisterIndex);
        Assert.Equal(3, instruction.Operand2.RegisterIndex);
    }

    [Fact]
    public void Interrupt_DecodesVector()
    {
        DecodedInstruction instruction = Decode(0xCD, 0x21);

        Assert.Equal(Mnemonic.Interrupt, instruction.Mnemonic);
        Assert.Equal(0x21u, instruction.Operand1.Immediate);
        Assert.Equal(2, instruction.Length);
    }

    [Fact]
    public void Int3_Iret_Into_Decode()
    {
        Assert.Equal(Mnemonic.Int3, Decode(0xCC).Mnemonic);
        Assert.Equal(Mnemonic.Into, Decode(0xCE).Mnemonic);
        Assert.Equal(Mnemonic.Iret, Decode(0xCF).Mnemonic);
    }

    [Fact]
    public void Lidt_DecodesMemoryOperand()
    {
        DecodedInstruction instruction = Decode(0x0F, 0x01, 0x1D, 0x00, 0x40, 0x00, 0x00);

        Assert.Equal(Mnemonic.Lidt, instruction.Mnemonic);
        Assert.Equal(OperandKind.Memory, instruction.Operand1.Kind);
        Assert.Equal(0x4000u, instruction.Operand1.Memory.Displacement);
        Assert.Equal(7, instruction.Length);
    }

    [Fact]
    public void Sgdt_Decodes()
    {
        DecodedInstruction instruction = Decode(0x0F, 0x01, 0x05, 0x00, 0x50, 0x00, 0x00);

        Assert.Equal(Mnemonic.Sgdt, instruction.Mnemonic);
    }

    [Fact]
    public void Shld_Immediate_DecodesOperands()
    {
        DecodedInstruction instruction = Decode(0x0F, 0xA4, 0xD8, 0x04);

        Assert.Equal(Mnemonic.Shld, instruction.Mnemonic);
        Assert.Equal(0, instruction.Operand1.RegisterIndex);
        Assert.Equal(32, instruction.Operand1.Size);
        Assert.Equal(3, instruction.Operand2.RegisterIndex);
        Assert.Equal(OperandKind.Immediate, instruction.Operand3.Kind);
        Assert.Equal(4u, instruction.Operand3.Immediate);
        Assert.Equal(4, instruction.Length);
    }

    [Fact]
    public void Shld_ClCount_DecodesRegisterCount()
    {
        DecodedInstruction instruction = Decode(0x0F, 0xA5, 0xD8);

        Assert.Equal(Mnemonic.Shld, instruction.Mnemonic);
        Assert.Equal(OperandKind.Register, instruction.Operand3.Kind);
        Assert.Equal(1, instruction.Operand3.RegisterIndex);
        Assert.Equal(3, instruction.Length);
    }

    [Fact]
    public void Shrd_Immediate_DecodesOperands()
    {
        DecodedInstruction instruction = Decode(0x0F, 0xAC, 0xD8, 0x0D);

        Assert.Equal(Mnemonic.Shrd, instruction.Mnemonic);
        Assert.Equal(0, instruction.Operand1.RegisterIndex);
        Assert.Equal(3, instruction.Operand2.RegisterIndex);
        Assert.Equal(13u, instruction.Operand3.Immediate);
        Assert.Equal(4, instruction.Length);
    }

    [Fact]
    public void Shrd_WordForm_UsesSixteenBitOperand()
    {
        DecodedInstruction instruction = Decode(0x66, 0x0F, 0xAD, 0xD8);

        Assert.Equal(Mnemonic.Shrd, instruction.Mnemonic);
        Assert.Equal(16, instruction.Operand1.Size);
        Assert.Equal(16, instruction.Operand2.Size);
        Assert.Equal(4, instruction.Length);
    }
}
