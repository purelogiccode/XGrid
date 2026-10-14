using Xunit;

namespace XGrid.Cpu.Tests;

public sealed class InterpreterTests
{
    [Fact]
    public void Mov_Immediate_To_Register()
    {
        XboxCpu cpu = CpuHarness.Create(0xB8, 0x78, 0x56, 0x34, 0x12);

        cpu.Step();

        Assert.Equal(0x12345678u, cpu.Context.ReadRegister(Gpr.Eax));
        Assert.Equal(0x1005u, cpu.Context.Eip);
    }

    [Fact]
    public void Mov_Register_To_Register()
    {
        XboxCpu cpu = CpuHarness.Create(0x89, 0xD8);
        cpu.Context.WriteRegister(Gpr.Ebx, 0xCAFEBABE);

        cpu.Step();

        Assert.Equal(0xCAFEBABEu, cpu.Context.ReadRegister(Gpr.Eax));
    }

    [Fact]
    public void Mov_Moffs_Load()
    {
        XboxCpu cpu = CpuHarness.Create(0xA1, 0x00, 0x20, 0x00, 0x00);
        cpu.Bus().Write32(0x2000, 0xDEADBEEF);

        cpu.Step();

        Assert.Equal(0xDEADBEEFu, cpu.Context.ReadRegister(Gpr.Eax));
    }

    [Fact]
    public void Mov_Moffs_Store()
    {
        XboxCpu cpu = CpuHarness.Create(0xA3, 0x00, 0x20, 0x00, 0x00);
        cpu.Context.WriteRegister(Gpr.Eax, 0x11223344);

        cpu.Step();

        Assert.Equal(0x11223344u, cpu.Bus().Read32(0x2000));
    }

    [Fact]
    public void Mov_Byte_Registers()
    {
        XboxCpu cpu = CpuHarness.Create(0xB4, 0x12, 0xB0, 0x34);

        cpu.Step();
        cpu.Step();

        Assert.Equal(0x1234u, cpu.Context.ReadRegister(Gpr.Eax));
    }

    [Fact]
    public void Mov_Memory_Byte()
    {
        XboxCpu cpu = CpuHarness.Create(0xC6, 0x00, 0x42);
        cpu.Context.WriteRegister(Gpr.Eax, 0x2000);

        cpu.Step();

        Assert.Equal(0x42, cpu.Bus().Read8(0x2000));
    }

    [Fact]
    public void Mov_16Bit_PreservesHighBits()
    {
        XboxCpu cpu = CpuHarness.Create(0x66, 0xB8, 0x34, 0x12);
        cpu.Context.WriteRegister(Gpr.Eax, 0xFFFF0000);

        cpu.Step();

        Assert.Equal(0xFFFF1234u, cpu.Context.ReadRegister(Gpr.Eax));
    }

    [Fact]
    public void Push_And_Pop_Register()
    {
        XboxCpu cpu = CpuHarness.Create(0x50, 0x5B);
        cpu.Context.WriteRegister(Gpr.Eax, 0x12345678);

        cpu.Step();

        Assert.Equal(0x7FFCu, cpu.Context.ReadRegister(Gpr.Esp));

        cpu.Step();

        Assert.Equal(0x12345678u, cpu.Context.ReadRegister(Gpr.Ebx));
        Assert.Equal(0x8000u, cpu.Context.ReadRegister(Gpr.Esp));
    }

    [Fact]
    public void Push_Immediate_SignExtends()
    {
        XboxCpu cpu = CpuHarness.Create(0x6A, 0xFF);

        cpu.Step();

        Assert.Equal(0x7FFCu, cpu.Context.ReadRegister(Gpr.Esp));
        Assert.Equal(0xFFFFFFFFu, cpu.Bus().Read32(0x7FFC));
    }

    [Fact]
    public void Push_MemoryOperand()
    {
        XboxCpu cpu = CpuHarness.Create(0xFF, 0x75, 0xE8);
        cpu.Context.WriteRegister(Gpr.Ebp, 0x2000);
        cpu.Bus().Write32(0x1FE8, 0xCAFEBABE);

        cpu.Step();

        Assert.Equal(0x7FFCu, cpu.Context.ReadRegister(Gpr.Esp));
        Assert.Equal(0xCAFEBABEu, cpu.Bus().Read32(0x7FFC));
    }

