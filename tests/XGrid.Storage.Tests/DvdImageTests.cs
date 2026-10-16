using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace XGrid.Storage.Tests;

public sealed class DvdImageTests : IDisposable
{
    private readonly List<string> _tempFiles = new();
    private bool _disposed;

    [Fact]
    public void IsoDvdImage_RoundTripsSectors()
    {
        byte[] file = new byte[4 * IsoDvdImage.SectorSize];
        for (int s = 0; s < 4; s++)
        {
            for (int i = 0; i < IsoDvdImage.SectorSize; i++)
            {
                file[(s * IsoDvdImage.SectorSize) + i] = (byte)((s * 16) + (i & 0x0F));
            }
        }

        using var dvd = new IsoDvdImage(WriteTemp(file));

        Assert.Equal(4u, dvd.SectorCount);
        byte[] sector = new byte[IsoDvdImage.SectorSize];
        dvd.ReadSector(2, sector);
        Assert.Equal(0x20, sector[0]);
        Assert.Equal(0x2F, sector[15]);
        Assert.Equal(0x2F, sector[2047]);
    }

    [Fact]
    public void IsoDvdImage_RejectsOutOfRangeAndShortBuffer()
    {
        using var dvd = new IsoDvdImage(WriteTemp(new byte[2 * IsoDvdImage.SectorSize]));

        Assert.Throws<ArgumentOutOfRangeException>(() => dvd.ReadSector(2, new byte[IsoDvdImage.SectorSize]));
        Assert.Throws<ArgumentException>(() => dvd.ReadSector(0, new byte[16]));
    }

    [Fact]
    public void IsoDvdImage_RejectsTinyFile()
    {
        Assert.Throws<InvalidDataException>(() => new IsoDvdImage(WriteTemp(new byte[100])));
    }

    [Fact]
    public void IsoDvdImage_RejectsMissingFile()
    {
        Assert.Throws<FileNotFoundException>(() => new IsoDvdImage(WriteTempPath("no-such.iso")));
    }

    [Fact]
    public void ChdDvdImage_RejectsNonChdFile()
    {
        Assert.Throws<InvalidDataException>(() => ChdDvdImage.Open(WriteTemp(new byte[4096])));
    }

    [Fact]
    public void ChdDvdImage_RejectsMissingFile()
    {
        Assert.Throws<InvalidDataException>(() => ChdDvdImage.Open(WriteTempPath("no-such.chd")));
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            foreach (string path in _tempFiles)
            {
                try
                {
                    File.Delete(path);
                }
                catch (IOException)
                {
                }
            }

            _disposed = true;
        }
    }

    private string WriteTemp(byte[] content)
    {
        string path = Path.Combine(Path.GetTempPath(), "xgrid-dvd-" + Guid.NewGuid().ToString("N") + ".iso");
        File.WriteAllBytes(path, content);
        _tempFiles.Add(path);
        return path;
    }

    private string WriteTempPath(string name)
    {
        string path = Path.Combine(Path.GetTempPath(), "xgrid-dvd-" + Guid.NewGuid().ToString("N") + "-" + name);
        _tempFiles.Add(path);
        return path;
    }
}
