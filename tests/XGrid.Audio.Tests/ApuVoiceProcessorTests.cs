using System.Collections.Generic;
using System.IO;
using Xunit;

namespace XGrid.Audio.Tests;

public sealed class ApuVoiceProcessorTests
{
    private const uint Base = McpxApuDevice.BaseAddress;

    private const uint FenAddr = 0x115C;
    private const uint FetForce1 = 0x1504;
    private const uint IsTs = 0x1000;
    private const uint VpVaddr = 0x202C;
    private const uint VpSslAddr = 0x2034;

    private const uint PioSetAntecedentVoice = 0x20120;
    private const uint PioVoiceOn = 0x20124;
    private const uint PioVoiceOff = 0x20128;
    private const uint PioSetCurrentVoice = 0x202F8;
    private const uint PioSetVoiceCfgFmt = 0x20304;
    private const uint PioSetVoiceSslA = 0x20320;
    private const uint PioSetVoiceSslB = 0x2035C;
    private const uint PioSetVoiceCfgBufLbo = 0x203A4;
    private const uint PioSetVoiceCfgBufEbo = 0x203DC;
    private const uint PioSetCurrentSsl = 0x20190;
    private const uint PioSslSegmentOffset = 0x20600;
    private const uint PioSslSegmentLength = 0x20604;

    private const uint TvL2D = 0x2054;

    private const uint VoiceCfgFmt = 0x04;
    private const uint VoiceParState = 0x54;
    private const uint VoiceParOffset = 0x58;
    private const uint VoiceTarPitchLink = 0x7C;

    private const uint VoiceStateActive = 1u << 21;
    private const uint FmtDataType = 1u << 24;
    private const uint FmtLoop = 1u << 25;

    private const uint Fevintsts = 1u << 6;
    private const uint Fenintsts = 1u << 5;
    private const uint Se2FeIdleVoice = 0x8000;

    private const uint VoicePage = 0x00200000;
    private const uint NotifyBase = 0x00300000;
    private const uint SslTable = 0x00400000;
    private const uint SegmentData = 0x00500000;

    private const ulong OneFrame = 4_000_000;

    [Fact]
    public void VoiceOn_AddsVoiceToTopOfListAndActivates()
    {
        var memory = new FakeMemory();
        var apu = Create(memory);
        apu.Write(Base + PioSetAntecedentVoice, 4, 1 << 16);
        apu.Write(Base + PioVoiceOn, 4, 0x40);

        Assert.Equal(0x40u, apu.Read(Base + TvL2D, 4));
        Assert.Equal(VoiceStateActive, memory.Read32(VoicePage + (0x40 * 0x80) + VoiceParState) & VoiceStateActive);
        Assert.Equal(0u, memory.Read32(VoicePage + (0x40 * 0x80) + VoiceParOffset));
    }

    [Fact]
    public void VoiceOn_InheritInsertsAfterAntecedent()
    {
        var memory = new FakeMemory();
        var apu = Create(memory);
        apu.Write(Base + TvL2D, 4, 0xFFFF);
        apu.Write(Base + PioSetAntecedentVoice, 4, 1 << 16);
        apu.Write(Base + PioVoiceOn, 4, 0x40);
        apu.Write(Base + PioSetAntecedentVoice, 4, 0x40);
        apu.Write(Base + PioVoiceOn, 4, 0x41);

        Assert.Equal(0x41u, memory.Read32(VoicePage + (0x40 * 0x80) + VoiceTarPitchLink) & 0xFFFF);
        Assert.Equal(0xFFFFu, memory.Read32(VoicePage + (0x41 * 0x80) + VoiceTarPitchLink) & 0xFFFF);
    }

    [Fact]
    public void FrameAdvance_AdvancesBufferVoiceByFrameSamples()
    {
        var memory = new FakeMemory();
        var apu = Create(memory);
        ActivateBufferVoice(apu, 0x40, ebo: 0x1000);

        apu.Advance(OneFrame);
        Assert.Equal(256u, memory.Read32(VoicePage + (0x40 * 0x80) + VoiceParOffset));

        apu.Advance(OneFrame);
        Assert.Equal(512u, memory.Read32(VoicePage + (0x40 * 0x80) + VoiceParOffset));
    }

