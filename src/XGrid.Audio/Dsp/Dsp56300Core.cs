// SPDX-License-Identifier: GPL-2.0-or-later
// Ported from xemu 0.8.139 hw/xbox/mcpx/apu/dsp/interp/dsp_cpu.c.
// Original copyright (c) 2015 espes; (c) 2020-2025 Matt Borgerson.
// Adapted from Hatari DSP M56001 emulation (C) 2001-2008 ARAnyM developer team.

using System;
using System.Runtime.InteropServices;

namespace XGrid.Audio.Dsp;

/// <summary>
/// DSP56300 core: state, reset, memory map, the bootstrap path and the
/// instruction interpreter (ported from the xemu C interpreter; see the
/// <c>Dsp56300Core.*</c> partial files).
/// </summary>
public sealed partial class Dsp56300Core
{
    /// <summary>Initializes a new instance of the <see cref="Dsp56300Core"/> class.</summary>
    /// <param name="isGp">True for the GP DSP, false for the EP DSP.</param>
    public Dsp56300Core(bool isGp)
    {
        State = new Dsp56300State(isGp);
        State.Reset();
        InitializeTables();
    }

    /// <summary>Gets the core state.</summary>
    public Dsp56300State State { get; }

    /// <summary>Resets the core.</summary>
    public void Reset()
    {
        State.Reset();
    }

    /// <summary>
    /// Loads the DSP program RAM from the APU scratch window and strips the
    /// high byte the boot ROM leaves in each word (xemu <c>dsp_c_bootstrap</c>).
    /// </summary>
    /// <param name="scratch">The scratch bytes (little-endian words).</param>
    /// <returns>The number of words whose high byte was cleared.</returns>
    public int Bootstrap(ReadOnlySpan<byte> scratch)
    {
        const int BootstrapWords = 0x800;
        const int BootstrapBytes = BootstrapWords * 4;
        if (scratch.Length < BootstrapBytes)
        {
            throw new ArgumentException("The DSP bootstrap scratch is shorter than 0x800 words.", nameof(scratch));
        }

        Span<byte> pramBytes = MemoryMarshal.AsBytes(State.Pram.AsSpan());
        scratch[..BootstrapBytes].CopyTo(pramBytes);

        int cleared = 0;
        for (int i = 0; i < BootstrapWords; i++)
        {
            if ((State.Pram[i] & 0xFF000000) != 0)
            {
                State.Pram[i] &= 0x00FFFFFF;
                cleared++;
            }
        }

        InvalidateOpcodeCache();
        return cleared;
    }

    /// <summary>Reads a DSP memory word.</summary>
    /// <param name="space">The memory space (<see cref="Dsp56300State.SpaceX"/>,
    /// <see cref="Dsp56300State.SpaceY"/> or <see cref="Dsp56300State.SpaceP"/>).</param>
    /// <param name="address">The word address.</param>
    /// <returns>The word value.</returns>
    public uint ReadMemory(int space, uint address)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(address, 0x01000000u, nameof(address));

        switch (space)
        {
            case Dsp56300State.SpaceX:
                if (address >= Dsp56300State.PeriphBase)
                {
                    return State.ReadPeripheral is null ? 0 : State.ReadPeripheral(address);
                }

                if (address >= Dsp56300State.MixBufferBase
                    && address < Dsp56300State.MixBufferBase + Dsp56300State.MixBufferSize)
                {
                    return State.MixBuffer[address - Dsp56300State.MixBufferBase];
                }

                if (address >= 0xC00 && address < 0xC00 + Dsp56300State.MixBufferSize)
                {
                    return State.MixBuffer[address - 0xC00];
                }

                return address < Dsp56300State.XramSize ? State.Xram[address] : 0x00FFFFFF;
            case Dsp56300State.SpaceY:
                return address < Dsp56300State.YramSize ? State.Yram[address] : 0;
            case Dsp56300State.SpaceP:
                return address < Dsp56300State.PramSize ? State.Pram[address] : 0;
            default:
                throw new ArgumentOutOfRangeException(nameof(space));
        }
    }

    /// <summary>Writes a DSP memory word.</summary>
    /// <param name="space">The memory space.</param>
    /// <param name="address">The word address.</param>
    /// <param name="value">The word value.</param>
    public void WriteMemory(int space, uint address, uint value)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(address, 0x01000000u, nameof(address));

        switch (space)
        {
            case Dsp56300State.SpaceX:
                if (address >= Dsp56300State.PeriphBase)
                {
                    State.WritePeripheral?.Invoke(address, value);
                    return;
                }

                if (address >= Dsp56300State.MixBufferBase
                    && address < Dsp56300State.MixBufferBase + Dsp56300State.MixBufferSize)
                {
                    State.MixBuffer[address - Dsp56300State.MixBufferBase] = value;
                    return;
                }

                if (address >= 0xC00 && address < 0xC00 + Dsp56300State.MixBufferSize)
                {
                    State.MixBuffer[address - 0xC00] = value;
                    return;
                }

                ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(address, (uint)Dsp56300State.XramSize, nameof(address));
                State.Xram[address] = value;
                return;
            case Dsp56300State.SpaceY:
                ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(address, (uint)Dsp56300State.YramSize, nameof(address));
                State.Yram[address] = value;
                return;
            case Dsp56300State.SpaceP:
                WriteMemoryP(address, value);
                return;
            default:
                throw new ArgumentOutOfRangeException(nameof(space));
        }
    }

    /// <summary>Posts an interrupt to the pending table.</summary>
    /// <param name="interrupt">The interrupt index (0-3).</param>
    public void AddInterrupt(int interrupt)
    {
        if ((uint)interrupt >= (uint)State.InterruptIpl.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(interrupt));
        }

        if (State.InterruptIpl[interrupt] == -1)
        {
            return;
        }

        if (State.InterruptIsPending[interrupt] == 0)
        {
            State.InterruptIsPending[interrupt] = 1;
            State.InterruptCounter++;
        }
    }

    /// <summary>Executes one instruction.</summary>
    public void Step()
    {
        ExecuteInstruction();
    }
}
