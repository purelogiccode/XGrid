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

/// <summary>One built fast-mode block (a run of natively emittable members).</summary>
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
/// explicit invalidate) makes stale entries self-invalidate on lookup.
///
/// A head is not built on first sight: the entry counts visits and formation
/// starts only at <see cref="XboxCpu.JitBuildThreshold"/> visits, because a
/// DynamicMethod compile costs ~1 ms and one-shot cold code must never pay
/// for it. A <c>null</c> block records a permanent verdict for that head
/// (first member has no native shape) so formation is not re-attempted until
/// the generation moves.
/// </summary>
internal sealed class JitBlockCache
{
    /// <summary>Maximum cached heads before non-built entries are dropped.</summary>
    public const int MaxHeads = 65536;

    private readonly Dictionary<uint, Entry> _heads = new();

    /// <summary>Gets the number of runners built.</summary>
    public ulong BlocksBuilt { get; private set; }

    /// <summary>Gets the number of heads with a cached permanent rejection.</summary>
    public ulong Rejected { get; private set; }

    /// <summary>Gets the number of visits to heads that were not yet eligible for building.</summary>
    public ulong PendingVisits { get; private set; }

    /// <summary>Gets the number of formation attempts that bailed transiently (entries not cached yet).</summary>
    public ulong TransientBails { get; private set; }

    /// <summary>Gets the number of live heads currently cached.</summary>
    public int Count => _heads.Count;

    /// <summary>Lookup verdict for a head.</summary>
    public enum Verdict
    {
        /// <summary>Eligible for formation — the caller should attempt it.</summary>
        Absent,

        /// <summary>Seen, but not yet at the build threshold: skip quietly.</summary>
        Pending,

        /// <summary>A permanent rejection is cached for this head/generation.</summary>
        Rejected,

        /// <summary>A built block is cached.</summary>
        Hit,
    }

    /// <summary>
    /// Looks up a head. Stale-generation entries are dropped; unseen heads are
    /// recorded as pending; pending heads become eligible at
    /// <paramref name="buildThreshold"/> visits.
    /// </summary>
    public Verdict Lookup(uint headLinear, ulong generation, int buildThreshold, out JitBlock? block)
    {
        block = null;
        if (!_heads.TryGetValue(headLinear, out Entry entry) || entry.Generation != generation)
        {
            _heads[headLinear] = new Entry { Generation = generation, Visits = 1 };
            PendingVisits++;
            return Verdict.Pending;
        }

        if (entry.Block is not null)
        {
            block = entry.Block;
            return Verdict.Hit;
        }

        if (entry.Rejected)
        {
            return Verdict.Rejected;
        }

        int visits = entry.Visits + 1;
        entry.Visits = visits;
        if (visits >= buildThreshold && visits >= entry.RetryAtVisits)
        {
            return Verdict.Absent;
        }

        _heads[headLinear] = entry;
        PendingVisits++;
        return Verdict.Pending;
    }

    /// <summary>Stores a built block (or a permanent rejection when <paramref name="block"/> is null).</summary>
    public void Store(uint headLinear, ulong generation, JitBlock? block)
    {
        if (_heads.Count >= MaxHeads)
        {
            DropUnbuiltEntries();
        }

        _heads[headLinear] = new Entry
        {
            Generation = generation,
            Block = block,
            Rejected = block is null,
        };
        if (block is null)
        {
            Rejected++;
        }
        else
        {
            BlocksBuilt++;
        }
    }

    /// <summary>Records a transient formation bail: retry the walk in 64 visits.</summary>
    public void NoteTransientBail(uint headLinear, ulong generation)
    {
        TransientBails++;
        if (_heads.TryGetValue(headLinear, out Entry entry) && entry.Generation == generation
            && entry.Block is null && !entry.Rejected)
        {
            entry.RetryAtVisits = entry.Visits + 64;
            _heads[headLinear] = entry;
        }
    }

    /// <summary>Drops every cached verdict.</summary>
    public void Invalidate()
    {
        _heads.Clear();
    }

    /// <summary>
    /// Drops pending (never-built, never-rejected) entries; falls back to a
    /// full clear when the table holds nothing but verdicts.
    /// </summary>
    private void DropUnbuiltEntries()
    {
        List<uint>? drop = null;
        foreach ((uint head, Entry entry) in _heads)
        {
            if (entry.Block is null && !entry.Rejected && entry.Visits < 16)
            {
                (drop ??= new List<uint>()).Add(head);
            }
        }

        if (drop is null || drop.Count == 0)
        {
            _heads.Clear();
            return;
        }

        foreach (uint head in drop)
        {
            _heads.Remove(head);
        }
    }

    private struct Entry
    {
        public ulong Generation { get; init; }

        public JitBlock? Block { get; set; }

        public bool Rejected { get; set; }

        public int Visits { get; set; }

        public int RetryAtVisits { get; set; }
    }
}
