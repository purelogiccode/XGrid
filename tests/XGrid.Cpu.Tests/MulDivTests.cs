using Xunit;

namespace XGrid.Cpu.Tests;

public sealed class MulDivTests
{
    [Fact]
    public void Mul_32Bit_NoOverflow()
    {
        XboxCpu cpu = CpuHarness.Create(0xF7, 0xE3);
        cpu.Context.WriteRegister(Gpr.Eax, 0x1000);
        cpu.Context.WriteRegister(Gpr.Ebx, 0x10);

        cpu.Step();

        Assert.Equal(0x10000u, cpu.Context.ReadRegister(Gpr.Eax));
        Assert.Equal(0u, cpu.Context.ReadRegister(Gpr.Edx));
        Assert.Equal(0u, (cpu.Context.Eflags & FlagBits.Cf));
    }

    [Fact]
    public void Mul_32Bit_Overflow()
    {
        XboxCpu cpu = CpuHarness.Create(0xF7, 0xE3);
        cpu.Context.WriteRegister(Gpr.Eax, 0x80000000);
        cpu.Context.WriteRegister(Gpr.Ebx, 2);

        cpu.Step();

        Assert.Equal(0u, cpu.Context.ReadRegister(Gpr.Eax));
        Assert.Equal(1u, cpu.Context.ReadRegister(Gpr.Edx));
        Assert.NotEqual(0u, (cpu.Context.Eflags & FlagBits.Cf));
        Assert.NotEqual(0u, (cpu.Context.Eflags & FlagBits.Of));
    }

    [Fact]
    public void Mul_8Bit_ProducesAx()
    {
        XboxCpu cpu = CpuHarness.Create(0xF6, 0xE3);
        cpu.Context.WriteRegister(Gpr.Eax, 2);
        cpu.Context.WriteRegister(Gpr.Ebx, 3);

        cpu.Step();

        Assert.Equal(6u, cpu.Context.ReadRegister(Gpr.Eax));
        Assert.Equal(0u, (cpu.Context.Eflags & FlagBits.Cf));
    }

    [Fact]
    public void Imul_OneOperand_FullProduct()
    {
        XboxCpu cpu = CpuHarness.Create(0xF7, 0xEB);
        cpu.Context.WriteRegister(Gpr.Eax, 0xFFFFFFFE);
        cpu.Context.WriteRegister(Gpr.Ebx, 3);

        cpu.Step();

        Assert.Equal(0xFFFFFFFAu, cpu.Context.ReadRegister(Gpr.Eax));
        Assert.Equal(0xFFFFFFFFu, cpu.Context.ReadRegister(Gpr.Edx));
        Assert.Equal(0u, (cpu.Context.Eflags & FlagBits.Cf));
    }

    [Fact]
    public void Imul_ThreeOperand()
    {
        XboxCpu cpu = CpuHarness.Create(0x6B, 0xC3, 0x05);
        cpu.Context.WriteRegister(Gpr.Ebx, 7);

        cpu.Step();

        Assert.Equal(35u, cpu.Context.ReadRegister(Gpr.Eax));
    }

    [Fact]
    public void Imul_TwoOperand()
    {
        XboxCpu cpu = CpuHarness.Create(0x0F, 0xAF, 0xC3);
        cpu.Context.WriteRegister(Gpr.Eax, 6);
        cpu.Context.WriteRegister(Gpr.Ebx, 7);

        cpu.Step();

        Assert.Equal(42u, cpu.Context.ReadRegister(Gpr.Eax));
    }

    [Fact]
    public void Div_32Bit()
    {
        XboxCpu cpu = CpuHarness.Create(0xF7, 0xF3);
        cpu.Context.WriteRegister(Gpr.Eax, 100);
        cpu.Context.WriteRegister(Gpr.Edx, 0);
        cpu.Context.WriteRegister(Gpr.Ebx, 7);

        cpu.Step();

        Assert.Equal(14u, cpu.Context.ReadRegister(Gpr.Eax));
        Assert.Equal(2u, cpu.Context.ReadRegister(Gpr.Edx));
    }

