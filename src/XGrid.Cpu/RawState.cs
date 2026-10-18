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

    /// <summary>Segment bases, indexed by <see cref="SegmentRegister"/> (6 entries).</summary>
    public fixed uint SegmentBases[6];
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
