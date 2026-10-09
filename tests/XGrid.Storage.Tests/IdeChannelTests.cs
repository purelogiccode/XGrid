using System;
using XGrid.Bus.Io;
using Xunit;

namespace XGrid.Storage.Tests;

public sealed class IdeChannelTests
{
    private sealed class ArrayDisk : IDiskImage
    {
        private readonly byte[] _data = new byte[16 * AtaDevice.SectorSize];

        public uint SectorCount => (uint)(_data.Length / AtaDevice.SectorSize);

        public void Dispose()
        {
        }

        public void ReadSector(uint lba, Span<byte> destination)
        {
            _data.AsSpan((int)lba * AtaDevice.SectorSize, AtaDevice.SectorSize).CopyTo(destination);
        }

        public void WriteSector(uint lba, ReadOnlySpan<byte> source)
        {
            source.CopyTo(_data.AsSpan((int)lba * AtaDevice.SectorSize, AtaDevice.SectorSize));
        }
    }

    private static (IdeController, IoSpace) Channel()
    {
        IdeController ide = new();
        ide.AttachPrimaryMaster(new ArrayDisk());
        IoSpace io = new();
        ide.RegisterPorts(io);
        return (ide, io);
    }

    [Fact]
    public void DriveSelectRoutesCommands()
    {
        (IdeController ide, IoSpace io) = Channel();

        // Select master (0xA0) and IDENTIFY: HDD block, word 0 == 0x0040.
        io.Write8(0x1F6, 0xA0);
        io.Write8(0x1F7, 0xEC);
        Assert.Equal(0x0040u, io.Read16(0x1F0));
        for (int i = 1; i < 256; i++)
        {
            io.Read16(0x1F0);
        }

        Assert.Equal(0xECu, ide.PrimaryMaster.LastCommand);

        // Select slave (0xB0) and IDENTIFY PACKET: ATAPI block, word 0 == 0x85C0.
        io.Write8(0x1F6, 0xB0);
        io.Write8(0x1F7, 0xA1);
        Assert.Equal(0xA1u, ide.PrimarySlave.LastCommand);
        Assert.Equal(0x85C0u, io.Read16(0x1F0));
    }

    [Fact]
    public void SharedSetupWritesLandOnBothDrives()
    {
        (IdeController _, IoSpace io) = Channel();

        // Port 0x1F1 is write-Features/read-Error, so probe sharing via the
        // sector-count register (offset 2) instead.
        io.Write8(0x1F6, 0xA0);
        io.Write8(0x1F2, 0x07);

        io.Write8(0x1F6, 0xB0);
        Assert.Equal(0x07u, io.Read8(0x1F2));
    }
}
