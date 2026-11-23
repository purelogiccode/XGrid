using System.Collections.Generic;
using System.IO;
using XGrid.Audio.Dsp;
using Xunit;

namespace XGrid.Audio.Tests;

/// <summary>
/// GP/EP frame-glue tests (xemu 0.8.139 <c>gp_ep.c</c> / <c>dsp.c</c>): the
/// sub-window memory map, <c>proc_rst_write</c>, the scatter/gather scratch and
/// FIFO windows, the peripheral registers, the DMA wiring, the frame loop and
/// the EP sample sink.
/// </summary>
public sealed class McpxApuDspTests
{
    private const uint SgeTable = 0x00100000;
    private const uint ScratchPage = 0x00200000;
    private const uint FifoPage = 0x00300000;

    // Global APU registers the glue reads.
    private const uint GpScratchAddress = 0x2040;
    private const uint GpFifoAddress = 0x2044;
    private const uint EpFifoAddress = 0x204C;
    private const uint GpScratchMaxSge = 0x20D4;
    private const uint GpFifoMaxSge = 0x20D8;
    private const uint EpFifoMaxSge = 0x20E0;

    // Global FIFO ring registers.
    private const uint GpOutputFifoBase = 0x3024;
    private const uint GpOutputFifoEnd = 0x3028;
    private const uint GpOutputFifoCurrent = 0x302C;
    private const uint EpOutputFifoBase = 0x4024;
    private const uint EpOutputFifoEnd = 0x4028;
    private const uint EpOutputFifoCurrent = 0x402C;

    // Peripheral-space addresses.
    private const uint DmaControlAddress = 0xFFFFD6;

    private const uint DmaControlWrite = (1u << 1) | (2u << 10);
    private const uint DmaControlWriteScratch = (1u << 1) | (0xFu << 5) | (2u << 10);

    [Fact]
    public void Window_MapsGpMemoryAndTheMixBuffer()
    {
        var gp = new McpxApuDsp(new FakeMemory(), new FakeRegisters(), isGp: true);

        gp.WriteWindow(0x0000, 0x123456);
        Assert.Equal(0x123456u, gp.Core.ReadMemory(Dsp56300State.SpaceX, 0));
        Assert.Equal(0x123456u, gp.ReadWindow(0x0000));

        gp.WriteWindow(0x5000, 0x654321);
        Assert.Equal(0x654321u, gp.Core.ReadMemory(Dsp56300State.SpaceX, McpxApuDsp.MixBufferBase));
        Assert.Equal(0x654321u, gp.ReadWindow(0x5000));

        gp.WriteWindow(0x6000, 0xABCDEF);
        Assert.Equal(0xABCDEFu, gp.Core.ReadMemory(Dsp56300State.SpaceY, 0));
        Assert.Equal(0xABCDEFu, gp.ReadWindow(0x6000));

        gp.WriteWindow(0xA000, 0x00F00D);
        Assert.Equal(0x00F00Du, gp.Core.ReadMemory(Dsp56300State.SpaceP, 0));
        Assert.Equal(0x00F00Du, gp.ReadWindow(0xA000));

        // 0x4000-0x4FFF is neither memory nor the reset register: it is the
        // processor's own register file (only GPRST at 0xFFFC is meaningful).
        gp.WriteWindow(0x4000, 0x5A5A5A);
        Assert.Equal(0x5A5A5Au, gp.ReadWindow(0x4000));
    }

    [Fact]
    public void Window_EpHasTheSmallerXAndYWindows()
    {
        var ep = new McpxApuDsp(new FakeMemory(), new FakeRegisters(), isGp: false);

        ep.WriteWindow(0x2FFC, 0x111111);
        Assert.Equal(0x111111u, ep.Core.ReadMemory(Dsp56300State.SpaceX, 0xBFF));

        ep.WriteWindow(0x3000, 0x222222);
        Assert.Equal(0x222222u, ep.ReadWindow(0x3000));

        ep.WriteWindow(0x63FC, 0x333333);
        Assert.Equal(0x333333u, ep.Core.ReadMemory(Dsp56300State.SpaceY, 0xFF));

        ep.WriteWindow(0x6400, 0x444444);
        Assert.Equal(0x444444u, ep.ReadWindow(0x6400));

        ep.WriteWindow(0xA000, 0x555555);
        Assert.Equal(0x555555u, ep.Core.ReadMemory(Dsp56300State.SpaceP, 0));
    }