    [Fact]
    public void FrameAdvance_LoopingBufferWrapsToLoopBegin()
    {
        var memory = new FakeMemory();
        var apu = Create(memory);
        ActivateBufferVoice(apu, 0x40, ebo: 0x180, loop: true);
        memory.Write32(VoicePage + (0x40 * 0x80) + 0x24, 0x40);

        apu.Advance(OneFrame);
        Assert.Equal(0x100u, memory.Read32(VoicePage + (0x40 * 0x80) + VoiceParOffset));

        apu.Advance(OneFrame);
        Assert.Equal(0x40u, memory.Read32(VoicePage + (0x40 * 0x80) + VoiceParOffset));
        Assert.Equal(VoiceStateActive, memory.Read32(VoicePage + (0x40 * 0x80) + VoiceParState) & VoiceStateActive);
    }

    [Fact]
    public void FrameAdvance_NonLoopingBufferEndsVoiceAndNotifies()
    {
        var memory = new FakeMemory();
        var apu = Create(memory);
        ActivateBufferVoice(apu, 0x40, ebo: 0x100);

        apu.Advance(OneFrame);

        Assert.Equal(0u, memory.Read32(VoicePage + (0x40 * 0x80) + VoiceParState) & VoiceStateActive);
        Assert.Equal(1, memory.Read8(NotifyBase + (16u * (2 + (0x40 * 4))) + 15));
        Assert.Equal(Fevintsts | Fenintsts, apu.InterruptStatus & (Fevintsts | Fenintsts));
        Assert.Equal(1, apu.VoiceOffCompletions);
    }

    [Fact]
    public void FrameAdvance_InactiveLinkedVoiceSendsIdleVoiceEveryFrame()
    {
        var memory = new FakeMemory();
        var apu = Create(memory);
        apu.Write(Base + FetForce1, 4, 1u << 15);
        apu.Write(Base + TvL2D, 4, 0xFFFF);
        apu.Write(Base + PioSetAntecedentVoice, 4, 1 << 16);
        apu.Write(Base + PioVoiceOn, 4, 0x40);
        apu.Write(Base + PioVoiceOff, 4, 0x40);

        apu.Advance(OneFrame);

        Assert.Equal(Se2FeIdleVoice, apu.Read(Base + 0x1300, 4));
        Assert.Equal(0x40u, apu.Read(Base + 0x1304, 4));
    }

    [Fact]
    public void Stream_SegmentCompletionFlipsSslAndNotifies()
    {
        var memory = new FakeMemory();
        var apu = Create(memory);
        const uint voice = 0x40;
        memory.Write32(SslTable, SegmentData);
        memory.Write32(SslTable + 4, 0x100);
        ActivateStreamVoice(apu, voice, sslACount: 1);

        apu.Advance(OneFrame);

        Assert.Equal(1, apu.SegmentCompletions);
        Assert.Equal(1, memory.Read8(NotifyBase + (16u * (2 + (voice * 4))) + 15));
        Assert.Equal(0u, memory.Read32(VoicePage + (voice * 0x80) + VoiceParOffset));
        Assert.Equal(VoiceStateActive, memory.Read32(VoicePage + (voice * 0x80) + VoiceParState) & VoiceStateActive);
    }

    [Fact]
    public void Stream_EndedStreamDeliversIdleVoiceAndUsesSslIndexNotifier()
    {
        var memory = new FakeMemory();
        var apu = Create(memory);
        const uint voice = 0x40;
        apu.Write(Base + FetForce1, 4, 1u << 15);
        memory.Write32(SslTable, SegmentData);
        memory.Write32(SslTable + 4, 0x100);
        ActivateStreamVoice(apu, voice, sslACount: 1);

        // Frame 1: completes SSL A and flips to SSL B (count 0).
        apu.Advance(OneFrame);
        // Frame 2: SSL B is empty, so the stream ends and the voice turns off.
        apu.Advance(OneFrame);

        Assert.Equal(0u, memory.Read32(VoicePage + (voice * 0x80) + VoiceParState) & VoiceStateActive);
        Assert.Equal(1, memory.Read8(NotifyBase + (16u * (2 + (voice * 4))) + 15));

        // The idle voice is reported on the next frame (xemu voice_off/SE2FE timing).
        apu.Advance(OneFrame);
        Assert.Equal(Se2FeIdleVoice, apu.Read(Base + 0x1300, 4));
        Assert.Equal(voice, apu.Read(Base + 0x1304, 4));
    }

