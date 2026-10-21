using System;
using Xunit;

namespace XGrid.Cpu.Tests;

public sealed class SseTests
{
    private const uint Data = 0x2000;
    private const uint Data2 = 0x2100;

    private static (ulong Low, ulong High) Floats(float x, float y, float z, float w)
    {
        ulong low = BitConverter.SingleToUInt32Bits(x) | ((ulong)BitConverter.SingleToUInt32Bits(y) << 32);
        ulong high = BitConverter.SingleToUInt32Bits(z) | ((ulong)BitConverter.SingleToUInt32Bits(w) << 32);
        return (low, high);
    }

    private static float Xmm(XboxCpu cpu, int register, int index)
    {
        return BitConverter.UInt32BitsToSingle(cpu.Context.ReadXmmElement(register, index));
    }

    [Fact]
    public void Movups_RegisterToRegister()
    {
        XboxCpu cpu = CpuHarness.Create(0x0F, 0x10, 0xC1);
        cpu.Context.WriteXmm128(1, 0x1122334455667788UL, 0x99AABBCCDDEEFF00UL);

        cpu.Step();

        Assert.Equal((0x1122334455667788UL, 0x99AABBCCDDEEFF00UL), cpu.Context.ReadXmm128(0));
    }

    [Fact]
    public void Movups_StoreAndLoadMemory()
    {
        XboxCpu cpu = CpuHarness.Create(
            0x0F, 0x11, 0x05, 0x00, 0x20, 0x00, 0x00,
            0x0F, 0x10, 0x0D, 0x00, 0x20, 0x00, 0x00);
        cpu.Context.WriteXmm128(0, 0x1122334455667788UL, 0x99AABBCCDDEEFF00UL);

        cpu.Step();
        cpu.Step();

        Assert.Equal((0x1122334455667788UL, 0x99AABBCCDDEEFF00UL), cpu.Context.ReadXmm128(1));
    }

    [Fact]
    public void Addps_AddsElements()
    {
        XboxCpu cpu = CpuHarness.Create(0x0F, 0x58, 0xC1);
        cpu.Context.WriteXmm128(0, Floats(1, 2, 3, 4).Low, Floats(1, 2, 3, 4).High);
        cpu.Context.WriteXmm128(1, Floats(5, 6, 7, 8).Low, Floats(5, 6, 7, 8).High);

        cpu.Step();

        Assert.Equal(6f, Xmm(cpu, 0, 0));
        Assert.Equal(8f, Xmm(cpu, 0, 1));
        Assert.Equal(10f, Xmm(cpu, 0, 2));
        Assert.Equal(12f, Xmm(cpu, 0, 3));
    }

    [Fact]
    public void Addss_PreservesUpperElements()
    {
        XboxCpu cpu = CpuHarness.Create(0xF3, 0x0F, 0x58, 0xC1);
        cpu.Context.WriteXmm128(0, Floats(1, 2, 3, 4).Low, Floats(1, 2, 3, 4).High);
        cpu.Context.WriteXmm128(1, Floats(5, 6, 7, 8).Low, Floats(5, 6, 7, 8).High);

        cpu.Step();

        Assert.Equal(6f, Xmm(cpu, 0, 0));
        Assert.Equal(2f, Xmm(cpu, 0, 1));
        Assert.Equal(3f, Xmm(cpu, 0, 2));
        Assert.Equal(4f, Xmm(cpu, 0, 3));
    }

    [Fact]
    public void Subps_SubtractsElements()
    {
        XboxCpu cpu = CpuHarness.Create(0x0F, 0x5C, 0xC1);
        cpu.Context.WriteXmm128(0, Floats(5, 6, 7, 8).Low, Floats(5, 6, 7, 8).High);
        cpu.Context.WriteXmm128(1, Floats(1, 2, 3, 4).Low, Floats(1, 2, 3, 4).High);

        cpu.Step();

        Assert.Equal(4f, Xmm(cpu, 0, 0));
        Assert.Equal(4f, Xmm(cpu, 0, 3));
    }

    [Fact]
    public void Mulps_MultipliesElements()
    {
        XboxCpu cpu = CpuHarness.Create(0x0F, 0x59, 0xC1);
        cpu.Context.WriteXmm128(0, Floats(2, 3, 4, 5).Low, Floats(2, 3, 4, 5).High);
        cpu.Context.WriteXmm128(1, Floats(10, 10, 10, 10).Low, Floats(10, 10, 10, 10).High);

        cpu.Step();

        Assert.Equal(20f, Xmm(cpu, 0, 0));
        Assert.Equal(50f, Xmm(cpu, 0, 3));
    }

