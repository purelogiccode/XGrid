using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using XGrid.Frontend.Avalonia.Models;
using Xunit;

namespace XGrid.Frontend.Tests;

public sealed class ChihiroNetworkFirmwareTests : IDisposable
{
    private const string Marker = "FIRM_VERSION is here!";
    private readonly string _root;

    public ChihiroNetworkFirmwareTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "xgrid-chihiro-netfw-" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(_root);
    }

    [Fact]
    public void ReadVersion_ParsesTheWordAfterTheMarker()
    {
        string path = WriteImage(0x13050621);

        Assert.Equal("13.05(06.21)", ChihiroNetworkFirmware.ReadVersion(path));
    }

    [Fact]
    public void ReadVersion_ReturnsNullWithoutMarker()
    {
        string path = Path.Combine(_root, "plain.bin");
        File.WriteAllBytes(path, new byte[64]);

        Assert.Null(ChihiroNetworkFirmware.ReadVersion(path));
    }

    [Fact]
    public void ReadVersion_ReturnsNullForAMissingFile()
    {
        Assert.Null(ChihiroNetworkFirmware.ReadVersion(Path.Combine(_root, "missing.bin")));
    }

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
    }

    private string WriteImage(uint version)
    {
        byte[] image = new byte[0x200];
        Encoding.ASCII.GetBytes(Marker).CopyTo(image, 0x100);
        int at = (0x100 + Marker.Length + 1 + 3) & ~3;
        BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(at), version);
        string path = Path.Combine(_root, "ver1305.bin");
        File.WriteAllBytes(path, image);
        return path;
    }
}
