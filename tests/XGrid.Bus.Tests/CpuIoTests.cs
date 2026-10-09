using XGrid.Cpu;
using XGrid.Memory;
using Xunit;

namespace XGrid.Bus.Tests;

public sealed class CpuIoTests
{
    private const uint CodeAddress = 0x1000;

    [Fact]
    public void InByteImmediateReadsPort()
    {
        var io = new StubIoBus { Read8Value = 0xAB };
        XboxCpu cpu = Create(io, 0xE4, 0x60);

        cpu.Step();

        Assert.Equal(0xABu, cpu.Context.Eax & 0xFF);
        Assert.Equal(0x60, io.LastPort);
    }

    [Fact]
    public void InWordImmediateReadsPort()
    {
        var io = new StubIoBus { Read16Value = 0xBEEF };
        XboxCpu cpu = Create(io, 0x66, 0xE5, 0x60);

        cpu.Step();

        Assert.Equal(0xBEEFu, cpu.Context.Eax & 0xFFFF);
    }

    [Fact]
    public void InDwordImmediateReadsPort()
    {
        var io = new StubIoBus { Read32Value = 0xDEADBEEF };
        XboxCpu cpu = Create(io, 0xE5, 0x60);

        cpu.Step();

        Assert.Equal(0xDEADBEEFu, cpu.Context.Eax);
    }

    [Fact]
    public void InByteDxReadsPort()
    {
        var io = new StubIoBus { Read8Value = 0x77 };
        XboxCpu cpu = Create(io, 0xEC);
        cpu.Context.Edx = 0x0060;

        cpu.Step();

        Assert.Equal(0x77u, cpu.Context.Eax & 0xFF);
        Assert.Equal(0x60, io.LastPort);
    }

    [Fact]
    public void OutByteImmediateWritesPort()
    {
        var io = new StubIoBus();
        XboxCpu cpu = Create(io, 0xE6, 0x60);
        cpu.Context.Eax = 0x5A;

        cpu.Step();

        Assert.Equal(0x60, io.LastPort);
        Assert.Equal(0x5Au, io.LastValue);
        Assert.Equal(8, io.LastSize);
    }

    [Fact]
    public void OutWordDxWritesPort()
    {
        var io = new StubIoBus();
        XboxCpu cpu = Create(io, 0x66, 0xEF);
        cpu.Context.Edx = 0x0060;
        cpu.Context.Eax = 0x1234;

        cpu.Step();

        Assert.Equal(0x60, io.LastPort);
        Assert.Equal(0x1234u, io.LastValue);
        Assert.Equal(16, io.LastSize);
    }

    [Fact]
    public void OutsbWritesByteAndAdvancesEsi()
    {
        var io = new StubIoBus();
        XboxCpu cpu = Create(io, out PhysicalMemory memory, 0x6E);
        cpu.Context.Edx = 0x0060;
        cpu.Context.Esi = 0x2000;
        memory.Write8(0x2000, 0x5A);

        cpu.Step();

        Assert.Equal(0x60, io.LastPort);
        Assert.Equal(0x5Au, io.LastValue);
        Assert.Equal(8, io.LastSize);
        Assert.Equal(0x2001u, cpu.Context.Esi);
    }

    [Fact]
    public void OutswWritesWordFromDx()
    {
        var io = new StubIoBus();
        XboxCpu cpu = Create(io, out PhysicalMemory memory, 0x66, 0x6F);
        cpu.Context.Edx = 0x0CFC;
        cpu.Context.Esi = 0x2000;
        memory.Write8(0x2000, 0xEF);
        memory.Write8(0x2001, 0xBE);

        cpu.Step();

        Assert.Equal(0x0CFC, io.LastPort);
        Assert.Equal(0xBEEFu, io.LastValue);
        Assert.Equal(16, io.LastSize);
        Assert.Equal(0x2002u, cpu.Context.Esi);
    }

