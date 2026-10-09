using Xunit;

namespace XGrid.Cpu.Tests;

public sealed class SystemInstructionTests
{
    [Fact]
    public void Movzx_Byte()
    {
        XboxCpu cpu = CpuHarness.Create(0x0F, 0xB6, 0xC3);
        cpu.Context.WriteRegister(Gpr.Ebx, 0xFFFFFF80);

        cpu.Step();

        Assert.Equal(0x80u, cpu.Context.ReadRegister(Gpr.Eax));
    }

    [Fact]
    public void Movsx_Byte()
    {
        XboxCpu cpu = CpuHarness.Create(0x0F, 0xBE, 0xC3);
        cpu.Context.WriteRegister(Gpr.Ebx, 0xFFFFFF80);

        cpu.Step();

        Assert.Equal(0xFFFFFF80u, cpu.Context.ReadRegister(Gpr.Eax));
    }

    [Fact]
    public void Movzx_Word()
    {
        XboxCpu cpu = CpuHarness.Create(0x0F, 0xB7, 0xC3);
        cpu.Context.WriteRegister(Gpr.Ebx, 0xFFFF8000);

        cpu.Step();

        Assert.Equal(0x8000u, cpu.Context.ReadRegister(Gpr.Eax));
    }

    [Fact]
    public void Movsx_Word()
    {
        XboxCpu cpu = CpuHarness.Create(0x0F, 0xBF, 0xC3);
        cpu.Context.WriteRegister(Gpr.Ebx, 0x00008000);

        cpu.Step();

        Assert.Equal(0xFFFF8000u, cpu.Context.ReadRegister(Gpr.Eax));
    }

    [Fact]
    public void PushaPopa_RoundTripsRegisters()
    {
        XboxCpu cpu = CpuHarness.Create(0x60, 0x61);
        cpu.Context.WriteRegister(Gpr.Eax, 0x11111111);
        cpu.Context.WriteRegister(Gpr.Ebx, 0x22222222);
        cpu.Context.WriteRegister(Gpr.Ecx, 0x33333333);
        cpu.Context.WriteRegister(Gpr.Edx, 0x44444444);

        cpu.Step();
        cpu.Step();

        Assert.Equal(0x11111111u, cpu.Context.ReadRegister(Gpr.Eax));
        Assert.Equal(0x22222222u, cpu.Context.ReadRegister(Gpr.Ebx));
        Assert.Equal(0x33333333u, cpu.Context.ReadRegister(Gpr.Ecx));
        Assert.Equal(0x44444444u, cpu.Context.ReadRegister(Gpr.Edx));
        Assert.Equal(0x8000u, cpu.Context.ReadRegister(Gpr.Esp));
    }

    [Fact]
    public void Cpuid_Leaf0_ReturnsVendor()
    {
        XboxCpu cpu = CpuHarness.Create(0x0F, 0xA2);
        cpu.Context.WriteRegister(Gpr.Eax, 0);

        cpu.Step();

        Assert.Equal(2u, cpu.Context.ReadRegister(Gpr.Eax));
        Assert.Equal(0x756E6547u, cpu.Context.ReadRegister(Gpr.Ebx));
        Assert.Equal(0x6C65746Eu, cpu.Context.ReadRegister(Gpr.Ecx));
        Assert.Equal(0x49656E69u, cpu.Context.ReadRegister(Gpr.Edx));
    }

    [Fact]
    public void Cpuid_Leaf1_ReturnsPentium3Identity()
    {
        XboxCpu cpu = CpuHarness.Create(0x0F, 0xA2);
        cpu.Context.WriteRegister(Gpr.Eax, 1);

        cpu.Step();

        Assert.Equal(0x0000068Au, cpu.Context.ReadRegister(Gpr.Eax));
        Assert.Equal(0u, cpu.Context.ReadRegister(Gpr.Ebx));
        Assert.Equal(0x0383F9FFu, cpu.Context.ReadRegister(Gpr.Edx));
    }

    [Fact]
    public void Cpuid_Leaf2_ReturnsCacheInfo()
    {
        XboxCpu cpu = CpuHarness.Create(0x0F, 0xA2);
        cpu.Context.WriteRegister(Gpr.Eax, 2);

        cpu.Step();

        Assert.Equal(0x03020101u, cpu.Context.ReadRegister(Gpr.Eax));
        Assert.Equal(0x0C040841u, cpu.Context.ReadRegister(Gpr.Edx));
    }

    [Fact]
    public void Cpuid_UnknownLeaf_ClampsToLeaf2()
    {
        XboxCpu cpu = CpuHarness.Create(0x0F, 0xA2);
        cpu.Context.WriteRegister(Gpr.Eax, 0x10);

        cpu.Step();

        Assert.Equal(0x03020101u, cpu.Context.ReadRegister(Gpr.Eax));
    }

