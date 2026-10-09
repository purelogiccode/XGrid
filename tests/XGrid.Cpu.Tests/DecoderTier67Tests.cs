using Xunit;

namespace XGrid.Cpu.Tests;

public sealed class DecoderTier67Tests
{
    private static DecodedInstruction Decode(params byte[] code)
    {
        return Decoder.Decode(code, 32, 32);
    }

    [Fact]
    public void Movzx_ByteSource()
    {
        DecodedInstruction instruction = Decode(0x0F, 0xB6, 0xC3);

        Assert.Equal(Mnemonic.Movzx, instruction.Mnemonic);
        Assert.Equal(32, instruction.Operand1.Size);
        Assert.Equal(8, instruction.Operand2.Size);
    }

    [Fact]
    public void Movsx_WordSource()
    {
        DecodedInstruction instruction = Decode(0x0F, 0xBF, 0xC3);

        Assert.Equal(Mnemonic.Movsx, instruction.Mnemonic);
        Assert.Equal(16, instruction.Operand2.Size);
    }

    [Fact]
    public void PushaPopa_Decode()
    {
        Assert.Equal(Mnemonic.Pusha, Decode(0x60).Mnemonic);
        Assert.Equal(Mnemonic.Popa, Decode(0x61).Mnemonic);
    }

    [Fact]
    public void SystemOpcodes_Decode()
    {
        Assert.Equal(Mnemonic.Wrmsr, Decode(0x0F, 0x30).Mnemonic);
        Assert.Equal(Mnemonic.Rdtsc, Decode(0x0F, 0x31).Mnemonic);
        Assert.Equal(Mnemonic.Rdmsr, Decode(0x0F, 0x32).Mnemonic);
        Assert.Equal(Mnemonic.Cpuid, Decode(0x0F, 0xA2).Mnemonic);
    }

    [Fact]
    public void Setcc_DecodesCondition()
    {
        DecodedInstruction instruction = Decode(0x0F, 0x94, 0xC0);

        Assert.Equal(Mnemonic.Setcc, instruction.Mnemonic);
        Assert.Equal(ConditionCode.Equal, instruction.Condition);
        Assert.Equal(8, instruction.Operand1.Size);
    }

    [Fact]
    public void Cmovcc_DecodesCondition()
    {
        DecodedInstruction instruction = Decode(0x0F, 0x44, 0xC3);

        Assert.Equal(Mnemonic.Cmovcc, instruction.Mnemonic);
        Assert.Equal(ConditionCode.Equal, instruction.Condition);
        Assert.Equal(32, instruction.Operand1.Size);
    }

    [Fact]
    public void RepMovsb_DecodesRep()
    {
        DecodedInstruction instruction = Decode(0xF3, 0xA4);

        Assert.Equal(Mnemonic.Movs, instruction.Mnemonic);
        Assert.Equal(RepPrefix.Rep, instruction.Rep);
        Assert.Equal(8, instruction.OperandSize);
    }

    [Fact]
    public void Movsd_DecodesOperandSize()
    {
        Assert.Equal(32, Decode(0xA5).OperandSize);
        Assert.Equal(16, Decode(0x66, 0xA5).OperandSize);
    }

    [Fact]
    public void RepneScasb_Decodes()
    {
        DecodedInstruction instruction = Decode(0xF2, 0xAE);

        Assert.Equal(Mnemonic.Scas, instruction.Mnemonic);
        Assert.Equal(RepPrefix.Repne, instruction.Rep);
    }

    [Fact]
    public void Cmpsb_Decodes()
    {
        Assert.Equal(Mnemonic.Cmps, Decode(0xA6).Mnemonic);
        Assert.Equal(Mnemonic.Stos, Decode(0xAB).Mnemonic);
        Assert.Equal(Mnemonic.Lods, Decode(0xAD).Mnemonic);
    }

    [Fact]
    public void InsOuts_Decode()
    {
        Assert.Equal(Mnemonic.Outs, Decode(0x6E).Mnemonic);
        Assert.Equal(8, Decode(0x6E).OperandSize);
        Assert.Equal(32, Decode(0x6F).OperandSize);
        Assert.Equal(16, Decode(0x66, 0x6F).OperandSize);
        Assert.Equal(Mnemonic.Ins, Decode(0x6C).Mnemonic);
        Assert.Equal(8, Decode(0x6C).OperandSize);
        Assert.Equal(32, Decode(0x6D).OperandSize);
        Assert.Equal(RepPrefix.Rep, Decode(0xF3, 0x6E).Rep);
    }

    [Fact]
    public void XaddCmpxchg_Decode()
    {
        DecodedInstruction xadd = Decode(0x0F, 0xC1, 0x01);
        Assert.Equal(Mnemonic.Xadd, xadd.Mnemonic);
        Assert.Equal(32, xadd.Operand1.Size);

        DecodedInstruction xaddByte = Decode(0x0F, 0xC0, 0x01);
        Assert.Equal(Mnemonic.Xadd, xaddByte.Mnemonic);
        Assert.Equal(8, xaddByte.Operand1.Size);

        DecodedInstruction cmpxchg = Decode(0x0F, 0xB1, 0x01);
        Assert.Equal(Mnemonic.Cmpxchg, cmpxchg.Mnemonic);
        Assert.Equal(32, cmpxchg.Operand1.Size);
    }

    [Fact]
    public void BsfBsr_Decode()
    {
        Assert.Equal(Mnemonic.Bsr, Decode(0x0F, 0xBD, 0xCA).Mnemonic);
        Assert.Equal(Mnemonic.Bsf, Decode(0x0F, 0xBC, 0xCA).Mnemonic);
        Assert.Equal(32, Decode(0x0F, 0xBD, 0xCA).Operand1.Size);
    }

    [Fact]
    public void BitTestGroup_Decode()
    {
        Assert.Equal(Mnemonic.Bt, Decode(0x0F, 0xA3, 0xCA).Mnemonic);
        Assert.Equal(Mnemonic.Bts, Decode(0x0F, 0xAB, 0xCA).Mnemonic);
        Assert.Equal(Mnemonic.Btr, Decode(0x0F, 0xB3, 0xCA).Mnemonic);
        Assert.Equal(Mnemonic.Btc, Decode(0x0F, 0xBB, 0xCA).Mnemonic);
    }

    [Fact]
    public void Cmpxchg8b_Decode()
    {
        DecodedInstruction instruction = Decode(0x0F, 0xC7, 0x0D, 0x00, 0x20, 0x00, 0x00);

        Assert.Equal(Mnemonic.Cmpxchg8b, instruction.Mnemonic);
        Assert.Equal(OperandKind.Memory, instruction.Operand1.Kind);
        Assert.Equal(64, instruction.Operand1.Size);
        Assert.Equal(7, instruction.Length);
    }

    [Fact]
    public void Cmpxchg8b_RegisterForm_IsInvalid()
    {
        DecodedInstruction instruction = Decode(0x0F, 0xC7, 0xC8);

        Assert.False(instruction.IsValid);
    }
}
