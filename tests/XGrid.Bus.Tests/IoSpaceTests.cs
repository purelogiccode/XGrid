using System;
using XGrid.Bus.Io;
using Xunit;

namespace XGrid.Bus.Tests;

public sealed class IoSpaceTests
{
    [Fact]
    public void UnmappedReadsReturnOpenBus()
    {
        var io = new IoSpace();

        Assert.Equal(0xFF, io.Read8(0x1234));
        Assert.Equal(0xFFFF, io.Read16(0x1234));
        Assert.Equal(0xFFFFFFFFu, io.Read32(0x1234));
    }

    [Fact]
    public void WindowReadWriteRoutesWithOffset()
    {
        var io = new IoSpace();
        var device = new StubPortDevice();
        io.Register(0x100, 4, device);

        io.Write8(0x101, 0x5A);

        Assert.Equal(0x5Au, device.LastValue);
        Assert.Equal(1, device.LastOffset);
    }

    [Fact]
    public void DisposeRemovesWindow()
    {
        var io = new IoSpace();
        IDisposable handle = io.Register(0x200, 2, new StubPortDevice());

        handle.Dispose();

        Assert.Equal(0xFF, io.Read8(0x200));
    }

    [Fact]
    public void OverlappingWindowsUseLatest()
    {
        var io = new IoSpace();
        var first = new StubPortDevice { ReadValue = 0x11 };
        var second = new StubPortDevice { ReadValue = 0x22 };
        io.Register(0x300, 2, first);
        io.Register(0x300, 2, second);

        Assert.Equal(0x22, io.Read8(0x300));
    }

    private sealed class StubPortDevice : IIoPortDevice
    {
        public uint ReadValue { get; set; } = 0x42;

        public uint LastValue { get; private set; }

        public int LastOffset { get; private set; } = -1;

        public uint Read(ushort offset, int size)
        {
            return ReadValue;
        }

        public void Write(ushort offset, int size, uint value)
        {
            LastValue = value;
            LastOffset = offset;
        }
    }
}
