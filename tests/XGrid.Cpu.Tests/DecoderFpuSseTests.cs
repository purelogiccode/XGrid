using Xunit;

namespace XGrid.Cpu.Tests;

public sealed class DecoderFpuSseTests
{
    private static DecodedInstruction Decode(params byte[] code)
    {
        return Decoder.Decode(code, 32, 32);
    }

    [Fact]
    public void FpuRegisterArithmetic_DecodesOperands()
    {
        DecodedInstruction instruction = Decode(0xD8, 0xC1);

        Assert.Equal(Mnemonic.Fadd, instruction.Mnemonic);
        Assert.Equal(0, instruction.Operand1.RegisterIndex);
        Assert.Equal(80, instruction.Operand1.Size);
        Assert.Equal(1, instruction.Operand2.RegisterIndex);
    }

    [Fact]
    public void FpuMemoryArithmetic_DecodesSizes()
    {
        Assert.Equal(32, Decode(0xD8, 0x05, 0x00, 0x00, 0x00, 0x00).Operand1.Size);
        Assert.Equal(64, Decode(0xDC, 0x05, 0x00, 0x00, 0x00, 0x00).Operand1.Size);
        Assert.Equal(32, Decode(0xDA, 0x05, 0x00, 0x00, 0x00, 0x00).Operand1.Size);
        Assert.Equal(16, Decode(0xDE, 0x05, 0x00, 0x00, 0x00, 0x00).Operand1.Size);
    }

    [Fact]
    public void FpuConstants_Decode()
    {
        Assert.Equal(Mnemonic.Fld1, Decode(0xD9, 0xE8).Mnemonic);
        Assert.Equal(Mnemonic.Fldz, Decode(0xD9, 0xEE).Mnemonic);
        Assert.Equal(Mnemonic.Fldpi, Decode(0xD9, 0xEB).Mnemonic);
        Assert.Equal(Mnemonic.Fldl2e, Decode(0xD9, 0xEA).Mnemonic);
    }

    [Fact]
    public void FpuLoadStoreForms_Decode()
    {
        Assert.Equal(32, Decode(0xD9, 0x05, 0, 0, 0, 0).Operand1.Size);
        Assert.Equal(32, Decode(0xD9, 0x15, 0, 0, 0, 0).Operand1.Size);
        Assert.Equal(64, Decode(0xDD, 0x1D, 0, 0, 0, 0).Operand1.Size);
        Assert.Equal(80, Decode(0xDB, 0x3D, 0, 0, 0, 0).Operand1.Size);
        Assert.Equal(64, Decode(0xDD, 0x05, 0, 0, 0, 0).Operand1.Size);
        Assert.Equal(80, Decode(0xDB, 0x2D, 0, 0, 0, 0).Operand1.Size);
    }

    [Fact]
    public void FpuRegisterSpecials_Decode()
    {
        Assert.Equal(Mnemonic.Fxch, Decode(0xD9, 0xC9).Mnemonic);
        Assert.Equal(1, Decode(0xD9, 0xC9).Operand1.RegisterIndex);
        Assert.Equal(Mnemonic.Fcompp, Decode(0xDE, 0xD9).Mnemonic);
        Assert.Equal(Mnemonic.Fnstsw, Decode(0xDF, 0xE0).Mnemonic);
        Assert.Equal(OperandKind.None, Decode(0xDF, 0xE0).Operand1.Kind);
        Assert.Equal(Mnemonic.Fninit, Decode(0xDB, 0xE3).Mnemonic);
        Assert.Equal(Mnemonic.Fnclex, Decode(0xDB, 0xE2).Mnemonic);
    }

    [Fact]
    public void FpuCompareAndMove_Decode()
    {
        Assert.Equal(Mnemonic.Fcom, Decode(0xD8, 0xD1).Mnemonic);
        Assert.Equal(Mnemonic.Fcomp, Decode(0xD8, 0xD9).Mnemonic);
        Assert.Equal(Mnemonic.Fcomi, Decode(0xDB, 0xF1).Mnemonic);
        Assert.Equal(Mnemonic.Fcmovb, Decode(0xDA, 0xC1).Mnemonic);
        Assert.Equal(Mnemonic.Fcmovne, Decode(0xDB, 0xC9).Mnemonic);
        Assert.Equal(Mnemonic.Fucomip, Decode(0xDF, 0xE9).Mnemonic);
    }

    [Fact]
    public void FpuControlWords_Decode()
    {
        Assert.Equal(Mnemonic.Fldcw, Decode(0xD9, 0x2D, 0, 0, 0, 0).Mnemonic);
        Assert.Equal(Mnemonic.Fnstcw, Decode(0xD9, 0x3D, 0, 0, 0, 0).Mnemonic);
        Assert.Equal(Mnemonic.Fnstsw, Decode(0xDD, 0x3D, 0, 0, 0, 0).Mnemonic);
        Assert.Equal(Mnemonic.Fnsave, Decode(0xDD, 0x35, 0, 0, 0, 0).Mnemonic);
        Assert.Equal(Mnemonic.Frstor, Decode(0xDD, 0x25, 0, 0, 0, 0).Mnemonic);
    }

