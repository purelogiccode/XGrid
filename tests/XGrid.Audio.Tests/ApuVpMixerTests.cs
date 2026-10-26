using System.Collections.Generic;
using System.IO;
using XGrid.Audio.Dsp;
using XGrid.Audio.Vp;
using Xunit;

namespace XGrid.Audio.Tests;

/// <summary>
/// The ported voice-processor mix path (TM8, xemu 0.8.139 <c>apu/vp/vp.c</c>):
/// the ADPCM block decoder, the sample fetch, the resampler, the envelopes and
/// the mix-bin accumulation the GP DSP microcode consumes.
/// </summary>
public sealed class ApuVpMixerTests
{
    private const uint Base = McpxApuDevice.BaseAddress;

    private const uint FenAddr = 0x115C;
    private const uint VpVaddr = 0x202C;
    private const uint VpSgeAddr = 0x2030;
    private const uint VpSslAddr = 0x2034;
    private const uint TvL2D = 0x2054;
    private const uint TvL3D = 0x2060;
    private const uint TvLmp = 0x206C;

    private const uint PioSetAntecedentVoice = 0x20120;
    private const uint PioVoiceOn = 0x20124;
    private const uint PioSetCurrentVoice = 0x202F8;
    private const uint PioSetVoiceCfgVbin = 0x20300;
    private const uint PioSetVoiceCfgFmt = 0x20304;
    private const uint PioSetVoiceCfgEnv0 = 0x20308;
    private const uint PioSetVoiceCfgEnva = 0x2030C;
    private const uint PioSetVoiceSslA = 0x20320;
    private const uint PioSetVoiceTarHrtf = 0x2031C;
    private const uint PioSetVoiceTarVola = 0x20360;
    private const uint PioSetVoiceTarVolb = 0x20364;
    private const uint PioSetVoiceTarVolc = 0x20368;
    private const uint PioSetVoiceTarPitch = 0x2037C;
    private const uint PioSetVoiceCfgBufBase = 0x203A0;
    private const uint PioSetVoiceCfgBufEbo = 0x203DC;
    private const uint PioSetSubmixHeadroom = 0x20200;
    private const uint PioSetHrtfSubmixes = 0x202C0;
    private const uint PioSetSslSegmentOffset = 0x20600;
    private const uint PioSetSslSegmentLength = 0x20604;

    private const uint VoiceParState = 0x54;
    private const uint VoiceParOffset = 0x58;

    private const uint VoicePage = 0x00200000;
    private const uint SgeTable = 0x00300000;
    private const uint NotifyBase = 0x00380000;
    private const uint SslTable = 0x00400000;
    private const uint SamplePage = 0x00500000;

    private const uint OneFrame = 4_000_000;

    // Mono 16-bit PCM in 16-bit containers: sample size S16 (1) and container B16 (1).
    private const uint FmtMonoS16 = (1u << 28) | (1u << 30);

    [Fact]
    public void AdpcmDecode_ProducesTheReferenceSamples()
    {
        byte[] block = new byte[36];
        block[0] = 0x00;
        block[1] = 0x10; // initial sample 0x1000
        block[2] = 0;    // step index 0
        block[3] = 0;    // reserved
        block[4] = 0x04; // low nibble 4 (+step), high nibble 0 (+step/8)

        short[] output = new short[65];
        int samples = ApuVpAdpcm.Decode(output, block, 1);

        Assert.Equal(65, samples);
        Assert.Equal(0x1000, output[0]);
        Assert.Equal(4103, output[1]);
        Assert.Equal(4104, output[2]);
    }

    [Fact]
    public void AdpcmDecode_RejectsAnOutOfRangeStepIndex()
    {
        byte[] block = new byte[36];
        block[2] = 89;

        short[] output = new short[65];
        Assert.Equal(0, ApuVpAdpcm.Decode(output, block, 1));
    }

    [Fact]
    public void Mixer_BufferVoiceLandsInTheGpMixBuffer()
    {
        var memory = new FakeMemory();
        McpxApuDevice apu = Create(memory);
        WriteConstantBuffer(memory, SamplePage, 0x4000, 0x1000);
        ActivateBufferVoice(apu, 0x40, ebo: 0x1000);

        apu.Advance(OneFrame);

        // A 16-bit sample of 0x4000 is 0.5; the unity gain keeps it at 0.5,
        // which the mix-buffer conversion stores as 0.5 * 8 * 0x100000.
        Assert.Equal(0x400000u, ReadMixBin(apu, 0, 0));
        Assert.Equal(0x400000u, ReadMixBin(apu, 0, ApuVpFrame.SamplesPerFrame - 1));

        // Eight 32-sample voice-processor frames advance the buffer offset by
        // one XGrid frame of 256 samples.
        Assert.Equal(256u, memory.Read32(VoicePage + (0x40 * 0x80) + VoiceParOffset) & 0xFFFFFF);
    }

