using Xunit;

namespace XGrid.Cpu.Tests;

public sealed class MmxTests
{
    private const uint Data = 0x2000;
    private const uint Scratch = 0x3000;

    [Fact]
    public void Movd_LoadsFromGpr()
    {
        XboxCpu cpu = CpuHarness.Create(0x0F, 0x6E, 0xC0);
        cpu.Context.Eax = 0x12345678;

        cpu.Step();

        Assert.Equal(0x12345678UL, cpu.Context.ReadMmx(0));
    }

    [Fact]
    public void Movd_StoresToGpr()
    {
        XboxCpu cpu = CpuHarness.Create(0x0F, 0x7E, 0xC0);
        cpu.Context.WriteMmx(0, 0xCAFEBABEUL);

        cpu.Step();

        Assert.Equal(0xCAFEBABEu, cpu.Context.Eax);
    }

    [Fact]
    public void Movq_CopiesRegister()
    {
        XboxCpu cpu = CpuHarness.Create(0x0F, 0x6F, 0xC1);
        cpu.Context.WriteMmx(1, 0x1122334455667788UL);

        cpu.Step();

        Assert.Equal(0x1122334455667788UL, cpu.Context.ReadMmx(0));
    }

    [Fact]
    public void Movq_StoreAndLoadMemory()
    {
        XboxCpu cpu = CpuHarness.Create(0x0F, 0x7F, 0x05, 0x00, 0x20, 0x00, 0x00, 0x0F, 0x6F, 0x0D, 0x00, 0x20, 0x00,
            0x00);
        cpu.Context.WriteMmx(0, 0xDEADBEEFCAFEF00DUL);

        cpu.Step();
        cpu.Step();

        Assert.Equal(0xDEADBEEFCAFEF00DUL, cpu.Ram().Read64(Data));
        Assert.Equal(0xDEADBEEFCAFEF00DUL, cpu.Context.ReadMmx(1));
    }

    [Fact]
    public void Paddb_AddsBytes()
    {
        XboxCpu cpu = CpuHarness.Create(0x0F, 0xFC, 0xC1);
        cpu.Context.WriteMmx(0, 0x0102030405060708UL);
        cpu.Context.WriteMmx(1, 0x0101010101010101UL);

        cpu.Step();

        Assert.Equal(0x0203040506070809UL, cpu.Context.ReadMmx(0));
    }

    [Fact]
    public void Paddsb_Saturates()
    {
        XboxCpu cpu = CpuHarness.Create(0x0F, 0xEC, 0xC1);
        cpu.Context.WriteMmx(0, 0x9090909090909070UL);
        cpu.Context.WriteMmx(1, 0x9090909090909020UL);

        cpu.Step();

        Assert.Equal(0x808080808080807FUL, cpu.Context.ReadMmx(0));
    }

    [Fact]
    public void Paddusb_Saturates()
    {
        XboxCpu cpu = CpuHarness.Create(0x0F, 0xDC, 0xC1);
        cpu.Context.WriteMmx(0, 0xF0F0F0F0F0F0F0F0UL);
        cpu.Context.WriteMmx(1, 0x2020202020202020UL);

        cpu.Step();

        Assert.Equal(0xFFFFFFFFFFFFFFFFUL, cpu.Context.ReadMmx(0));
    }

    [Fact]
    public void Psubb_SubtractsBytes()
    {
        XboxCpu cpu = CpuHarness.Create(0x0F, 0xF8, 0xC1);
        cpu.Context.WriteMmx(0, 0x0202020202020202UL);
        cpu.Context.WriteMmx(1, 0x0101010101010101UL);

        cpu.Step();

        Assert.Equal(0x0101010101010101UL, cpu.Context.ReadMmx(0));
    }

    [Fact]
    public void Psubusb_ClampsToZero()
    {
        XboxCpu cpu = CpuHarness.Create(0x0F, 0xD8, 0xC1);
        cpu.Context.WriteMmx(0, 0x1010101010101010UL);
        cpu.Context.WriteMmx(1, 0x2020202020202020UL);

        cpu.Step();

        Assert.Equal(0UL, cpu.Context.ReadMmx(0));
    }

