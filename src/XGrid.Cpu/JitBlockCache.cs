using System.Collections.Generic;

namespace XGrid.Cpu;

/// <summary>
/// Fast-mode block runner: serves the members of one natively-emitted block,
/// updating the guest context in place and returning the number of
/// instructions served. May throw <c>PageFaultSignal</c> through the emitted
/// frames when a member's memory access faults (the context is synced to the
/// member's boundary before the call, so the caller's fault delivery is
/// exact).
/// </summary>
internal delegate int JitRun(XboxCpu cpu, int budget);

/// <summary>One built fast-mode block (a maximal run of natively emittable members).</summary>
internal sealed class JitBlock
{
    public JitBlock(
        uint headLinear,
        uint headEip,
        int operandSize,
        int addressSize,
        ulong generation,
        int memberCount,
        JitRun runner)
    {
        HeadLinear = headLinear;
        HeadEip = headEip;
        OperandSize = operandSize;
        AddressSize = addressSize;
        Generation = generation;
        MemberCount = memberCount;
        Runner = runner;
    }

    /// <summary>Gets the linear fetch address of the block head (the cache key).</summary>
    public uint HeadLinear { get; }

    /// <summary>Gets the EIP of the block head.</summary>
    public uint HeadEip { get; }

    /// <summary>Gets the decode-time operand size the block was built for.</summary>
    public int OperandSize { get; }

    /// <summary>Gets the decode-time address size the block was built for.</summary>
    public int AddressSize { get; }

    /// <summary>Gets the decode/block generation the block was built for.</summary>
    public ulong Generation { get; }

    /// <summary>Gets the number of members the runner serves (at most <see cref="JitEmitter.MaxMembers"/>).</summary>
    public int MemberCount { get; }

    /// <summary>Gets the emitted runner.</summary>
    public JitRun Runner { get; }
}

/// <summary>
/// Head-keyed fast-mode block cache. Every entry is stamped with the block
/// generation: a generation bump (decode-cache page drop, full flush, or an
/// explicit invalidate) makes stale entries self-invalidate on lookup. A
/// <c>null</c> block records a permanent verdict for that head (the first
/// member has no native shape) so formation is not re-attempted until the
/// generation moves; transient formation bails (entries not cached yet) are
/// not recorded and are retried.
/// </summary>
internal sealed class JitBlockCache
{
    /// <summary>Maximum cached heads before the whole table is dropped.</summary>
    public const int MaxHeads = 8192;

    private readonly Dictionary<uint, Entry> _heads = new();

    /// <summary>Gets the number of runners built.</summary>
    public ulong BlocksBuilt { get; private set; }

    /// <summary>Gets the number of heads with a cached permanent rejection.</summary>
    public ulong Rejected { get; private set; }

    /// <summary>Gets the number of formation attempts that bailed transiently (entries not cached yet).</summary>
    public ulong TransientBails { get; private set; }

    /// <summary>Gets the number of live heads currently cached.</summary>
    public int Count => _heads.Count;

    /// <summary>Lookup verdict for a head.</summary>
    public enum Verdict
    {
        /// <summary>No entry (or a stale one) — the caller should attempt formation.</summary>
        Absent,

        /// <summary>A permanent rejection is cached for this head/generation.</summary>
        Rejected,

        /// <summary>A built block is cached.</summary>
        Hit,
    }

    /// <summary>Looks up a head; stale-generation entries are dropped.</summary>
    public Verdict Lookup(uint headLinear, ulong generation, out JitBlock? block)
    {
        block = null;
        if (!_heads.TryGetValue(headLinear, out Entry entry))
        {
            return Verdict.Absent;
        }

        if (entry.Generation != generation)
        {
            _heads.Remove(headLinear);
            return Verdict.Absent;
        }

        block = entry.Block;
        return entry.Block is null ? Verdict.Rejected : Verdict.Hit;
    }

    /// <summary>Stores a built block (or a permanent rejection when <paramref name="block"/> is null).</summary>
    public void Store(uint headLinear, ulong generation, JitBlock? block)
    {
        if (_heads.Count >= MaxHeads)
        {
            _heads.Clear();
        }

        _heads[headLinear] = new Entry { Generation = generation, Block = block };
        if (block is null)
        {
            Rejected++;
        }
        else
        {
            BlocksBuilt++;
        }
    }

    /// <summary>Records a transient formation bail (no verdict cached).</summary>
    public void NoteTransientBail()
    {
        TransientBails++;
    }

    /// <summary>Drops every cached verdict.</summary>
    public void Invalidate()
    {
        _heads.Clear();
    }

    private readonly struct Entry
    {
        public ulong Generation { get; init; }

        public JitBlock? Block { get; init; }
    }
}
