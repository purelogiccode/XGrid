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
    private RawState* _rawStateBlock;
    private RawChainEntry* _rawChainTable;
    private ulong* _rawActiveBits;
    private ulong* _rawPtBits;
    private ulong[]? _rawActiveSource;
    private ulong[]? _rawPtSource;

    private const int ScratchEip = 0;
    private const int ScratchEflags = 1;
    private const int ScratchTsc = 2;
    private const int ScratchProgress = 3;
    private const int ScratchChain = 4;
    private const int ScratchChainCount = 5;
    private const int ScratchFault = 6;
    private const int ScratchWords = 8;

    internal static readonly bool RawTraceEnabled =
        string.Equals(Environment.GetEnvironmentVariable("XGRID_RAW_TRACE"), "1", StringComparison.Ordinal);

    /// <summary>
    /// Gets whether direct raw-to-raw chaining is enabled. <c>XGRID_RAW_CHAIN=0</c>
    /// disables the chain tails and publications (A/B switch, diagnostics only).
    /// </summary>
    internal static readonly bool RawChainEnabled =
        !string.Equals(Environment.GetEnvironmentVariable("XGRID_RAW_CHAIN"), "0", StringComparison.Ordinal);

    /// <summary>Gets or sets whether fast mode may use the C3 raw block emitter.</summary>
    public bool JitRawBlocks { get; set; }

    /// <summary>Gets the number of raw runners built (diagnostic).</summary>
    public ulong JitRawBlocksBuilt { get; private set; }

    /// <summary>Gets the number of raw runner invocations (diagnostic).</summary>
    public ulong JitRawInvocations { get; private set; }

    /// <summary>Gets the number of members served by raw runners (diagnostic).</summary>
    public ulong JitRawServedMembers { get; private set; }

    /// <summary>Gets the number of direct raw-to-raw chain jumps (diagnostic).</summary>
    public ulong JitRawChainedBlocks { get; private set; }

    /// <summary>Gets the number of raw runner invocations that ended on a captured fault (diagnostic).</summary>
    public ulong JitRawFaults { get; private set; }

    /// <summary>Gets the number of chain-target publications (diagnostic).</summary>
    public ulong RawChainPublishes { get; private set; }

    /// <summary>Gets the number of chain-table clears (diagnostic).</summary>
    public ulong RawChainClears { get; private set; }

    /// <summary>
    /// Builds the raw machine view for the current call. Segment bases and the
    /// RAM size are refreshed because they can change between calls; the
    /// pointers themselves are stable once pinned.
    /// </summary>
    internal RawState* BuildRawState()
    {
        EnsureRawArraysPinned();

        // Invariant half (pinned pointers, mask, scratch addresses): filled
        // once.
        if (!_rawStateReady)
        {
            _rawStateReady = true;
            _rawStateTemplate = default;
            _rawStateTemplate.Registers = _rawRegisters;
            _rawStateTemplate.XlatTag = _rawXlatTag;
            _rawStateTemplate.XlatPhys = _rawXlatPhys;
            _rawStateTemplate.XlatWritable = _rawXlatWritable;
            _rawStateTemplate.Ram = _rawRam;
            _rawStateTemplate.XlatMask = DataXlatMask;
            _rawStateTemplate.Eip = (uint*)(_rawScratch + ScratchEip);
            _rawStateTemplate.Eflags = (uint*)(_rawScratch + ScratchEflags);
            _rawStateTemplate.Tsc = (ulong*)(_rawScratch + ScratchTsc);
            _rawStateTemplate.Progress = (int*)(_rawScratch + ScratchProgress);
            _rawStateTemplate.ChainTable = _rawChainTable;
            _rawStateTemplate.ChainServed = (int*)(_rawScratch + ScratchChain);
            _rawStateTemplate.ChainCount = (int*)(_rawScratch + ScratchChainCount);
            _rawStateTemplate.Fault = (int*)(_rawScratch + ScratchFault);
        }

        RawState* state = _rawStateBlock;
        *state = _rawStateTemplate;
        state->RamSize = _ramSize;
        state->Generation = _blocks is null ? null : _blocks.GenerationPointer;
        RefreshRawState(state);

        // Across a RunJitBlock call these are invariant: the watch range is
        // host-side configuration and segment loads are excluded from planned
        // members, so no servable block can change a segment base.
        AddressSpace? space = _ramSpace;
        state->WatchArmed = space is not null && space.WatchEnd > space.WatchStart ? 1u : 0u;
        state->WatchStart = space?.WatchStart ?? 0;
        state->WatchEnd = space?.WatchEnd ?? 0;

        state->SegmentBases[(int)SegmentRegister.Es] = Context.GetSegmentBase(SegmentRegister.Es);
        state->SegmentBases[(int)SegmentRegister.Cs] = Context.GetSegmentBase(SegmentRegister.Cs);
        state->SegmentBases[(int)SegmentRegister.Ss] = Context.GetSegmentBase(SegmentRegister.Ss);
        state->SegmentBases[(int)SegmentRegister.Ds] = Context.GetSegmentBase(SegmentRegister.Ds);
        state->SegmentBases[(int)SegmentRegister.Fs] = Context.GetSegmentBase(SegmentRegister.Fs);
        state->SegmentBases[(int)SegmentRegister.Gs] = Context.GetSegmentBase(SegmentRegister.Gs);
        return state;
    }

    /// <summary>
    /// Refreshes the raw-state fields a servable block can still change
    /// between two raw invocations of one <c>RunJitBlock</c> call: CR0.WP (a
    /// MovCr0 helper) and the decode-cache guard tables (paging may be
    /// enabled). Everything else in <see cref="BuildRawState"/> is invariant
    /// across the call.
    /// </summary>
    internal void RefreshRawState(RawState* state)
    {
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

        state->ActiveBits = _rawActiveBits;
        state->PtBits = pt is null ? null : _rawPtBits;
        state->StoreGuardBail = active is null ? 1u : 0u;
        state->Cr0Wp = (Context.Cr0 & ControlRegisterBits.Cr0WriteProtect) != 0 ? 1u : 0u;

        // Chaining: the interrupt-pending word can change between block
        // invocations of one call (the source tests it at its chain edge). A
        // source with no cheap pending flag never chains while IF=1 (it
        // returns to RunJitBlock, which acknowledges). The generation is read
        // live from the pinned slot at the chain edge.
        IInterruptSource? source = InterruptSource;
        nint pendingAddress = source?.PendingFlagAddress ?? 0;
        state->IrqPending = (uint*)pendingAddress;
        state->IrqArmed = pendingAddress != 0 ? 1u : 0u;
    }

    /// <summary>Publishes a raw block as a chaining target.</summary>
    internal void PublishRawChainBlock(uint headEip, ulong generation, int members, nint target, nint entries)
    {
        // The table is allocated with the other pinned raw arrays; publishing
        // happens at block formation, which can precede the first raw
        // invocation, so ensure the allocation here.
        EnsureRawArraysPinned();
        if (_rawChainTable is null)
        {
            return;
        }

        uint index = (headEip * RawChainHash.Multiplier) >> RawChainHash.IndexShift;
        RawChainEntry* entry = &_rawChainTable[index];
        RawChainPublishes++;
        if (XboxCpu.RawTraceEnabled)
        {
            XboxCpu.RawTrace($"  chain publish head={headEip:X5} gen={generation} members={members}");
        }
        entry->Valid = 1;
        entry->Eip = headEip;
        entry->Generation = (uint)generation;
        entry->Members = (uint)members;
        entry->Target = target;
        entry->Entries = entries;
    }

    /// <summary>
    /// Clears every chaining target. Called whenever the JIT block cache is
    /// invalidated without a decode-generation bump, so no chain edge can
    /// reach a block whose pinned entries array may be collected.
    /// </summary>
    internal void ClearRawChainTable()
    {
        RawChainClears++;
        if (_rawChainTable is not null)
        {
            new Span<byte>(_rawChainTable, RawChainHash.TableSize * sizeof(RawChainEntry)).Clear();
        }
    }

    /// <summary>
    /// Copies the live context fields into the raw scratch block.
    /// </summary>
    internal void RawEnter()
    {
        // Guard the interpolation: this runs once per raw invocation (millions
        // per run), and an unguarded interpolated string allocates on every
        // call even when tracing is off.
        if (RawTraceEnabled)
        {
            RawTrace(
                $"  enter eip={Context.Eip:X} eflags={Context.Eflags:X} tsc={Context.Tsc} " +
                $"eax={Context.Eax:X} ecx={Context.Ecx:X} " +
                $"regs={(ulong)(nint)_rawRegisters:X} live={(ulong)(nint)Context.RawRegistersAddress():X}");
        }

        _rawScratch[ScratchEip] = Context.Eip;
        _rawScratch[ScratchEflags] = Context.Eflags;
        _rawScratch[ScratchTsc] = Context.Tsc;
        _rawScratch[ScratchProgress] = 0;
        _rawScratch[ScratchChain] = 0;
        _rawScratch[ScratchChainCount] = 0;
        _rawScratch[ScratchFault] = 0;
        _rawPendingFault = null;
    }

    /// <summary>Copies the raw scratch block back into the live context.</summary>
    internal void RawExit()
    {
        if (RawTraceEnabled)
        {
            RawTrace($"  exit eip={_rawScratch[ScratchEip]:X} eflags={_rawScratch[ScratchEflags]:X}");
        }

        Context.Eip = (uint)_rawScratch[ScratchEip];
        Context.Eflags = (uint)_rawScratch[ScratchEflags];
        Context.Tsc = _rawScratch[ScratchTsc];
        _jitProgress = (int)_rawScratch[ScratchProgress];

        // A fault unwinds without the chained epilogue's reset; never let a
        // carry leak into the next invocation.
        _rawScratch[ScratchChain] = 0;
        JitRawChainedBlocks += (ulong)(int)_rawScratch[ScratchChainCount];
        _rawScratch[ScratchChainCount] = 0;
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

    // TEMP-DIAG removed: bounded MMX load/store trace (menu divergence 968.38M).


    /// <summary>Records one built raw runner (called by block formation).</summary>
    internal void NoteRawBlockBuilt() => JitRawBlocksBuilt++;

    /// <summary>Records one raw runner invocation (called by the block loop).</summary>
    internal void NoteRawInvocation() => JitRawInvocations++;

    /// <summary>Records members served by a raw runner (called by the block loop).</summary>
    internal void NoteRawServed(int members) => JitRawServedMembers += (ulong)members;

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
        _rawStateBlock = (RawState*)NativeMemory.Alloc((nuint)sizeof(RawState));
        _rawChainTable = (RawChainEntry*)NativeMemory.Alloc(
            (nuint)(RawChainHash.TableSize * sizeof(RawChainEntry)));
        new Span<byte>(_rawChainTable, RawChainHash.TableSize * sizeof(RawChainEntry)).Clear();
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
    private RawState _rawStateTemplate;
    private bool _rawStateReady;

    /// <summary>
    /// Exception a raw helper captured (never thrown through the native frame).
    /// The emitted code sees <see cref="RawState.Fault"/> set, exits with the
    /// fault sentinel, and <c>RunJitBlock</c> rethrows this in managed code.
    /// </summary>
    private Exception? _rawPendingFault;

    /// <summary>
    /// Records an exception a live raw helper caught: sets the emitted code's
    /// fault word and remembers the exception for the managed rethrow. Called
    /// inside the helper's catch, where propagating would unwind through a
    /// hand-emitted frame that CoreCLR cannot walk.
    /// </summary>
    internal void RawCaptureFault(Exception exception)
    {
        if (_rawScratch is not null)
        {
            _rawScratch[ScratchFault] = 1;
        }

        if (RawTraceEnabled)
        {
            RawTrace($"  capture {exception.GetType().Name}: {exception.Message}");
        }

        _rawPendingFault ??= exception;
    }

    /// <summary>
    /// Rethrows the exception a raw helper captured. Called by
    /// <c>RunJitBlock</c> after the raw frame has returned and
    /// <see cref="RawExit"/> published the context, so ordinary managed
    /// exception handling (fault delivery, diagnostics) sees it exactly as the
    /// IL runner's.
    /// </summary>
    internal void ThrowRawFault()
    {
        Exception? pending = _rawPendingFault;
        _rawPendingFault = null;
        if (pending is null)
        {
            throw new InvalidOperationException("raw runner reported a fault without a captured exception");
        }

        System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(pending).Throw();
    }

    /// <summary>Resolves the CPU from <see cref="RawSelfHandle"/> (weak handle; never null inside a run).</summary>
    internal static XboxCpu RawFromHandle(IntPtr handle) =>
        (XboxCpu)GCHandle.FromIntPtr(handle).Target!;

    /// <summary>
    /// Resolves the CPU from a raw handle without assuming the target is alive
    /// (a helper's capture path must never throw out of the native frame).
    /// </summary>
    internal static XboxCpu? RawFromHandleOrNull(IntPtr handle)
    {
        GCHandle gcHandle = GCHandle.FromIntPtr(handle);
        if (!gcHandle.IsAllocated)
        {
            return null;
        }

        return gcHandle.Target as XboxCpu;
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