    [Fact]
    public void Leave_RestoresStackAndFrame()
    {
        XboxCpu cpu = CpuHarness.Create(0xC9);
        cpu.Context.WriteRegister(Gpr.Ebp, 0x7FF0);
        cpu.Bus().Write32(0x7FF0, 0x12345678);

        cpu.Step();

        Assert.Equal(0x7FF4u, cpu.Context.ReadRegister(Gpr.Esp));
        Assert.Equal(0x12345678u, cpu.Context.ReadRegister(Gpr.Ebp));
    }

    [Fact]
    public void Enter_AllocatesFrame()
    {
        XboxCpu cpu = CpuHarness.Create(0xC8, 0x10, 0x00, 0x00);
        cpu.Context.WriteRegister(Gpr.Ebp, 0x8000);
        cpu.Context.WriteRegister(Gpr.Esp, 0x7F00);

        cpu.Step();

        Assert.Equal(0x7EFCu, cpu.Context.ReadRegister(Gpr.Ebp));
        Assert.Equal(0x7EECu, cpu.Context.ReadRegister(Gpr.Esp));
        Assert.Equal(0x8000u, cpu.Bus().Read32(0x7EFC));
    }

    [Fact]
    public void Enter_NestingOne_PushesFramePointer()
    {
        XboxCpu cpu = CpuHarness.Create(0xC8, 0x00, 0x00, 0x01);
        cpu.Context.WriteRegister(Gpr.Ebp, 0x8000);
        cpu.Context.WriteRegister(Gpr.Esp, 0x7F00);

        cpu.Step();

        Assert.Equal(0x7EFCu, cpu.Context.ReadRegister(Gpr.Ebp));
        Assert.Equal(0x7EF8u, cpu.Context.ReadRegister(Gpr.Esp));
        Assert.Equal(0x7EFCu, cpu.Bus().Read32(0x7EF8));
    }

    [Fact]
    public void Pop_MemoryOperand_WithFsOverride()
    {
        XboxCpu cpu = CpuHarness.Create(0x64, 0x8F, 0x05, 0x00, 0x00, 0x00, 0x00);
        cpu.Context.SetSegment(SegmentRegister.Fs, 0x10, 0x5000);
        cpu.Context.WriteRegister(Gpr.Esp, 0x7FF0);
        cpu.Bus().Write32(0x7FF0, 0xDEADBEEF);

        cpu.Step();

        Assert.Equal(0xDEADBEEFu, cpu.Bus().Read32(0x5000));
        Assert.Equal(0x7FF4u, cpu.Context.ReadRegister(Gpr.Esp));
    }

    [Fact]
    public void Xchg_SwapsRegisters()
    {
        XboxCpu cpu = CpuHarness.Create(0x93);
        cpu.Context.WriteRegister(Gpr.Eax, 0x11111111);
        cpu.Context.WriteRegister(Gpr.Ebx, 0x22222222);

        cpu.Step();

        Assert.Equal(0x22222222u, cpu.Context.ReadRegister(Gpr.Eax));
        Assert.Equal(0x11111111u, cpu.Context.ReadRegister(Gpr.Ebx));
    }

    [Fact]
    public void Lea_ComputesAddress()
    {
        XboxCpu cpu = CpuHarness.Create(0x8D, 0x44, 0x8B, 0x08);
        cpu.Context.WriteRegister(Gpr.Ebx, 0x1000);
        cpu.Context.WriteRegister(Gpr.Ecx, 0x10);

        cpu.Step();

        Assert.Equal(0x1048u, cpu.Context.ReadRegister(Gpr.Eax));
    }

    [Fact]
    public void Add_RegisterRegister()
    {
        XboxCpu cpu = CpuHarness.Create(0x01, 0xD8);
        cpu.Context.WriteRegister(Gpr.Eax, 5);
        cpu.Context.WriteRegister(Gpr.Ebx, 7);

        cpu.Step();

        Assert.Equal(12u, cpu.Context.ReadRegister(Gpr.Eax));
        Assert.False((cpu.Context.Eflags & FlagBits.Cf) != 0);
        Assert.False((cpu.Context.Eflags & FlagBits.Zf) != 0);
    }

    [Fact]
    public void Cmc_ComplementsCarryFlag()
    {
        XboxCpu cpu = CpuHarness.Create(0xF5, 0xF5);
        cpu.Context.Eflags &= ~FlagBits.Cf;

        cpu.Step();
        Assert.True((cpu.Context.Eflags & FlagBits.Cf) != 0);

        cpu.Step();
        Assert.False((cpu.Context.Eflags & FlagBits.Cf) != 0);
    }