    [Fact]
    public void Fwait_Decodes()
    {
        Assert.Equal(Mnemonic.Fwait, Decode(0x9B).Mnemonic);
    }

    [Fact]
    public void SseMovePrefixes_SelectMnemonic()
    {
        Assert.Equal(Mnemonic.Movups, Decode(0x0F, 0x10, 0xC1).Mnemonic);
        Assert.Equal(Mnemonic.Movss, Decode(0xF3, 0x0F, 0x10, 0xC1).Mnemonic);
        Assert.Equal(Mnemonic.Unsupported, Decode(0x66, 0x0F, 0x10, 0xC1).Mnemonic);
        Assert.Equal(Mnemonic.Unsupported, Decode(0xF2, 0x0F, 0x10, 0xC1).Mnemonic);
    }

    [Fact]
    public void SseArithmeticPrefixes_SelectMnemonic()
    {
        Assert.Equal(Mnemonic.Addps, Decode(0x0F, 0x58, 0xC1).Mnemonic);
        Assert.Equal(Mnemonic.Addss, Decode(0xF3, 0x0F, 0x58, 0xC1).Mnemonic);
        Assert.Equal(Mnemonic.Unsupported, Decode(0x66, 0x0F, 0x58, 0xC1).Mnemonic);
    }

    [Fact]
    public void SseCompare_DecodesPredicate()
    {
        DecodedInstruction packed = Decode(0x0F, 0xC2, 0xC1, 0x03);
        Assert.Equal(Mnemonic.Cmpps, packed.Mnemonic);
        Assert.Equal(3u, packed.Operand3.Immediate);

        DecodedInstruction scalar = Decode(0xF3, 0x0F, 0xC2, 0xC1, 0x01);
        Assert.Equal(Mnemonic.Cmpss, scalar.Mnemonic);
    }

    [Fact]
    public void SseStateGroup_Decodes()
    {
        Assert.Equal(Mnemonic.Fxsave, Decode(0x0F, 0xAE, 0x05, 0, 0, 0, 0).Mnemonic);
        Assert.Equal(Mnemonic.Fxrstor, Decode(0x0F, 0xAE, 0x0D, 0, 0, 0, 0).Mnemonic);
        Assert.Equal(Mnemonic.Ldmxcsr, Decode(0x0F, 0xAE, 0x15, 0, 0, 0, 0).Mnemonic);
        Assert.Equal(Mnemonic.Stmxcsr, Decode(0x0F, 0xAE, 0x1D, 0, 0, 0, 0).Mnemonic);
        Assert.Equal(Mnemonic.Lfence, Decode(0x0F, 0xAE, 0xE8).Mnemonic);
        Assert.Equal(Mnemonic.Mfence, Decode(0x0F, 0xAE, 0xF0).Mnemonic);
        Assert.Equal(Mnemonic.Sfence, Decode(0x0F, 0xAE, 0xF8).Mnemonic);
    }

    [Fact]
    public void SseConversions_Decode()
    {
        Assert.Equal(Mnemonic.Cvtsi2ss, Decode(0xF3, 0x0F, 0x2A, 0xC0).Mnemonic);
        Assert.Equal(Mnemonic.Cvtpi2ps, Decode(0x0F, 0x2A, 0xC1).Mnemonic);
        Assert.Equal(Mnemonic.Cvttss2si, Decode(0xF3, 0x0F, 0x2C, 0xC0).Mnemonic);
        Assert.Equal(Mnemonic.Cvtss2si, Decode(0xF3, 0x0F, 0x2D, 0xC0).Mnemonic);
        Assert.Equal(Mnemonic.Cvtps2pi, Decode(0x0F, 0x2D, 0xC1).Mnemonic);
        Assert.Equal(Mnemonic.Cvttps2pi, Decode(0x0F, 0x2C, 0xC1).Mnemonic);
    }

    [Fact]
    public void SseMisc_Decode()
    {
        Assert.Equal(Mnemonic.Movmskps, Decode(0x0F, 0x50, 0xC1).Mnemonic);
        Assert.Equal(Mnemonic.Shufps, Decode(0x0F, 0xC6, 0xC1, 0x1B).Mnemonic);
        Assert.Equal(Mnemonic.Unpcklps, Decode(0x0F, 0x14, 0xC1).Mnemonic);
        Assert.Equal(Mnemonic.Unpckhps, Decode(0x0F, 0x15, 0xC1).Mnemonic);
        Assert.Equal(Mnemonic.Comiss, Decode(0x0F, 0x2F, 0xC1).Mnemonic);
        Assert.Equal(Mnemonic.Ucomiss, Decode(0x0F, 0x2E, 0xC1).Mnemonic);
        Assert.Equal(Mnemonic.Movntps, Decode(0x0F, 0x2B, 0x05, 0, 0, 0, 0).Mnemonic);
    }