    [Fact]
    public void Divps_DividesElements()
    {
        XboxCpu cpu = CpuHarness.Create(0x0F, 0x5E, 0xC1);
        cpu.Context.WriteXmm128(0, Floats(8, 9, 10, 11).Low, Floats(8, 9, 10, 11).High);
        cpu.Context.WriteXmm128(1, Floats(2, 3, 4, 5).Low, Floats(2, 3, 4, 5).High);

        cpu.Step();

        Assert.Equal(4f, Xmm(cpu, 0, 0));
        Assert.Equal(3f, Xmm(cpu, 0, 1));
        Assert.Equal(2.5f, Xmm(cpu, 0, 2));
        Assert.Equal(2.2f, Xmm(cpu, 0, 3));
    }

    [Fact]
    public void Minps_ReturnsSourceForNan()
    {
        XboxCpu cpu = CpuHarness.Create(0x0F, 0x5D, 0xC1);
        cpu.Context.WriteXmm128(0, Floats(float.NaN, 1, 1, 1).Low, Floats(float.NaN, 1, 1, 1).High);
        cpu.Context.WriteXmm128(1, Floats(5, 5, 5, 5).Low, Floats(5, 5, 5, 5).High);

        cpu.Step();

        Assert.Equal(5f, Xmm(cpu, 0, 0));
    }

    [Fact]
    public void Maxps_SelectsMaximum()
    {
        XboxCpu cpu = CpuHarness.Create(0x0F, 0x5F, 0xC1);
        cpu.Context.WriteXmm128(0, Floats(1, 9, 3, 9).Low, Floats(1, 9, 3, 9).High);
        cpu.Context.WriteXmm128(1, Floats(5, 2, 7, 2).Low, Floats(5, 2, 7, 2).High);

        cpu.Step();

        Assert.Equal(5f, Xmm(cpu, 0, 0));
        Assert.Equal(9f, Xmm(cpu, 0, 1));
        Assert.Equal(7f, Xmm(cpu, 0, 2));
        Assert.Equal(9f, Xmm(cpu, 0, 3));
    }

    // MINPS/MAXPS return the source operand when the two values compare equal,
    // so a ±0 pair keeps the source's sign instead of the destination's.
    [Fact]
    public void Maxps_EqualZeros_ReturnsSourceSign()
    {
        XboxCpu cpu = CpuHarness.Create(0x0F, 0x5F, 0xC1);
        cpu.Context.WriteXmm128(0, Floats(0f, 0f, 0f, 0f).Low, Floats(0f, 0f, 0f, 0f).High);
        cpu.Context.WriteXmm128(1, Floats(-0f, -0f, -0f, -0f).Low, Floats(-0f, -0f, -0f, -0f).High);

        cpu.Step();

        Assert.Equal(0x80000000u, cpu.Context.ReadXmmElement(0, 0));
    }

    [Fact]
    public void Minps_EqualZeros_ReturnsSourceSign()
    {
        XboxCpu cpu = CpuHarness.Create(0x0F, 0x5D, 0xC1);
        cpu.Context.WriteXmm128(0, Floats(-0f, -0f, -0f, -0f).Low, Floats(-0f, -0f, -0f, -0f).High);
        cpu.Context.WriteXmm128(1, Floats(0f, 0f, 0f, 0f).Low, Floats(0f, 0f, 0f, 0f).High);

        cpu.Step();

        Assert.Equal(0x00000000u, cpu.Context.ReadXmmElement(0, 0));
    }

    [Fact]
    public void Sqrtps_ComputesRoots()
    {
        XboxCpu cpu = CpuHarness.Create(0x0F, 0x51, 0xC1);
        cpu.Context.WriteXmm128(1, Floats(4, 9, 16, 25).Low, Floats(4, 9, 16, 25).High);

        cpu.Step();

        Assert.Equal(2f, Xmm(cpu, 0, 0));
        Assert.Equal(3f, Xmm(cpu, 0, 1));
        Assert.Equal(4f, Xmm(cpu, 0, 2));
        Assert.Equal(5f, Xmm(cpu, 0, 3));
    }