    [Fact]
    public void Idiv_32Bit_Signed()
    {
        XboxCpu cpu = CpuHarness.Create(0xF7, 0xFB);
        cpu.Context.WriteRegister(Gpr.Eax, 0xFFFFFFF6);
        cpu.Context.WriteRegister(Gpr.Edx, 0xFFFFFFFF);
        cpu.Context.WriteRegister(Gpr.Ebx, 3);

        cpu.Step();

        Assert.Equal(0xFFFFFFFDu, cpu.Context.ReadRegister(Gpr.Eax));
        Assert.Equal(0xFFFFFFFFu, cpu.Context.ReadRegister(Gpr.Edx));
    }

    [Fact]
    public void Div_ByZero_WithoutIdt_Throws()
    {
        XboxCpu cpu = CpuHarness.Create(0xF7, 0xF3);
        cpu.Context.WriteRegister(Gpr.Ebx, 0);

        Assert.Throws<UnhandledGuestException>(cpu.Step);
    }

    [Fact]
    public void Div_QuotientOverflow_WithoutIdt_Throws()
    {
        XboxCpu cpu = CpuHarness.Create(0xF7, 0xF3);
        cpu.Context.WriteRegister(Gpr.Eax, 0xFFFFFFFF);
        cpu.Context.WriteRegister(Gpr.Edx, 0xFFFFFFFF);
        cpu.Context.WriteRegister(Gpr.Ebx, 1);

        Assert.Throws<UnhandledGuestException>(cpu.Step);
    }

    [Fact]
    public void Aad_Base10_ConvertsBcdToBinary()
    {
        XboxCpu cpu = CpuHarness.Create(0xD5, 0x0A);
        cpu.Context.WriteRegister(Gpr.Eax, 0x00000209);

        cpu.Step();

        Assert.Equal(0x0000001Du, cpu.Context.ReadRegister(Gpr.Eax));
        Assert.Equal(0u, (cpu.Context.Eflags & FlagBits.Zf));
        Assert.Equal(0u, (cpu.Context.Eflags & FlagBits.Sf));
    }

    [Fact]
    public void Aad_ZeroResult_SetsZeroFlag()
    {
        XboxCpu cpu = CpuHarness.Create(0xD5, 0x0A);
        cpu.Context.WriteRegister(Gpr.Eax, 0x00000000);

        cpu.Step();

        Assert.Equal(0u, cpu.Context.ReadRegister(Gpr.Eax));
        Assert.NotEqual(0u, (cpu.Context.Eflags & FlagBits.Zf));
    }

    [Fact]
    public void Aam_Base10_SplitsIntoDigits()
    {
        XboxCpu cpu = CpuHarness.Create(0xD4, 0x0A);
        cpu.Context.WriteRegister(Gpr.Eax, 0x0000001D);

        cpu.Step();

        Assert.Equal(0x00000209u, cpu.Context.ReadRegister(Gpr.Eax));
        Assert.Equal(0u, (cpu.Context.Eflags & FlagBits.Zf));
    }

    [Fact]
    public void Aam_Zero_SetsZeroFlag()
    {
        XboxCpu cpu = CpuHarness.Create(0xD4, 0x0A);
        cpu.Context.WriteRegister(Gpr.Eax, 0x00000000);

        cpu.Step();

        Assert.Equal(0u, cpu.Context.ReadRegister(Gpr.Eax));
        Assert.NotEqual(0u, (cpu.Context.Eflags & FlagBits.Zf));
    }

    [Fact]
    public void Aam_ZeroBase_WithoutIdt_Throws()
    {
        XboxCpu cpu = CpuHarness.Create(0xD4, 0x00);

        Assert.Throws<UnhandledGuestException>(cpu.Step);
    }
}