    [Fact]
    public void Add_Overflow_SetsOverflowFlag()
    {
        XboxCpu cpu = CpuHarness.Create(0x01, 0xD8);
        cpu.Context.WriteRegister(Gpr.Eax, 0x7FFFFFFF);
        cpu.Context.WriteRegister(Gpr.Ebx, 1);

        cpu.Step();

        Assert.Equal(0x80000000u, cpu.Context.ReadRegister(Gpr.Eax));
        Assert.True((cpu.Context.Eflags & FlagBits.Of) != 0);
    }

    [Fact]
    public void Adc_UsesCarryIn()
    {
        XboxCpu cpu = CpuHarness.Create(0x11, 0xD8);
        cpu.Context.WriteRegister(Gpr.Eax, 1);
        cpu.Context.WriteRegister(Gpr.Ebx, 1);
        cpu.Context.Eflags = FlagBits.Cf;

        cpu.Step();

        Assert.Equal(3u, cpu.Context.ReadRegister(Gpr.Eax));
    }

    [Fact]
    public void Sub_ComputesResult()
    {
        XboxCpu cpu = CpuHarness.Create(0x29, 0xD8);
        cpu.Context.WriteRegister(Gpr.Eax, 10);
        cpu.Context.WriteRegister(Gpr.Ebx, 3);

        cpu.Step();

        Assert.Equal(7u, cpu.Context.ReadRegister(Gpr.Eax));
    }

    [Fact]
    public void Cmp_DoesNotWriteResult()
    {
        XboxCpu cpu = CpuHarness.Create(0x39, 0xD8);
        cpu.Context.WriteRegister(Gpr.Eax, 5);
        cpu.Context.WriteRegister(Gpr.Ebx, 5);

        cpu.Step();

        Assert.Equal(5u, cpu.Context.ReadRegister(Gpr.Eax));
        Assert.True((cpu.Context.Eflags & FlagBits.Zf) != 0);
    }

    [Fact]
    public void Inc_PreservesCarry()
    {
        XboxCpu cpu = CpuHarness.Create(0x40);
        cpu.Context.WriteRegister(Gpr.Eax, 0xFFFFFFFF);
        cpu.Context.Eflags = FlagBits.Cf;

        cpu.Step();

        Assert.Equal(0u, cpu.Context.ReadRegister(Gpr.Eax));
        Assert.True((cpu.Context.Eflags & FlagBits.Cf) != 0);
        Assert.True((cpu.Context.Eflags & FlagBits.Zf) != 0);
    }

    [Fact]
    public void Dec_Decrements()
    {
        XboxCpu cpu = CpuHarness.Create(0x48);
        cpu.Context.WriteRegister(Gpr.Eax, 1);

        cpu.Step();

        Assert.Equal(0u, cpu.Context.ReadRegister(Gpr.Eax));
        Assert.True((cpu.Context.Eflags & FlagBits.Zf) != 0);
    }

    [Fact]
    public void And_Or_Xor_Test()
    {
        XboxCpu cpu = CpuHarness.Create(0x21, 0xD8, 0x09, 0xD8, 0x31, 0xD8, 0x85, 0xD8);
        cpu.Context.WriteRegister(Gpr.Ebx, 0xFF00FF00);

        cpu.Context.WriteRegister(Gpr.Eax, 0xF0F0F0F0);
        cpu.Step();
        Assert.Equal(0xF000F000u, cpu.Context.ReadRegister(Gpr.Eax));

        cpu.Context.WriteRegister(Gpr.Eax, 0xF0F0F0F0);
        cpu.Step();
        Assert.Equal(0xFFF0FFF0u, cpu.Context.ReadRegister(Gpr.Eax));

        cpu.Context.WriteRegister(Gpr.Eax, 0xFFFFFFFF);
        cpu.Step();
        Assert.Equal(0x00FF00FFu, cpu.Context.ReadRegister(Gpr.Eax));

        cpu.Context.WriteRegister(Gpr.Eax, 0);
        cpu.Step();
        Assert.True((cpu.Context.Eflags & FlagBits.Zf) != 0);
    }

    [Fact]
    public void Not_And_Neg()
    {
        XboxCpu cpu = CpuHarness.Create(0xF7, 0xD0, 0xF7, 0xD8);

        cpu.Context.WriteRegister(Gpr.Eax, 0);
        cpu.Step();
        Assert.Equal(0xFFFFFFFFu, cpu.Context.ReadRegister(Gpr.Eax));

        cpu.Context.WriteRegister(Gpr.Eax, 1);
        cpu.Step();
        Assert.Equal(0xFFFFFFFFu, cpu.Context.ReadRegister(Gpr.Eax));
        Assert.True((cpu.Context.Eflags & FlagBits.Cf) != 0);
    }