    [Fact]
    public void InsbReadsByteAndAdvancesEdi()
    {
        var io = new StubIoBus { Read8Value = 0xAB };
        XboxCpu cpu = Create(io, out PhysicalMemory memory, 0x6C);
        cpu.Context.Edx = 0x0060;
        cpu.Context.Edi = 0x3000;

        cpu.Step();

        Assert.Equal(0xAB, memory.Read8(0x3000));
        Assert.Equal(0x60, io.LastPort);
        Assert.Equal(0x3001u, cpu.Context.Edi);
    }

    [Fact]
    public void RepOutsbWritesCountBytes()
    {
        var io = new StubIoBus();
        XboxCpu cpu = Create(io, out PhysicalMemory memory, 0xF3, 0x6E);
        cpu.Context.Edx = 0x0060;
        cpu.Context.Esi = 0x2000;
        cpu.Context.Ecx = 3;
        memory.Write8(0x2000, 0x11);
        memory.Write8(0x2001, 0x22);
        memory.Write8(0x2002, 0x33);

        cpu.Step();

        Assert.Equal(3, io.WriteCount);
        Assert.Equal(0x33u, io.LastValue);
        Assert.Equal(0u, cpu.Context.Ecx);
        Assert.Equal(0x2003u, cpu.Context.Esi);
    }

    [Fact]
    public void RepOutsbWithZeroCountDoesNothing()
    {
        var io = new StubIoBus();
        XboxCpu cpu = Create(io, 0xF3, 0x6E);
        cpu.Context.Edx = 0x0060;
        cpu.Context.Esi = 0x2000;
        cpu.Context.Ecx = 0;

        cpu.Step();

        Assert.Equal(0, io.WriteCount);
        Assert.Equal(0x2000u, cpu.Context.Esi);
    }

    private static XboxCpu Create(StubIoBus io, params byte[] code)
    {
        return Create(io, out _, code);
    }

    private static XboxCpu Create(StubIoBus io, out PhysicalMemory memory, params byte[] code)
    {
        memory = new PhysicalMemory(0x10000);
        var bus = new AddressSpace(memory, UnmappedAccessPolicy.Throw);
        var cpu = new XboxCpu(bus, io);

        cpu.Reset();
        cpu.Context.Cr0 = 1;
        cpu.Context.CodeSegment32 = true;
        cpu.Context.SetSegment(SegmentRegister.Cs, 0x08, 0);
        cpu.Context.SetSegment(SegmentRegister.Ds, 0x10, 0);
        cpu.Context.SetSegment(SegmentRegister.Ss, 0x10, 0);
        cpu.Context.Eflags = FlagBits.Reserved1;
        cpu.Context.Esp = 0x8000;

        for (int i = 0; i < code.Length; i++)
        {
            memory.Write8(CodeAddress + (uint)i, code[i]);
        }

        cpu.Context.Eip = CodeAddress;
        return cpu;
    }

    private sealed class StubIoBus : IIoBus
    {
        public byte Read8Value { get; set; }

        public ushort Read16Value { get; set; }

        public uint Read32Value { get; set; }

        public ushort LastPort { get; private set; }

        public uint LastValue { get; private set; }

        public int LastSize { get; private set; }

        public int WriteCount { get; private set; }

        public byte Read8(ushort port)
        {
            LastPort = port;
            return Read8Value;
        }

        public ushort Read16(ushort port)
        {
            LastPort = port;
            return Read16Value;
        }

        public uint Read32(ushort port)
        {
            LastPort = port;
            return Read32Value;
        }

        public void Write8(ushort port, byte value)
        {
            Capture(port, value, 8);
        }

        public void Write16(ushort port, ushort value)
        {
            Capture(port, value, 16);
        }

        public void Write32(ushort port, uint value)
        {
            Capture(port, value, 32);
        }

        private void Capture(ushort port, uint value, int size)
        {
            LastPort = port;
            LastValue = value;
            LastSize = size;
            WriteCount++;
        }
    }
}
