using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using XGrid.Memory;

namespace XGrid.Cpu;

/// <summary>
/// C3 raw-emitter host side: pins the blittable structures the emitted code
/// addresses directly (register file, translate-cache tables, RAM) and keeps
/// the mutable context fields (Eip/Eflags/Tsc, fault progress) in a small
/// unmanaged scratch block that is copied in before a raw block and back out
/// after it.
///
/// <para>Pin handles stay alive for the CPU's lifetime, which is what makes the
/// pointers stable across the helper calls a raw block makes (a GC can run
/// inside a helper). Objects that contain references cannot be pinned, hence
/// the scratch block instead of addressing the context directly.</para>
/// </summary>
public sealed unsafe partial class XboxCpu
{
    private readonly List<GCHandle> _rawPins = [];
    private bool _rawArraysPinned;
    private uint* _rawRegisters;
    private uint* _rawXlatTag;
    private uint* _rawXlatPhys;
    private byte* _rawXlatWritable;
    private byte* _rawRam;
    private ulong* _rawScratch;

    private const int ScratchEip = 0;
    private const int ScratchEflags = 1;
    private const int ScratchTsc = 2;
    private const int ScratchProgress = 3;
    private const int ScratchWords = 8;

    /// <summary>Gets or sets whether fast mode may use the C3 raw block emitter.</summary>
    public bool JitRawBlocks { get; set; }

    /// <summary>Gets the number of raw runners built (diagnostic).</summary>
    public ulong JitRawBlocksBuilt { get; private set; }

    /// <summary>Gets the number of raw runner invocations (diagnostic).</summary>
    public ulong JitRawInvocations { get; private set; }

    /// <summary>
    /// Builds the raw machine view for the current call. Segment bases and the
    /// RAM size are refreshed because they can change between calls; the
    /// pointers themselves are stable once pinned.
    /// </summary>
    internal RawState BuildRawState()
    {
        EnsureRawArraysPinned();
        RawState state = default;
        state.Registers = _rawRegisters;
        state.XlatTag = _rawXlatTag;
        state.XlatPhys = _rawXlatPhys;
        state.XlatWritable = _rawXlatWritable;
        state.Ram = _rawRam;
        state.RamSize = _ramSize;
        state.XlatMask = DataXlatMask;
        state.Eip = (uint*)(_rawScratch + ScratchEip);
        state.Eflags = (uint*)(_rawScratch + ScratchEflags);
        state.Tsc = (ulong*)(_rawScratch + ScratchTsc);
        state.Progress = (int*)(_rawScratch + ScratchProgress);
        state.SegmentBases[(int)SegmentRegister.Es] = Context.GetSegmentBase(SegmentRegister.Es);
        state.SegmentBases[(int)SegmentRegister.Cs] = Context.GetSegmentBase(SegmentRegister.Cs);
        state.SegmentBases[(int)SegmentRegister.Ss] = Context.GetSegmentBase(SegmentRegister.Ss);
        state.SegmentBases[(int)SegmentRegister.Ds] = Context.GetSegmentBase(SegmentRegister.Ds);
        state.SegmentBases[(int)SegmentRegister.Fs] = Context.GetSegmentBase(SegmentRegister.Fs);
        state.SegmentBases[(int)SegmentRegister.Gs] = Context.GetSegmentBase(SegmentRegister.Gs);
        return state;
    }

    /// <summary>Copies the live context fields into the raw scratch block.</summary>
    internal void RawEnter()
    {
        if (string.Equals(Environment.GetEnvironmentVariable("XGRID_RAW_TRACE"), "1", StringComparison.Ordinal))
        {
            Console.Error.WriteLine($"raw enter eip={Context.Eip:X} eflags={Context.Eflags:X}");
        }

        _rawScratch[ScratchEip] = Context.Eip;
        _rawScratch[ScratchEflags] = Context.Eflags;
        _rawScratch[ScratchTsc] = Context.Tsc;
        _rawScratch[ScratchProgress] = 0;
    }

    /// <summary>Copies the raw scratch block back into the live context.</summary>
    internal void RawExit()
    {
        if (string.Equals(Environment.GetEnvironmentVariable("XGRID_RAW_TRACE"), "1", StringComparison.Ordinal))
        {
            Console.Error.WriteLine($"raw exit eip={_rawScratch[ScratchEip]:X} eflags={_rawScratch[ScratchEflags]:X} eax={_rawRegisters[0]:X} ebx={_rawRegisters[3]:X} ecx={_rawRegisters[1]:X} edx={_rawRegisters[2]:X} progress={_rawScratch[ScratchProgress]}");
        }

        Context.Eip = (uint)_rawScratch[ScratchEip];
        Context.Eflags = (uint)_rawScratch[ScratchEflags];
        Context.Tsc = _rawScratch[ScratchTsc];
        _jitProgress = (int)_rawScratch[ScratchProgress];
    }

    /// <summary>Records one built raw runner (called by block formation).</summary>
    internal void NoteRawBlockBuilt() => JitRawBlocksBuilt++;

    /// <summary>Records one raw runner invocation (called by the block loop).</summary>
    internal void NoteRawInvocation() => JitRawInvocations++;

    private void EnsureRawArraysPinned()
    {
        if (_rawArraysPinned)
        {
            return;
        }

        _rawArraysPinned = true;
        if (!RawCodeMemory.IsSupported)
        {
            return;
        }

        _rawRegisters = PinArray(Context.RawRegisterArray);
        _rawXlatTag = PinArray(DataXlatTagArray);
        _rawXlatPhys = PinArray(DataXlatPhysArray);
        _rawXlatWritable = (byte*)PinArray(DataXlatWritableArray);
        PhysicalMemory? ram = RawRamDevice;
        if (ram is not null)
        {
            _rawRam = PinArray(ram.RawBytes);
        }

        _rawScratch = (ulong*)NativeMemory.Alloc(ScratchWords * sizeof(ulong));
    }

    private T* PinArray<T>(T[] array)
        where T : unmanaged
    {
        GCHandle pin = GCHandle.Alloc(array, GCHandleType.Pinned);
        _rawPins.Add(pin);
        return (T*)pin.AddrOfPinnedObject();
    }

    /// <summary>Gets the data translate-cache tag table (raw emitter pin target).</summary>
    internal uint[] DataXlatTagArray => _dataXlatTag;

    /// <summary>Gets the data translate-cache physical table (raw emitter pin target).</summary>
    internal uint[] DataXlatPhysArray => _dataXlatPhys;

    /// <summary>Gets the data translate-cache writability table (raw emitter pin target).</summary>
    internal bool[] DataXlatWritableArray => _dataXlatWritable;

    /// <summary>Gets the RAM device the raw emitter reads directly (null when unattached).</summary>
    internal PhysicalMemory? RawRamDevice => _ramDirect;

    /// <summary>Runs one instruction through the interpreter body (raw helper).</summary>
    internal void RawExecuteInstruction(in DecodedInstruction instruction) => ExecuteInstruction(in instruction);

    /// <summary>Reads guest memory through the exact interpreter path (raw helper).</summary>
    internal uint RawReadMemory(uint address, int size) => ReadMemory(address, size);

    /// <summary>Writes guest memory through the exact interpreter path (raw helper).</summary>
    internal void RawWriteMemory(uint address, int size, uint value) => WriteMemory(address, size, value);
}