    [Fact]
    public void Rcpps_ComputesReciprocals()
    {
        XboxCpu cpu = CpuHarness.Create(0x0F, 0x53, 0xC1);
        cpu.Context.WriteXmm128(1, Floats(2, 4, 0.5f, 1).Low, Floats(2, 4, 0.5f, 1).High);

        cpu.Step();

        Assert.Equal(0.5f, Xmm(cpu, 0, 0));
        Assert.Equal(0.25f, Xmm(cpu, 0, 1));
        Assert.Equal(2f, Xmm(cpu, 0, 2));
        Assert.Equal(1f, Xmm(cpu, 0, 3));
    }

    [Fact]
    public void Rsqrtps_ComputesReciprocalRoots()
    {
        XboxCpu cpu = CpuHarness.Create(0x0F, 0x52, 0xC1);
        cpu.Context.WriteXmm128(1, Floats(4, 16, 1, 0.25f).Low, Floats(4, 16, 1, 0.25f).High);

        cpu.Step();

        Assert.Equal(0.5f, Xmm(cpu, 0, 0));
        Assert.Equal(0.25f, Xmm(cpu, 0, 1));
        Assert.Equal(1f, Xmm(cpu, 0, 2));
        Assert.Equal(2f, Xmm(cpu, 0, 3));
    }

    [Fact]
    public void Xorps_XorsBits()
    {
        XboxCpu cpu = CpuHarness.Create(0x0F, 0x57, 0xC1);
        cpu.Context.WriteXmm128(0, 0xFF00FF00FF00FF00UL, 0x00FF00FF00FF00FFUL);
        cpu.Context.WriteXmm128(1, 0x0FF00FF00FF00FF0UL, 0xF00FF00FF00FF00FUL);

        cpu.Step();

        Assert.Equal((0xF0F0F0F0F0F0F0F0UL, 0xF0F0F0F0F0F0F0F0UL), cpu.Context.ReadXmm128(0));
    }

    [Fact]
    public void Cmpps_EqualProducesMask()
    {
        XboxCpu cpu = CpuHarness.Create(0x0F, 0xC2, 0xC1, 0x00);
        cpu.Context.WriteXmm128(0, Floats(1, 2, 3, 4).Low, Floats(1, 2, 3, 4).High);
        cpu.Context.WriteXmm128(1, Floats(1, 9, 3, 9).Low, Floats(1, 9, 3, 9).High);

        cpu.Step();

        Assert.Equal(0xFFFFFFFFu, cpu.Context.ReadXmmElement(0, 0));
        Assert.Equal(0u, cpu.Context.ReadXmmElement(0, 1));
        Assert.Equal(0xFFFFFFFFu, cpu.Context.ReadXmmElement(0, 2));
        Assert.Equal(0u, cpu.Context.ReadXmmElement(0, 3));
    }

    [Fact]
    public void Cmpps_LessThanProducesMask()
    {
        XboxCpu cpu = CpuHarness.Create(0x0F, 0xC2, 0xC1, 0x01);
        cpu.Context.WriteXmm128(0, Floats(1, 2, 3, 4).Low, Floats(1, 2, 3, 4).High);
        cpu.Context.WriteXmm128(1, Floats(2, 2, 2, 2).Low, Floats(2, 2, 2, 2).High);

        cpu.Step();

        Assert.Equal(0xFFFFFFFFu, cpu.Context.ReadXmmElement(0, 0));
        Assert.Equal(0u, cpu.Context.ReadXmmElement(0, 1));
    }

    [Fact]
    public void Shufps_ShufflesElements()
    {
        XboxCpu cpu = CpuHarness.Create(0x0F, 0xC6, 0xC1, 0x1B);
        cpu.Context.WriteXmm128(0, Floats(1, 2, 3, 4).Low, Floats(1, 2, 3, 4).High);
        cpu.Context.WriteXmm128(1, Floats(5, 6, 7, 8).Low, Floats(5, 6, 7, 8).High);

        cpu.Step();

        Assert.Equal(4f, Xmm(cpu, 0, 0));
        Assert.Equal(3f, Xmm(cpu, 0, 1));
        Assert.Equal(6f, Xmm(cpu, 0, 2));
        Assert.Equal(5f, Xmm(cpu, 0, 3));
    }

