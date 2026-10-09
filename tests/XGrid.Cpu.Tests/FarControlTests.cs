using Xunit;

namespace XGrid.Cpu.Tests;

public sealed class FarControlTests
{
    [Fact]
    public void MovSreg_RealMode_LoadsSelectorShiftedBase()
    {
        XboxCpu cpu = CpuHarness.Create(0x8E, 0xC0);
        cpu.Context.Cr0 = 0;
        cpu.Context.Eax = 0x1000;

        cpu.Step();

        Assert.Equal(0x10000u, cpu.Context.GetSegmentBase(SegmentRegister.Es));
        Assert.Equal(0x1000, cpu.Context.GetSegmentSelector(SegmentRegister.Es));
    }

    [Fact]
    public void MovSreg_ProtectedMode_ReadsGdtDescriptor()
    {
        XboxCpu cpu = CpuHarness.Create(0x8E, 0xC0);
        cpu.Context.Gdtr.Base = 0x4000;
        cpu.Context.Gdtr.Limit = 0x100;
        cpu.Ram().Write32(0x4008, 0x50000000);
        cpu.Ram().Write32(0x400C, 0x12000034);
        cpu.Context.Eax = 0x0008;

        cpu.Step();

        Assert.Equal(0x12345000u, cpu.Context.GetSegmentBase(SegmentRegister.Es));
    }

    [Fact]
    public void MovSregRead_StoresSelector()
    {
        XboxCpu cpu = CpuHarness.Create(0x8C, 0xC0);

        cpu.Step();

        Assert.Equal(0x10u, cpu.Context.Eax & 0xFFFF);
    }

    [Fact]
    public void FarJump_RealMode_SetsSelectorBase()
    {
        XboxCpu cpu = CpuHarness.Create(0xEA, 0x00, 0x30, 0x00, 0x10);
        cpu.Context.Cr0 = 0;

        cpu.Step();

        Assert.Equal(0x3000u, cpu.Context.Eip);
        Assert.Equal(0x10000u, cpu.Context.GetSegmentBase(SegmentRegister.Cs));
    }

    [Fact]
    public void FarJump_ProtectedMode_UsesGdtBase()
    {
        XboxCpu cpu = CpuHarness.Create(0xEA, 0x00, 0x00, 0x20, 0x00, 0x08, 0x00);
        cpu.Context.Gdtr.Base = 0x4000;
        cpu.Ram().Write32(0x4008, 0x50000000);
        cpu.Ram().Write32(0x400C, 0x12000034);

        cpu.Step();

        Assert.Equal(0x00200000u, cpu.Context.Eip);
        Assert.Equal(0x12345000u, cpu.Context.GetSegmentBase(SegmentRegister.Cs));
        Assert.Equal(0x0008, cpu.Context.GetSegmentSelector(SegmentRegister.Cs));
    }

    [Fact]
    public void FarCall_PushesReturnFrame()
    {
        XboxCpu cpu = CpuHarness.Create(0x9A, 0x00, 0x10, 0x00, 0x00, 0x08, 0x00);

        cpu.Step();

        Assert.Equal(0x1000u, cpu.Context.Eip);
        Assert.Equal(CpuHarness.StackAddress - 8u, cpu.Context.Esp);
        Assert.Equal(0x1007u, cpu.Ram().Read32(CpuHarness.StackAddress - 8));
        Assert.Equal(0x0008, cpu.Ram().Read16(CpuHarness.StackAddress - 4));
    }

    [Fact]
    public void Retf_PopsReturnFrame()
    {
        XboxCpu cpu = CpuHarness.Create(0xCB);
        cpu.Ram().Write32(CpuHarness.StackAddress, 0x2222);
        cpu.Ram().Write16(CpuHarness.StackAddress + 4, 0x0008);

        cpu.Step();

        Assert.Equal(0x2222u, cpu.Context.Eip);
        Assert.Equal(CpuHarness.StackAddress + 8u, cpu.Context.Esp);
    }

    [Fact]
    public void PushPopSegment_RoundTrips()
    {
        XboxCpu cpu = CpuHarness.Create(0x06, 0x07);

        cpu.Step();
        Assert.Equal(CpuHarness.StackAddress - 4u, cpu.Context.Esp);
        Assert.Equal(0x10u, cpu.Ram().Read32(CpuHarness.StackAddress - 4) & 0xFFFF);

        cpu.Step();
        Assert.Equal(CpuHarness.StackAddress, cpu.Context.Esp);
        Assert.Equal(0x10, cpu.Context.GetSegmentSelector(SegmentRegister.Es));
    }

    [Fact]
    public void PushPopFs_RoundTrips()
    {
        XboxCpu cpu = CpuHarness.Create(0x0F, 0xA0, 0x0F, 0xA1);
        cpu.Context.SetSegment(SegmentRegister.Fs, 0x20, 0);

        cpu.Step();
        cpu.Step();

        Assert.Equal(0x20, cpu.Context.GetSegmentSelector(SegmentRegister.Fs));
    }

    [Fact]
    public void FarJumpImmediate_DecodesOperands()
    {
        DecodedInstruction instruction = Decoder.Decode([0xEA, 0x00, 0x00, 0x20, 0x00, 0x08, 0x00], 32, 32);

        Assert.Equal(Mnemonic.JmpFar, instruction.Mnemonic);
        Assert.Equal(0x00200000u, instruction.Operand1.Immediate);
        Assert.Equal(0x0008u, instruction.Operand2.Immediate);
    }

    [Fact]
    public void FarCallMemory_Decodes()
    {
        DecodedInstruction instruction = Decoder.Decode([0xFF, 0x1D, 0x00, 0x20, 0x00, 0x00], 32, 32);

        Assert.Equal(Mnemonic.CallFar, instruction.Mnemonic);
        Assert.Equal(OperandKind.Memory, instruction.Operand1.Kind);
    }

    [Fact]
    public void FarCallRegisterForm_IsUnsupported()
    {
        Assert.Equal(Mnemonic.Unsupported, Decoder.Decode([0xFF, 0xD8], 32, 32).Mnemonic);
    }
}
