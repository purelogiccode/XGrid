using System;
using Xunit;

namespace XGrid.Cpu.Tests;

public sealed class FpuTests
{
    private const uint Data = 0x2000;
    private const uint Data2 = 0x2100;

    private static double St(XboxCpu cpu, int index)
    {
        return cpu.Context.Fpu.GetRegister(index).ToDouble();
    }

    [Fact]
    public void Fld1_PushesOneAndMovesTop()
    {
        XboxCpu cpu = CpuHarness.Create(0xD9, 0xE8);

        cpu.Step();

        Assert.Equal(1.0, St(cpu, 0));
        Assert.Equal(7, cpu.Context.Fpu.Top);
    }

    [Fact]
    public void Fldz_PushesZero()
    {
        XboxCpu cpu = CpuHarness.Create(0xD9, 0xEE);

        cpu.Step();

        Assert.Equal(0.0, St(cpu, 0));
    }

    [Fact]
    public void Fldpi_PushesPi()
    {
        XboxCpu cpu = CpuHarness.Create(0xD9, 0xEB);

        cpu.Step();

        Assert.Equal(Math.PI, St(cpu, 0), 15);
    }

    [Fact]
    public void Fild_LoadsInt32()
    {
        XboxCpu cpu = CpuHarness.Create(0xDB, 0x05, 0x00, 0x20, 0x00, 0x00);
        cpu.Ram().Write32(Data, 42);

        cpu.Step();

        Assert.Equal(42.0, St(cpu, 0));
    }

    [Fact]
    public void Fild_LoadsInt64()
    {
        XboxCpu cpu = CpuHarness.Create(0xDF, 0x2D, 0x00, 0x20, 0x00, 0x00);
        cpu.Ram().Write64(Data, 4294967296UL);

        cpu.Step();

        Assert.Equal(4294967296.0, St(cpu, 0));
    }

    [Fact]
    public void Fistp_StoresInt32()
    {
        XboxCpu cpu = CpuHarness.Create(0xD9, 0xE8, 0xDB, 0x1D, 0x00, 0x20, 0x00, 0x00);

        cpu.Step();
        cpu.Step();

        Assert.Equal(1u, cpu.Ram().Read32(Data));
        Assert.Equal(0, cpu.Context.Fpu.Top);
    }

    [Fact]
    public void Fistp_StoresInt64()
    {
        XboxCpu cpu = CpuHarness.Create(0xDB, 0x05, 0x00, 0x20, 0x00, 0x00, 0xDF, 0x3D, 0x00, 0x21, 0x00, 0x00);
        cpu.Ram().Write32(Data, 123456);

        cpu.Step();
        cpu.Step();

        Assert.Equal(123456UL, cpu.Ram().Read64(Data2));
    }

    [Fact]
    public void Fistp_UsesControlWordRounding()
    {
        XboxCpu cpu = CpuHarness.Create(0xDD, 0x05, 0x00, 0x20, 0x00, 0x00, 0xDB, 0x1D, 0x00, 0x21, 0x00, 0x00);
        cpu.Ram().Write64(Data, (ulong)BitConverter.DoubleToInt64Bits(2.5));
        cpu.Context.Fpu.ControlWord = 0x0F7F;

        cpu.Step();
        cpu.Step();

        Assert.Equal(2u, cpu.Ram().Read32(Data2));
    }

    [Fact]
    public void Fld_LoadsFloat32()
    {
        XboxCpu cpu = CpuHarness.Create(0xD9, 0x05, 0x00, 0x20, 0x00, 0x00);
        cpu.Ram().Write32(Data, BitConverter.SingleToUInt32Bits(3.5f));

        cpu.Step();

        Assert.Equal(3.5, St(cpu, 0));
    }

    [Fact]
    public void Fstp_StoresFloat64()
    {
        XboxCpu cpu = CpuHarness.Create(0xD9, 0xE8, 0xDD, 0x1D, 0x00, 0x20, 0x00, 0x00);

        cpu.Step();
        cpu.Step();

        Assert.Equal(1.0, BitConverter.Int64BitsToDouble((long)cpu.Ram().Read64(Data)));
        Assert.Equal(0, cpu.Context.Fpu.Top);
    }

