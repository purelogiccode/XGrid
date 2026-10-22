using System;
using System.Buffers.Binary;
using System.IO;
using Xunit;

namespace XGrid.Storage.Tests;

public sealed class Qcow2DiskImageTests
{
    private const int ClusterBits = 16;
    private const int ClusterSize = 1 << ClusterBits;

    [Fact]
    public void ZeroFlaggedCluster_ReadsAsZeros()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".qcow2");
        try
        {
            WriteTestImage(path);
            using var image = new Qcow2DiskImage(path);

            Span<byte> zeroCluster = stackalloc byte[512];
            image.ReadSector(0, zeroCluster);
            Assert.All(zeroCluster.ToArray(), b => Assert.Equal(0, b));

            Span<byte> dataCluster = stackalloc byte[512];
            image.ReadSector(128, dataCluster);
            Assert.All(dataCluster.ToArray(), b => Assert.Equal(0xBB, b));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static void WriteTestImage(string path)
    {
        const ulong l2Offset = ClusterSize;
        const ulong dataOffset = 2ul * ClusterSize;
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        Span<byte> header = stackalloc byte[104];
        BinaryPrimitives.WriteUInt32BigEndian(header, 0x514649FBu);
        BinaryPrimitives.WriteUInt32BigEndian(header[4..], 2u);
        BinaryPrimitives.WriteUInt32BigEndian(header[20..], ClusterBits);
        BinaryPrimitives.WriteUInt64BigEndian(header[24..], (2 * ClusterSize));
        BinaryPrimitives.WriteUInt32BigEndian(header[36..], 1u);
        BinaryPrimitives.WriteUInt64BigEndian(header[40..], 104ul);
        stream.Write(header);

        Span<byte> l1 = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64BigEndian(l1, l2Offset);
        stream.Write(l1);

        stream.Position = (long)l2Offset;
        Span<byte> l2Entry = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64BigEndian(l2Entry, dataOffset | 0x01ul);
        stream.Write(l2Entry);
        BinaryPrimitives.WriteUInt64BigEndian(l2Entry, dataOffset + ClusterSize);
        stream.Write(l2Entry);

        stream.Position = (long)dataOffset;
        Span<byte> stale = stackalloc byte[512];
        stale.Fill(0xAA);
        stream.Write(stale);
        Span<byte> data = stackalloc byte[512];
        data.Fill(0xBB);
        stream.Position = (long)(dataOffset + ClusterSize);
        stream.Write(data);
    }
}
