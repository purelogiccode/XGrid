// SPDX-License-Identifier: GPL-2.0-or-later
// Ported from xemu 0.8.139 hw/xbox/mcpx/apu/dsp/interp/dsp_cpu.c and
// hw/xbox/mcpx/apu/dsp/dsp_c.c (dsp_c_sync_to_vm / dsp_c_sync_from_vm).
// Original copyright (c) 2015 espes; (c) 2020-2025 Matt Borgerson.

using System;
using System.IO;

namespace XGrid.Audio.Dsp;

/// <summary>
/// DSP56300 core snapshot support: the complete architectural state, including
/// the carried cycle budget, so a restore resumes bit-identically.
/// </summary>
public sealed partial class Dsp56300Core
{
    private const int SnapshotVersion = 1;

    /// <summary>Saves the complete core state.</summary>
    /// <param name="writer">The snapshot writer.</param>
    public void SaveState(BinaryWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.Write(SnapshotVersion);
        writer.Write(State.IsIdle);
        writer.Write(State.CycleCount);
        writer.Write(State.InstrCycle);
        writer.Write(State.Pc);
        writer.Write(State.LoopRep);
        writer.Write(State.PcOnRep);
        writer.Write(State.CurrentInstruction);
        writer.Write(State.CurrentInstructionLength);
        writer.Write(State.InterruptState);
        writer.Write(State.InterruptInstrFetch);
        writer.Write(State.InterruptSavePc);
        writer.Write(State.InterruptCounter);
        writer.Write(State.InterruptIplToRaise);
        writer.Write(State.InterruptPipelineCount);
        writer.Write(State.NumInstructions);
        writer.Write(_saveCycles);

        for (int i = 0; i < State.InterruptIpl.Length; i++)
        {
            writer.Write(State.InterruptIpl[i]);
            writer.Write(State.InterruptIsPending[i]);
        }

        WriteWords(writer, State.Registers);
        WriteWords(writer, State.Stack[0]);
        WriteWords(writer, State.Stack[1]);
        WriteWords(writer, State.Xram);
        WriteWords(writer, State.Yram);
        WriteWords(writer, State.Pram);
        WriteWords(writer, State.MixBuffer);
        WriteWords(writer, State.Periph);
    }

    /// <summary>Restores the complete core state and drops the decode cache.</summary>
    /// <param name="reader">The snapshot reader.</param>
    public void LoadState(BinaryReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        int version = reader.ReadInt32();
        if (version != SnapshotVersion)
        {
            throw new InvalidDataException($"Unsupported DSP core snapshot version {version}.");
        }

        State.IsIdle = reader.ReadBoolean();
        State.CycleCount = reader.ReadUInt32();
        State.InstrCycle = reader.ReadUInt16();
        State.Pc = reader.ReadUInt32();
        State.LoopRep = reader.ReadUInt32();
        State.PcOnRep = reader.ReadBoolean();
        State.CurrentInstruction = reader.ReadUInt32();
        State.CurrentInstructionLength = reader.ReadUInt32();
        State.InterruptState = reader.ReadUInt16();
        State.InterruptInstrFetch = reader.ReadUInt16();
        State.InterruptSavePc = reader.ReadUInt16();
        State.InterruptCounter = reader.ReadUInt16();
        State.InterruptIplToRaise = reader.ReadUInt16();
        State.InterruptPipelineCount = reader.ReadUInt16();
        State.NumInstructions = reader.ReadUInt32();
        _saveCycles = reader.ReadInt32();

        for (int i = 0; i < State.InterruptIpl.Length; i++)
        {
            State.InterruptIpl[i] = reader.ReadInt16();
            State.InterruptIsPending[i] = reader.ReadUInt16();
        }

        ReadWords(reader, State.Registers);
        ReadWords(reader, State.Stack[0]);
        ReadWords(reader, State.Stack[1]);
        ReadWords(reader, State.Xram);
        ReadWords(reader, State.Yram);
        ReadWords(reader, State.Pram);
        ReadWords(reader, State.MixBuffer);
        ReadWords(reader, State.Periph);
        InvalidateOpcodeCache();
    }

    private static void WriteWords(BinaryWriter writer, uint[] words)
    {
        for (int i = 0; i < words.Length; i++)
        {
            writer.Write(words[i]);
        }
    }

    private static void ReadWords(BinaryReader reader, uint[] words)
    {
        for (int i = 0; i < words.Length; i++)
        {
            words[i] = reader.ReadUInt32();
        }
    }
}