    [Fact]
    public void Cpuid_ExtendedLeaf_ReportsNoExtendedSupport()
    {
        XboxCpu cpu = CpuHarness.Create(0x0F, 0xA2);
        cpu.Context.WriteRegister(Gpr.Eax, 0x80000000u);

        cpu.Step();

        Assert.Equal(0u, cpu.Context.ReadRegister(Gpr.Eax));
        Assert.Equal(0x756E6547u, cpu.Context.ReadRegister(Gpr.Ebx));
        Assert.Equal(0x6C65746Eu, cpu.Context.ReadRegister(Gpr.Ecx));
        Assert.Equal(0x49656E69u, cpu.Context.ReadRegister(Gpr.Edx));
    }

    [Fact]
    public void Rdtsc_ReturnsTimeStampCounter()
    {
        XboxCpu cpu = CpuHarness.Create(0x0F, 0x31);
        cpu.Context.Tsc = 0x0000000100000000;

        cpu.Step();

        Assert.Equal(1u, cpu.Context.ReadRegister(Gpr.Eax));
        Assert.Equal(1u, cpu.Context.ReadRegister(Gpr.Edx));
    }

    [Fact]
    public void Rdmsr_PowerOn_ReturnsQuirk()
    {
        XboxCpu cpu = CpuHarness.Create(0x0F, 0x32);
        cpu.Context.WriteRegister(Gpr.Ecx, 0x2A);

        cpu.Step();

        Assert.Equal(0xC5040000u, cpu.Context.ReadRegister(Gpr.Eax));
        Assert.Equal(0u, cpu.Context.ReadRegister(Gpr.Edx));
    }

    [Fact]
    public void Wrmsr_Then_Rdmsr_RoundTrips()
    {
        XboxCpu cpu = CpuHarness.Create(0x0F, 0x30, 0x0F, 0x32);
        cpu.Context.WriteRegister(Gpr.Ecx, 0x174);
        cpu.Context.WriteRegister(Gpr.Eax, 0x00000008);

        cpu.Step();
        cpu.Context.WriteRegister(Gpr.Eax, 0);
        cpu.Context.WriteRegister(Gpr.Edx, 0);
        cpu.Step();

        Assert.Equal(8u, cpu.Context.ReadRegister(Gpr.Eax));
    }

    [Fact]
    public void Setcc_SetsByteFromCondition()
    {
        XboxCpu cpu = CpuHarness.Create(0x0F, 0x94, 0xC0, 0x0F, 0x95, 0xC3);
        cpu.Context.Eflags = FlagBits.ZF;

        cpu.Step();
        Assert.Equal(1u, cpu.Context.ReadRegister8(0));

        cpu.Step();
        Assert.Equal(0u, cpu.Context.ReadRegister8(3));
    }

    [Fact]
    public void Cmovcc_MovesWhenConditionHolds()
    {
        XboxCpu cpu = CpuHarness.Create(0x0F, 0x44, 0xC3);
        cpu.Context.WriteRegister(Gpr.Eax, 0x11111111);
        cpu.Context.WriteRegister(Gpr.Ebx, 0x22222222);
        cpu.Context.Eflags = FlagBits.ZF;

        cpu.Step();

        Assert.Equal(0x22222222u, cpu.Context.ReadRegister(Gpr.Eax));
    }

    [Fact]
    public void Cmovcc_DoesNotMoveWhenConditionFails()
    {
        XboxCpu cpu = CpuHarness.Create(0x0F, 0x44, 0xC3);
        cpu.Context.WriteRegister(Gpr.Eax, 0x11111111);
        cpu.Context.WriteRegister(Gpr.Ebx, 0x22222222);
        cpu.Context.Eflags = 0;

        cpu.Step();

        Assert.Equal(0x11111111u, cpu.Context.ReadRegister(Gpr.Eax));
    }

    [Fact]
    public void Arpl_RaisesRplOfDestination()
    {
        XboxCpu cpu = CpuHarness.Create(0x63, 0xC8);
        cpu.Context.WriteRegister(Gpr.Eax, 0xFFFF0008);
        cpu.Context.WriteRegister(Gpr.Ecx, 0x0003);

        cpu.Step();

        Assert.Equal(0xFFFF000Bu, cpu.Context.ReadRegister(Gpr.Eax));
        Assert.NotEqual(0u, cpu.Context.Eflags & FlagBits.ZF);
    }

    [Fact]
    public void Arpl_DoesNotLowerRpl()
    {
        XboxCpu cpu = CpuHarness.Create(0x63, 0xC8);
        cpu.Context.WriteRegister(Gpr.Eax, 0x000B);
        cpu.Context.WriteRegister(Gpr.Ecx, 0x0003);
        cpu.Context.Eflags |= FlagBits.ZF;

        cpu.Step();

        Assert.Equal(0x000Bu, cpu.Context.ReadRegister(Gpr.Eax));
        Assert.Equal(0u, cpu.Context.Eflags & FlagBits.ZF);
    }

