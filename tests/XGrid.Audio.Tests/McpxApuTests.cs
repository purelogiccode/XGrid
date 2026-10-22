using System.Collections.Generic;
using Xunit;

namespace XGrid.Audio.Tests;

public sealed class McpxApuTests
{
    private const uint Base = McpxApuDevice.BaseAddress;

    // Register offsets within the APU window.
    private const uint IsTs = 0x1000;
    private const uint Ien = 0x1004;
    private const uint Fectl = 0x1100;
    private const uint FenAddr = 0x115C;
    private const uint FedecMeth = 0x1300;
    private const uint FedecParam = 0x1304;
    private const uint FemMemAddr = 0x1324;
    private const uint FemMemData = 0x1334;
    private const uint FetForce1 = 0x1504;
    private const uint VpVaddr = 0x202C;
    private const uint VpPioFree = 0x20010;
    private const uint VpVoiceOff = 0x20128;

    private const uint Gintsts = 1u << 0;
    private const uint Fetintsts = 1u << 4;
    private const uint Fenintsts = 1u << 5;
    private const uint Fevintsts = 1u << 6;
    private const uint Se2FeIdleVoice = 0x8000;

    [Fact]
    public void PioFree_ReadsAsEmpty()
    {
        var apu = new McpxApuDevice(new FakeMemory());

        Assert.Equal(0x80u, apu.Read(Base + VpPioFree, 4));
    }

    [Fact]
    public void VoiceOff_RaisesNotifyImmediately()
    {
        var apu = new McpxApuDevice(new FakeMemory());
        apu.Write(Base + Ien, 4, Gintsts | Fevintsts | Fenintsts);
        var events = new List<bool>();
        apu.InterruptChanged += events.Add;

        apu.Write(Base + VpVoiceOff, 4, 64);

        Assert.Equal((Fevintsts | Fenintsts | Gintsts), apu.InterruptStatus);
        Assert.True(apu.InterruptAsserted);
        Assert.True(Assert.Single(events));
    }

    [Fact]
    public void VoiceOff_DeliversIdleVoiceMessageOnNextFrame()
    {
        var apu = new McpxApuDevice(new FakeMemory());
        apu.Write(Base + Ien, 4, Gintsts | Fetintsts | Fevintsts | Fenintsts);
        apu.Write(Base + FetForce1, 4, 1u << 15);
        apu.Write(Base + VpVoiceOff, 4, 64);
        apu.Write(Base + IsTs, 4, apu.InterruptStatus);

        Assert.Equal(0u, apu.Read(Base + FedecMeth, 4));

        apu.Advance(4_000_000);

        Assert.Equal(Se2FeIdleVoice, apu.Read(Base + FedecMeth, 4));
        Assert.Equal(64u, apu.Read(Base + FedecParam, 4));
        Assert.Equal((Fetintsts | Gintsts), apu.InterruptStatus);
        Assert.True(apu.InterruptAsserted);
    }

    [Fact]
    public void IstsWrite_ClearsWrittenBits()
    {
        var apu = new McpxApuDevice(new FakeMemory());
        apu.Write(Base + VpVoiceOff, 4, 1);

        Assert.Equal(Fenintsts | Fevintsts, apu.InterruptStatus);

        apu.Write(Base + IsTs, 4, Fevintsts);

        Assert.Equal(Fenintsts, apu.InterruptStatus);
    }

    [Fact]
    public void GlobalInterrupt_RequiresEnableBit()
    {
        var apu = new McpxApuDevice(new FakeMemory());
        apu.Write(Base + VpVoiceOff, 4, 1);

        Assert.Equal(Fenintsts | Fevintsts, apu.InterruptStatus);
        Assert.False(apu.InterruptAsserted);

        apu.Write(Base + Ien, 4, Gintsts | Fevintsts);
        Assert.False(apu.InterruptAsserted);

        apu.Write(Base + Fectl, 4, 0);
        Assert.True(apu.InterruptAsserted);
        Assert.Equal(Gintsts | Fevintsts | Fenintsts, apu.InterruptStatus);
    }

    [Fact]
    public void FemMemData_WritesGuestMemory()
    {
        var memory = new FakeMemory();
        var apu = new McpxApuDevice(memory);
        apu.Write(Base + FemMemAddr, 4, 0x1000);

        apu.Write(Base + FemMemData, 4, 0xDEADBEEF);

        Assert.Equal(0xDEADBEEFu, memory.Read32(0x1000));
    }

    [Fact]
    public void VoiceOff_ClearsVoiceActiveAndWritesNotifier()
    {
        var memory = new FakeMemory();
        var apu = new McpxApuDevice(memory);
        const uint voiceBase = 0x2000;
        const uint notifyBase = 0x8000;
        apu.Write(Base + VpVaddr, 4, voiceBase);
        apu.Write(Base + FenAddr, 4, notifyBase);
        memory.Write32(voiceBase + (64 * 0x80) + 0x54, 1u << 21);

        apu.Write(Base + VpVoiceOff, 4, 64);

        Assert.Equal(0u, memory.Read32(voiceBase + (64 * 0x80) + 0x54) & (1u << 21));
        Assert.Equal(1, memory.Read8(notifyBase + (16u * (2 + (64 * 4))) + 15));
        Assert.Equal(1, memory.Read8(notifyBase + (16u * (2 + (64 * 4))) - 1));
    }

    [Fact]
    public void Reset_ClearsInterruptState()
    {
        var apu = new McpxApuDevice(new FakeMemory());
        apu.Write(Base + Ien, 4, Gintsts | Fevintsts);
        apu.Write(Base + VpVoiceOff, 4, 1);
        Assert.True(apu.InterruptAsserted);

        apu.Reset();

        Assert.Equal(0u, apu.InterruptStatus);
        Assert.False(apu.InterruptAsserted);
    }

    private sealed class FakeMemory : IApuMemory
    {
        private readonly Dictionary<uint, uint> _dwords = [];

        public uint Read32(uint address)
        {
            return _dwords.TryGetValue(address, out uint value) ? value : 0;
        }

        public void Write32(uint address, uint value)
        {
            _dwords[address] = value;
        }

        public void Write8(uint address, byte value)
        {
            uint aligned = address & ~3u;
            int shift = (int)(address & 3u) * 8;
            uint current = Read32(aligned);
            Write32(aligned, (current & ~(0xFFu << shift)) | ((uint)value << shift));
        }

        public byte Read8(uint address)
        {
            return (byte)(Read32(address & ~3u) >> ((int)(address & 3u) * 8));
        }
    }
}