    [Fact]
    public void Pmullw_MultipliesLowWords()
    {
        XboxCpu cpu = CpuHarness.Create(0x0F, 0xD5, 0xC1);
        cpu.Context.WriteMmx(0, 0x0003000200010002UL);
        cpu.Context.WriteMmx(1, 0x0003000200010003UL);

        cpu.Step();

        Assert.Equal(0x0009000400010006UL, cpu.Context.ReadMmx(0));
    }

    [Fact]
    public void Pmulhw_MultipliesHighWords()
    {
        XboxCpu cpu = CpuHarness.Create(0x0F, 0xE5, 0xC1);
        cpu.Context.WriteMmx(0, 0x0000000000004000UL);
        cpu.Context.WriteMmx(1, 0x0000000000004000UL);

        cpu.Step();

        Assert.Equal(0x0000000000001000UL, cpu.Context.ReadMmx(0));
    }

    [Fact]
    public void Pmulhuw_MultipliesHighWordsUnsigned()
    {
        XboxCpu cpu = CpuHarness.Create(0x0F, 0xE4, 0xC1);
        cpu.Context.WriteMmx(0, 0x0000000000008000UL);
        cpu.Context.WriteMmx(1, 0x0000000000008000UL);

        cpu.Step();

        Assert.Equal(0x0000000000004000UL, cpu.Context.ReadMmx(0));
    }

    [Fact]
    public void Pmaddwd_AddsProducts()
    {
        XboxCpu cpu = CpuHarness.Create(0x0F, 0xF5, 0xC1);
        cpu.Context.WriteMmx(0, 0x0004000300020001UL);
        cpu.Context.WriteMmx(1, 0x0004000300020001UL);

        cpu.Step();

        Assert.Equal(0x0000001900000005UL, cpu.Context.ReadMmx(0));
    }

    [Fact]
    public void Pcmpeqb_ProducesMask()
    {
        XboxCpu cpu = CpuHarness.Create(0x0F, 0x74, 0xC1);
        cpu.Context.WriteMmx(0, 0x0102030405060708UL);
        cpu.Context.WriteMmx(1, 0x0100030405000708UL);

        cpu.Step();

        Assert.Equal(0xFF00FFFFFF00FFFFUL, cpu.Context.ReadMmx(0));
    }

    [Fact]
    public void Pcmpgtb_IsSigned()
    {
        XboxCpu cpu = CpuHarness.Create(0x0F, 0x64, 0xC1);
        cpu.Context.WriteMmx(0, 0x0000000000008002UL);
        cpu.Context.WriteMmx(1, 0x0000000000000101UL);

        cpu.Step();

        Assert.Equal(0x00000000000000FFUL, cpu.Context.ReadMmx(0));
    }

    [Fact]
    public void Pand_Por_Pxor()
    {
        XboxCpu cpu = CpuHarness.Create(0x0F, 0xDB, 0xC1, 0x0F, 0xEB, 0xC1, 0x0F, 0xEF, 0xC1);
        cpu.Context.WriteMmx(0, 0xFF00FF00FF00FF00UL);
        cpu.Context.WriteMmx(1, 0x0FF00FF00FF00FF0UL);

        cpu.Step();
        Assert.Equal(0x0F000F000F000F00UL, cpu.Context.ReadMmx(0));

        cpu.Context.WriteMmx(0, 0xFF00FF00FF00FF00UL);
        cpu.Step();
        Assert.Equal(0xFFF0FFF0FFF0FFF0UL, cpu.Context.ReadMmx(0));

        cpu.Context.WriteMmx(0, 0xFF00FF00FF00FF00UL);
        cpu.Step();
        Assert.Equal(0xF0F0F0F0F0F0F0F0UL, cpu.Context.ReadMmx(0));
    }

    [Fact]
    public void Psllw_ImmediateShifts()
    {
        XboxCpu cpu = CpuHarness.Create(0x0F, 0x71, 0xF0, 0x04);
        cpu.Context.WriteMmx(0, 0x0000000000000001UL);

        cpu.Step();

        Assert.Equal(0x0000000000000010UL, cpu.Context.ReadMmx(0));
    }

