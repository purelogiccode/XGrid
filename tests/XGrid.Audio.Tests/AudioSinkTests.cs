using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using XGrid.Audio.Dsp;
using Xunit;

namespace XGrid.Audio.Tests;

/// <summary>
/// Host PCM sink tests (P1.4): the EP output FIFO feeds an
/// <see cref="IAudioBackend"/>, and the WAVE backend writes a real RIFF file.
/// </summary>
public sealed class AudioSinkTests
{
    private const uint Base = McpxApuDevice.BaseAddress;
    private const uint SgeTable = 0x00100000;
    private const uint FifoPage = 0x00300000;

    private const uint EpFifoAddress = 0x204C;
    private const uint EpFifoMaxSge = 0x20E0;
    private const uint EpOutputFifoBase = 0x4024;
    private const uint EpOutputFifoEnd = 0x4028;
    private const uint EpOutputFifoCurrent = 0x402C;
    private const uint DmaControlAddress = 0xFFFFD6;

    [Fact]
    public void WaveBackend_WritesARiffFileWithThePcm()
    {
        string path = Path.Combine(Path.GetTempPath(), $"xgrid-audio-{Guid.NewGuid():N}.wav");
        try
        {
            var backend = new WaveAudioBackend(path);
            backend.Submit([1, 2, 3, 4, 5, 6, 7, 8]);
            Assert.Equal(8, (int)backend.BytesWritten);
            Assert.Equal(2, (int)backend.Frames);
            backend.Dispose();

            byte[] file = File.ReadAllBytes(path);
            Assert.Equal(52, file.Length);
            Assert.Equal("RIFF", Encoding.ASCII.GetString(file, 0, 4));
            Assert.Equal(44u, BitConverter.ToUInt32(file, 4));
            Assert.Equal("WAVE", Encoding.ASCII.GetString(file, 8, 4));
            Assert.Equal("fmt ", Encoding.ASCII.GetString(file, 12, 4));
            Assert.Equal(16u, BitConverter.ToUInt32(file, 16));
            Assert.Equal(1, BitConverter.ToUInt16(file, 20));
            Assert.Equal(2, BitConverter.ToUInt16(file, 22));
            Assert.Equal(48000u, BitConverter.ToUInt32(file, 24));
            Assert.Equal(192000u, BitConverter.ToUInt32(file, 28));
            Assert.Equal(4, BitConverter.ToUInt16(file, 32));
            Assert.Equal(16, BitConverter.ToUInt16(file, 34));
            Assert.Equal("data", Encoding.ASCII.GetString(file, 36, 4));
            Assert.Equal(8u, BitConverter.ToUInt32(file, 40));
            Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8], file[44..]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void VoiceStream_ProducesPcmThroughTheHostSink()
    {
        var memory = new FakeMemory();
        var sink = new RecordingBackend();
        var apu = new McpxApuDevice(memory);
        apu.AudioBackend = sink;
        apu.ApuDspCore = true;

        // The EP output FIFO 0 ring lives at guest 0x001000 and the SGE table
        // maps page 1 onto the sample page (xemu ep_fifo_rw).
        apu.Write(Base + EpFifoAddress, 4, SgeTable);
        apu.Write(Base + EpFifoMaxSge, 4, 2);
        memory.Write32(SgeTable + 8, FifoPage);
        apu.Write(Base + EpOutputFifoBase, 4, 0x001000);
        apu.Write(Base + EpOutputFifoEnd, 4, 0x001100);
        apu.Write(Base + EpOutputFifoCurrent, 4, 0x001000);

        // A 16-sample stereo "voice": a 16-bit ramp in EP X memory.
        byte[] expected = new byte[32];
        for (uint i = 0; i < 16; i++)
        {
            ushort sample = (ushort)(i * 0x0800);
            expected[(i * 2) + 0] = (byte)sample;
            expected[(i * 2) + 1] = (byte)(sample >> 8);
            apu.Write(Base + 0x50000 + (i * 4), 4, (uint)sample << 8);
        }

        // A DMA block at EP P:0 writes 16 16-bit items to output FIFO 0.
        uint[] block =
        [
            0x4000,             // next block: end of list
            0x402,              // write, buffer 0, format 1 (16-bit)
            16,                 // item count
            0,                  // DSP offset: X:0
            0,                  // scratch offset
            0,                  // scratch base
            0xFFF,              // scratch size - 1
        ];
        for (int i = 0; i < block.Length; i++)
        {
            apu.Write(Base + 0x5A000 + ((uint)i * 4), 4, block[i]);
        }

        McpxApuDsp ep = apu.Dsp!.Ep;
        ep.Dma.NextBlock = 0x2800;
        ep.Core.WriteMemory(Dsp56300State.SpaceX, DmaControlAddress, 1);

        Assert.Equal(expected, sink.Bytes);
        Assert.Equal(32, (int)ep.SunkBytes);

        // The samples are sunk, so the guest ring receives silence and the
        // current pointer advances by the transferred length.
        Assert.Equal(0u, memory.Read32(FifoPage));
        Assert.Equal(0u, memory.Read32(FifoPage + 0x1C));
        Assert.Equal(0x001020u, apu.Read(Base + EpOutputFifoCurrent, 4) & 0x00FFFFFF);
    }

    [Fact]
    public void AudioBackend_WithoutTheCoreKeepsTheBridgeSilent()
    {
        var sink = new RecordingBackend();
        var apu = new McpxApuDevice(new FakeMemory()) { AudioBackend = sink };

        apu.Advance(8_000_000);

        Assert.False(apu.ApuDspCore);
        Assert.Empty(sink.Bytes);
    }

    private sealed class RecordingBackend : IAudioBackend
    {
        private readonly List<byte> _bytes = [];

        public byte[] Bytes => _bytes.ToArray();

        public void Submit(ReadOnlySpan<byte> samples)
        {
            foreach (byte value in samples)
            {
                _bytes.Add(value);
            }
        }
    }

    private sealed class FakeMemory : IApuMemory
    {
        private readonly Dictionary<uint, byte> _ram = [];

        public uint Read32(uint address)
        {
            return (uint)(ReadByte(address)
                | (ReadByte(address + 1) << 8)
                | (ReadByte(address + 2) << 16)
                | (ReadByte(address + 3) << 24));
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

        private byte ReadByte(uint address)
        {
            return _ram.GetValueOrDefault(address, (byte)0);
        }
    }
}
