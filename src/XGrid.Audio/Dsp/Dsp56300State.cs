// SPDX-License-Identifier: GPL-2.0-or-later
// Ported from xemu 0.8.139 hw/xbox/mcpx/apu/dsp/interp/dsp_cpu.h and
// hw/xbox/mcpx/apu/dsp/interp/dsp_cpu_regs.h.
// Original copyright (c) 2015 espes; (c) 2020-2025 Matt Borgerson.
// Adapted from Hatari DSP M56001 emulation (C) 2001-2008 ARAnyM developer team.

using System;

namespace XGrid.Audio.Dsp;

/// <summary>
/// DSP56300 core state, register indices and memory sizes.
/// </summary>
/// <remarks>
/// The layout follows the xemu 0.8.139 C interpreter so a ported instruction
/// handler can be checked against the reference state directly.
/// </remarks>
public sealed class Dsp56300State
{
    /// <summary>Number of architectural registers.</summary>
    public const int RegisterCount = 0x40;

    /// <summary>X memory size in words.</summary>
    public const int XramSize = 4096;

    /// <summary>Y memory size in words.</summary>
    public const int YramSize = 2048;

    /// <summary>P memory size in words.</summary>
    public const int PramSize = 4096;

    /// <summary>Mix-buffer base in X memory.</summary>
    public const uint MixBufferBase = 0x001400;

    /// <summary>Mix-buffer size in words.</summary>
    public const int MixBufferSize = 1024;

    /// <summary>Peripheral-space base in X memory.</summary>
    public const uint PeriphBase = 0xFFFF80;

    /// <summary>Peripheral-space size in words.</summary>
    public const int PeriphSize = 128;

    /// <summary>X memory space id.</summary>
    public const int SpaceX = 0;

    /// <summary>Y memory space id.</summary>
    public const int SpaceY = 1;

    /// <summary>P memory space id.</summary>
    public const int SpaceP = 2;

    // Register indices (dsp_cpu_regs.h).
    /// <summary>X0 register index.</summary>
    public const int RegX0 = 0x04;

    /// <summary>X1 register index.</summary>
    public const int RegX1 = 0x05;

    /// <summary>Y0 register index.</summary>
    public const int RegY0 = 0x06;

    /// <summary>Y1 register index.</summary>
    public const int RegY1 = 0x07;

    /// <summary>A0 register index.</summary>
    public const int RegA0 = 0x08;

    /// <summary>B0 register index.</summary>
    public const int RegB0 = 0x09;

    /// <summary>A2 register index.</summary>
    public const int RegA2 = 0x0A;

    /// <summary>B2 register index.</summary>
    public const int RegB2 = 0x0B;

    /// <summary>A1 register index.</summary>
    public const int RegA1 = 0x0C;

    /// <summary>B1 register index.</summary>
    public const int RegB1 = 0x0D;

    /// <summary>A register index.</summary>
    public const int RegA = 0x0E;

    /// <summary>B register index.</summary>
    public const int RegB = 0x0F;

    /// <summary>R0 register index.</summary>
    public const int RegR0 = 0x10;

    /// <summary>N0 register index.</summary>
    public const int RegN0 = 0x18;

    /// <summary>M0 register index.</summary>
    public const int RegM0 = 0x20;

    /// <summary>Status register index.</summary>
    public const int RegSr = 0x39;

    /// <summary>Operating mode register index.</summary>
    public const int RegOmr = 0x3A;

    /// <summary>Stack pointer register index.</summary>
    public const int RegSp = 0x3B;

    /// <summary>Stack high register index.</summary>
    public const int RegSsh = 0x3C;

    /// <summary>Stack low register index.</summary>
    public const int RegSsl = 0x3D;

    /// <summary>Loop address register index.</summary>
    public const int RegLa = 0x3E;

    /// <summary>Loop counter register index.</summary>
    public const int RegLc = 0x3F;

    // Status-register bit positions.
    /// <summary>Carry bit.</summary>
    public const int SrC = 0;

    /// <summary>Overflow bit.</summary>
    public const int SrV = 1;

    /// <summary>Zero bit.</summary>
    public const int SrZ = 2;

    /// <summary>Negative bit.</summary>
    public const int SrN = 3;

    /// <summary>Unnormalized bit.</summary>
    public const int SrU = 4;

    /// <summary>Extension bit.</summary>
    public const int SrE = 5;

    /// <summary>Limit bit.</summary>
    public const int SrL = 6;

    /// <summary>Scaling mode 0 bit.</summary>
    public const int SrS0 = 10;

    /// <summary>Scaling mode 1 bit.</summary>
    public const int SrS1 = 11;

    /// <summary>Sixteen-bit compatibility mode bit.</summary>
    public const int SrSc = 13;

    /// <summary>Double-precision mode bit.</summary>
    public const int SrDm = 14;

    /// <summary>Loop flag bit.</summary>
    public const int SrLf = 15;

    /// <summary>Fractional mode bit.</summary>
    public const int SrFv = 16;

    // Interrupt state.
    /// <summary>No interrupt is being processed.</summary>
    public const int InterruptNone = 0;

    /// <summary>Interrupts are disabled.</summary>
    public const int InterruptDisabled = 1;