    [Fact]
    public void Execute_StopsAtHlt()
    {
        XboxCpu cpu = CpuHarness.Create(0x90, 0xF4, 0x90);

        int executed = cpu.Execute(10);

        Assert.Equal(2, executed);
        Assert.True(cpu.Context.Halted);
        Assert.Equal(0x1002u, cpu.Context.Eip);
    }

    [Fact]
    public void UnsupportedInstruction_Throws()
    {
        XboxCpu cpu = CpuHarness.Create(0x0F, 0x0B);

        Assert.Throws<UnsupportedInstructionException>(cpu.Step);
    }

    [Fact]
    public void Xadd_Registers()
    {
        XboxCpu cpu = CpuHarness.Create(0x0F, 0xC1, 0xC8);
        cpu.Context.WriteRegister(Gpr.Eax, 5);
        cpu.Context.WriteRegister(Gpr.Ecx, 7);

        cpu.Step();

        Assert.Equal(12u, cpu.Context.ReadRegister(Gpr.Eax));
        Assert.Equal(5u, cpu.Context.ReadRegister(Gpr.Ecx));
    }

    [Fact]
    public void Xadd_Memory()
    {
        XboxCpu cpu = CpuHarness.Create(0x0F, 0xC1, 0x08);
        cpu.Bus().Write32(0x2000, 10);
        cpu.Context.WriteRegister(Gpr.Eax, 0x2000);
        cpu.Context.WriteRegister(Gpr.Ecx, 3);

        cpu.Step();

        Assert.Equal(13u, cpu.Bus().Read32(0x2000));
        Assert.Equal(10u, cpu.Context.ReadRegister(Gpr.Ecx));
    }

    [Fact]
    public void Cmpxchg_MemoryEqual_StoresSource()
    {
        XboxCpu cpu = CpuHarness.Create(0x0F, 0xB1, 0x0D, 0x00, 0x20, 0x00, 0x00);
        cpu.Bus().Write32(0x2000, 7);
        cpu.Context.WriteRegister(Gpr.Eax, 7);
        cpu.Context.WriteRegister(Gpr.Ecx, 9);

        cpu.Step();

        Assert.Equal(9u, cpu.Bus().Read32(0x2000));
        Assert.Equal(7u, cpu.Context.ReadRegister(Gpr.Eax));
        Assert.True((cpu.Context.Eflags & FlagBits.Zf) != 0);
    }

    [Fact]
    public void Cmpxchg_MemoryNotEqual_LoadsAccumulator()
    {
        XboxCpu cpu = CpuHarness.Create(0x0F, 0xB1, 0x0D, 0x00, 0x20, 0x00, 0x00);
        cpu.Bus().Write32(0x2000, 5);
        cpu.Context.WriteRegister(Gpr.Eax, 7);
        cpu.Context.WriteRegister(Gpr.Ecx, 9);

        cpu.Step();

        Assert.Equal(5u, cpu.Bus().Read32(0x2000));
        Assert.Equal(5u, cpu.Context.ReadRegister(Gpr.Eax));
        Assert.True((cpu.Context.Eflags & FlagBits.Zf) == 0);
    }

    [Fact]
    public void Cmpxchg8b_Equal_ExchangesPair()
    {
        XboxCpu cpu = CpuHarness.Create(0x0F, 0xC7, 0x0D, 0x00, 0x20, 0x00, 0x00);
        cpu.Bus().Write32(0x2000, 0x11111111);
        cpu.Bus().Write32(0x2004, 0x22222222);
        cpu.Context.WriteRegister(Gpr.Eax, 0x11111111);
        cpu.Context.WriteRegister(Gpr.Edx, 0x22222222);
        cpu.Context.WriteRegister(Gpr.Ebx, 0x33333333);
        cpu.Context.WriteRegister(Gpr.Ecx, 0x44444444);
        cpu.Context.Eflags = FlagBits.Cf | FlagBits.Sf;

        cpu.Step();

        Assert.Equal(0x33333333u, cpu.Bus().Read32(0x2000));
        Assert.Equal(0x44444444u, cpu.Bus().Read32(0x2004));
        Assert.True((cpu.Context.Eflags & FlagBits.Zf) != 0);
        Assert.True((cpu.Context.Eflags & FlagBits.Cf) != 0);
        Assert.True((cpu.Context.Eflags & FlagBits.Sf) != 0);
    }

