using XGrid.Memory;

namespace XGrid.Cpu.Tests;

internal static class CpuHarness
{
    public const uint CodeAddress = 0x1000;
    public const uint StackAddress = 0x8000;

    public static XboxCpu Create(params byte[] code)
    {
        var memory = new PhysicalMemory(0x10000);
        var bus = new AddressSpace(memory, UnmappedAccessPolicy.Throw);
        var cpu = new XboxCpu(bus);

        cpu.Reset();
        cpu.Context.Cr0 = 1;
        cpu.Context.CodeSegment32 = true;
        cpu.Context.SetSegment(SegmentRegister.Cs, 0x08, 0);
        cpu.Context.SetSegment(SegmentRegister.Ds, 0x10, 0);
        cpu.Context.SetSegment(SegmentRegister.Ss, 0x10, 0);
        cpu.Context.SetSegment(SegmentRegister.Es, 0x10, 0);
        cpu.Context.SetSegment(SegmentRegister.Fs, 0x10, 0);
        cpu.Context.SetSegment(SegmentRegister.Gs, 0x10, 0);
        cpu.Context.Eflags = FlagBits.Reserved1;
        cpu.Context.Esp = StackAddress;

        // Flat 32-bit code/data descriptors at selector 0x08/0x10.
        cpu.Context.Gdtr.Base = 0x7000;
        cpu.Context.Gdtr.Limit = 0xFFFF;
        memory.Write32(0x7008, 0x0000FFFF);
        memory.Write32(0x700C, 0x00CF9A00);
        memory.Write32(0x7010, 0x0000FFFF);
        memory.Write32(0x7014, 0x00CF9200);

        for (int i = 0; i < code.Length; i++)
        {
            memory.Write8(CodeAddress + (uint)i, code[i]);
        }

        cpu.Context.Eip = CodeAddress;
        return cpu;
    }

    public static AddressSpace Bus(this XboxCpu cpu)
    {
        return (AddressSpace)cpu.Memory;
    }

    public static PhysicalMemory Ram(this XboxCpu cpu)
    {
        return ((AddressSpace)cpu.Memory).Ram;
    }
}
