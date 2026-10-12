using System;
using System.IO;
using XGrid.Core;

namespace XGrid.Cpu;

/// <summary>V1 snapshot support for the interpreter (plan V1).
/// Captures every mutable field that affects future execution; the software
/// TLB is a derived cache and is flushed (not stored) on load.</summary>
public sealed partial class XboxCpu : ISnapshotable
{
    private const uint CpuStateVersion = 1;

    /// <summary>Saves the complete CPU state to the writer.</summary>
    public void SaveState(BinaryWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);

        XboxContext context = Context;
        writer.Write(CpuStateVersion);

        for (int i = 0; i < 8; i++)
        {
            writer.Write(context.ReadRegister((Gpr)i));
        }

        writer.Write(context.Eip);
        writer.Write(context.Eflags);

        for (int i = 0; i < 6; i++)
        {
            SegmentRegister segment = (SegmentRegister)i;
            writer.Write(context.GetSegmentSelector(segment));
            writer.Write(context.GetSegmentBase(segment));
        }

        writer.Write(context.CodeSegment32);
        writer.Write(context.Tr);
        writer.Write(context.Ldtr);

        writer.Write(context.Cr0);
        writer.Write(context.Cr2);
        writer.Write(context.Cr3);
        writer.Write(context.Cr4);

        for (int i = 0; i < 8; i++)
        {
            writer.Write(context.ReadDebugRegister(i));
        }

        writer.Write(context.Gdtr.Base);
        writer.Write(context.Gdtr.Limit);
        writer.Write(context.Idtr.Base);
        writer.Write(context.Idtr.Limit);

        writer.Write(context.Halted);
        writer.Write(context.Tsc);
        writer.Write(context.Mxcsr);

        FpuState fpu = context.Fpu;
        writer.Write(fpu.ControlWord);
        writer.Write(fpu.StatusWord);
        writer.Write(fpu.TagWord);
        for (int i = 0; i < 8; i++)
        {
            Fpu80 register = fpu.GetPhysicalRegister(i);
            writer.Write(register.SignExponent);
            writer.Write(register.Significand);
        }

        for (int i = 0; i < 8; i++)
        {
            for (int element = 0; element < 4; element++)
            {
                writer.Write(context.ReadXmmElement(i, element));
            }
        }

        writer.Write(_sysenterCs);
        writer.Write(_sysenterEsp);
        writer.Write(_sysenterEip);

        writer.Write(InterruptCount);
        writer.Write(LastFaultVector);
        writer.Write(LastFaultEip);
        writer.Write(LastFaultErrorCode);
    }

    /// <summary>Restores the complete CPU state from the reader.</summary>
    public void LoadState(BinaryReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        uint version = reader.ReadUInt32();
        if (version != CpuStateVersion)
        {
            throw new InvalidDataException($"Unsupported CPU state version {version} (expected {CpuStateVersion}).");
        }

        XboxContext context = Context;
        for (int i = 0; i < 8; i++)
        {
            context.WriteRegister((Gpr)i, reader.ReadUInt32());
        }

        context.Eip = reader.ReadUInt32();
        context.Eflags = reader.ReadUInt32();

        for (int i = 0; i < 6; i++)
        {
            SegmentRegister segment = (SegmentRegister)i;
            ushort selector = reader.ReadUInt16();
            uint baseAddress = reader.ReadUInt32();
            context.SetSegment(segment, selector, baseAddress);
        }

        context.CodeSegment32 = reader.ReadBoolean();
        context.Tr = reader.ReadUInt16();
        context.Ldtr = reader.ReadUInt16();

        context.Cr0 = reader.ReadUInt32();
        context.Cr2 = reader.ReadUInt32();
        context.Cr3 = reader.ReadUInt32();
        context.Cr4 = reader.ReadUInt32();

        for (int i = 0; i < 8; i++)
        {
            context.WriteDebugRegister(i, reader.ReadUInt32());
        }

        context.Gdtr.Base = reader.ReadUInt32();
        context.Gdtr.Limit = reader.ReadUInt16();
        context.Idtr.Base = reader.ReadUInt32();
        context.Idtr.Limit = reader.ReadUInt16();

        context.Halted = reader.ReadBoolean();
        context.Tsc = reader.ReadUInt64();
        context.Mxcsr = reader.ReadUInt32();

        FpuState fpu = context.Fpu;
        fpu.ControlWord = reader.ReadUInt16();
        fpu.StatusWord = reader.ReadUInt16();
        fpu.TagWord = reader.ReadUInt16();
        for (int i = 0; i < 8; i++)
        {
            ushort signExponent = reader.ReadUInt16();
            ulong significand = reader.ReadUInt64();
            fpu.SetPhysicalRegister(i, new Fpu80(signExponent, significand));
        }

        for (int i = 0; i < 8; i++)
        {
            for (int element = 0; element < 4; element++)
            {
                context.WriteXmmElement(i, element, reader.ReadUInt32());
            }
        }

        _sysenterCs = reader.ReadUInt32();
        _sysenterEsp = reader.ReadUInt32();
        _sysenterEip = reader.ReadUInt32();

        InterruptCount = reader.ReadInt32();
        LastFaultVector = reader.ReadInt32();
        LastFaultEip = reader.ReadUInt32();
        LastFaultErrorCode = reader.ReadUInt32();

        // The TLB caches page-table walks over guest RAM; the tables themselves
        // are restored via RAM, so drop any cached translations instead of
        // snapshotting them (a stale entry would be a silent divergence).
        Mmu.Flush();
    }
}
