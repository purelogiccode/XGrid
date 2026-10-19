using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using XGrid.Core.Devices;
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
    private ulong* _rawActiveBits;
    private ulong* _rawPtBits;
    private ulong[]? _rawActiveSource;
    private ulong[]? _rawPtSource;

    private const int ScratchEip = 0;
    private const int ScratchEflags = 1;
    private const int ScratchTsc = 2;
    private const int ScratchProgress = 3;
    private const int ScratchWords = 8;

    private static readonly bool RawTraceEnabled =
        string.Equals(Environment.GetEnvironmentVariable("XGRID_RAW_TRACE"), "1", StringComparison.Ordinal);

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

        // Store guard tables (pinned on first sight; the instances are
        // effectively permanent, the reference check is belt and braces).
        DecodeCache? cache = _decodeCache;
        ulong[]? active = cache?.ActivePageBits;
        if (active is not null && !ReferenceEquals(active, _rawActiveSource))
        {
            _rawActiveSource = active;
            _rawActiveBits = PinArray(active);
        }

        ulong[]? pt = Mmu.PageTablePageBits;
        if (pt is not null && !ReferenceEquals(pt, _rawPtSource))
        {
            _rawPtSource = pt;
            _rawPtBits = PinArray(pt);
        }

        state.ActiveBits = _rawActiveBits;
        state.PtBits = pt is null ? null : _rawPtBits;
        state.StoreGuardBail = active is null ? 1u : 0u;
        state.Cr0Wp = (Context.Cr0 & ControlRegisterBits.Cr0WriteProtect) != 0 ? 1u : 0u;
        AddressSpace? space = _ramSpace;
        state.WatchArmed = space is not null && space.WatchEnd > space.WatchStart ? 1u : 0u;

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
        RawTrace(
            $"  enter eip={Context.Eip:X} eflags={Context.Eflags:X} tsc={Context.Tsc} " +
            $"eax={Context.Eax:X} ecx={Context.Ecx:X} " +
            $"regs={(ulong)(nint)_rawRegisters:X} live={(ulong)(nint)Context.RawRegistersAddress():X}");

        _rawScratch[ScratchEip] = Context.Eip;
        _rawScratch[ScratchEflags] = Context.Eflags;
        _rawScratch[ScratchTsc] = Context.Tsc;
        _rawScratch[ScratchProgress] = 0;
    }

    /// <summary>Copies the raw scratch block back into the live context.</summary>
    internal void RawExit()
    {
        RawTrace($"  exit eip={_rawScratch[ScratchEip]:X} eflags={_rawScratch[ScratchEflags]:X}");

        Context.Eip = (uint)_rawScratch[ScratchEip];
        Context.Eflags = (uint)_rawScratch[ScratchEflags];
        Context.Tsc = _rawScratch[ScratchTsc];
        _jitProgress = (int)_rawScratch[ScratchProgress];
    }

    private bool _rawTrace => RawTraceEnabled;

    /// <summary>Appends one diagnostic line (file, not stderr: a hard crash loses buffered output).</summary>
    internal static void RawTrace(string line)
    {
        if (!RawTraceEnabled)
        {
            return;
        }

        try
        {
            System.IO.File.AppendAllText(
                System.IO.Path.Combine(System.IO.Path.GetTempPath(), "xgrid-raw-trace.txt"),
                line + Environment.NewLine);
        }
        catch (System.IO.IOException)
        {
            // Diagnostics only.
        }
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
        _rawSelfHandle = GCHandle.Alloc(this, GCHandleType.Weak);
    }

    private T* PinArray<T>(T[] array)
        where T : unmanaged
    {
        GCHandle pin = GCHandle.Alloc(array, GCHandleType.Pinned);
        _rawPins.Add(pin);
        return (T*)pin.AddrOfPinnedObject();
    }

    /// <summary>
    /// Gets a GC-stable representation of this CPU for the emitted code: a
    /// weak <see cref="GCHandle"/> whose <see cref="GCHandle.ToIntPtr"/> value
    /// never changes, so a helper call can resolve the live (possibly moved)
    /// object reference. Passing a raw object pointer across helper calls is
    /// unsound: a GC during a helper moves the object and every later call
    /// would write into freed memory.
    /// </summary>
    internal IntPtr RawSelfHandle => GCHandle.ToIntPtr(_rawSelfHandle);

    private GCHandle _rawSelfHandle;

    /// <summary>Resolves the CPU from <see cref="RawSelfHandle"/> (weak handle; never null inside a run).</summary>
    internal static XboxCpu RawFromHandle(IntPtr handle) =>
        (XboxCpu)GCHandle.FromIntPtr(handle).Target!;

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

    /// <summary>
    /// Fast-mode post-member IRQ poll (raw STI/POPF/string/port members):
    /// acknowledges once and records a delivered vector for
    /// <c>RunJitBlock</c> to transfer at the member's boundary, exactly like
    /// the IL runner's post-poll. Returns 1 when a vector was recorded.
    /// </summary>
    internal int RawPollInterrupt()
    {
        IInterruptSource? source = InterruptSource;
        if (source is null)
        {
            return 0;
        }

        int vector = source.Acknowledge();
        if (vector < 0)
        {
            return 0;
        }

        _jitPending = vector;
        return 1;
    }

    /// <summary>Raw device fast path: read through an already-translated physical address.</summary>
    internal uint RawReadPhysicalJit(uint physical, int size) => ReadPhysicalJit(physical, size);

    /// <summary>Raw device fast path: write through an already-translated physical address.</summary>
    internal void RawWritePhysicalJit(uint physical, int size, uint value) =>
        WritePhysicalJit(physical, size, value);

    /// <summary>Raw inline-store guard: run the decode-cache invalidation for a store's physical range.</summary>
    internal void RawNoteStorePhysical(uint physical, int sizeInBits) =>
        NoteStorePhysical(physical, sizeInBits);

    /// <summary>Reads guest memory through the exact interpreter path (raw helper).</summary>
    internal uint RawReadMemory(uint address, int size) => ReadMemory(address, size);

    /// <summary>Writes guest memory through the exact interpreter path (raw helper).</summary>
    internal void RawWriteMemory(uint address, int size, uint value) => WriteMemory(address, size, value);
}
