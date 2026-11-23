using System;
using System.IO;
using Xunit;

namespace XGrid.Memory.Tests;

public sealed class PhysicalMemorySnapshotTests
{
    [Fact]
    public void Ram_RoundTrip_PreservesEveryByte()
    {
        var ram = new PhysicalMemory(4096);
        Span<byte> pattern = ram.GetSpan(0, 4096);
        for (int i = 0; i < pattern.Length; i++)
        {
            pattern[i] = (byte)((i * 2654435761u) >> 16);
        }

        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            ram.SaveState(writer);
        }

        ram.Clear();
        Assert.Equal(0u, ram.Read32(0x123));

        stream.Position = 0;
        using (var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            ram.LoadState(reader);
        }

        ReadOnlySpan<byte> restored = ram.GetReadOnlySpan(0, 4096);
        for (int i = 0; i < restored.Length; i++)
        {
            Assert.Equal((byte)((i * 2654435761u) >> 16), restored[i]);
        }
    }

    [Fact]
    public void Ram_LoadIntoDifferentSize_Throws()
    {
        var small = new PhysicalMemory(64);
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            small.SaveState(writer);
        }

        var large = new PhysicalMemory(128);
        stream.Position = 0;
        using (var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            Assert.Throws<InvalidDataException>(() => large.LoadState(reader));
        }
    }

    [Fact]
    public void Ram_BadVersion_Throws()
    {
        var ram = new PhysicalMemory(64);
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(99u);
        }

        stream.Position = 0;
        using (var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            Assert.Throws<InvalidDataException>(() => ram.LoadState(reader));
        }
    }
}