    [Fact]
    public void Fst_StoresWithoutPopping()
    {
        XboxCpu cpu = CpuHarness.Create(0xD9, 0xE8, 0xD9, 0x15, 0x00, 0x20, 0x00, 0x00);

        cpu.Step();
        cpu.Step();

        Assert.Equal(BitConverter.SingleToUInt32Bits(1f), cpu.Ram().Read32(Data));
        Assert.Equal(1.0, St(cpu, 0));
    }

    [Fact]
    public void Fstp_ExtendedRoundTrip()
    {
        XboxCpu cpu = CpuHarness.Create(0xD9, 0xE8, 0xDB, 0x3D, 0x00, 0x20, 0x00, 0x00, 0xDB, 0x2D, 0x00, 0x20, 0x00, 0x00);

        cpu.Step();
        cpu.Step();
        cpu.Step();

        Assert.Equal(1.0, St(cpu, 0));
        Assert.Equal(7, cpu.Context.Fpu.Top);
    }

    [Fact]
    public void Fadd_RegisterAdds()
    {
        XboxCpu cpu = CpuHarness.Create(0xD9, 0xE8, 0xD9, 0xE8, 0xD8, 0xC1);

        cpu.Step();
        cpu.Step();
        cpu.Step();

        Assert.Equal(2.0, St(cpu, 0));
    }

    [Fact]
    public void Faddp_AddsAndPops()
    {
        XboxCpu cpu = CpuHarness.Create(0xD9, 0xE8, 0xD9, 0xE8, 0xDE, 0xC1);

        cpu.Step();
        cpu.Step();
        cpu.Step();

        Assert.Equal(2.0, St(cpu, 0));
        Assert.Equal(7, cpu.Context.Fpu.Top);
    }

    [Fact]
    public void Fsub_SubtractsSourceFromDestination()
    {
        XboxCpu cpu = CpuHarness.Create(0xDB, 0x05, 0x00, 0x20, 0x00, 0x00, 0xDB, 0x05, 0x04, 0x20, 0x00, 0x00, 0xD8, 0xE1);
        cpu.Ram().Write32(Data, 10);
        cpu.Ram().Write32(Data + 4, 4);

        cpu.Step();
        cpu.Step();
        cpu.Step();

        Assert.Equal(-6.0, St(cpu, 0));
    }

    [Fact]
    public void Fsubr_ReversesOperands()
    {
        XboxCpu cpu = CpuHarness.Create(0xDB, 0x05, 0x00, 0x20, 0x00, 0x00, 0xDB, 0x05, 0x04, 0x20, 0x00, 0x00, 0xD8, 0xE9);
        cpu.Ram().Write32(Data, 10);
        cpu.Ram().Write32(Data + 4, 4);

        cpu.Step();
        cpu.Step();
        cpu.Step();

        Assert.Equal(6.0, St(cpu, 0));
    }

    [Fact]
    public void Fdiv_Divides()
    {
        XboxCpu cpu = CpuHarness.Create(0xDB, 0x05, 0x00, 0x20, 0x00, 0x00, 0xDB, 0x05, 0x04, 0x20, 0x00, 0x00, 0xD8, 0xF1);
        cpu.Ram().Write32(Data, 10);
        cpu.Ram().Write32(Data + 4, 4);

        cpu.Step();
        cpu.Step();
        cpu.Step();

        Assert.Equal(0.4, St(cpu, 0), 12);
    }

    [Fact]
    public void Fiadd_AddsIntegerMemory()
    {
        XboxCpu cpu = CpuHarness.Create(0xDB, 0x05, 0x00, 0x20, 0x00, 0x00, 0xDA, 0x05, 0x04, 0x20, 0x00, 0x00);
        cpu.Ram().Write32(Data, 10);
        cpu.Ram().Write32(Data + 4, 32);

        cpu.Step();
        cpu.Step();

        Assert.Equal(42.0, St(cpu, 0));
    }