    [Fact]
    public void Unpcklps_InterleavesLowElements()
    {
        XboxCpu cpu = CpuHarness.Create(0x0F, 0x14, 0xC1);
        cpu.Context.WriteXmm128(0, Floats(1, 2, 3, 4).Low, Floats(1, 2, 3, 4).High);
        cpu.Context.WriteXmm128(1, Floats(5, 6, 7, 8).Low, Floats(5, 6, 7, 8).High);

        cpu.Step();

        Assert.Equal(1f, Xmm(cpu, 0, 0));
        Assert.Equal(5f, Xmm(cpu, 0, 1));
        Assert.Equal(2f, Xmm(cpu, 0, 2));
        Assert.Equal(6f, Xmm(cpu, 0, 3));
    }

    [Fact]
    public void Unpckhps_InterleavesHighElements()
    {
        XboxCpu cpu = CpuHarness.Create(0x0F, 0x15, 0xC1);
        cpu.Context.WriteXmm128(0, Floats(1, 2, 3, 4).Low, Floats(1, 2, 3, 4).High);
        cpu.Context.WriteXmm128(1, Floats(5, 6, 7, 8).Low, Floats(5, 6, 7, 8).High);

        cpu.Step();

        Assert.Equal(3f, Xmm(cpu, 0, 0));
        Assert.Equal(7f, Xmm(cpu, 0, 1));
        Assert.Equal(4f, Xmm(cpu, 0, 2));
        Assert.Equal(8f, Xmm(cpu, 0, 3));
    }

    [Fact]
    public void Comiss_EqualSetsZeroFlag()
    {
        XboxCpu cpu = CpuHarness.Create(0x0F, 0x2F, 0xC1);
        cpu.Context.WriteXmm128(0, Floats(1, 2, 3, 4).Low, Floats(1, 2, 3, 4).High);
        cpu.Context.WriteXmm128(1, Floats(1, 9, 9, 9).Low, Floats(1, 9, 9, 9).High);

        cpu.Step();

        Assert.NotEqual(0u, (cpu.Context.Eflags & FlagBits.Zf));
        Assert.Equal(0u, (cpu.Context.Eflags & FlagBits.Cf));
        Assert.Equal(0u, (cpu.Context.Eflags & FlagBits.Pf));
    }

    [Fact]
    public void Ucomiss_UnorderedSetsAllFlags()
    {
        XboxCpu cpu = CpuHarness.Create(0x0F, 0x2E, 0xC1);
        cpu.Context.WriteXmm128(0, Floats(float.NaN, 0, 0, 0).Low, Floats(float.NaN, 0, 0, 0).High);
        cpu.Context.WriteXmm128(1, Floats(1, 0, 0, 0).Low, Floats(1, 0, 0, 0).High);

        cpu.Step();

        Assert.NotEqual(0u, (cpu.Context.Eflags & FlagBits.Zf));
        Assert.NotEqual(0u, (cpu.Context.Eflags & FlagBits.Pf));
        Assert.NotEqual(0u, (cpu.Context.Eflags & FlagBits.Cf));
    }

    [Fact]
    public void Cvtsi2ss_ConvertsInteger()
    {
        XboxCpu cpu = CpuHarness.Create(0xF3, 0x0F, 0x2A, 0xC0);
        cpu.Context.Eax = 5;
        cpu.Context.WriteXmm128(0, Floats(9, 9, 9, 9).Low, Floats(9, 9, 9, 9).High);

        cpu.Step();

        Assert.Equal(5f, Xmm(cpu, 0, 0));
        Assert.Equal(9f, Xmm(cpu, 0, 1));
    }

    [Fact]
    public void Cvtss2si_RoundsToNearestEven()
    {
        XboxCpu cpu = CpuHarness.Create(0xF3, 0x0F, 0x2D, 0xC0);
        cpu.Context.WriteXmm128(0, Floats(2.5f, 0, 0, 0).Low, Floats(2.5f, 0, 0, 0).High);

        cpu.Step();

        Assert.Equal(2u, cpu.Context.Eax);
    }

    [Fact]
    public void Cvttss2si_Truncates()
    {
        XboxCpu cpu = CpuHarness.Create(0xF3, 0x0F, 0x2C, 0xC0);
        cpu.Context.WriteXmm128(0, Floats(2.9f, 0, 0, 0).Low, Floats(2.9f, 0, 0, 0).High);

        cpu.Step();

        Assert.Equal(2u, cpu.Context.Eax);
    }

