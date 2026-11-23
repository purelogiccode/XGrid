using System;
using XGrid.Audio.Dsp;
using Xunit;

namespace XGrid.Audio.Tests;

/// <summary>
/// Port-fidelity tests for the xemu 0.8.139 DSP56300 core slice.  The
/// instruction interpreter is not ported yet; these pin the state, memory map,
/// bootstrap and 56-bit arithmetic that the interpreter will build on.
/// </summary>
public sealed class Dsp56300PortTests
{
    [Fact]
    public void Reset_UsesReferenceDefaults()
    {
        var core = new Dsp56300Core(isGp: false);

        Assert.Equal(0u, core.State.Pc);
        Assert.Equal(0x02u, core.State.Registers[Dsp56300State.RegOmr]);
        for (int i = 0; i < 8; i++)
        {
            Assert.Equal(0x00FFFFu, core.State.Registers[Dsp56300State.RegM0 + i]);
        }

        Assert.Equal(Dsp56300State.InterruptNone, core.State.InterruptState);
        Assert.All(core.State.InterruptIpl, ipl => Assert.Equal((short)3, ipl));
        Assert.False(core.State.IsIdle);
    }

    [Fact]
    public void MemoryMap_MixBufferAliasAndPeripheral()
    {
        var core = new Dsp56300Core(isGp: false);
        core.State.ReadPeripheral = _ => 0xA5A5A5u;

        core.WriteMemory(Dsp56300State.SpaceX, Dsp56300State.MixBufferBase, 0x123456);
        Assert.Equal(0x123456u, core.ReadMemory(Dsp56300State.SpaceX, Dsp56300State.MixBufferBase));
        Assert.Equal(0x123456u, core.ReadMemory(Dsp56300State.SpaceX, 0xC00));

        Assert.Equal(0x00FFFFFFu, core.ReadMemory(Dsp56300State.SpaceX, 0x1000));
        Assert.Equal(0xA5A5A5u, core.ReadMemory(Dsp56300State.SpaceX, Dsp56300State.PeriphBase));
    }

    [Fact]
    public void Bootstrap_LoadsPramAndClearsHighByte()
    {
        var core = new Dsp56300Core(isGp: true);
        byte[] scratch = new byte[0x800 * 4];

        // 0xAB000001 and 0x00000002 in little-endian words.
        scratch[0] = 0x01;
        scratch[3] = 0xAB;
        scratch[4] = 0x02;

        int cleared = core.Bootstrap(scratch);

        Assert.Equal(1, cleared);
        Assert.Equal(0x000001u, core.State.Pram[0]);
        Assert.Equal(0x000002u, core.State.Pram[1]);
    }

    [Fact]
    public void AddInterrupt_TracksPendingAndCounter()
    {
        var core = new Dsp56300Core(isGp: false);

        core.AddInterrupt(Dsp56300State.InterReset);
        core.AddInterrupt(Dsp56300State.InterReset);

        Assert.Equal(1, core.State.InterruptIsPending[Dsp56300State.InterReset]);
        Assert.Equal(1, core.State.InterruptCounter);

        core.State.InterruptIpl[Dsp56300State.InterTrap] = -1;
        core.AddInterrupt(Dsp56300State.InterTrap);

        Assert.Equal(0, core.State.InterruptIsPending[Dsp56300State.InterTrap]);
        Assert.Equal(1, core.State.InterruptCounter);
    }

    [Fact]
    public void Arithmetic_AddSubShiftAndSignExtend()
    {
        Span<uint> source = stackalloc uint[3];
        Span<uint> dest = stackalloc uint[3];

        source[2] = 1;
        dest[2] = 1;
        _ = Dsp56300Arithmetic.Add56(source, dest);
        Assert.Equal(2u, dest[2]);

        _ = Dsp56300Arithmetic.Sub56(source, dest);
        Assert.Equal(1u, dest[2]);

        _ = Dsp56300Arithmetic.Asl56(dest, 1);
        Assert.Equal(2u, dest[2]);

        _ = Dsp56300Arithmetic.Asr56(dest, 1);
        Assert.Equal(1u, dest[2]);

        Assert.Equal(0xFFFFFFFFu, Dsp56300Arithmetic.SignExtend(8, 0xFF));
        Assert.Equal(0xFF800000u, Dsp56300Arithmetic.SignExtend(24, 0x800000));
    }

    [Fact]
    public void InvalidateOpcodeCache_KeepsTheNopSentinel()
    {
        // A bootstrap (and a P-memory write) invalidates the decode cache; the
        // tag-cache sentinel must survive it, because 0x000000 is `nop`, not an
        // empty slot. The frame glue hit this on the real dashboard microcode:
        // every nop decoded as undefined and the PC never advanced.
        var core = new Dsp56300Core(isGp: true);
        core.InvalidateOpcodeCache();
        core.State.Pram[0] = 0x000000;
        core.State.Pram[1] = 0x0C0000;

        core.Step();

        Assert.Equal(0, (int)core.UndefinedInstructions);
        Assert.Equal(1u, core.State.Pc);
    }

    [Fact]
    public void Dma_WritesFifoFromXMemory()
    {
        var core = new Dsp56300Core(isGp: false);
        byte[] captured = [];
        int capturedIndex = -1;

        var dma = new DspDma(
            core.ReadMemory,
            core.WriteMemory,
            (_, _, _, _, _) => { },
            (buffer, offset, index, length, write) =>
            {
                if (!write)
                {
                    return;
                }

                capturedIndex = index;
                captured = buffer[offset..(offset + length)];
            });

        // Block at P:0, EOL next, direction=write, buffer 0, 16-bit format,
        // two items from X:0.
        core.WriteMemory(Dsp56300State.SpaceP, 0, 0x4000);
        core.WriteMemory(Dsp56300State.SpaceP, 1, 0x0402);
        core.WriteMemory(Dsp56300State.SpaceP, 2, 2);
        core.WriteMemory(Dsp56300State.SpaceP, 3, 0);
        core.WriteMemory(Dsp56300State.SpaceP, 4, 0);
        core.WriteMemory(Dsp56300State.SpaceP, 5, 0);
        core.WriteMemory(Dsp56300State.SpaceP, 6, 0);
        core.WriteMemory(Dsp56300State.SpaceX, 0, 0x1234);
        core.WriteMemory(Dsp56300State.SpaceX, 1, 0x5678);

        dma.NextBlock = 0x2800;
        dma.Write(DspDmaRegister.Control, DspDma.ControlActionStart);

        Assert.Equal(0, capturedIndex);
        Assert.Equal(new byte[] { 0x12, 0x00, 0x56, 0x00 }, captured);
        Assert.True(dma.Eol);
    }
}
