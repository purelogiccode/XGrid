using System;
using Xunit;

namespace XGrid.Storage.Tests;

public sealed class AtapiTests
{
    private sealed class ArrayDisk : IDiskImage
    {
        private readonly byte[] _data;

        public ArrayDisk(int sectors)
        {
            _data = new byte[sectors * AtaDevice.SectorSize];
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

    private static AtaDevice AtapiDrive()
    {
        return new AtaDevice(atapi: true);
    }

    [Fact]
    public void IdentifyPacketReturnsAtapiSignatureBlock()
    {
        AtaDevice drive = AtapiDrive();
        Assert.True(drive.IsAtapi);
        drive.WriteTaskfile(7, 8, 0xA1);

        uint first = drive.ReadTaskfile(0, 32);
        Assert.Equal(0x85C0u, first & 0xFFFFu);

        for (int i = 1; i < 128; i++)
        {
            drive.ReadTaskfile(0, 32);
        }

        Assert.Equal(0x50u, drive.ReadTaskfile(7, 8));
    }

    [Fact]
    public void IdentifyOnAtapiAbortsWithAtapiSignature()
    {
        AtaDevice drive = AtapiDrive();
        drive.WriteTaskfile(7, 8, 0xEC);

        Assert.Equal(0x51u, drive.ReadTaskfile(7, 8));
        Assert.Equal(0x04u, drive.ReadTaskfile(1, 8));
        Assert.Equal(0x14u, drive.ReadTaskfile(4, 8));
        Assert.Equal(0xEBu, drive.ReadTaskfile(5, 8));
    }

    [Fact]
    public void IdentifyPacketOnHddAborts()
    {
        AtaDevice drive = new();
        drive.Attach(new ArrayDisk(16));
        drive.WriteTaskfile(7, 8, 0xA1);

        Assert.Equal(0x51u, drive.ReadTaskfile(7, 8));
    }

    [Fact]
    public void PacketInquirySucceedsWithoutMedia()
    {
        AtaDevice drive = AtapiDrive();
        drive.WriteTaskfile(1, 8, 0x00);
        drive.WriteTaskfile(7, 8, 0xA0);

        // 12-byte INQUIRY packet via byte writes.
        byte[] packet = new byte[12];
        packet[0] = 0x12;
        packet[4] = 36;
        foreach (byte b in packet)
        {
            drive.WriteTaskfile(0, 8, b);
        }

        Assert.Equal(0x58u, drive.ReadTaskfile(7, 8));
        uint first = drive.ReadTaskfile(0, 32);
        Assert.Equal(0x05u, first & 0xFFu);
        for (int i = 4; i < 36; i += 4)
        {
            drive.ReadTaskfile(0, 32);
        }

        Assert.Equal(0x50u, drive.ReadTaskfile(7, 8));
    }

    [Fact]
    public void PacketTestUnitReadyFailsNotReady()
    {
        AtaDevice drive = AtapiDrive();
        drive.WriteTaskfile(1, 8, 0x00);
        drive.WriteTaskfile(7, 8, 0xA0);

        for (int i = 0; i < 12; i++)
        {
            drive.WriteTaskfile(0, 8, 0x00);
        }

        // QEMU ide_atapi_cmd_error: READY|ERR without SEEK, sense<<4, reason CoD|IO.
        Assert.Equal(0x41u, drive.ReadTaskfile(7, 8));
        Assert.Equal(0x20u, drive.ReadTaskfile(1, 8));
        Assert.Equal(0x03u, drive.ReadTaskfile(2, 8) & 0x07u);
    }

    [Fact]
    public void PacketOnHddAborts()
    {
        AtaDevice drive = new();
        drive.Attach(new ArrayDisk(16));
        drive.WriteTaskfile(7, 8, 0xA0);

        Assert.Equal(0x51u, drive.ReadTaskfile(7, 8));
    }

    [Fact]
    public void UnknownOpcodeFailsIllegalRequest()
    {
        AtaDevice drive = AtapiDrive();
        drive.WriteTaskfile(1, 8, 0x00);
        drive.WriteTaskfile(7, 8, 0xA0);

        byte[] packet = new byte[12];
        packet[0] = 0xFF;
        foreach (byte b in packet)
        {
            drive.WriteTaskfile(0, 8, b);
        }

        Assert.Equal(0x41u, drive.ReadTaskfile(7, 8));
        Assert.Equal(0x50u, drive.ReadTaskfile(1, 8));
        Assert.Equal(0x05u, drive.SenseKey);
        Assert.True(drive.ConsumePacketError());
        Assert.False(drive.ConsumePacketError());
    }

    [Fact]
    public void ModeSenseUnknownPageFailsInvalidField()
    {
        AtaDevice drive = AtapiDrive();
        drive.WriteTaskfile(1, 8, 0x00);
        drive.WriteTaskfile(7, 8, 0xA0);

        // MODE SENSE(10), page 0x3E, 28-byte allocation (xemu oracle packet).
        byte[] packet = new byte[] { 0x5A, 0x00, 0x3E, 0x00, 0x00, 0x00, 0x00, 0x00, 0x1C, 0x00, 0x00, 0x00 };
        foreach (byte b in packet)
        {
            drive.WriteTaskfile(0, 8, b);
        }

        Assert.Equal(0x41u, drive.ReadTaskfile(7, 8));
        Assert.Equal(0x05u, drive.SenseKey);
        Assert.Equal(0x24u, drive.SenseAsc);
    }

    [Fact]
    public void ReadCapacityOnEmptyDriveFailsNotReady()
    {
        AtaDevice drive = AtapiDrive();
        drive.WriteTaskfile(1, 8, 0x00);
        drive.WriteTaskfile(7, 8, 0xA0);

        byte[] packet = new byte[12];
        packet[0] = 0x25;
        foreach (byte b in packet)
        {
            drive.WriteTaskfile(0, 8, b);
        }

        Assert.Equal(0x41u, drive.ReadTaskfile(7, 8));
        Assert.Equal(0x20u, drive.ReadTaskfile(1, 8));
    }

    [Fact]
    public void RequestSenseReportsMediumNotPresent()
    {
        AtaDevice drive = AtapiDrive();

        // Fail a TEST UNIT READY first to raise unit-attention state.
        drive.WriteTaskfile(1, 8, 0x00);
        drive.WriteTaskfile(7, 8, 0xA0);
        for (int i = 0; i < 12; i++)
        {
            drive.WriteTaskfile(0, 8, 0x00);
        }

        Assert.Equal(0x02u, drive.SenseKey);

        // REQUEST SENSE with 18-byte allocation.
        drive.WriteTaskfile(1, 8, 0x00);
        drive.WriteTaskfile(7, 8, 0xA0);
        byte[] packet = new byte[12];
        packet[0] = 0x03;
        packet[4] = 18;
        foreach (byte b in packet)
        {
            drive.WriteTaskfile(0, 8, b);
        }

        Assert.Equal(0x58u, drive.ReadTaskfile(7, 8));
        byte[] sense = new byte[18];
        for (int i = 0; i < 18; i++)
        {
            sense[i] = (byte)drive.ReadTaskfile(0, 8);
        }

        Assert.Equal(0x70u, sense[0]);
        Assert.Equal(0x02u, sense[2]);
        Assert.Equal(0x3Au, sense[12]);
        Assert.Equal(0x50u, drive.ReadTaskfile(7, 8));
        Assert.Equal(0u, drive.SenseKey);
    }
}
