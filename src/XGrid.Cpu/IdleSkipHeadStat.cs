using System.Runtime.InteropServices;

namespace XGrid.Cpu;

/// <summary>Why a candidate idle-loop head was rejected from skipping (§119 measurement).</summary>
public enum IdleSkipRejectReason : byte
{
    /// <summary>Not rejected (used for armable-park accounting).</summary>
    None = 0,

    /// <summary>Body read a non-RAM physical address the host could not classify as skippable.</summary>
    MmioRead = 1,

    /// <summary>Body executed an In/Out (PIO) instruction.</summary>
    Pio = 2,

    /// <summary>Body wrote memory (RAM or device).</summary>
    MemWrite = 3,

    /// <summary>Body's watch list grew past the fixed watch capacity.</summary>
    WatchOverflow = 4,

    /// <summary>Body failed the fixed-point data-flow rules (register countdowns, pointer chases).</summary>
    StaticRule = 5,

    /// <summary>Body broke candidate structure or contained a non-allow-listed mnemonic.</summary>
    Structural = 6,
}

/// <summary>Per-loop-head skip-attribution stat for the §119 measurement table.</summary>
[StructLayout(LayoutKind.Sequential)]
public readonly struct IdleSkipHeadStat
{
    /// <summary>Gets the loop-head EIP (virtual, identity-mapped boot stages).</summary>
    public uint Head { get; init; }

    /// <summary>Gets the CPU steps attributed to this head (inter-visit TSC deltas).</summary>
    public ulong Steps { get; init; }

    /// <summary>Gets the rejection reason (None for armable parks).</summary>
    public IdleSkipRejectReason Reason { get; init; }

    /// <summary>Gets the first offending physical address (MMIO) or PIO port; 0 when not applicable.</summary>
    public uint Address { get; init; }
}