    [Fact]
    public void Mixer_ZeroVolumeSilencesTheBin()
    {
        var memory = new FakeMemory();
        McpxApuDevice apu = Create(memory);
        WriteConstantBuffer(memory, SamplePage, 0x4000, 0x1000);
        ActivateBufferVoice(apu, 0x40, ebo: 0x1000, volume0: 0xFFF);

        apu.Advance(OneFrame);

        Assert.Equal(0u, ReadMixBin(apu, 0, 0));
    }

    [Fact]
    public void Mixer_SubmixHeadroomScalesTheBin()
    {
        var memory = new FakeMemory();
        McpxApuDevice apu = Create(memory);
        WriteConstantBuffer(memory, SamplePage, 0x4000, 0x1000);
        ActivateBufferVoice(apu, 0x40, ebo: 0x1000);

        // Two bits of headroom quarter the gain (xemu submix_headroom).
        apu.Write(Base + PioSetSubmixHeadroom, 4, 2);

        apu.Advance(OneFrame);

        Assert.Equal(0x100000u, ReadMixBin(apu, 0, 0));
    }

    [Fact]
    public void Mixer_PitchedVoiceConsumesSourceSamplesFaster()
    {
        var memory = new FakeMemory();
        McpxApuDevice apu = Create(memory);
        WriteConstantBuffer(memory, SamplePage, 0x4000, 0x4000);
        ActivateBufferVoice(apu, 0x40, ebo: 0x4000);

        // Pitch +4096 is one octave up: two source samples per output sample.
        apu.Write(Base + PioSetCurrentVoice, 4, 0x40);
        apu.Write(Base + PioSetVoiceTarPitch, 4, 0x1000_0000);

        apu.Advance(OneFrame);

        // Two source frames are held as resampler lookahead, then two are
        // consumed per output frame.
        Assert.Equal(514u, memory.Read32(VoicePage + (0x40 * 0x80) + VoiceParOffset) & 0xFFFFFF);
        Assert.Equal(0x400000u, ReadMixBin(apu, 0, 0));
    }

    [Fact]
    public void Mixer_AttackEnvelopeRampsTheLevelThenHolds()
    {
        var memory = new FakeMemory();
        McpxApuDevice apu = Create(memory);
        WriteConstantBuffer(memory, SamplePage, 0x4000, 0x1000);

        // VOICE_ON with ENVA = attack (2), attack rate 1 -> 16 counter steps,
        // and a hold time of 1 (16 counter steps) so the hold phase persists.
        apu.Write(Base + PioSetAntecedentVoice, 4, 1 << 16);
        apu.Write(Base + PioSetCurrentVoice, 4, 0x40);
        apu.Write(Base + PioSetVoiceCfgFmt, 4, FmtMonoS16);
        apu.Write(Base + PioSetVoiceCfgEnv0, 4, 1);
        apu.Write(Base + PioSetVoiceCfgEnva, 4, 1u << 12);
        apu.Write(Base + PioSetVoiceCfgBufEbo, 4, 0x1000);
        apu.Write(Base + PioVoiceOn, 4, 0x40 | (2u << 28));
        SetVolumes(apu, 0, 0xFFF, 0xFFF, 0xFFF, 0xFFF, 0xFFF, 0xFFF, 0xFFF);

        // 16 counter steps: the last one writes 15 * 255 / 16.
        apu.Advance(OneFrame);
        apu.Advance(OneFrame);
        Assert.Equal(239u, memory.Read32(VoicePage + (0x40 * 0x80) + VoiceParOffset) >> 24);
        Assert.Equal(2u, memory.Read32(VoicePage + (0x40 * 0x80) + VoiceParState) >> 28);

        // The 17th step reaches full scale and advances to hold.
        apu.Advance(OneFrame);
        Assert.Equal(255u, memory.Read32(VoicePage + (0x40 * 0x80) + VoiceParOffset) >> 24);
        Assert.Equal(3u, memory.Read32(VoicePage + (0x40 * 0x80) + VoiceParState) >> 28);
        Assert.Equal(0x400000u, ReadMixBin(apu, 0, 0));
    }