    [Fact]
    public void Fcom_SetsConditionCodesForEqual()
    {
        XboxCpu cpu = CpuHarness.Create(0xD9, 0xE8, 0xD9, 0xE8, 0xD8, 0xD1);

        cpu.Step();
        cpu.Step();
        cpu.Step();

        Assert.True((cpu.Context.Fpu.StatusWord & 0x4000) != 0);
        Assert.True((cpu.Context.Fpu.StatusWord & 0x0100) == 0);
    }

    [Fact]
    public void Fcom_SetsConditionCodesForLess()
    {
        XboxCpu cpu = CpuHarness.Create(0xD9, 0xE8, 0xD9, 0xEE, 0xD8, 0xD1);

        cpu.Step();
        cpu.Step();
        cpu.Step();

        Assert.True((cpu.Context.Fpu.StatusWord & 0x4000) == 0);
        Assert.True((cpu.Context.Fpu.StatusWord & 0x0100) != 0);
    }

    [Fact]
    public void Fcompp_PopsTwice()
    {
        XboxCpu cpu = CpuHarness.Create(0xD9, 0xE8, 0xD9, 0xE8, 0xDE, 0xD9);

        cpu.Step();
        cpu.Step();
        cpu.Step();

        Assert.Equal(0, cpu.Context.Fpu.Top);
    }

    [Fact]
    public void Fcomi_SetsZeroFlag()
    {
        XboxCpu cpu = CpuHarness.Create(0xD9, 0xE8, 0xD9, 0xE8, 0xDB, 0xF1);

        cpu.Step();
        cpu.Step();
        cpu.Step();

        Assert.True((cpu.Context.Eflags & FlagBits.ZF) != 0);
    }

    [Fact]
    public void Fnstsw_StoresStatusInAx()
    {
        XboxCpu cpu = CpuHarness.Create(0xD9, 0xE8, 0xD9, 0xE8, 0xD8, 0xD1, 0xDF, 0xE0);

        cpu.Step();
        cpu.Step();
        cpu.Step();
        cpu.Step();

        Assert.True((cpu.Context.ReadRegister16(Gpr.Eax) & 0x4000) != 0);
    }

    [Fact]
    public void Fldcw_LoadsControlWord()
    {
        XboxCpu cpu = CpuHarness.Create(0xD9, 0x2D, 0x00, 0x20, 0x00, 0x00);
        cpu.Ram().Write16(Data, 0x0F7F);

        cpu.Step();

        Assert.Equal(0x0F7F, cpu.Context.Fpu.ControlWord);
    }

    [Fact]
    public void Fnstcw_StoresControlWord()
    {
        XboxCpu cpu = CpuHarness.Create(0xD9, 0x3D, 0x00, 0x20, 0x00, 0x00);

        cpu.Step();

        Assert.Equal(0x037F, cpu.Ram().Read16(Data));
    }

    [Fact]
    public void Fnstenv_Fldenv_RoundTrip()
    {
        XboxCpu cpu = CpuHarness.Create(0xD9, 0x35, 0x00, 0x20, 0x00, 0x00, 0xD9, 0x25, 0x00, 0x20, 0x00, 0x00);

        cpu.Step();
        cpu.Context.Fpu.ControlWord = 0x1234;
        cpu.Step();

        Assert.Equal(0x037F, cpu.Context.Fpu.ControlWord);
    }

    [Fact]
    public void Fnsave_Frstor_RoundTrip()
    {
        XboxCpu cpu = CpuHarness.Create(0xD9, 0xE8, 0xDD, 0x35, 0x00, 0x20, 0x00, 0x00, 0xDD, 0x25, 0x00, 0x20, 0x00, 0x00);

        cpu.Step();
        cpu.Step();
        cpu.Step();

        Assert.Equal(1.0, St(cpu, 0));
        Assert.Equal(7, cpu.Context.Fpu.Top);
    }