    [Fact]
    public void Cvtps2pi_ConvertsTwoFloats()
    {
        XboxCpu cpu = CpuHarness.Create(0x0F, 0x2D, 0xC1);
        cpu.Context.WriteXmm128(1, Floats(2.5f, 3.5f, 9, 9).Low, Floats(2.5f, 3.5f, 9, 9).High);

        cpu.Step();

        Assert.Equal(0x0000000400000002UL, cpu.Context.ReadMmx(0));
    }

    [Fact]
    public void Cvtpi2ps_ConvertsTwoIntegers()
    {
        XboxCpu cpu = CpuHarness.Create(0x0F, 0x2A, 0xC1);
        cpu.Context.WriteMmx(1, 0x0000000300000002UL);
        cpu.Context.WriteXmm128(0, Floats(9, 9, 9, 9).Low, Floats(9, 9, 9, 9).High);

        cpu.Step();

        Assert.Equal(2f, Xmm(cpu, 0, 0));
        Assert.Equal(3f, Xmm(cpu, 0, 1));
        Assert.Equal(9f, Xmm(cpu, 0, 2));
    }

    [Fact]
    public void Movmskps_CollectsSignBits()
    {
        XboxCpu cpu = CpuHarness.Create(0x0F, 0x50, 0xC1);
        cpu.Context.WriteXmm128(1, Floats(-1, 2, -3, 4).Low, Floats(-1, 2, -3, 4).High);

        cpu.Step();

        Assert.Equal(0x5u, cpu.Context.Eax);
    }

    [Fact]
    public void Movlps_LoadsLowHalf()
    {
        XboxCpu cpu = CpuHarness.Create(0x0F, 0x12, 0x05, 0x00, 0x20, 0x00, 0x00);
        cpu.Ram().Write64(Data,
            (ulong)(BitConverter.SingleToUInt32Bits(5f) | ((ulong)BitConverter.SingleToUInt32Bits(6f) << 32)));
        cpu.Context.WriteXmm128(0, Floats(1, 2, 3, 4).Low, Floats(1, 2, 3, 4).High);

        cpu.Step();

        Assert.Equal(5f, Xmm(cpu, 0, 0));
        Assert.Equal(6f, Xmm(cpu, 0, 1));
        Assert.Equal(3f, Xmm(cpu, 0, 2));
        Assert.Equal(4f, Xmm(cpu, 0, 3));
    }

    [Fact]
    public void Movhps_LoadsHighHalf()
    {
        XboxCpu cpu = CpuHarness.Create(0x0F, 0x16, 0x05, 0x00, 0x20, 0x00, 0x00);
        cpu.Ram().Write64(Data,
            (ulong)(BitConverter.SingleToUInt32Bits(5f) | ((ulong)BitConverter.SingleToUInt32Bits(6f) << 32)));
        cpu.Context.WriteXmm128(0, Floats(1, 2, 3, 4).Low, Floats(1, 2, 3, 4).High);

        cpu.Step();

        Assert.Equal(1f, Xmm(cpu, 0, 0));
        Assert.Equal(2f, Xmm(cpu, 0, 1));
        Assert.Equal(5f, Xmm(cpu, 0, 2));
        Assert.Equal(6f, Xmm(cpu, 0, 3));
    }

    [Fact]
    public void Movhlps_CopiesHighToLow()
    {
        XboxCpu cpu = CpuHarness.Create(0x0F, 0x12, 0xC1);
        cpu.Context.WriteXmm128(0, Floats(1, 2, 3, 4).Low, Floats(1, 2, 3, 4).High);
        cpu.Context.WriteXmm128(1, Floats(5, 6, 7, 8).Low, Floats(5, 6, 7, 8).High);

        cpu.Step();

        Assert.Equal(7f, Xmm(cpu, 0, 0));
        Assert.Equal(8f, Xmm(cpu, 0, 1));
        Assert.Equal(3f, Xmm(cpu, 0, 2));
    }

