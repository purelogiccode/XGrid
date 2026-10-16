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

    private sealed class ArrayDvd : IDvdImage
    {
        private readonly byte[] _data;

        public ArrayDvd(int sectors)
        {
            _data = new byte[sectors * AtaDevice.DvdSectorSize];
            for (int s = 0; s < sectors; s++)
            {
                for (int i = 0; i < AtaDevice.DvdSectorSize; i++)
                {
                    _data[(s * AtaDevice.DvdSectorSize) + i] = (byte)s;
                }
            }
        }

        public uint SectorCount => (uint)(_data.Length / AtaDevice.DvdSectorSize);

        public void Dispose()
        {
        }

        public void ReadSector(uint lba, Span<byte> destination)
        {
            _data.AsSpan((int)lba * AtaDevice.DvdSectorSize, AtaDevice.DvdSectorSize).CopyTo(destination);
        }
    }

    private static AtaDevice AtapiDriveWithMedia(int sectors = 16)
    {
        var drive = new AtaDevice(atapi: true);
        drive.AttachDvd(new ArrayDvd(sectors));
        return drive;
    }

    private static void SendPacket(AtaDevice drive, byte[] packet)
    {
        drive.WriteTaskfile(1, 8, 0x00);
        drive.WriteTaskfile(7, 8, 0xA0);
        foreach (byte b in packet)
        {
            drive.WriteTaskfile(0, 8, b);
        }
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

    [Fact]
    public void TestUnitReadySucceedsWithMedia()
    {
        AtaDevice drive = AtapiDriveWithMedia();
        SendPacket(drive, new byte[12]);

        Assert.Equal(0x50u, drive.ReadTaskfile(7, 8));
        Assert.Equal(0u, drive.SenseKey);
    }

    [Fact]
    public void ReadCapacityReturnsLastLbaAndBlockSize()
    {
        AtaDevice drive = AtapiDriveWithMedia();
        byte[] packet = new byte[12];
        packet[0] = 0x25;
        SendPacket(drive, packet);

        Assert.Equal(0x58u, drive.ReadTaskfile(7, 8));
        uint first = drive.ReadTaskfile(0, 32);
        uint second = drive.ReadTaskfile(0, 32);

        // Big-endian last LBA (15) then block length (2048); PIO words assemble LE.
        Assert.Equal(0x0F000000u, first);
        Assert.Equal(0x00080000u, second);
        Assert.Equal(0x50u, drive.ReadTaskfile(7, 8));
    }

    [Fact]
    public void Read10ReturnsSectorData()
    {
        AtaDevice drive = AtapiDriveWithMedia();
        byte[] packet = new byte[12];
        packet[0] = 0x28;
        packet[2] = 0x00;
        packet[3] = 0x00;
        packet[4] = 0x00;
        packet[5] = 0x05;
        packet[7] = 0x00;
        packet[8] = 0x01;
        SendPacket(drive, packet);

        Assert.Equal(0x58u, drive.ReadTaskfile(7, 8));
        for (int i = 0; i < 512; i++)
        {
            Assert.Equal(0x05050505u, drive.ReadTaskfile(0, 32));
        }

        Assert.Equal(0x50u, drive.ReadTaskfile(7, 8));
    }

    [Fact]
    public void Read10MultiSectorReturnsConcatenatedData()
    {
        AtaDevice drive = AtapiDriveWithMedia();
        byte[] packet = new byte[12];
        packet[0] = 0x28;
        packet[5] = 0x02;
        packet[8] = 0x02;
        SendPacket(drive, packet);

        for (int i = 0; i < 512; i++)
        {
            Assert.Equal(0x02020202u, drive.ReadTaskfile(0, 32));
        }

        for (int i = 0; i < 512; i++)
        {
            Assert.Equal(0x03030303u, drive.ReadTaskfile(0, 32));
        }

        Assert.Equal(0x50u, drive.ReadTaskfile(7, 8));
    }

    [Fact]
    public void Read12ReturnsSectorData()
    {
        AtaDevice drive = AtapiDriveWithMedia();
        byte[] packet = new byte[12];
        packet[0] = 0xA8;
        packet[5] = 0x07;
        packet[6] = 0x00;
        packet[7] = 0x00;
        packet[8] = 0x00;
        packet[9] = 0x01;
        SendPacket(drive, packet);

        Assert.Equal(0x58u, drive.ReadTaskfile(7, 8));
        for (int i = 0; i < 512; i++)
        {
            Assert.Equal(0x07070707u, drive.ReadTaskfile(0, 32));
        }

        Assert.Equal(0x50u, drive.ReadTaskfile(7, 8));
    }

    [Fact]
    public void ReadBeyondEndFailsLbaOutOfRange()
    {
        AtaDevice drive = AtapiDriveWithMedia();
        byte[] packet = new byte[12];
        packet[0] = 0x28;
        packet[5] = 0x0F;
        packet[8] = 0x02;
        SendPacket(drive, packet);

        Assert.Equal(0x41u, drive.ReadTaskfile(7, 8));
        Assert.Equal(0x05u, drive.SenseKey);
        Assert.Equal(0x21u, drive.SenseAsc);
    }

    [Fact]
    public void ReadOverTransferCapFailsInvalidField()
    {
        AtaDevice drive = AtapiDriveWithMedia(256);
        byte[] packet = new byte[12];
        packet[0] = 0xA8;
        packet[9] = 0x81;
        SendPacket(drive, packet);

        Assert.Equal(0x41u, drive.ReadTaskfile(7, 8));
        Assert.Equal(0x05u, drive.SenseKey);
        Assert.Equal(0x24u, drive.SenseAsc);
    }

    [Fact]
    public void ReadZeroLengthCompletesWithoutData()
    {
        AtaDevice drive = AtapiDriveWithMedia();
        byte[] packet = new byte[12];
        packet[0] = 0x28;
        packet[5] = 0x03;
        SendPacket(drive, packet);

        Assert.Equal(0x50u, drive.ReadTaskfile(7, 8));
    }

    [Fact]
    public void RequestSenseAfterIllegalWithMediaReportsIllegal()
    {
        AtaDevice drive = AtapiDriveWithMedia();
        byte[] bad = new byte[12];
        bad[0] = 0xFF;
        SendPacket(drive, bad);
        Assert.Equal(0x05u, drive.SenseKey);

        byte[] packet = new byte[12];
        packet[0] = 0x03;
        packet[4] = 18;
        SendPacket(drive, packet);

        byte[] sense = new byte[18];
        for (int i = 0; i < 18; i++)
        {
            sense[i] = (byte)drive.ReadTaskfile(0, 8);
        }

        Assert.Equal(0x70u, sense[0]);
        Assert.Equal(0x05u, sense[2]);
        Assert.Equal(0x20u, sense[12]);
        Assert.Equal(0u, drive.SenseKey);
    }

    [Fact]
    public void AttachDvdOnHddThrows()
    {
        var drive = new AtaDevice();

        Assert.Throws<InvalidOperationException>(() => drive.AttachDvd(new ArrayDvd(4)));
    }

    [Fact]
    public void PacketLogRetainsFullBurstInOrder()
    {
        AtaDevice drive = AtapiDriveWithMedia();
        const int total = 30;

        for (int i = 0; i < total; i++)
        {
            byte[] packet = new byte[12];
            packet[0] = 0xFF;
            packet[1] = (byte)(i >> 8);
            packet[2] = (byte)i;
            SendPacket(drive, packet);
        }

        Assert.Equal(total, drive.PacketLog.Count);
        Assert.Equal("FF:0000000000", drive.PacketLog[0]);
        Assert.Equal($"FF:00{total - 1:X2}000000", drive.PacketLog[total - 1]);
    }

    [Fact]
    public void PacketLogCapsAtCapacityEvictingOldest()
    {
        AtaDevice drive = AtapiDriveWithMedia();
        const int total = AtaDevice.PacketLogCapacity + 10;

        for (int i = 0; i < total; i++)
        {
            byte[] packet = new byte[12];
            packet[0] = 0xFF;
            packet[1] = (byte)(i >> 8);
            packet[2] = (byte)i;
            SendPacket(drive, packet);
        }

        Assert.Equal(AtaDevice.PacketLogCapacity, drive.PacketLog.Count);
        const int first = total - AtaDevice.PacketLogCapacity;
        Assert.Equal($"FF:{first >> 8:X2}{first & 0xFF:X2}000000", drive.PacketLog[0]);
        const int last = total - 1;
        Assert.Equal($"FF:{last >> 8:X2}{last & 0xFF:X2}000000", drive.PacketLog[^1]);
    }
}
