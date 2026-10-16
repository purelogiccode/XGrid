using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace XGrid.Audio.Tests;

public sealed class DspBridgeTests
{
    private const uint Base = McpxApuDevice.BaseAddress;

    private const uint GpsAddr = 0x2040;
    private const uint GpRst = 0x3FFFC;
    private const uint EpRst = 0x5FFFC;
    private const uint IsTs = 0x1000;
    private const uint Ien = 0x1004;

    private const uint DspBit3 = 1u << 3;
    private const uint SgeTable = 0x00100000;
    private const uint StateWordPhys = 0x03924810u;
    private const uint StateWordPage = StateWordPhys & ~0xFFFu;

    private const ulong OneFrame = 4_000_000;

    [Fact]
    public void GpStart_RisingEdgeMarksRunning()
    {
        var apu = new McpxApuDevice(new FakeMemory());

        Assert.False(apu.GpRunning);
        apu.Write(Base + GpRst, 4, 3);
        Assert.True(apu.GpRunning);
        Assert.Contains("GP DSP start", apu.EventLog, StringComparer.Ordinal);
    }

    [Fact]
    public void GpStop_ClearingRunBitStops()
    {
        var apu = new McpxApuDevice(new FakeMemory());
        apu.Write(Base + GpRst, 4, 3);
        apu.Write(Base + GpRst, 4, 0);

        Assert.False(apu.GpRunning);
        Assert.Contains("GP DSP stop", apu.EventLog, StringComparer.Ordinal);
    }

    [Fact]
    public void EpStartStop_TrackedIndependently()
    {
        var apu = new McpxApuDevice(new FakeMemory());
        apu.Write(Base + EpRst, 4, 3);

        Assert.True(apu.EpRunning);
        Assert.False(apu.GpRunning);

        apu.Write(Base + EpRst, 4, 2);
        Assert.False(apu.EpRunning);
    }

    [Fact]
    public void Absorb_PendingSubmitFreedWhenMapped()
    {
        var memory = new FakeMemory();
        var apu = StartMappedGp(memory);
        memory.Write32(StateWordPhys, 2);

        apu.Advance(OneFrame);

        Assert.Equal(0u, memory.Read32(StateWordPhys));
        Assert.Equal(1, apu.DspAbsorbs);
    }

    [Fact]
    public void Absorb_ParamsLoadedFreedSilently()
    {
        var memory = new FakeMemory();
        var apu = StartMappedGp(memory);
        memory.Write32(StateWordPhys, 3);

        apu.Advance(OneFrame);

        Assert.Equal(0u, memory.Read32(StateWordPhys));
        Assert.Equal(1, apu.DspAbsorbs);
        Assert.Equal(0, apu.DspBit3Raises);
        Assert.Equal(0u, apu.InterruptStatus & DspBit3);
    }

    [Fact]
    public void NoAbsorb_WhenGpStopped()
    {
        var memory = new FakeMemory();
        var apu = new McpxApuDevice(memory);
        apu.Write(Base + GpsAddr, 4, SgeTable);
        memory.Write32(SgeTable, StateWordPage);
        memory.Write32(StateWordPhys, 2);

        apu.Advance(OneFrame);

        Assert.Equal(2u, memory.Read32(StateWordPhys));
        Assert.Equal(0, apu.DspAbsorbs);
    }

    [Fact]
    public void NoAbsorb_WhenScratchUnmapped()
    {
        var memory = new FakeMemory();
        var apu = new McpxApuDevice(memory);
        apu.Write(Base + GpRst, 4, 3);
        apu.Write(Base + GpsAddr, 4, SgeTable);
        memory.Write32(SgeTable, 0x00100000u);
        memory.Write32(StateWordPhys, 2);

        apu.Advance(OneFrame);

        Assert.Equal(2u, memory.Read32(StateWordPhys));
        Assert.Equal(0, apu.DspAbsorbs);
    }

    [Fact]
    public void Wake_FirstSubmitRaisesBit3AndIrq()
    {
        var memory = new FakeMemory();
        var apu = StartMappedGp(memory);
        apu.Write(Base + Ien, 4, 0xAFB);
        memory.Write32(StateWordPhys, 2);

        apu.Advance(OneFrame);

        Assert.Equal(0u, memory.Read32(StateWordPhys));
        Assert.Equal(1, apu.DspAbsorbs);
        Assert.Equal(1, apu.DspBit3Raises);
        Assert.Equal(DspBit3, apu.InterruptStatus & DspBit3);
        Assert.True(apu.InterruptAsserted);
    }

    [Fact]
    public void Wake_DebouncedWithinGap()
    {
        var memory = new FakeMemory();
        var apu = StartMappedGp(memory);
        apu.Write(Base + Ien, 4, 0xAFB);
        memory.Write32(StateWordPhys, 2);
        apu.Advance(OneFrame);
        Assert.Equal(1, apu.DspBit3Raises);

        for (int i = 0; i < 10; i++)
        {
            memory.Write32(StateWordPhys, 2);
            apu.Advance(OneFrame);
        }

        Assert.Equal(11, apu.DspAbsorbs);
        Assert.Equal(1, apu.DspBit3Raises);
    }