    [Fact]
    public void Fsqrt_ComputesSquareRoot()
    {
        XboxCpu cpu = CpuHarness.Create(0xDB, 0x05, 0x00, 0x20, 0x00, 0x00, 0xD9, 0xFA);
        cpu.Ram().Write32(Data, 4);

        cpu.Step();
        cpu.Step();

        Assert.Equal(2.0, St(cpu, 0));
    }

    [Fact]
    public void Fchs_And_Fabs()
    {
        XboxCpu cpu = CpuHarness.Create(0xD9, 0xE8, 0xD9, 0xE0, 0xD9, 0xE1);

        cpu.Step();
        cpu.Step();

        Assert.Equal(-1.0, St(cpu, 0));

        cpu.Step();

        Assert.Equal(1.0, St(cpu, 0));
    }

    [Fact]
    public void Fxch_SwapsTop()
    {
        XboxCpu cpu = CpuHarness.Create(0xDB, 0x05, 0x00, 0x20, 0x00, 0x00, 0xDB, 0x05, 0x04, 0x20, 0x00, 0x00, 0xD9, 0xC9);
        cpu.Ram().Write32(Data, 1);
        cpu.Ram().Write32(Data + 4, 2);

        cpu.Step();
        cpu.Step();
        cpu.Step();

        Assert.Equal(1.0, St(cpu, 0));
        Assert.Equal(2.0, St(cpu, 1));
    }

    [Fact]
    public void Fcmovb_MovesWhenCarrySet()
    {
        XboxCpu cpu = CpuHarness.Create(0xF9, 0xD9, 0xE8, 0xD9, 0xEE, 0xDA, 0xC1);

        cpu.Step();
        cpu.Step();
        cpu.Step();
        cpu.Step();

        Assert.Equal(1.0, St(cpu, 0));
    }

    [Fact]
    public void Fbld_Fbstp_RoundTrip()
    {
        XboxCpu cpu = CpuHarness.Create(0xDF, 0x25, 0x00, 0x20, 0x00, 0x00, 0xDF, 0x35, 0x00, 0x21, 0x00, 0x00);
        cpu.Ram().Write8(Data, 0x34);
        cpu.Ram().Write8(Data + 1, 0x12);

        cpu.Step();
        cpu.Step();

        Assert.Equal(0x34, cpu.Ram().Read8(Data2));
        Assert.Equal(0x12, cpu.Ram().Read8(Data2 + 1));
    }

    [Fact]
    public void Frndint_RoundsToNearestEven()
    {
        XboxCpu cpu = CpuHarness.Create(0xDD, 0x05, 0x00, 0x20, 0x00, 0x00, 0xD9, 0xFC);
        cpu.Ram().Write64(Data, (ulong)BitConverter.DoubleToInt64Bits(2.5));

        cpu.Step();
        cpu.Step();

        Assert.Equal(2.0, St(cpu, 0));
    }

    [Fact]
    public void Fscale_ScalesByPowerOfTwo()
    {
        XboxCpu cpu = CpuHarness.Create(
            0xDB, 0x05, 0x00, 0x20, 0x00, 0x00,
            0xDB, 0x05, 0x04, 0x20, 0x00, 0x00,
            0xD9, 0xFD);
        cpu.Ram().Write32(Data, 3);
        cpu.Ram().Write32(Data + 4, 1);

        cpu.Step();
        cpu.Step();
        cpu.Step();

        Assert.Equal(8.0, St(cpu, 0));
    }

    [Fact]
    public void Fxtract_SplitsExponentAndSignificand()
    {
        XboxCpu cpu = CpuHarness.Create(0xDD, 0x05, 0x00, 0x20, 0x00, 0x00, 0xD9, 0xF4);
        cpu.Ram().Write64(Data, (ulong)BitConverter.DoubleToInt64Bits(12.0));

        cpu.Step();
        cpu.Step();

        Assert.Equal(1.5, St(cpu, 0));
        Assert.Equal(3.0, St(cpu, 1));
    }