    [Fact]
    public void Mixer_HrtfSubmixRedirectsA3dVoice()
    {
        var memory = new FakeMemory();
        McpxApuDevice apu = Create(memory);
        WriteConstantBuffer(memory, SamplePage, 0x4000, 0x1000);

        // The first four bins of a 3D voice come from the HRTF submix register.
        apu.Write(Base + PioSetHrtfSubmixes, 4, 0x05050505);

        // A voice with no HRTF coefficients is silenced by the HRTF stage, so
        // this voice declares itself non-HRTF (0xFFFF is HRTF_NULL_HANDLE).
        ActivateBufferVoice(
            apu,
            0x10,
            ebo: 0x1000,
            vbin: (0x1Fu << 21) | (0x1Fu << 26),
            fmt: FmtMonoS16 | 0x3FF);
        apu.Write(Base + PioSetVoiceTarHrtf, 4, 0xFFFF);

        apu.Advance(OneFrame);

        Assert.Equal(0x400000u, ReadMixBin(apu, 5, 0));
        Assert.Equal(0u, ReadMixBin(apu, 0, 0));
    }

    [Fact]
    public void Mixer_StreamVoiceWalksTheSslSegmentAndNotifies()
    {
        var memory = new FakeMemory();
        McpxApuDevice apu = Create(memory);

        // SSL A page 1 holds a 256-sample 16-bit segment at SamplePage.
        memory.Write32(SslTable + 8, SamplePage);
        memory.Write32(SslTable + 12, 0x100 | (1 << 16));
        WriteConstantBuffer(memory, SamplePage, 0x4000, 256);

        apu.Write(Base + PioSetAntecedentVoice, 4, 1 << 16);
        apu.Write(Base + PioSetCurrentVoice, 4, 0x40);
        apu.Write(Base + PioSetVoiceCfgFmt, 4, FmtMonoS16 | (1u << 24) | (1u << 23));
        apu.Write(Base + PioSetVoiceSslA, 4, (1u << 8) | 1);
        apu.Write(Base + PioVoiceOn, 4, 0x40);
        SetVolumes(apu, 0, 0xFFF, 0xFFF, 0xFFF, 0xFFF, 0xFFF, 0xFFF, 0xFFF);

        apu.Advance(OneFrame);

        // The 256-sample segment completes inside one XGrid frame.
        Assert.Equal(1, apu.SegmentCompletions);
        Assert.Equal(1, memory.Read8(NotifyBase + (16u * (2 + (0x40 * 4))) + 15));
        Assert.Equal(0x400000u, ReadMixBin(apu, 0, 0));
    }

    [Fact]
    public void Mixer_SslSegmentRegistersLandInGuestMemory()
    {
        var memory = new FakeMemory();
        McpxApuDevice apu = Create(memory);
        apu.Write(Base + PioSetSslSegmentOffset + 0x1F8, 4, SamplePage);
        apu.Write(Base + PioSetSslSegmentLength + 0x1F8, 4, 0x40 | (1 << 16));

        Assert.Equal(SamplePage, memory.Read32(SslTable + (0x3F * 8)));
        Assert.Equal(0x40u | (1 << 16), memory.Read32(SslTable + (0x3F * 8) + 4));
    }

    [Fact]
    public void Mixer_PausedVoiceLeavesTheBinsSilent()
    {
        var memory = new FakeMemory();
        McpxApuDevice apu = Create(memory);
        WriteConstantBuffer(memory, SamplePage, 0x4000, 0x1000);
        ActivateBufferVoice(apu, 0x40, ebo: 0x1000);

        // Active but paused: the voice stays linked and mixes nothing.
        uint state = memory.Read32(VoicePage + (0x40 * 0x80) + VoiceParState);
        memory.Write32(VoicePage + (0x40 * 0x80) + VoiceParState, state | (1u << 18));

        apu.Advance(OneFrame);

        Assert.Equal(0u, ReadMixBin(apu, 0, 0));
        Assert.Equal(0u, memory.Read32(VoicePage + (0x40 * 0x80) + VoiceParOffset));
    }

    [Fact]
    public void Mixer_SnapshotRoundTripsTheMixerState()
    {
        var memory = new FakeMemory();
        McpxApuDevice first = Create(memory);
        WriteConstantBuffer(memory, SamplePage, 0x4000, 0x1000);
        ActivateBufferVoice(first, 0x40, ebo: 0x1000);
        first.Write(Base + PioSetSubmixHeadroom, 4, 3);
        first.Write(Base + PioSetHrtfSubmixes, 4, 0x01020304);
        first.Advance(OneFrame);
        byte[] baseline = Save(first);

        var second = new McpxApuDevice(new FakeMemory()) { ApuDspCore = true };
        Load(second, baseline);

        Assert.Equal(baseline, Save(second));
    }