    [Fact]
    public void Bswap_ReversesByteOrder()
    {
        XboxCpu cpu = CpuHarness.Create(0x0F, 0xC8);
        cpu.Context.WriteRegister(Gpr.Eax, 0x11223344);

        cpu.Step();

        Assert.Equal(0x44332211u, cpu.Context.ReadRegister(Gpr.Eax));
    }

    [Fact]
    public void Ltr_And_Str_RoundTrip()
    {
        XboxCpu cpu = CpuHarness.Create(0x0F, 0x00, 0xD8, 0x0F, 0x00, 0xC8);
        cpu.Context.WriteRegister(Gpr.Eax, 0x18);

        cpu.Step();

        Assert.Equal(0x18, cpu.Context.Tr);

        cpu.Step();

        Assert.Equal(0x18u, cpu.Context.ReadRegister(Gpr.Eax));
    }

    [Fact]
    public void Cbw_SignExtendsNegativeByte()
    {
        XboxCpu cpu = CpuHarness.Create(0x66, 0x98);
        cpu.Context.WriteRegister(Gpr.Eax, 0x12345680);
        cpu.Context.Eflags |= FlagBits.CF;

        cpu.Step();

        Assert.Equal(0x1234FF80u, cpu.Context.ReadRegister(Gpr.Eax));
        Assert.Equal(FlagBits.CF, cpu.Context.Eflags & FlagBits.CF);
    }

    [Fact]
    public void Cbw_ZeroExtendsPositiveByte()
    {
        XboxCpu cpu = CpuHarness.Create(0x66, 0x98);
        cpu.Context.WriteRegister(Gpr.Eax, 0xABCD007F);

        cpu.Step();

        Assert.Equal(0xABCD007Fu, cpu.Context.ReadRegister(Gpr.Eax));
    }

    [Fact]
    public void Cwde_SignExtendsNegativeWord()
    {
        XboxCpu cpu = CpuHarness.Create(0x98);
        cpu.Context.WriteRegister(Gpr.Eax, 0x12348000);

        cpu.Step();

        Assert.Equal(0xFFFF8000u, cpu.Context.ReadRegister(Gpr.Eax));
    }

    [Fact]
    public void Cwde_ZeroExtendsPositiveWord()
    {
        XboxCpu cpu = CpuHarness.Create(0x98);
        cpu.Context.WriteRegister(Gpr.Eax, 0x12347FFF);

        cpu.Step();

        Assert.Equal(0x00007FFFu, cpu.Context.ReadRegister(Gpr.Eax));
    }

    [Fact]
    public void Cdq_SignExtendsNegativeEax()
    {
        XboxCpu cpu = CpuHarness.Create(0x99);
        cpu.Context.WriteRegister(Gpr.Eax, 0xFF676980);
        cpu.Context.WriteRegister(Gpr.Edx, 0x12345678);
        cpu.Context.Eflags |= FlagBits.ZF;

        cpu.Step();

        Assert.Equal(0xFF676980u, cpu.Context.ReadRegister(Gpr.Eax));
        Assert.Equal(0xFFFFFFFFu, cpu.Context.ReadRegister(Gpr.Edx));
        Assert.Equal(FlagBits.ZF, cpu.Context.Eflags & FlagBits.ZF);
    }

    [Fact]
    public void Cdq_ZeroExtendsPositiveEax()
    {
        XboxCpu cpu = CpuHarness.Create(0x99);
        cpu.Context.WriteRegister(Gpr.Eax, 0x12345678);
        cpu.Context.WriteRegister(Gpr.Edx, 0xFFFFFFFF);

        cpu.Step();

        Assert.Equal(0u, cpu.Context.ReadRegister(Gpr.Edx));
    }

    [Fact]
    public void Cwd_SignExtendsNegativeAx()
    {
        XboxCpu cpu = CpuHarness.Create(0x66, 0x99);
        cpu.Context.WriteRegister(Gpr.Eax, 0x00008000);
        cpu.Context.WriteRegister(Gpr.Edx, 0xABCD1234);

        cpu.Step();

        Assert.Equal(0xABCDFFFFu, cpu.Context.ReadRegister(Gpr.Edx));
    }

    [Fact]
    public void Cwd_ZeroExtendsPositiveAx()
    {
        XboxCpu cpu = CpuHarness.Create(0x66, 0x99);
        cpu.Context.WriteRegister(Gpr.Eax, 0x00007FFF);
        cpu.Context.WriteRegister(Gpr.Edx, 0xABCD1234);

        cpu.Step();

        Assert.Equal(0xABCD0000u, cpu.Context.ReadRegister(Gpr.Edx));
    }
}