    [Fact]
    public void ResetWrite_ClearingARunBitResetsTheCore()
    {
        var gp = new McpxApuDsp(new FakeMemory(), new FakeRegisters(), isGp: true);
        gp.Core.State.Pc = 0x123;
        gp.Core.State.Registers[Dsp56300State.RegA1] = 0x42;

        gp.WriteWindow(McpxApuDsp.ResetRegister, 0x2);

        Assert.Equal(0u, gp.Core.State.Pc);
        Assert.Equal(0u, gp.Core.State.Registers[Dsp56300State.RegA1]);
        Assert.False(gp.IsRunning);
        Assert.Equal(0x2u, gp.ResetValue);
    }

    [Fact]
    public void ResetWrite_RisingEdgeBootstrapsTheProgramFromTheScratchWindow()
    {
        var memory = new FakeMemory();
        var registers = new FakeRegisters();
        var gp = new McpxApuDsp(memory, registers, isGp: true);
        registers.Write(GpScratchAddress, SgeTable);
        registers.Write(GpScratchMaxSge, 1);
        memory.Write32(SgeTable, ScratchPage);

        // The boot ROM leaves a dirty high byte in each scratch word.
        memory.Write32(ScratchPage, 0xAB000001);
        memory.Write32(ScratchPage + 4, 0x00000002);

        gp.WriteWindow(McpxApuDsp.ResetRegister, 0x3);

        Assert.True(gp.IsRunning);
        Assert.Equal(0x000001u, gp.Core.State.Pram[0]);
        Assert.Equal(0x000002u, gp.Core.State.Pram[1]);
    }

    [Fact]
    public void ScratchWrite_CrossesPagesThroughTheSgeTable()
    {
        var memory = new FakeMemory();
        var registers = new FakeRegisters();
        var gp = new McpxApuDsp(memory, registers, isGp: true);
        registers.Write(GpScratchAddress, SgeTable);
        registers.Write(GpScratchMaxSge, 1);
        memory.Write32(SgeTable, ScratchPage);
        memory.Write32(SgeTable + 8, ScratchPage + 0x1000);

        for (uint i = 0; i < 8; i++)
        {
            gp.Core.WriteMemory(Dsp56300State.SpaceX, i, 0x111111u * (i + 1));
        }

        // A scratch block at address 0xFF0: the first word starts in page 0,
        // the rest continues in page 1 (xemu scatter_gather_rw).
        WriteDmaBlock(gp, DmaControlWriteScratch, itemCount: 8, dspOffset: 0, scratchOffset: 0xFF0);
        gp.Core.WriteMemory(Dsp56300State.SpaceX, DmaControlAddress, 1);

        Assert.Equal(0x111111u, memory.Read32(ScratchPage + 0xFF0));
        Assert.Equal(0x222222u, memory.Read32(ScratchPage + 0xFF4));
        Assert.Equal(0x444444u, memory.Read32(ScratchPage + 0xFFC));
        Assert.Equal(0x555555u, memory.Read32(ScratchPage + 0x1000));
        Assert.Equal(0x777777u, memory.Read32(ScratchPage + 0x1008));
        Assert.Equal(0, (int)gp.ScratchOutOfRange);
    }

    [Fact]
    public void FifoWrite_WrapsTheRingAndUpdatesTheCurrentRegister()
    {
        var memory = new FakeMemory();
        var registers = new FakeRegisters();
        var gp = new McpxApuDsp(memory, registers, isGp: true);
        registers.Write(GpFifoAddress, SgeTable);
        registers.Write(GpFifoMaxSge, 2);
        memory.Write32(SgeTable + (1 * 8), FifoPage);
        memory.Write32(SgeTable + (2 * 8), FifoPage + 0x1000);
        registers.Write(GpOutputFifoBase, 0x001000);
        registers.Write(GpOutputFifoEnd, 0x002000);
        registers.Write(GpOutputFifoCurrent, 0x001FF0);

        for (uint i = 0; i < 8; i++)
        {
            gp.Core.WriteMemory(Dsp56300State.SpaceX, i, 0x111111u * (i + 1));
        }

        WriteDmaBlock(gp, DmaControlWrite, itemCount: 8, dspOffset: 0, scratchOffset: 0);
        gp.Core.WriteMemory(Dsp56300State.SpaceX, DmaControlAddress, 1);

        Assert.Equal(0x111111u, memory.Read32(FifoPage + 0xFF0));
        Assert.Equal(0x444444u, memory.Read32(FifoPage + 0xFFC));
        Assert.Equal(0x555555u, memory.Read32(FifoPage));
        Assert.Equal(0x888888u, memory.Read32(FifoPage + 0xC));
        Assert.Equal(0x001010u, registers.Read(GpOutputFifoCurrent) & 0x00FFFFFF);
        Assert.Equal(0, (int)gp.FifoOutOfRange);
    }