    [Fact]
    public void Cmpxchg8b_NotEqual_LoadsPair()
    {
        XboxCpu cpu = CpuHarness.Create(0x0F, 0xC7, 0x0D, 0x00, 0x20, 0x00, 0x00);
        cpu.Bus().Write32(0x2000, 0x11111111);
        cpu.Bus().Write32(0x2004, 0x99999999);
        cpu.Context.WriteRegister(Gpr.Eax, 0x11111111);
        cpu.Context.WriteRegister(Gpr.Edx, 0x22222222);
        cpu.Context.WriteRegister(Gpr.Ebx, 0x33333333);
        cpu.Context.WriteRegister(Gpr.Ecx, 0x44444444);
        cpu.Context.Eflags = FlagBits.Zf | FlagBits.Cf;

        cpu.Step();

        Assert.Equal(0x11111111u, cpu.Bus().Read32(0x2000));
        Assert.Equal(0x99999999u, cpu.Bus().Read32(0x2004));
        Assert.Equal(0x11111111u, cpu.Context.ReadRegister(Gpr.Eax));
        Assert.Equal(0x99999999u, cpu.Context.ReadRegister(Gpr.Edx));
        Assert.True((cpu.Context.Eflags & FlagBits.Zf) == 0);
        Assert.True((cpu.Context.Eflags & FlagBits.Cf) != 0);
    }

    [Fact]
    public void Bsr_FindsHighestBit()
    {
        XboxCpu cpu = CpuHarness.Create(0x0F, 0xBD, 0xC1);
        cpu.Context.WriteRegister(Gpr.Ecx, 0x100);

        cpu.Step();

        Assert.Equal(8u, cpu.Context.ReadRegister(Gpr.Eax));
        Assert.True((cpu.Context.Eflags & FlagBits.Zf) == 0);
    }

    [Fact]
    public void Bsf_FindsLowestBit()
    {
        XboxCpu cpu = CpuHarness.Create(0x0F, 0xBC, 0xC1);
        cpu.Context.WriteRegister(Gpr.Ecx, 0x12);

        cpu.Step();

        Assert.Equal(1u, cpu.Context.ReadRegister(Gpr.Eax));
    }

    [Fact]
    public void Bsf_ZeroSourceSetsZeroFlag()
    {
        XboxCpu cpu = CpuHarness.Create(0x0F, 0xBC, 0xC1);
        cpu.Context.WriteRegister(Gpr.Eax, 0x99);
        cpu.Context.WriteRegister(Gpr.Ecx, 0);

        cpu.Step();

        Assert.Equal(0x99u, cpu.Context.ReadRegister(Gpr.Eax));
        Assert.True((cpu.Context.Eflags & FlagBits.Zf) != 0);
    }

    [Fact]
    public void Btr_MemoryClearsBitAndSetsCarry()
    {
        XboxCpu cpu = CpuHarness.Create(0x0F, 0xB3, 0x0D, 0x00, 0x20, 0x00, 0x00);
        cpu.Bus().Write32(0x2000, 0xFF);
        cpu.Context.WriteRegister(Gpr.Ecx, 3);

        cpu.Step();

        Assert.Equal(0xF7u, cpu.Bus().Read32(0x2000));
        Assert.True((cpu.Context.Eflags & FlagBits.Cf) != 0);
    }

    [Fact]
    public void Bts_RegisterSetsBit()
    {
        XboxCpu cpu = CpuHarness.Create(0x0F, 0xAB, 0xC8);
        cpu.Context.WriteRegister(Gpr.Eax, 0);
        cpu.Context.WriteRegister(Gpr.Ecx, 5);

        cpu.Step();

        Assert.Equal(0x20u, cpu.Context.ReadRegister(Gpr.Eax));
        Assert.True((cpu.Context.Eflags & FlagBits.Cf) == 0);
    }

    [Fact]
    public void Bt_DoesNotModifyDestination()
    {
        XboxCpu cpu = CpuHarness.Create(0x0F, 0xA3, 0xC8);
        cpu.Context.WriteRegister(Gpr.Eax, 0x100);
        cpu.Context.WriteRegister(Gpr.Ecx, 8);

        cpu.Step();

        Assert.Equal(0x100u, cpu.Context.ReadRegister(Gpr.Eax));
        Assert.True((cpu.Context.Eflags & FlagBits.Cf) != 0);
    }
}