    [Fact]
    public void Psrlq_ImmediateShifts()
    {
        XboxCpu cpu = CpuHarness.Create(0x0F, 0x73, 0xD0, 0x08);
        cpu.Context.WriteMmx(0, 0x0000000000000100UL);

        cpu.Step();

        Assert.Equal(0x0000000000000001UL, cpu.Context.ReadMmx(0));
    }

    [Fact]
    public void Psraw_RegisterCountShiftsArithmetically()
    {
        XboxCpu cpu = CpuHarness.Create(0x0F, 0xE1, 0xC1);
        cpu.Context.WriteMmx(0, 0x0000000000008000UL);
        cpu.Context.WriteMmx(1, 0x0000000000000001UL);

        cpu.Step();

        Assert.Equal(0x000000000000C000UL, cpu.Context.ReadMmx(0));
    }

    [Fact]
    public void Pavgb_Averages()
    {
        XboxCpu cpu = CpuHarness.Create(0x0F, 0xE0, 0xC1);
        cpu.Context.WriteMmx(0, 0x1010101010101010UL);
        cpu.Context.WriteMmx(1, 0x2020202020202020UL);

        cpu.Step();

        Assert.Equal(0x1818181818181818UL, cpu.Context.ReadMmx(0));
    }

    [Fact]
    public void Psadbw_SumsAbsoluteDifferences()
    {
        XboxCpu cpu = CpuHarness.Create(0x0F, 0xF6, 0xC1);
        cpu.Context.WriteMmx(0, 0x0101010101010101UL);
        cpu.Context.WriteMmx(1, 0x0202020202020202UL);

        cpu.Step();

        Assert.Equal(8UL, cpu.Context.ReadMmx(0));
    }

    [Fact]
    public void Packsswb_PacksWithSaturation()
    {
        XboxCpu cpu = CpuHarness.Create(0x0F, 0x63, 0xC1);
        cpu.Context.WriteMmx(0, 0x0004000300020001UL);
        cpu.Context.WriteMmx(1, 0x0008000700060005UL);

        cpu.Step();

        Assert.Equal(0x0807060504030201UL, cpu.Context.ReadMmx(0));
    }

    [Fact]
    public void Punpcklbw_InterleavesLowBytes()
    {
        XboxCpu cpu = CpuHarness.Create(0x0F, 0x60, 0xC1);
        cpu.Context.WriteMmx(0, 0x0807060504030201UL);
        cpu.Context.WriteMmx(1, 0x1817161514131211UL);

        cpu.Step();

        Assert.Equal(0x1404130312021101UL, cpu.Context.ReadMmx(0));
    }

    [Fact]
    public void Pshufw_ShufflesWords()
    {
        XboxCpu cpu = CpuHarness.Create(0x0F, 0x70, 0xC1, 0x1B);
        cpu.Context.WriteMmx(1, 0x4444333322221111UL);

        cpu.Step();

        Assert.Equal(0x1111222233334444UL, cpu.Context.ReadMmx(0));
    }

    [Fact]
    public void Pmovmskb_MovesSignBits()
    {
        XboxCpu cpu = CpuHarness.Create(0x0F, 0xD7, 0xC1);
        cpu.Context.WriteMmx(1, 0x8000000000000080UL);

        cpu.Step();

        Assert.Equal(0x81u, cpu.Context.Eax);
    }

    [Fact]
    public void Pextrw_ExtractsWord()
    {
        XboxCpu cpu = CpuHarness.Create(0x0F, 0xC5, 0xC0, 0x01);
        cpu.Context.WriteMmx(0, 0x4444333322221111UL);

        cpu.Step();

        Assert.Equal(0x2222u, cpu.Context.Eax);
    }

    [Fact]
    public void Pinsrw_InsertsWord()
    {
        XboxCpu cpu = CpuHarness.Create(0x0F, 0xC4, 0xC0, 0x02);
        cpu.Context.WriteMmx(0, 0x4444333322221111UL);
        cpu.Context.Eax = 0xABCD;

        cpu.Step();

        Assert.Equal(0x4444ABCD22221111UL, cpu.Context.ReadMmx(0));
    }