    [Fact]
    public void Frame_RunsTheMicrocodeUntilItHalts()
    {
        var gp = new McpxApuDsp(new FakeMemory(), new FakeRegisters(), isGp: true);
        gp.Realtime = true;

        // movep x:0,x:$04 writes X:0 to peripheral 0xFFFFC4, the halt register.
        gp.Core.WriteMemory(Dsp56300State.SpaceP, 0, 0x08C084);
        gp.Core.WriteMemory(Dsp56300State.SpaceP, 1, 0x0C0000);
        gp.Core.WriteMemory(Dsp56300State.SpaceX, 0, 1);

        gp.RunFrame();

        Assert.True(gp.Core.State.IsIdle);
        Assert.Equal(1, (int)gp.Frames);
        Assert.Equal(4u, gp.Core.State.CycleCount);
        Assert.Equal(1u << 1, gp.Interrupts & (1u << 1));
        Assert.Equal(0, (int)gp.FrameCycleLimitHits);
    }

    [Fact]
    public void Frame_StopsAtTheCycleCeilingForARunawayProgram()
    {
        var gp = new McpxApuDsp(new FakeMemory(), new FakeRegisters(), isGp: true)
        {
            FrameCycleLimit = 3000,
        };

        gp.Core.WriteMemory(Dsp56300State.SpaceP, 0, 0x0C0000);

        gp.RunFrame();

        Assert.Equal(1, (int)gp.FrameCycleLimitHits);
        Assert.Equal(1, (int)gp.Frames);
        Assert.False(gp.Core.State.IsIdle);
    }

    [Fact]
    public void Frame_NonRealtimeRunsOneSlice()
    {
        var gp = new McpxApuDsp(new FakeMemory(), new FakeRegisters(), isGp: true)
        {
            Realtime = false,
        };
        gp.Core.WriteMemory(Dsp56300State.SpaceP, 0, 0x0C0000);

        gp.RunFrame();

        Assert.Equal(1000u, gp.Core.State.CycleCount);
        Assert.Equal(0, (int)gp.FrameCycleLimitHits);
    }

    [Fact]
    public void Pair_RunsTheEpOncePerEightFrames()
    {
        var pair = new McpxApuDspPair(new FakeMemory(), new FakeRegisters());
        pair.Gp.Realtime = false;
        pair.Ep.Realtime = false;

        pair.WriteWindow(McpxApuDsp.GpWindowBase + McpxApuDsp.ResetRegister, 0x3);
        pair.WriteWindow(McpxApuDsp.EpWindowBase + McpxApuDsp.ResetRegister, 0x3);
        pair.Gp.Core.WriteMemory(Dsp56300State.SpaceP, 0, 0x0C0000);
        pair.Ep.Core.WriteMemory(Dsp56300State.SpaceP, 0, 0x0C0000);

        for (uint frame = 0; frame < 16; frame++)
        {
            pair.Frame(frame, default);
        }

        Assert.Equal(16, (int)pair.Gp.Frames);
        Assert.Equal(2, (int)pair.Ep.Frames);
    }

    [Fact]
    public void Pair_WritesTheMixBinsIntoTheGpMixBuffer()
    {
        var pair = new McpxApuDspPair(new FakeMemory(), new FakeRegisters());
        float[] mixBins = new float[McpxApuDsp.MixBinCount * McpxApuDsp.SamplesPerFrame];
        mixBins[0] = 1f;
        mixBins[(31 * McpxApuDsp.SamplesPerFrame) + 31] = -1f;

        pair.Frame(0, mixBins);

        Assert.Equal(0x7FFFFFu, pair.Gp.Core.ReadMemory(Dsp56300State.SpaceX, McpxApuDsp.MixBufferBase));
        Assert.Equal(
            0x800000u,
            pair.Gp.Core.ReadMemory(Dsp56300State.SpaceX, McpxApuDsp.MixBufferBase + 0x3FF));
        Assert.Equal(0x7FFFFFu, pair.Gp.ReadWindow(0x5000));
    }

