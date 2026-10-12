using System.Collections.Generic;
using System.IO;
using Xunit;

namespace XGrid.Audio.Tests;

public sealed class ApuSnapshotTests
{
    private sealed class FakeMemory : IApuMemory
    {
        private readonly Dictionary<uint, byte> _ram = new();

        public uint Read32(uint address) =>
            (uint)(ReadByte(address) | (ReadByte(address + 1) << 8) | (ReadByte(address + 2) << 16) | (ReadByte(address + 3) << 24));

        public void Write32(uint address, uint value)
        {
            for (int i = 0; i < 4; i++)
            {
                Write8(address + (uint)i, (byte)(value >> (i * 8)));
            }
        }

        public void Write8(uint address, byte value) => _ram[address] = value;

        private byte ReadByte(uint address) => _ram.GetValueOrDefault(address, (byte)0);
    }

    private static byte[] Save(McpxApuDevice device)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        device.SaveState(writer);
        writer.Flush();
        return stream.ToArray();
    }

    private static void Load(McpxApuDevice device, byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        using var reader = new BinaryReader(stream);
        device.LoadState(reader);
    }

    [Fact]
    public void Apu_RoundTrip()
    {
        static McpxApuDevice Create() => new(new FakeMemory());
        static void Exercise(McpxApuDevice apu)
        {
            const uint Base = McpxApuDevice.BaseAddress;
            apu.Write(Base + 0x1004, 4, 0x00000071);
            apu.Write(Base + 0x1100, 4, 0x000000E0);
            apu.Write(Base + 0x115C, 4, 0x00100000);
            apu.Write(Base + 0x202C, 4, 0x00200000);
            apu.Write(Base + 0x1504, 4, 1u << 15);
            apu.Write(Base + 0x20124, 4, 7);
            apu.Write(Base + 0x20128, 4, 7);
            apu.Advance(8_000_000);
            apu.Read(Base + 0x1000, 4);
        }

        var first = Create();
        Exercise(first);
        byte[] baseline = Save(first);
        Assert.True(first.VoiceOffCompletions > 0);

        var second = Create();
        Load(second, baseline);
        Assert.Equal(baseline, Save(second));
        Assert.Equal(first.InterruptStatus, second.InterruptStatus);
        Assert.Equal(first.VoiceOffCompletions, second.VoiceOffCompletions);

        Exercise(second);
        Load(second, baseline);
        Assert.Equal(baseline, Save(second));
    }
}
