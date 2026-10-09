using XGrid.Memory;
using Xunit;

namespace XGrid.Cpu.Tests;

public sealed class CpuPagingTests
{
    private const uint PageDirectory = 0x4000;
    private const uint PageTable = 0x5000;
    private const uint IdtAddress = 0x6000;
    private const uint HandlerAddress = 0x3000;

    private static void Map4K(AddressSpace bus, uint virtualAddress, uint physicalAddress, bool writable = true)
    {
        uint pdeAddress = PageDirectory + ((virtualAddress >> 22) & 0x3FF) * 4;
        if ((bus.Read32(pdeAddress) & 1u) == 0)
        {
            bus.Write32(pdeAddress, PageTable | 3u);
        }

        uint pteAddress = PageTable + ((virtualAddress >> 12) & 0x3FF) * 4;
        bus.Write32(pteAddress, (physicalAddress & 0xFFFFF000u) | 1u | (writable ? 2u : 0u));
    }

    private static void EnablePaging(XboxCpu cpu)
    {
        cpu.Context.Cr3 = PageDirectory;
        cpu.Context.Cr0 = ControlRegisterBits.Cr0ProtectedMode | ControlRegisterBits.Cr0Paging;
        cpu.Mmu.Flush();
    }

    private static void InstallIdt(XboxCpu cpu, int vector, uint handler)
    {
        uint gate = IdtAddress + (uint)vector * 8;
        cpu.Bus().Write16(gate, (ushort)(handler & 0xFFFF));
        cpu.Bus().Write16(gate + 2, 0x08);
        cpu.Bus().Write16(gate + 4, 0x8E00);
        cpu.Bus().Write16(gate + 6, (ushort)(handler >> 16));
        cpu.Context.Idtr.Base = IdtAddress;
        cpu.Context.Idtr.Limit = 0x7FF;
    }

    [Fact]
    public void MovCr_ReadsControlRegister()
    {
        XboxCpu cpu = CpuHarness.Create(0x0F, 0x20, 0xC0);
        cpu.Context.Cr0 = 0x00010011;

        cpu.Step();

        Assert.Equal(0x00010011u, cpu.Context.ReadRegister(Gpr.Eax));
    }

    [Fact]
    public void MovCr_WritesControlRegister()
    {
        XboxCpu cpu = CpuHarness.Create(0x0F, 0x22, 0xDB);
        cpu.Context.WriteRegister(Gpr.Ebx, 0x00005000);

        cpu.Step();

        Assert.Equal(0x00005000u, cpu.Context.Cr3);
    }

    [Fact]
    public void MovDr_ReadsDebugRegister()
    {
        XboxCpu cpu = CpuHarness.Create(0x0F, 0x21, 0xC0);
        cpu.Context.WriteDebugRegister(0, 0xCAFEBABE);

        cpu.Step();

        Assert.Equal(0xCAFEBABEu, cpu.Context.ReadRegister(Gpr.Eax));
    }

    [Fact]
    public void MovDr_WritesDebugRegister()
    {
        XboxCpu cpu = CpuHarness.Create(0x0F, 0x23, 0xC0);
        cpu.Context.WriteRegister(Gpr.Eax, 0x12345678);

        cpu.Step();

        Assert.Equal(0x12345678u, cpu.Context.ReadDebugRegister(0));
    }

    [Fact]
    public void Execute_ThroughPaging()
    {
        XboxCpu cpu = CpuHarness.Create(0xB8, 0x2A, 0x00, 0x00, 0x00);
        Map4K(cpu.Bus(), 0x00001000, 0x00001000);
        Map4K(cpu.Bus(), 0x00008000, 0x00008000);
        EnablePaging(cpu);

        cpu.Step();

        Assert.Equal(42u, cpu.Context.ReadRegister(Gpr.Eax));
    }

    [Fact]
    public void PageFault_DeliversToVector14()
    {
        XboxCpu cpu = CpuHarness.Create(0xA1, 0x00, 0x20, 0x00, 0x00);
        Map4K(cpu.Bus(), 0x00001000, 0x00001000);
        Map4K(cpu.Bus(), 0x00006000, 0x00006000);
        Map4K(cpu.Bus(), 0x00007000, 0x00007000);
        Map4K(cpu.Bus(), 0x00008000, 0x00008000);
        InstallIdt(cpu, 14, HandlerAddress);
        EnablePaging(cpu);

        cpu.Step();

        Assert.Equal(HandlerAddress, cpu.Context.Eip);
        Assert.Equal(0x2000u, cpu.Context.Cr2);
        Assert.Equal(0x7FF0u, cpu.Context.ReadRegister(Gpr.Esp));
        Assert.Equal(0x1000u, cpu.Bus().Read32(0x7FF4));
        Assert.Equal(0u, cpu.Bus().Read32(0x7FF0));
    }

    [Fact]
    public void Invlpg_Executes()
    {
        XboxCpu cpu = CpuHarness.Create(0x0F, 0x01, 0x38);
        cpu.Context.WriteRegister(Gpr.Eax, 0x00400000);

        cpu.Step();

        Assert.Equal(0x1003u, cpu.Context.Eip);
    }

    [Fact]
    public void MovCr3Write_FlushesTlb()
    {
        XboxCpu cpu = CpuHarness.Create(
            0x8B, 0x05, 0x00, 0x20, 0x00, 0x00,
            0xB8, 0x00, 0x40, 0x00, 0x00,
            0x0F, 0x22, 0xD8,
            0x8B, 0x05, 0x00, 0x20, 0x00, 0x00);
        Map4K(cpu.Bus(), 0x00001000, 0x00001000);
        Map4K(cpu.Bus(), 0x00002000, 0x00003000);
        EnablePaging(cpu);
        cpu.Bus().Write32(0x3000, 0xAAAA);
        cpu.Bus().Write32(0x6000, 0xBBBB);

        cpu.Step();
        Assert.Equal(0xAAAAu, cpu.Context.ReadRegister(Gpr.Eax));

        Map4K(cpu.Bus(), 0x00002000, 0x00006000);
        cpu.Step();
        cpu.Step();
        cpu.Step();

        Assert.Equal(0xBBBBu, cpu.Context.ReadRegister(Gpr.Eax));
    }

    [Fact]
    public void Wbinvd_Executes()
    {
        XboxCpu cpu = CpuHarness.Create(0x0F, 0x09);

        cpu.Step();

        Assert.Equal(0x1002u, cpu.Context.Eip);
    }

    [Fact]
    public void InvalidOpcode_WithAbsentIdtGate_Throws()
    {
        XboxCpu cpu = CpuHarness.Create(0x0F, 0x0B);
        cpu.Context.Idtr.Base = IdtAddress;
        cpu.Context.Idtr.Limit = 0x7FF;

        Assert.Throws<UnsupportedInstructionException>(() => cpu.Step());
    }
}