    [Fact]
    public void Movlhps_CopiesLowToHigh()
    {
        XboxCpu cpu = CpuHarness.Create(0x0F, 0x16, 0xC1);
        cpu.Context.WriteXmm128(0, Floats(1, 2, 3, 4).Low, Floats(1, 2, 3, 4).High);
        cpu.Context.WriteXmm128(1, Floats(5, 6, 7, 8).Low, Floats(5, 6, 7, 8).High);

        cpu.Step();

        Assert.Equal(1f, Xmm(cpu, 0, 0));
        Assert.Equal(5f, Xmm(cpu, 0, 2));
        Assert.Equal(6f, Xmm(cpu, 0, 3));
    }

    [Fact]
    public void Movss_LoadPreservesUpperElements()
    {
        XboxCpu cpu = CpuHarness.Create(0xF3, 0x0F, 0x10, 0x05, 0x00, 0x20, 0x00, 0x00);
        cpu.Ram().Write32(Data, BitConverter.SingleToUInt32Bits(9f));
        cpu.Context.WriteXmm128(0, Floats(1, 2, 3, 4).Low, Floats(1, 2, 3, 4).High);

        cpu.Step();

        Assert.Equal(9f, Xmm(cpu, 0, 0));
        Assert.Equal(2f, Xmm(cpu, 0, 1));
        Assert.Equal(4f, Xmm(cpu, 0, 3));
    }

    [Fact]
    public void Movss_StoreWritesLowElement()
    {
        XboxCpu cpu = CpuHarness.Create(0xF3, 0x0F, 0x11, 0x05, 0x00, 0x20, 0x00, 0x00);
        cpu.Context.WriteXmm128(0, Floats(9, 2, 3, 4).Low, Floats(9, 2, 3, 4).High);

        cpu.Step();

        Assert.Equal(9f, BitConverter.UInt32BitsToSingle(cpu.Ram().Read32(Data)));
    }

    [Fact]
    public void Ldmxcsr_And_Stmxcsr_RoundTrip()
    {
        XboxCpu cpu = CpuHarness.Create(
            0x0F, 0xAE, 0x15, 0x00, 0x20, 0x00, 0x00,
            0x0F, 0xAE, 0x1D, 0x00, 0x21, 0x00, 0x00);
        cpu.Ram().Write32(Data, 0x5F80);

        cpu.Step();
        cpu.Step();

        Assert.Equal(0x5F80u, cpu.Context.Mxcsr);
        Assert.Equal(0x5F80u, cpu.Ram().Read32(Data2));
    }

    [Fact]
    public void Fxsave_Fxrstor_RoundTrip()
    {
        XboxCpu cpu = CpuHarness.Create(
            0x0F, 0xAE, 0x05, 0x00, 0x20, 0x00, 0x00,
            0x0F, 0xAE, 0x0D, 0x00, 0x20, 0x00, 0x00);
        cpu.Context.Fpu.SetRegister(0, Fpu80.One);
        cpu.Context.WriteXmm128(0, 0x1122334455667788UL, 0x99AABBCCDDEEFF00UL);
        cpu.Context.Mxcsr = 0x5F80;

        cpu.Step();

        cpu.Context.Fpu.Init();
        cpu.Context.WriteXmm128(0, 0, 0);
        cpu.Context.Mxcsr = 0x1F80;

        cpu.Step();

        Assert.Equal(Fpu80.One, cpu.Context.Fpu.GetRegister(0));
        Assert.Equal((0x1122334455667788UL, 0x99AABBCCDDEEFF00UL), cpu.Context.ReadXmm128(0));
        Assert.Equal(0x5F80u, cpu.Context.Mxcsr);
    }

    [Fact]
    public void Movntps_StoresFullRegister()
    {
        XboxCpu cpu = CpuHarness.Create(0x0F, 0x2B, 0x05, 0x00, 0x20, 0x00, 0x00);
        cpu.Context.WriteXmm128(0, 0x1122334455667788UL, 0x99AABBCCDDEEFF00UL);

        cpu.Step();

        Assert.Equal(0x1122334455667788UL, cpu.Ram().Read64(Data));
        Assert.Equal(0x99AABBCCDDEEFF00UL, cpu.Ram().Read64(Data + 8));
    }

    [Fact]
    public void Prefetch_IsNoOp()
    {
        XboxCpu cpu = CpuHarness.Create(0x0F, 0x18, 0x0D, 0x00, 0x20, 0x00, 0x00);

        cpu.Step();

        Assert.Equal(CpuHarness.CodeAddress + 7, cpu.Context.Eip);
    }
}
