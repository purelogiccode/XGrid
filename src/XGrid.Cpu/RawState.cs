using System;
using System.Runtime.InteropServices;

namespace XGrid.Cpu;

/// <summary>
/// C3 raw-emitter machine view: the pinned pointers and segment bases a native
/// block addresses directly. Filled once per <c>RunJitBlock</c> call (segments
/// cannot change inside a servable block) and passed by pointer to the emitted
/// code.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct RawState
{
    /// <summary>Guest register file (8 x uint32), pinned.</summary>
    public uint* Registers;

    /// <summary>Data translate-cache tags (DataXlatSlots x uint32), pinned.</summary>
    public uint* XlatTag;

    /// <summary>Data translate-cache physical pages (DataXlatSlots x uint32), pinned.</summary>
    public uint* XlatPhys;

    /// <summary>Data translate-cache writability flags, pinned.</summary>
    public byte* XlatWritable;

    /// <summary>
    /// Decode-cache active physical pages (ulong bitset words, pinned): a
    /// store to an active page must run the decode-cache invalidation, so the
    /// emitted store guard routes it to the exact slow path.
    /// </summary>
    public ulong* ActiveBits;

    /// <summary>
    /// MMU page-table physical pages (ulong bitset words, pinned; null when
    /// tracking is off): a store to one of these full-flushes the caches.
    /// </summary>
    public ulong* PtBits;

    /// <summary>1 when a bus write watch is armed: inline stores test the watch range.</summary>
    public uint WatchArmed;

    /// <summary>Bus write watch range, physical (valid when <see cref="WatchArmed"/> is 1).</summary>
    public uint WatchStart;

    /// <summary>Exclusive end of the bus write watch range.</summary>
    public uint WatchEnd;

    /// <summary>1 when CR0.WP is set: a non-writable page takes the slow path (IL parity).</summary>
    public uint Cr0Wp;

    /// <summary>1 when the inline store guard cannot be evaluated (no decode cache): stores go slow.</summary>
    public uint StoreGuardBail;

    /// <summary>Base RAM bytes, pinned (null when no RAM device is attached).</summary>
    public byte* Ram;

    /// <summary>RAM size in bytes.</summary>
    public uint RamSize;

    /// <summary>Data translate-cache slot mask.</summary>
    public uint XlatMask;

    /// <summary>Context Eip field.</summary>
    public uint* Eip;

    /// <summary>Context Eflags field.</summary>
    public uint* Eflags;

    /// <summary>Context Tsc field.</summary>
    public ulong* Tsc;

    /// <summary>RunJitBlock progress field (fault reporting).</summary>
    public int* Progress;

    /// <summary>
    /// Direct-mapped chaining table (see <see cref="RawChainEntry"/>): a
    /// block's exit looks up its next guest EIP and tail-jumps to the target's
    /// native entry when the entry matches the head and generation.
    /// </summary>
    public RawChainEntry* ChainTable;

    /// <summary>Pinned live JIT block-cache generation word (chain validation).</summary>
    public ulong* Generation;

    /// <summary>1 when <see cref="IrqPending"/> points at a usable 0/1 pending word.</summary>
    public uint IrqArmed;

    /// <summary>Accumulated served count of the blocks already chained in this runner call.</summary>
    public int* ChainServed;

    /// <summary>Chained-jump counter for this runner invocation (diagnostic).</summary>
    public int* ChainCount;

    /// <summary>
    /// Non-zero once a helper captured an exception (see
    /// <c>XboxCpu.RawCaptureFault</c>): the emitted code tests this after every
    /// helper call and exits with the fault sentinel instead of continuing.
    /// A managed exception must never unwind through the hand-emitted native
    /// frame — CoreCLR cannot walk it (no unwind data), so delivery depends on
    /// stack luck; the helpers therefore catch, record, and report.
    /// </summary>
    public int* Fault;

    /// <summary>0/1 interrupt-pending word from <c>IInterruptSource.PendingFlagAddress</c> (null when unavailable).</summary>
    public uint* IrqPending;

    /// <summary>Segment bases, indexed by <see cref="SegmentRegister"/> (6 entries).</summary>
    public fixed uint SegmentBases[6];
}

/// <summary>
/// One direct-mapped raw-chaining slot. A block whose terminator leaves the
/// guest EIP at another block's head can tail-jump to that block's native
/// entry instead of returning to <c>RunJitBlock</c>; the entry carries
/// everything the target's prologue needs as arguments. Store-bearing blocks
/// never chain (they can invalidate the table mid-path); stale entries fail
/// the generation compare.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct RawChainEntry
{
    /// <summary>1 when this slot holds a published block (an empty slot has 0).</summary>
    public uint Valid;

    /// <summary>Guest EIP of the target block (the lookup key).</summary>
    public uint Eip;

    /// <summary>Block-cache generation at publication (low 32 bits).</summary>
    public uint Generation;

    /// <summary>Target member count (the remaining-budget gate).</summary>
    public uint Members;

    /// <summary>Native target entry point (the tail-jump destination).</summary>
    public nint Target;

    /// <summary>Target block's pinned decode-entry array data pointer.</summary>
    public nint Entries;
}

/// <summary>Shared chaining-table hash: <c>(eip * Multiplier) &gt;&gt; IndexShift</c>.</summary>
internal static class RawChainHash
{
    /// <summary>Knuth's multiplicative constant.</summary>
    public const uint Multiplier = 2654435761u;

    /// <summary>Shift producing a 14-bit index (table size 16384).</summary>
    public const int IndexShift = 18;

    /// <summary>Table slot count.</summary>
    public const int TableSize = 1 << (32 - IndexShift);
}

/// <summary>
/// Executable-memory allocator for the C3 raw emitter (Windows x64 only; every
/// other platform keeps the IL block JIT).
/// </summary>
internal static unsafe class RawCodeMemory
{
    private const uint MemCommit = 0x1000;
    private const uint MemReserve = 0x2000;
    private const uint PageExecuteReadWrite = 0x40;
    private const uint MemRelease = 0x8000;

    /// <summary>Gets whether the raw emitter can run on this host.</summary>
    public static bool IsSupported { get; } =
        OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X64;

    /// <summary>Reserves an executable region of at least the given size.</summary>
    public static byte* Allocate(int size)
    {
        if (!IsSupported || size <= 0)
        {
            return null;
        }

        void* block = VirtualAlloc(null, (nuint)size, MemCommit | MemReserve, PageExecuteReadWrite);
        return (byte*)block;
    }

    /// <summary>Releases a region from <see cref="Allocate"/>.</summary>
    public static void Free(void* block)
    {
        if (block is not null)
        {
            VirtualFree(block, 0, MemRelease);
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern void* VirtualAlloc(void* address, nuint size, uint allocationType, uint protect);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool VirtualFree(void* address, nuint size, uint freeType);
}