    [Fact]
    public void EpSink_ConsumesOutputFifoZeroAndPushesSilence()
    {
        var memory = new FakeMemory();
        var registers = new FakeRegisters();
        byte[]? sunk = null;
        var ep = new McpxApuDsp(memory, registers, isGp: false, (buffer, offset, length) =>
        {
            sunk = buffer[offset..(offset + length)];
            return true;
        });

        registers.Write(EpFifoAddress, SgeTable);
        registers.Write(EpFifoMaxSge, 2);
        memory.Write32(SgeTable + (1 * 8), FifoPage);
        registers.Write(EpOutputFifoBase, 0x001000);
        registers.Write(EpOutputFifoEnd, 0x001100);
        registers.Write(EpOutputFifoCurrent, 0x001000);

        for (uint i = 0; i < 8; i++)
        {
            ep.Core.WriteMemory(Dsp56300State.SpaceX, i, 0x111111u * (i + 1));
        }

        WriteDmaBlock(ep, DmaControlWrite, itemCount: 8, dspOffset: 0, scratchOffset: 0);
        ep.Core.WriteMemory(Dsp56300State.SpaceX, DmaControlAddress, 1);

        Assert.NotNull(sunk);
        Assert.Equal(32, sunk!.Length);
        Assert.Equal(0x11, sunk[0]);
        Assert.Equal(0x88, sunk[28]);
        Assert.Equal(32, (int)ep.SunkBytes);

        // The guest buffer receives silence, and the ring advances.
        Assert.Equal(0u, memory.Read32(FifoPage));
        Assert.Equal(0u, memory.Read32(FifoPage + 0x1C));
        Assert.Equal(0x001020u, registers.Read(EpOutputFifoCurrent) & 0x00FFFFFF);
    }

    [Fact]
    public void EpSink_DeclinedKeepsTheSamplesInGuestMemory()
    {
        var memory = new FakeMemory();
        var registers = new FakeRegisters();
        var ep = new McpxApuDsp(memory, registers, isGp: false, (_, _, _) => false);

        registers.Write(EpFifoAddress, SgeTable);
        registers.Write(EpFifoMaxSge, 2);
        memory.Write32(SgeTable + (1 * 8), FifoPage);
        registers.Write(EpOutputFifoBase, 0x001000);
        registers.Write(EpOutputFifoEnd, 0x001100);
        registers.Write(EpOutputFifoCurrent, 0x001000);

        for (uint i = 0; i < 8; i++)
        {
            ep.Core.WriteMemory(Dsp56300State.SpaceX, i, 0x111111u * (i + 1));
        }

        WriteDmaBlock(ep, DmaControlWrite, itemCount: 8, dspOffset: 0, scratchOffset: 0);
        ep.Core.WriteMemory(Dsp56300State.SpaceX, DmaControlAddress, 1);

        Assert.Equal(0, (int)ep.SunkBytes);
        Assert.Equal(0x111111u, memory.Read32(FifoPage));
        Assert.Equal(0x888888u, memory.Read32(FifoPage + 0x1C));
    }

    [Fact]
    public void Peripheral_ReportsTheDmaEndOfListAndInterruptWord()
    {
        var gp = new McpxApuDsp(new FakeMemory(), new FakeRegisters(), isGp: true);
        WriteDmaBlock(gp, DmaControlWrite, itemCount: 1, dspOffset: 0, scratchOffset: 0);

        gp.Core.WriteMemory(Dsp56300State.SpaceX, DmaControlAddress, 1);

        Assert.True(gp.Dma.Eol);
        Assert.Equal(0x80u, gp.Core.ReadMemory(Dsp56300State.SpaceX, 0xFFFFC5) & 0x80u);

        // Writing the EOL bit to the interrupt register clears it.
        gp.Core.WriteMemory(Dsp56300State.SpaceX, 0xFFFFC5, 0x80);
        Assert.False(gp.Dma.Eol);
        Assert.Equal(0u, gp.Core.ReadMemory(Dsp56300State.SpaceX, 0xFFFFC5) & 0x80u);
    }

    [Theory]
    [InlineData(0f, 0u)]
    [InlineData(0.5f, 0x400000u)]
    [InlineData(1f, 0x7FFFFFu)]
    [InlineData(-1f, 0x800000u)]
    [InlineData(2f, 0x7FFFFFu)]
    [InlineData(-2f, 0x800000u)]
    public void FloatTo24_MatchesTheReferenceClamp(float value, uint expected)
    {
        Assert.Equal(expected, McpxApuDsp.FloatTo24(value));
    }

