using System;
using Xunit;

namespace XGrid.Storage.Tests;

public sealed class AtaDataPortTests
{
    private sealed class ArrayDisk : IDiskImage
    {
        private readonly byte[] _data;

        public ArrayDisk(int sectors)
        {
            _data = new byte[sectors * AtaDevice.SectorSize];
            for (int i = 0; i < _data.Length; i++)
            {
                _data[i] = (byte)i;
            }
        }

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

    private static AtaDevice AttachedDrive()
    {
        AtaDevice drive = new();
        drive.Attach(new ArrayDisk(16));
        return drive;
    }

    [Fact]
    public void IdentifyCompletesAfter128DwordReads()
    {
        AtaDevice drive = AttachedDrive();
        drive.WriteTaskfile(7, 8, 0xEC);

        for (int i = 0; i < 128; i++)
        {
            drive.ReadTaskfile(0, 32);
        }

        // Full 512-byte block consumed: status back to ready, error clear.
        Assert.Equal(0x50u, drive.ReadTaskfile(7, 8));
        Assert.Equal(0u, drive.ReadTaskfile(1, 8));
    }

    [Fact]
    public void IdentifyCompletesAfter256WordReads()
    {
        AtaDevice drive = AttachedDrive();
        drive.WriteTaskfile(7, 8, 0xEC);

        for (int i = 0; i < 256; i++)
        {
            drive.ReadTaskfile(0, 16);
        }

        Assert.Equal(0x50u, drive.ReadTaskfile(7, 8));
    }

    [Fact]
    public void DwordReadReturnsLittleEndianBytes()
    {
        AtaDevice drive = AttachedDrive();
        drive.WriteTaskfile(7, 8, 0xEC);

        uint word0 = drive.ReadTaskfile(0, 16);
        Assert.Equal(0x0040u, word0);

        drive.WriteTaskfile(7, 8, 0xEC);
        uint dword0 = drive.ReadTaskfile(0, 32);
        Assert.Equal(word0, dword0 & 0xFFFFu);
        Assert.Equal(0u, dword0 >> 16);
    }

    [Fact]
    public void SecurityUnlockCompletesAfter128DwordWrites()
    {
        AtaDevice drive = AttachedDrive();
        Assert.True(drive.SecurityLocked);
        drive.WriteTaskfile(7, 8, 0xF2);

        for (int i = 0; i < 128; i++)
        {
            drive.WriteTaskfile(0, 32, 0x87543A21u);
        }

        Assert.False(drive.SecurityLocked);
        Assert.Equal(0x50u, drive.ReadTaskfile(7, 8));
    }

    [Fact]
    public void OverReadPastBlockEndReturnsZeroWithoutThrow()
    {
        AtaDevice drive = AttachedDrive();
        drive.WriteTaskfile(7, 8, 0xEC);

        for (int i = 0; i < 128; i++)
        {
            drive.ReadTaskfile(0, 32);
        }

        Assert.Equal(0u, drive.ReadTaskfile(0, 32));
        Assert.Equal(0x50u, drive.ReadTaskfile(7, 8));
    }
}
