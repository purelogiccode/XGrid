using System;
using System.Collections.Generic;
using System.IO;
using XGrid.Host;
using XGrid.Storage;
using Xunit;

namespace XGrid.Integration.Tests;

public sealed class DvdAttachTests : IDisposable
{
    private readonly List<string> _tempFiles = [];
    private bool _disposed;

    [Fact]
    public void OpenDvdImage_RoutesIsoByMagic()
    {
        using IDvdImage dvd = XboxMachine.OpenDvdImage(WriteTemp(new byte[4096]));

        Assert.IsType<IsoDvdImage>(dvd);
        Assert.Equal(2u, dvd.SectorCount);
    }

    [Fact]
    public void OpenDvdImage_RoutesChdByMagic()
    {
        byte[] fake = new byte[4096];
        System.Text.Encoding.ASCII.GetBytes("MComprHD").CopyTo(fake, 0);

        // Magic routes to the CHD reader, whose parse then rejects the fake.
        Assert.Throws<InvalidDataException>(() => XboxMachine.OpenDvdImage(WriteTemp(fake)));
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
        string path = Path.Combine(Path.GetTempPath(), "xgrid-dvdattach-" + Guid.NewGuid().ToString("N") + ".bin");
        File.WriteAllBytes(path, content);
        _tempFiles.Add(path);
        return path;
    }
}
