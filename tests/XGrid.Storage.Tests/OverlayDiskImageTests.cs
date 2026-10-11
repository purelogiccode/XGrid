using System;
using Xunit;

namespace XGrid.Storage.Tests;

public sealed class OverlayDiskImageTests
{
    private sealed class MemoryDiskImage : IDiskImage
    {
        private readonly byte[] _data;

        public MemoryDiskImage(uint sectors)
        {
            _data = new byte[sectors * 512];
            SectorCount = sectors;
        }

        public uint SectorCount { get; }

        public int Writes { get; private set; }

        public void ReadSector(uint lba, Span<byte> destination)
        {
            _data.AsSpan((int)lba * 512, 512).CopyTo(destination);
        }

        public void WriteSector(uint lba, ReadOnlySpan<byte> source)
        {
            Writes++;
            source[..512].CopyTo(_data.AsSpan((int)lba * 512, 512));
        }

        public void Dispose()
        {
        }
    }

    [Fact]
    public void Write_IsCapturedInOverlay_NotBacking()
    {
        var backing = new MemoryDiskImage(4);
        using var overlay = new OverlayDiskImage(backing);

        Span<byte> data = stackalloc byte[512];
        data.Fill(0x5A);
        overlay.WriteSector(2, data);

        Span<byte> read = stackalloc byte[512];
        overlay.ReadSector(2, read);

        Assert.All(read.ToArray(), b => Assert.Equal(0x5A, b));
        Assert.Equal(0, backing.Writes);
        Assert.Equal(1, overlay.ModifiedSectorCount);
    }

    [Fact]
    public void Read_FallsThroughToBacking_WhenNotOverlaid()
    {
        var backing = new MemoryDiskImage(4);
        Span<byte> initial = stackalloc byte[512];
        initial.Fill(0x11);
        backing.WriteSector(1, initial);

        using var overlay = new OverlayDiskImage(backing);
        Span<byte> read = stackalloc byte[512];
        overlay.ReadSector(1, read);

        Assert.All(read.ToArray(), b => Assert.Equal(0x11, b));
    }

    [Fact]
    public void SectorCount_DelegatesToBacking()
    {
        using var overlay = new OverlayDiskImage(new MemoryDiskImage(4));
        Assert.Equal(4u, overlay.SectorCount);
    }

    [Fact]
    public void WriteLog_RecordsOrderAndRepeats()
    {
        using var overlay = new OverlayDiskImage(new MemoryDiskImage(8));
        Span<byte> data = stackalloc byte[512];

        overlay.WriteSector(5, data);
        overlay.WriteSector(2, data);
        overlay.WriteSector(5, data);

        Assert.Equal(3, overlay.WriteCount);
        Assert.Equal(new uint[] { 5, 2, 5 }, overlay.WriteLog);
        Assert.Equal(2, overlay.ModifiedSectorCount);
        Assert.Equal(new uint[] { 2, 5 }, overlay.GetModifiedSectors());
    }

    [Fact]
    public void WriteLog_Empty_WhenNoWrites()
    {
        using var overlay = new OverlayDiskImage(new MemoryDiskImage(4));
        Assert.Equal(0, overlay.WriteCount);
        Assert.Empty(overlay.WriteLog);
        Assert.Empty(overlay.GetModifiedSectors());
    }
}