    /// <summary>A long interrupt is being processed.</summary>
    public const int InterruptLong = 2;

    /// <summary>Reset interrupt vector.</summary>
    public const int InterReset = 0;

    /// <summary>Illegal-instruction interrupt vector.</summary>
    public const int InterIllegal = 1;

    /// <summary>Stack-error interrupt vector.</summary>
    public const int InterStackError = 2;

    /// <summary>Trap interrupt vector.</summary>
    public const int InterTrap = 3;

    /// <summary>Initializes a new instance of the <see cref="Dsp56300State"/> class.</summary>
    /// <param name="isGp">True for the GP DSP, false for the EP DSP.</param>
    public Dsp56300State(bool isGp)
    {
        IsGp = isGp;
    }

    /// <summary>Gets a value indicating whether this is the GP DSP.</summary>
    public bool IsGp { get; }

    /// <summary>Gets or sets a value indicating whether the DSP is idle.</summary>
    public bool IsIdle { get; set; }

    /// <summary>Gets or sets the executed cycle count.</summary>
    public uint CycleCount { get; set; }

    /// <summary>Gets or sets the current instruction cycle count.</summary>
    public ushort InstrCycle { get; set; }

    /// <summary>Gets or sets the program counter.</summary>
    public uint Pc { get; set; }

    /// <summary>Gets the architectural register file.</summary>
    public uint[] Registers { get; } = new uint[RegisterCount];

    /// <summary>Gets the stack, indexed by [high/low][depth].</summary>
    public uint[][] Stack { get; } =
    [
        new uint[16],
        new uint[16],
    ];

    /// <summary>Gets the X memory.</summary>
    public uint[] Xram { get; } = new uint[XramSize];

    /// <summary>Gets the Y memory.</summary>
    public uint[] Yram { get; } = new uint[YramSize];

    /// <summary>Gets the P memory.</summary>
    public uint[] Pram { get; } = new uint[PramSize];

    /// <summary>Gets the mix buffer.</summary>
    public uint[] MixBuffer { get; } = new uint[MixBufferSize];

    /// <summary>Gets the peripheral window.</summary>
    public uint[] Periph { get; } = new uint[PeriphSize];

    /// <summary>Gets or sets the REP loop count.</summary>
    public uint LoopRep { get; set; }

    /// <summary>Gets or sets a value indicating whether the PC is on a REP instruction.</summary>
    public bool PcOnRep { get; set; }

    /// <summary>Gets or sets the current instruction word.</summary>
    public uint CurrentInstruction { get; set; }

    /// <summary>Gets or sets the current instruction length.</summary>
    public uint CurrentInstructionLength { get; set; }

    /// <summary>Gets or sets the interrupt state.</summary>
    public ushort InterruptState { get; set; }

    /// <summary>Gets or sets the interrupt vector currently being fetched.</summary>
    public ushort InterruptInstrFetch { get; set; }

    /// <summary>Gets or sets the saved PC for an interrupt.</summary>
    public ushort InterruptSavePc { get; set; }

    /// <summary>Gets or sets the pending-interrupt count.</summary>
    public ushort InterruptCounter { get; set; }

    /// <summary>Gets or sets the IPL level to raise.</summary>
    public ushort InterruptIplToRaise { get; set; }

    /// <summary>Gets or sets the interrupt pipeline count.</summary>
    public ushort InterruptPipelineCount { get; set; }

    /// <summary>Gets the per-level interrupt IPL values.</summary>
    public short[] InterruptIpl { get; } = new short[4];

    /// <summary>Gets the per-level pending flags.</summary>
    public ushort[] InterruptIsPending { get; } = new ushort[4];

    /// <summary>Gets or sets the instruction counter.</summary>
    public uint NumInstructions { get; set; }

    /// <summary>Gets or sets a value indicating whether illegal instructions assert.</summary>
    public bool ExceptionDebugging { get; set; }

    /// <summary>Gets or sets the peripheral read callback.</summary>
    public Func<uint, uint>? ReadPeripheral { get; set; }

    /// <summary>Gets or sets the peripheral write callback.</summary>
    public Action<uint, uint>? WritePeripheral { get; set; }

    /// <summary>Resets the core to the DSP56300 reset state.</summary>
    public void Reset()
    {
        Array.Clear(Periph);
        Array.Clear(Stack[0]);
        Array.Clear(Stack[1]);
        Array.Clear(Registers);

        Pc = 0;
        Registers[RegOmr] = 0x02;
        for (int i = 0; i < 8; i++)
        {
            Registers[RegM0 + i] = 0x00FFFF;
        }

        Array.Clear(InterruptIsPending);
        InterruptState = InterruptNone;
        InterruptInstrFetch = 0xFFFF;
        InterruptSavePc = 0xFFFF;
        InterruptCounter = 0;
        InterruptPipelineCount = 0;
        for (int i = 0; i < InterruptIpl.Length; i++)
        {
            InterruptIpl[i] = 3;
        }

        LoopRep = 0;
        PcOnRep = false;
        CurrentInstruction = 0;
        CurrentInstructionLength = 0;
        NumInstructions = 0;
        InstrCycle = 0;
        CycleCount = 0;
        IsIdle = false;
        ExceptionDebugging = true;
    }
}
