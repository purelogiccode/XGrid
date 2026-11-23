using System;
using System.IO;
using XGrid.Bus.Io;
using XGrid.Core.Snapshots;
using Xunit;

namespace XGrid.Storage.Tests;

public sealed class DeviceSnapshotTests
{
    private sealed class MemoryDiskImage(uint sectors) : IDiskImage
    {
        private readonly byte[] _data = new byte[sectors * 512];

        public uint SectorCount { get; } = sectors;

        public void ReadSector(uint lba, Span<byte> destination)
        {
            _data.AsSpan((int)lba * 512, 512).CopyTo(destination);
        }

        public void WriteSector(uint lba, ReadOnlySpan<byte> source)
        {
            source[..512].CopyTo(_data.AsSpan((int)lba * 512, 512));
        }

        public void Dispose()
        {
        }
    }

    private sealed class MemoryDvdImage(uint sectors) : IDvdImage
    {
        private readonly byte[] _data = new byte[sectors * 2048];

        public uint SectorCount { get; } = sectors;

        public void ReadSector(uint lba, Span<byte> destination)
        {
            _data.AsSpan((int)lba * 2048, 2048).CopyTo(destination);
        }

        public void Dispose()
        {
        }
    }

    private static byte[] Save(ISnapshotable device)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        device.SaveState(writer);
        writer.Flush();
        return stream.ToArray();
    }

    private static void Load(ISnapshotable device, byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        using var reader = new BinaryReader(stream);
        device.LoadState(reader);
    }

    [Fact]
    public void Overlay_RoundTrip()
    {
        using var first = Create();
        byte[] baseline = Save(first);
        Assert.Equal(2, first.ModifiedSectorCount);

        using var second = new OverlayDiskImage(new MemoryDiskImage(16));
        Load(second, baseline);
        Assert.Equal(baseline, Save(second));
        Assert.Equal(2, second.ModifiedSectorCount);
        Assert.Equal(new uint[] { 3, 9 }, second.GetModifiedSectors());

        Span<byte> sector = stackalloc byte[512];
        sector.Fill(0xFF);
        second.WriteSector(5, sector);
        Load(second, baseline);
        Assert.Equal(baseline, Save(second));
        return;

        static OverlayDiskImage Create()
        {
            var overlay = new OverlayDiskImage(new MemoryDiskImage(16));
            Span<byte> sector = stackalloc byte[512];
            sector.Fill(0x5A);
            overlay.WriteSector(3, sector);
            sector.Fill(0xA5);
            overlay.WriteSector(9, sector);
            return overlay;
        }
    }

    [Fact]
    public void Overlay_SectorCountMismatch_FailsLoudly()
    {
        using var first = new OverlayDiskImage(new MemoryDiskImage(16));
        byte[] baseline = Save(first);

        using var other = new OverlayDiskImage(new MemoryDiskImage(8));
        Assert.Throws<InvalidDataException>(() => Load(other, baseline));
    }

    [Fact]
    public void AtaHdd_RoundTrip()
    {
        var first = Create();
        Exercise(first);
        byte[] baseline = Save(first);

        var second = Create();
        Load(second, baseline);
        Assert.Equal(baseline, Save(second));
        Assert.Equal(first.LastCommand, second.LastCommand);
        Assert.Equal(first.Status, second.Status);

        Exercise(second);
        Load(second, baseline);
        Assert.Equal(baseline, Save(second));
        return;

        static AtaDevice Create()
        {
            var drive = new AtaDevice();
            drive.Attach(new OverlayDiskImage(new MemoryDiskImage(32)));
            return drive;
        }

        static void Exercise(AtaDevice drive)
        {
            drive.WriteTaskfile(1, 8, 0x00);
            drive.WriteTaskfile(2, 8, 0x01);
            drive.WriteTaskfile(3, 8, 0x04);
            drive.WriteTaskfile(7, 8, 0xEC);
            for (int i = 0; i < 256; i++)
            {
                drive.ReadTaskfile(0, 16);
            }
        }
    }

    [Fact]
    public void AtaAtapi_RoundTrip()
    {
        var first = Create();
        Exercise(first);
        byte[] baseline = Save(first);

        var second = Create();
        Load(second, baseline);
        Assert.Equal(baseline, Save(second));

        Exercise(second);
        Load(second, baseline);
        Assert.Equal(baseline, Save(second));
        return;

        static AtaDevice Create()
        {
            var drive = new AtaDevice(atapi: true);
            drive.AttachDvd(new MemoryDvdImage(64));
            return drive;
        }

        static void Exercise(AtaDevice drive)
        {
            drive.WriteTaskfile(1, 8, 0x00);
            drive.WriteTaskfile(7, 8, 0xA0);
            drive.WriteTaskfile(0, 8, 0x12);
            drive.WriteTaskfile(0, 8, 0x00);
            drive.WriteTaskfile(0, 8, 0x00);
            drive.WriteTaskfile(0, 8, 0x00);
            drive.WriteTaskfile(0, 8, 0x24);
            drive.WriteTaskfile(0, 8, 0x00);
            for (int i = 0; i < 6; i++)
            {
                drive.WriteTaskfile(0, 8, 0x00);
            }

            for (int i = 0; i < 36; i++)
            {
                drive.ReadTaskfile(0, 8);
            }
        }
    }

    [Fact]
    public void Ide_RoundTrip()
    {
        var first = Create();
        first.WriteConfig(0x04, 2, 0x0007);
        first.PrimaryMaster.WriteTaskfile(2, 8, 0x01);
        first.PrimaryMaster.WriteTaskfile(7, 8, 0xEC);
        for (int i = 0; i < 256; i++)
        {
            first.PrimaryMaster.ReadTaskfile(0, 16);
        }

        first.PrimaryMaster.ReadTaskfile(7, 8);
        byte[] baseline = Save(first);

        var second = Create();
        Load(second, baseline);
        Assert.Equal(baseline, Save(second));
        Assert.Equal(first.LastCommand, second.LastCommand);
        Assert.Equal(first.BusMasterStarts, second.BusMasterStarts);

        first.PrimaryMaster.WriteTaskfile(7, 8, 0x91);
        Load(second, baseline);
        Assert.Equal(baseline, Save(second));
        return;

        static IdeController Create()
        {
            var io = new IoSpace();
            var ide = new IdeController();
            ide.AttachPrimaryMaster(new OverlayDiskImage(new MemoryDiskImage(32)));
            ide.AttachDvd(new MemoryDvdImage(64));
            ide.RegisterPorts(io);
            ide.HddPath = "hdd-test-path";
            ide.DvdPath = "dvd-test-path";
            return ide;
        }
    }
}