    private static McpxApuDevice Create(FakeMemory memory)
    {
        var apu = new McpxApuDevice(memory) { ApuDspCore = true };
        apu.Write(Base + VpVaddr, 4, VoicePage);
        apu.Write(Base + VpSgeAddr, 4, SgeTable);
        apu.Write(Base + VpSslAddr, 4, SslTable);
        apu.Write(Base + FenAddr, 4, NotifyBase);
        apu.Write(Base + TvL2D, 4, 0xFFFF);
        apu.Write(Base + TvL3D, 4, 0xFFFF);
        apu.Write(Base + TvLmp, 4, 0xFFFF);

        // SGE entry 0 maps the sample page.
        memory.Write32(SgeTable, SamplePage);
        return apu;
    }

    private static void ActivateBufferVoice(
        McpxApuDevice apu,
        uint voice,
        uint ebo,
        uint volume0 = 0,
        uint vbin = 0,
        uint fmt = FmtMonoS16)
    {
        apu.Write(Base + PioSetAntecedentVoice, 4, 1 << 16);
        apu.Write(Base + PioSetCurrentVoice, 4, voice);
        apu.Write(Base + PioSetVoiceCfgVbin, 4, vbin);
        apu.Write(Base + PioSetVoiceCfgFmt, 4, fmt);
        apu.Write(Base + PioSetVoiceCfgBufBase, 4, 0);
        apu.Write(Base + PioSetVoiceCfgBufEbo, 4, ebo);
        apu.Write(Base + PioVoiceOn, 4, voice);
        SetVolumes(apu, volume0, 0xFFF, 0xFFF, 0xFFF, 0xFFF, 0xFFF, 0xFFF, 0xFFF);
    }

    private static void SetVolumes(
        McpxApuDevice apu,
        uint v0,
        uint v1,
        uint v2,
        uint v3,
        uint v4,
        uint v5,
        uint v6,
        uint v7)
    {
        apu.Write(Base + PioSetVoiceTarVola, 4, (v0 << 4) | (v1 << 20) | (v6 & 0xF) | ((v7 & 0xF) << 16));
        apu.Write(Base + PioSetVoiceTarVolb, 4, (v2 << 4) | (v3 << 20) | ((v6 >> 4) & 0xF) | (((v7 >> 4) & 0xF) << 16));
        apu.Write(Base + PioSetVoiceTarVolc, 4, (v4 << 4) | (v5 << 20) | ((v6 >> 8) & 0xF) | (((v7 >> 8) & 0xF) << 16));
    }

    private static void WriteConstantBuffer(FakeMemory memory, uint address, ushort sample, uint samples)
    {
        uint value = sample | ((uint)sample << 16);
        for (uint i = 0; i < samples; i += 2)
        {
            memory.Write32(address + (i * 2), value);
        }
    }

    private static uint ReadMixBin(McpxApuDevice apu, int bin, int sample)
    {
        return apu.Dsp!.Gp.Core.ReadMemory(
            Dsp56300State.SpaceX,
            McpxApuDsp.MixBufferBase + (uint)((bin * ApuVpFrame.SamplesPerFrame) + sample)) & 0xFFFFFF;
    }

    private static byte[] Save(McpxApuDevice apu)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            apu.SaveState(writer);
        }

        return stream.ToArray();
    }

    private static void Load(McpxApuDevice apu, byte[] state)
    {
        using var stream = new MemoryStream(state);
        using var reader = new BinaryReader(stream);
        apu.LoadState(reader);
    }

    private sealed class FakeMemory : IApuMemory
    {
        private readonly Dictionary<uint, byte> _ram = [];

        public uint Read32(uint address)
        {
            return (uint)(Read8(address)
                | (Read8(address + 1) << 8)
                | (Read8(address + 2) << 16)
                | (Read8(address + 3) << 24));
        }

        public void Write32(uint address, uint value)
        {
            for (int i = 0; i < 4; i++)
            {
                Write8(address + (uint)i, (byte)(value >> (i * 8)));
            }
        }

        public void Write8(uint address, byte value)
        {
            _ram[address] = value;
        }

        public byte Read8(uint address)
        {
            return _ram.GetValueOrDefault(address, (byte)0);
        }
    }
}