    [Fact]
    public void Emms_ClearsTags()
    {
        XboxCpu cpu = CpuHarness.Create(0x0F, 0x77);
        cpu.Context.WriteMmx(0, 0x12345678UL);

        cpu.Step();

        Assert.Equal(0xFFFF, cpu.Context.Fpu.TagWord);
    }

    [Fact]
    public void Movntq_StoresToMemory()
    {
        XboxCpu cpu = CpuHarness.Create(0x0F, 0xE7, 0x05, 0x00, 0x20, 0x00, 0x00);
        cpu.Context.WriteMmx(0, 0x1122334455667788UL);

        cpu.Step();

        Assert.Equal(0x1122334455667788UL, cpu.Ram().Read64(Data));
    }

    [Fact]
    public void Maskmovq_StoresMaskedBytes()
    {
        XboxCpu cpu = CpuHarness.Create(0x0F, 0xF7, 0xC1);
        cpu.Context.WriteMmx(0, 0xAABBCCDDEEFF0011UL);
        cpu.Context.WriteMmx(1, 0x0000000000800080UL);
        cpu.Context.Edi = Scratch;

        cpu.Step();

        Assert.Equal(0x11, cpu.Ram().Read8(Scratch));
        Assert.Equal(0x00, cpu.Ram().Read8(Scratch + 1));
        Assert.Equal(0xFF, cpu.Ram().Read8(Scratch + 2));
    }

    // The packed min/max pair is signed for words (0F EA / 0F EE). The first
    // version of the decoder had no PMINSW/PMAXSW and implemented these two
    // opcodes with the *unsigned* byte-operand rule, so every negative lane
    // picked the wrong side (found by the CPU oracle, decisions 220).
    [Fact]
    public void Pminsw_ComparesSignedWords()
    {
        XboxCpu cpu = CpuHarness.Create(0x0F, 0xEA, 0xC1);
        cpu.Context.WriteMmx(0, 0x000100008000FFFFUL);
        cpu.Context.WriteMmx(1, 0xFF000304FF000102UL);

        cpu.Step();

        // min(-1, 258) = -1, min(-32768, -256) = -32768, min(0, 772) = 0,
        // min(1, -256) = -256.
        Assert.Equal(0xFF0000008000FFFFUL, cpu.Context.ReadMmx(0));
    }

    [Fact]
    public void Pmaxsw_ComparesSignedWords()
    {
        XboxCpu cpu = CpuHarness.Create(0x0F, 0xEE, 0xC1);
        cpu.Context.WriteMmx(0, 0x000100008000FFFFUL);
        cpu.Context.WriteMmx(1, 0xFF000304FF000102UL);

        cpu.Step();

        // max(-1, 258) = 258, max(-32768, -256) = -256, max(0, 772) = 772,
        // max(1, -256) = 1.
        Assert.Equal(0x00010304FF000102UL, cpu.Context.ReadMmx(0));
    }

    // A shift count at or past the element width saturates to the sign fill of
    // each element, not one fill for the whole register (the old code tested
    // only the low word/dword's sign bit).
    [Fact]
    public void Psraw_CountPastWordWidth_FillsEachWordWithItsSign()
    {
        XboxCpu cpu = CpuHarness.Create(0x0F, 0x71, 0xE0, 0x10);
        cpu.Context.WriteMmx(0, 0x000100008000FFFFUL);

        cpu.Step();

        Assert.Equal(0x00000000FFFFFFFFUL, cpu.Context.ReadMmx(0));
    }

    [Fact]
    public void Psrad_CountPastDwordWidth_FillsEachDwordWithItsSign()
    {
        XboxCpu cpu = CpuHarness.Create(0x0F, 0x72, 0xE0, 0x20);
        cpu.Context.WriteMmx(0, 0x7FFFFFFF80000001UL);

        cpu.Step();

        Assert.Equal(0x00000000FFFFFFFFUL, cpu.Context.ReadMmx(0));
    }

    [Fact]
    public void Psrlw_CountPastWordWidth_Clears()
    {
        XboxCpu cpu = CpuHarness.Create(0x0F, 0x71, 0xD0, 0x10);
        cpu.Context.WriteMmx(0, 0x8000FFFF8000FFFFUL);

        cpu.Step();

        Assert.Equal(0UL, cpu.Context.ReadMmx(0));
    }
}