    [Fact]
    public void VoiceOff_StreamVoiceNotifiesCurrentSsl()
    {
        var memory = new FakeMemory();
        var apu = Create(memory);
        const uint voice = 0x40;
        memory.Write32(SslTable, SegmentData);
        memory.Write32(SslTable + 4, 0x100);
        ActivateStreamVoice(apu, voice, sslACount: 1);
        apu.Advance(OneFrame);

        apu.Write(Base + PioVoiceOff, 4, voice);

        Assert.Equal(1, memory.Read8(NotifyBase + (16u * (2 + (voice * 4) + 1)) + 15));
    }

    [Fact]
    public void SslSegmentWrite_LandsInGuestMemory()
    {
        var memory = new FakeMemory();
        var apu = Create(memory);
        apu.Write(Base + PioSetCurrentSsl, 4, 0x40);
        apu.Write(Base + PioSslSegmentOffset, 4, 0x00ABCD00);
        apu.Write(Base + PioSslSegmentLength, 4, 0x00000100);

        Assert.Equal(0x00ABCD00u, memory.Read32(SslTable + (0x40 * 8)));
        Assert.Equal(0x00000100u, memory.Read32(SslTable + (0x40 * 8) + 4));
    }

    [Fact]
    public void Snapshot_RoundTripsVoiceProcessorState()
    {
        static void Configure(McpxApuDevice apu, FakeMemory memory)
        {
            memory.Write32(SslTable, SegmentData);
            memory.Write32(SslTable + 4, 0x100);
            ActivateStreamVoice(apu, 0x40, sslACount: 1);
            apu.Advance(OneFrame);
        }

        var firstMemory = new FakeMemory();
        var first = new McpxApuDevice(firstMemory);
        Configure(first, firstMemory);
        byte[] baseline = Save(first);

        var second = new McpxApuDevice(new FakeMemory());
        Load(second, baseline);
        Assert.Equal(baseline, Save(second));
        Assert.Equal(first.SegmentCompletions, second.SegmentCompletions);
        Assert.Equal(first.NotifierWrites, second.NotifierWrites);
    }

    private static McpxApuDevice Create(FakeMemory memory)
    {
        var apu = new McpxApuDevice(memory);
        apu.Write(Base + VpVaddr, 4, VoicePage);
        apu.Write(Base + VpSslAddr, 4, SslTable);
        apu.Write(Base + FenAddr, 4, NotifyBase);
        apu.Write(Base + 0x2054, 4, 0xFFFF);
        apu.Write(Base + 0x2060, 4, 0xFFFF);
        apu.Write(Base + 0x206C, 4, 0xFFFF);
        return apu;
    }

    private static void ActivateBufferVoice(McpxApuDevice apu, uint voice, uint ebo, bool loop = false)
    {
        apu.Write(Base + TvL2D, 4, 0xFFFF);
        apu.Write(Base + PioSetAntecedentVoice, 4, 1 << 16);
        apu.Write(Base + PioSetCurrentVoice, 4, voice);
        apu.Write(Base + PioSetVoiceCfgFmt, 4, loop ? FmtLoop : 0);
        apu.Write(Base + PioSetVoiceCfgBufEbo, 4, ebo);
        apu.Write(Base + PioVoiceOn, 4, voice);
    }

    private static void ActivateStreamVoice(McpxApuDevice apu, uint voice, byte sslACount)
    {
        apu.Write(Base + TvL2D, 4, 0xFFFF);
        apu.Write(Base + PioSetAntecedentVoice, 4, 1 << 16);
        apu.Write(Base + PioSetCurrentVoice, 4, voice);
        apu.Write(Base + PioSetVoiceCfgFmt, 4, FmtDataType);
        apu.Write(Base + PioSetVoiceSslA, 4, ((uint)sslACount) | (0u << 8));
        apu.Write(Base + PioSetVoiceSslB, 4, 0);
        apu.Write(Base + PioVoiceOn, 4, voice);
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

        public byte Read8(uint address) => _ram.GetValueOrDefault(address, (byte)0);

        private byte ReadByte(uint address) => _ram.GetValueOrDefault(address, (byte)0);
    }
}