    [Fact]
    public void Wake_EligibleAgainAfterGap()
    {
        var memory = new FakeMemory();
        var apu = StartMappedGp(memory);
        memory.Write32(StateWordPhys, 2);
        apu.Advance(OneFrame);
        Assert.Equal(1, apu.DspBit3Raises);

        for (int i = 0; i < 240; i++)
        {
            apu.Advance(OneFrame);
        }

        memory.Write32(StateWordPhys, 2);
        apu.Advance(OneFrame);

        Assert.Equal(2, apu.DspAbsorbs);
        Assert.Equal(2, apu.DspBit3Raises);
    }

    [Fact]
    public void GuestAck_ClearsBit3()
    {
        var memory = new FakeMemory();
        var apu = StartMappedGp(memory);
        apu.Write(Base + Ien, 4, 0xAFB);
        memory.Write32(StateWordPhys, 2);
        apu.Advance(OneFrame);
        Assert.Equal(DspBit3, apu.InterruptStatus & DspBit3);

        apu.Write(Base + IsTs, 4, DspBit3);

        Assert.Equal(0u, apu.InterruptStatus & DspBit3);
    }

    [Fact]
    public void Snapshot_RoundTripsDspState()
    {
        var firstMemory = new FakeMemory();
        var first = StartMappedGp(firstMemory);
        firstMemory.Write32(StateWordPhys, 2);
        first.Advance(OneFrame);
        byte[] baseline = Save(first);

        var second = new McpxApuDevice(new FakeMemory());
        Load(second, baseline);

        Assert.Equal(baseline, Save(second));
        Assert.True(second.GpRunning);
        Assert.Equal(first.DspAbsorbs, second.DspAbsorbs);
        Assert.Equal(first.DspBit3Raises, second.DspBit3Raises);
    }

    [Fact]
    public void Snapshot_V2LoadDerivesRunningFromGpRst()
    {
        var memory = new FakeMemory();
        var source = StartMappedGp(memory);
        byte[] v2 = SaveV2(source);

        // Live memory keeps the guest's SGE mapping and a pending block; the
        // V2 payload only carries registers. Loading must resume the engine
        // end to end (the pd546.xgs restore scenario).
        var live = new FakeMemory();
        live.Write32(SgeTable, StateWordPage);
        live.Write32(StateWordPhys, 2);
        var loaded = new McpxApuDevice(live);
        Load(loaded, v2);

        Assert.True(loaded.GpRunning);
        Assert.False(loaded.EpRunning);
        Assert.Equal(0, loaded.DspAbsorbs);
        Assert.Equal(0, loaded.DspBit3Raises);

        loaded.Advance(OneFrame);

        Assert.Equal(0u, live.Read32(StateWordPhys));
        Assert.Equal(1, loaded.DspAbsorbs);
        Assert.Equal(1, loaded.DspBit3Raises);
    }

    private static McpxApuDevice StartMappedGp(FakeMemory memory)
    {
        var apu = new McpxApuDevice(memory);
        apu.Write(Base + GpsAddr, 4, SgeTable);
        memory.Write32(SgeTable, StateWordPage);
        apu.Write(Base + GpRst, 4, 3);
        return apu;
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

    // Hand-built V2 payload mirroring McpxApuDevice.SaveState at version 2:
    // version + regs + pending voices + locks + SSL bases/counts + SSL
    // index/segment + base page/handles/remainder + counters + event log.
    private static byte[] SaveV2(McpxApuDevice device)
    {
        byte[] v3 = Save(device);
        using var stream = new MemoryStream(v3);
        using var reader = new BinaryReader(stream);
        using var output = new MemoryStream();
        using var writer = new BinaryWriter(output);

        Assert.Equal(3, reader.ReadInt32());
        writer.Write(2);

        const int RegCount = 0x80000 / 4;
        for (int i = 0; i < RegCount; i++)
        {
            writer.Write(reader.ReadUInt32());
        }

        int pending = reader.ReadInt32();
        writer.Write(pending);
        for (int i = 0; i < pending; i++)
        {
            writer.Write(reader.ReadUInt16());
        }

        for (int i = 0; i < 4; i++)
        {
            writer.Write(reader.ReadUInt64());
        }

        for (int i = 0; i < 512; i++)
        {
            writer.Write(reader.ReadUInt32());
            writer.Write(reader.ReadByte());
        }

        for (int i = 0; i < 256; i++)
        {
            writer.Write(reader.ReadInt32());
            writer.Write(reader.ReadInt32());
        }

        writer.Write(reader.ReadUInt32());
        writer.Write(reader.ReadUInt32());
        writer.Write(reader.ReadUInt32());
        writer.Write(reader.ReadUInt64());

        // Skip the six V3 DSP fields: gp/ep running, ep divider, wake gap,
        // absorbs, raises.
        reader.ReadBoolean();
        reader.ReadBoolean();
        reader.ReadUInt32();
        reader.ReadUInt32();
        reader.ReadInt64();
        reader.ReadInt64();

        long remaining = reader.BaseStream.Length - reader.BaseStream.Position;
        writer.Write(reader.ReadBytes((int)remaining));
        writer.Flush();
        return output.ToArray();
    }

    private sealed class FakeMemory : IApuMemory
    {
        private readonly Dictionary<uint, byte> _ram = new();

        public uint Read32(uint address) =>
            (uint)(ReadByte(address) | (ReadByte(address + 1) << 8) | (ReadByte(address + 2) << 16) |
                   (ReadByte(address + 3) << 24));

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
}
