using Xunit;

namespace XGrid.Cpu.Tests;

public sealed class DecoderSystemTests
{
    private static DecodedInstruction Decode(params byte[] code)
    {
        return Decoder.Decode(code, 32, 32);
    }

    [Fact]
    public void MovCrRead_Decodes()
    {
        DecodedInstruction instruction = Decode(0x0F, 0x20, 0xC0);

        Assert.Equal(Mnemonic.MovCrRead, instruction.Mnemonic);
        Assert.Equal(0, instruction.Operand1.RegisterIndex);
        Assert.Equal(0u, instruction.Operand2.Immediate);
        Assert.Equal(3, instruction.Length);
    }

    [Fact]
    public void MovCrWrite_Decodes()
    {
        DecodedInstruction instruction = Decode(0x0F, 0x22, 0xDB);

        Assert.Equal(Mnemonic.MovCrWrite, instruction.Mnemonic);
        Assert.Equal(3, instruction.Operand1.RegisterIndex);
        Assert.Equal(3u, instruction.Operand2.Immediate);
    }

    [Fact]
    public void MovDrRead_Decodes()
    {
        DecodedInstruction instruction = Decode(0x0F, 0x21, 0xC0);

        Assert.Equal(Mnemonic.MovDrRead, instruction.Mnemonic);
    }

    [Fact]
    public void MovDrWrite_Decodes()
    {
        DecodedInstruction instruction = Decode(0x0F, 0x23, 0xC0);

        Assert.Equal(Mnemonic.MovDrWrite, instruction.Mnemonic);
    }

    [Fact]
    public void MovCr_MemoryForm_IsUnsupported()
    {
        DecodedInstruction instruction = Decode(0x0F, 0x20, 0x00);

        Assert.Equal(Mnemonic.Unsupported, instruction.Mnemonic);
    }

    [Fact]
    public void Invlpg_DecodesMemoryOperand()
    {
        DecodedInstruction instruction = Decode(0x0F, 0x01, 0x38);

        Assert.Equal(Mnemonic.Invlpg, instruction.Mnemonic);
        Assert.Equal(OperandKind.Memory, instruction.Operand1.Kind);
        Assert.Equal(Gpr.Eax, instruction.Operand1.Memory.Base);
        Assert.Equal(3, instruction.Length);
    }

    [Fact]
    public void Invd_Decodes()
    {
        DecodedInstruction instruction = Decode(0x0F, 0x08);

        Assert.Equal(Mnemonic.Invd, instruction.Mnemonic);
        Assert.Equal(2, instruction.Length);
    }

    [Fact]
    public void Wbinvd_Decodes()
    {
        DecodedInstruction instruction = Decode(0x0F, 0x09);

        Assert.Equal(Mnemonic.Wbinvd, instruction.Mnemonic);
        Assert.Equal(2, instruction.Length);
    }

    [Fact]
    public void Arpl_Decodes()
    {
        DecodedInstruction instruction = Decode(0x63, 0xC8);

        Assert.Equal(Mnemonic.Arpl, instruction.Mnemonic);
        Assert.Equal(OperandKind.Register, instruction.Operand1.Kind);
        Assert.Equal(OperandKind.Register, instruction.Operand2.Kind);
        Assert.Equal(2, instruction.Length);
    }

    [Fact]
    public void Leave_Decodes()
    {
        DecodedInstruction instruction = Decode(0xC9);

        Assert.Equal(Mnemonic.Leave, instruction.Mnemonic);
        Assert.Equal(1, instruction.Length);
    }

    [Fact]
    public void Enter_Decodes()
    {
        DecodedInstruction instruction = Decode(0xC8, 0x10, 0x00, 0x01);

        Assert.Equal(Mnemonic.Enter, instruction.Mnemonic);
        Assert.Equal(4, instruction.Length);
    }

    [Fact]
    public void Bswap_Decodes()
    {
        DecodedInstruction instruction = Decode(0x0F, 0xC8);

        Assert.Equal(Mnemonic.Bswap, instruction.Mnemonic);
        Assert.Equal(0, instruction.Operand1.RegisterIndex);
        Assert.Equal(2, instruction.Length);
    }

    [Fact]
    public void Cbw_Decodes()
    {
        DecodedInstruction instruction = Decode(0x98);

        Assert.Equal(Mnemonic.Cbw, instruction.Mnemonic);
        Assert.Equal(1, instruction.Length);
    }

    [Fact]
    public void Cdq_Decodes()
    {
        DecodedInstruction instruction = Decode(0x99);

        Assert.Equal(Mnemonic.Cdq, instruction.Mnemonic);
        Assert.Equal(1, instruction.Length);
    }

    [Fact]
    public void Cwd_DecodesWithOperandSizeOverride()
    {
        DecodedInstruction instruction = Decode(0x66, 0x99);

        Assert.Equal(Mnemonic.Cdq, instruction.Mnemonic);
        Assert.Equal(16, instruction.OperandSize);
        Assert.Equal(2, instruction.Length);
    }

    [Fact]
    public void Ltr_Decodes()
    {
        DecodedInstruction instruction = Decode(0x0F, 0x00, 0xD8);

        Assert.Equal(Mnemonic.Ltr, instruction.Mnemonic);
        Assert.Equal(0, instruction.Operand1.RegisterIndex);
        Assert.Equal(3, instruction.Length);
    }
}