    [Fact]
    public void Fprem_ComputesTruncatedRemainder()
    {
        XboxCpu cpu = CpuHarness.Create(
            0xDB, 0x05, 0x00, 0x20, 0x00, 0x00,
            0xDB, 0x05, 0x04, 0x20, 0x00, 0x00,
            0xD9, 0xF8);
        cpu.Ram().Write32(Data, 7);
        cpu.Ram().Write32(Data + 4, 17);

        cpu.Step();
        cpu.Step();
        cpu.Step();

        Assert.Equal(3.0, St(cpu, 0));
    }

    [Fact]
    public void F2xm1_ComputesTwoToTheXMinusOne()
    {
        XboxCpu cpu = CpuHarness.Create(0xDB, 0x05, 0x00, 0x20, 0x00, 0x00, 0xD9, 0xF0);
        cpu.Ram().Write32(Data, 3);

        cpu.Step();
        cpu.Step();

        Assert.Equal(7.0, St(cpu, 0));
    }

    [Fact]
    public void Fsin_ComputesSine()
    {
        XboxCpu cpu = CpuHarness.Create(0xDD, 0x05, 0x00, 0x20, 0x00, 0x00, 0xD9, 0xFE);
        cpu.Ram().Write64(Data, (ulong)BitConverter.DoubleToInt64Bits(Math.PI / 2));

        cpu.Step();
        cpu.Step();

        Assert.Equal(1.0, St(cpu, 0), 12);
    }

    [Fact]
    public void Fpatan_ComputesArcTangent()
    {
        XboxCpu cpu = CpuHarness.Create(
            0xDB, 0x05, 0x00, 0x20, 0x00, 0x00,
            0xDB, 0x05, 0x04, 0x20, 0x00, 0x00,
            0xD9, 0xF3);
        cpu.Ram().Write32(Data, 1);
        cpu.Ram().Write32(Data + 4, 1);

        cpu.Step();
        cpu.Step();
        cpu.Step();

        Assert.Equal(Math.PI / 4, St(cpu, 0), 12);
    }

    [Fact]
    public void Ftst_SetsZeroConditionForZero()
    {
        XboxCpu cpu = CpuHarness.Create(0xD9, 0xEE, 0xD9, 0xE4);

        cpu.Step();
        cpu.Step();

        Assert.True((cpu.Context.Fpu.StatusWord & 0x4000) != 0);
    }

    [Fact]
    public void Fxam_ClassifiesZero()
    {
        XboxCpu cpu = CpuHarness.Create(0xD9, 0xEE, 0xD9, 0xE5);

        cpu.Step();
        cpu.Step();

        Assert.True((cpu.Context.Fpu.StatusWord & 0x4000) != 0);
        Assert.True((cpu.Context.Fpu.StatusWord & 0x0400) == 0);
    }

    [Fact]
    public void Fninit_ResetsControlWord()
    {
        XboxCpu cpu = CpuHarness.Create(0xDB, 0xE3);
        cpu.Context.Fpu.ControlWord = 0x1234;

        cpu.Step();

        Assert.Equal(0x037F, cpu.Context.Fpu.ControlWord);
    }

    [Fact]
    public void Ffree_MarksRegisterEmpty()
    {
        XboxCpu cpu = CpuHarness.Create(0xD9, 0xE8, 0xDD, 0xC0);

        cpu.Step();
        cpu.Step();

        Assert.Equal(FpuState.TagEmpty, (cpu.Context.Fpu.TagWord >> 14) & 3);
    }

    [Fact]
    public void Fdecstp_And_Fincstp_MoveTop()
    {
        XboxCpu cpu = CpuHarness.Create(0xD9, 0xF6, 0xD9, 0xF7);

        cpu.Step();
        Assert.Equal(7, cpu.Context.Fpu.Top);

        cpu.Step();
        Assert.Equal(0, cpu.Context.Fpu.Top);
    }
}