    [Fact]
    public void FloatTo24_NaNIsZero()
    {
        Assert.Equal(0u, McpxApuDsp.FloatTo24(float.NaN));
    }

    [Fact]
    public void Snapshot_RoundTripsTheDspCoreState()
    {
        var memory = new FakeMemory();
        var first = new McpxApuDevice(memory) { ApuDspCore = true };
        first.Write(McpxApuDevice.BaseAddress + 0x3FFFC, 4, 0x3);
        first.Write(McpxApuDevice.BaseAddress + 0x30000, 4, 0x123456);
        first.Advance(4_000_000);
        byte[] baseline = Save(first);

        var second = new McpxApuDevice(new FakeMemory()) { ApuDspCore = true };
        Load(second, baseline);

        Assert.Equal(baseline, Save(second));
        Assert.Equal(0x123456u, second.Read(McpxApuDevice.BaseAddress + 0x30000, 4));
    }

    [Fact]
    public void Snapshot_DspPayloadIsSkippedWithoutTheCore()
    {
        var first = new McpxApuDevice(new FakeMemory()) { ApuDspCore = true };
        first.Write(McpxApuDevice.BaseAddress + 0x3FFFC, 4, 0x3);
        byte[] baseline = Save(first);

        var second = new McpxApuDevice(new FakeMemory());
        Load(second, baseline);

        Assert.False(second.ApuDspCore);
    }

    [Fact]
    public void Device_RoutesTheSubWindowsWhenTheCoreIsEnabled()
    {
        var memory = new FakeMemory();
        var apu = new McpxApuDevice(memory) { ApuDspCore = true };
        const uint Base = McpxApuDevice.BaseAddress;

        apu.Write(Base + 0x30000, 4, 0x654321);
        apu.Write(Base + 0x3FFFC, 4, 0x3);

        // The bootstrap loads program memory only; the X window survives.
        Assert.True(apu.GpRunning);
        Assert.Equal(0x654321u, apu.Read(Base + 0x30000, 4));

        // P:0 halts through the peripheral space, P:1 would loop.
        apu.Write(Base + 0xA000, 4, 0x08C084);
        apu.Write(Base + 0xA004, 4, 0x0C0000);
        apu.Write(Base + 0x30000, 4, 1);
        apu.Advance(4_000_000);

        Assert.Equal(1u, apu.Read(Base + 0x30000, 4));
        Assert.Equal(0x08C084u, apu.Read(Base + 0xA000, 4));
        Assert.Equal(0x3u, apu.Read(Base + 0x3FFFC, 4));
    }

    [Fact]
    public void Device_DefaultPathKeepsTheBridgeWindow()
    {
        var apu = new McpxApuDevice(new FakeMemory());

        apu.Write(McpxApuDevice.BaseAddress + 0x30000, 4, 0x1234);

        Assert.False(apu.ApuDspCore);
        Assert.Equal(0x1234u, apu.Read(McpxApuDevice.BaseAddress + 0x30000, 4));
    }

    /// <summary>
    /// Builds an EOL DMA block at P:0 (xemu <c>dsp_dma.c</c> node layout) and
    /// leaves the control write to the caller.
    /// </summary>
    private static void WriteDmaBlock(
        McpxApuDsp dsp,
        uint control,
        int itemCount,
        uint dspOffset,
        uint scratchOffset)
    {
        const uint Eol = 0x4000;
        dsp.Core.WriteMemory(Dsp56300State.SpaceP, 0, Eol);
        dsp.Core.WriteMemory(Dsp56300State.SpaceP, 1, control);
        dsp.Core.WriteMemory(Dsp56300State.SpaceP, 2, (uint)itemCount);
        dsp.Core.WriteMemory(Dsp56300State.SpaceP, 3, dspOffset);
        dsp.Core.WriteMemory(Dsp56300State.SpaceP, 4, scratchOffset);
        dsp.Core.WriteMemory(Dsp56300State.SpaceP, 5, 0);
        dsp.Core.WriteMemory(Dsp56300State.SpaceP, 6, 0xFFF);
        dsp.Dma.NextBlock = 0x2800;
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

    private sealed class FakeRegisters : IApuDspRegisters
    {
        private readonly uint[] _regs = new uint[0x20000];

        public uint Read(uint offset)
        {
            return _regs[offset >> 2];
        }

        public void Write(uint offset, uint value)
        {
            _regs[offset >> 2] = value;
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
