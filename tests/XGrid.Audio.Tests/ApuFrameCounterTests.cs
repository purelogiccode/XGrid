using System.Collections.Generic;
using Xunit;

namespace XGrid.Audio.Tests;

/// <summary>
/// Pins <c>NV_PAPU_XGSCNT</c>: the EP frame counter the guest watches as a
/// playback clock. The reference reads it as
/// <c>ep_frame_div * NUM_SAMPLES_PER_FRAME</c> (xemu 0.8.137
/// <c>hw/xbox/mcpx/apu/apu.c</c> + <c>apu_regs.h</c>: XGSCNT = 0x200C,
/// 32 mixed samples per frame).
/// </summary>
public sealed class ApuFrameCounterTests
{
    private const uint Base = McpxApuDevice.BaseAddress;
    private const uint XgsCnt = 0x200C;

    /// <summary>The voice-processor frame length (McpxApuDevice.FrameCycles).</summary>
    private const ulong FrameCycles = (733333333ul * 256ul) / 48000ul;

    [Fact]
    public void XgscntCountsThirtyTwoSamplesPerFrame()
    {
        var apu = new McpxApuDevice(new FakeMemory());

        Assert.Equal(0u, apu.Read(Base + XgsCnt, 4));

        apu.Advance(FrameCycles);
        Assert.Equal(32u, apu.Read(Base + XgsCnt, 4));

        apu.Advance(FrameCycles * 2);
        Assert.Equal(96u, apu.Read(Base + XgsCnt, 4));
    }

    [Fact]
    public void XgscntDoesNotAdvanceInsideAFrame()
    {
        var apu = new McpxApuDevice(new FakeMemory());

        apu.Advance(FrameCycles - 1);
        Assert.Equal(0u, apu.Read(Base + XgsCnt, 4));

        apu.Advance(1);
        Assert.Equal(32u, apu.Read(Base + XgsCnt, 4));
    }

    [Fact]
    public void XgscntIsDerivedAndIgnoresWrites()
    {
        var apu = new McpxApuDevice(new FakeMemory());
        apu.Advance(FrameCycles);

        apu.Write(Base + XgsCnt, 4, 0xDEADBEEFu);

        Assert.Equal(32u, apu.Read(Base + XgsCnt, 4));
        apu.Advance(FrameCycles);
        Assert.Equal(64u, apu.Read(Base + XgsCnt, 4));
    }

    private sealed class FakeMemory : IApuMemory
    {
        private readonly Dictionary<uint, byte> _ram = new();

        public void Write8(uint address, byte value) => _ram[address] = value;

        public uint Read32(uint address)
        {
            uint value = 0;
            for (int i = 3; i >= 0; i--)
            {
                value = (value << 8) | _ram.GetValueOrDefault(address + (uint)i, (byte)0);
            }

            return value;
        }

        public void Write32(uint address, uint value)
        {
            for (int i = 0; i < 4; i++)
            {
                _ram[address + (uint)i] = (byte)(value >> (i * 8));
            }
        }
    }
}