    [Fact]
    public void MmxBasics_Decode()
    {
        Assert.Equal(Mnemonic.Movd, Decode(0x0F, 0x6E, 0xC0).Mnemonic);
        Assert.Equal(Mnemonic.Movq, Decode(0x0F, 0x6F, 0xC1).Mnemonic);
        Assert.Equal(Mnemonic.Emms, Decode(0x0F, 0x77).Mnemonic);
        Assert.Equal(Mnemonic.Paddb, Decode(0x0F, 0xFC, 0xC1).Mnemonic);
        Assert.Equal(64, Decode(0x0F, 0xFC, 0xC1).Operand1.Size);
    }

    [Fact]
    public void MmxShiftImmediate_Decodes()
    {
        DecodedInstruction word = Decode(0x0F, 0x71, 0xF0, 0x04);
        Assert.Equal(Mnemonic.Psllw, word.Mnemonic);
        Assert.Equal(4u, word.Operand2.Immediate);

        Assert.Equal(Mnemonic.Psrad, Decode(0x0F, 0x72, 0xE0, 0x02).Mnemonic);
        Assert.Equal(Mnemonic.Psrlq, Decode(0x0F, 0x73, 0xD0, 0x08).Mnemonic);
    }

    [Fact]
    public void MmxShuffleAndLaneOps_Decode()
    {
        DecodedInstruction shuffle = Decode(0x0F, 0x70, 0xC1, 0x1B);
        Assert.Equal(Mnemonic.Pshufw, shuffle.Mnemonic);
        Assert.Equal(0x1Bu, shuffle.Operand3.Immediate);

        DecodedInstruction insert = Decode(0x0F, 0xC4, 0xC0, 0x02);
        Assert.Equal(Mnemonic.Pinsrw, insert.Mnemonic);
        Assert.Equal(64, insert.Operand1.Size);

        DecodedInstruction extract = Decode(0x0F, 0xC5, 0xC0, 0x01);
        Assert.Equal(Mnemonic.Pextrw, extract.Mnemonic);
        Assert.Equal(32, extract.Operand1.Size);

        Assert.Equal(Mnemonic.Pmovmskb, Decode(0x0F, 0xD7, 0xC1).Mnemonic);
        Assert.Equal(Mnemonic.Maskmovq, Decode(0x0F, 0xF7, 0xC1).Mnemonic);
        Assert.Equal(Mnemonic.Movntq, Decode(0x0F, 0xE7, 0x05, 0, 0, 0, 0).Mnemonic);
    }

    [Fact]
    public void Prefetch_Decodes()
    {
        Assert.Equal(Mnemonic.Prefetch, Decode(0x0F, 0x18, 0x0D, 0, 0, 0, 0).Mnemonic);
        Assert.Equal(Mnemonic.Nop, Decode(0x0F, 0x18, 0x25, 0, 0, 0, 0).Mnemonic);
    }

    [Fact]
    public void Sse2Encodings_AreUnsupported()
    {
        Assert.Equal(Mnemonic.Unsupported, Decode(0x66, 0x0F, 0x6F, 0xC1).Mnemonic);
        Assert.Equal(Mnemonic.Unsupported, Decode(0x66, 0x0F, 0xFC, 0xC1).Mnemonic);
        Assert.Equal(Mnemonic.Unsupported, Decode(0xF3, 0x0F, 0x7E, 0xC1).Mnemonic);
        Assert.Equal(Mnemonic.Unsupported, Decode(0x0F, 0xD4, 0xC1).Mnemonic);
        Assert.Equal(Mnemonic.Unsupported, Decode(0x0F, 0xF4, 0xC1).Mnemonic);
        Assert.Equal(Mnemonic.Unsupported, Decode(0x0F, 0xFB, 0xC1).Mnemonic);
        Assert.Equal(Mnemonic.Unsupported, Decode(0x66, 0x0F, 0xFE, 0xC1).Mnemonic);
    }

    [Fact]
    public void FpuUndefinedRegisters_AreUnsupported()
    {
        Assert.Equal(Mnemonic.Unsupported, Decode(0xD9, 0xD1).Mnemonic);
        Assert.Equal(Mnemonic.Unsupported, Decode(0xD9, 0xD8).Mnemonic);
        Assert.Equal(Mnemonic.Unsupported, Decode(0xDE, 0xD1).Mnemonic);
        Assert.Equal(Mnemonic.Unsupported, Decode(0xDC, 0xD1).Mnemonic);
    }
}
