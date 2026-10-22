using System;
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
internal delegate int JitRun(XboxCpu cpu, DecodeCacheEntry[] entries, int budget);

/// <summary>
/// C3 raw runner: the native block entered through a managed-to-native
/// transition. Every parameter is blittable on purpose - a class or array
/// parameter would be marshalled (the callee would see a marshaller proxy, not
/// the object reference), which is how the first bring-up handed the raw code a
/// bogus CPU pointer.
/// </summary>
internal unsafe delegate int RawRun(IntPtr cpu, RawState* state, IntPtr entries, int budget);

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
        DecodeCacheEntry[] entries,
        JitRun? runner,
        RawRun? rawRunner,
        nint rawEntry,
        bool hasSti,
        bool hasStore)
    {
        HeadLinear = headLinear;
        HeadEip = headEip;
        OperandSize = operandSize;
        AddressSize = addressSize;
        Generation = generation;
        MemberCount = memberCount;
        Entries = entries;
        Runner = runner;
        RawRunner = rawRunner;
        RawEntry = rawEntry;
        HasSti = hasSti;
        HasStore = hasStore;
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
    // ReSharper disable once UnusedAutoPropertyAccessor.Global
    public ulong Generation { get; }

    /// <summary>Gets the number of members the runner serves (at most <see cref="JitEmitter.MaxMembers"/>).</summary>
    public int MemberCount { get; }

    /// <summary>Gets the member decode entries (CallSpec fallback instructions).</summary>
    public DecodeCacheEntry[] Entries { get; }

    /// <summary>Gets the IL runner (null when the block is raw-only).</summary>
    public JitRun? Runner { get; }

    /// <summary>Gets the C3 raw runner function pointer (0 when absent).</summary>
    public RawRun? RawRunner { get; }

    /// <summary>Gets the native raw entry point (0 when absent; chain target).</summary>
    public nint RawEntry { get; }

    /// <summary>Gets whether the block contains an STI member (fast-mode idle-wait detection).</summary>
    public bool HasSti { get; }

    /// <summary>Gets whether the block contains a member with a memory/side-effect write (fast-mode idle-wait detection).</summary>
    public bool HasStore { get; }
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

    private const int MemoSize = 32;

    private readonly Dictionary<uint, Entry> _heads = new();
    private readonly uint[] _memoHead = new uint[MemoSize];
    private readonly ulong[] _memoGeneration = new ulong[MemoSize];
    private readonly JitBlock?[] _memoBlock = new JitBlock?[MemoSize];

    /// <summary>
    /// Direct-mapped lookup memo: skips the dictionary for the hottest heads
    /// (generation-checked, so invalidation is automatic; an evicted entry's
    /// block stays valid — invalidation is generation-based, not table-based).
    /// </summary>
    public JitBlock? TryMemo(uint headLinear, ulong generation)
    {
        int slot = (int)(headLinear & (MemoSize - 1));
        JitBlock? block = _memoBlock[slot];
        if (block is not null && _memoHead[slot] == headLinear && _memoGeneration[slot] == generation)
        {
            return block;
        }

        return null;
    }

    private void Memoize(uint headLinear, ulong generation, JitBlock block)
    {
        int slot = (int)(headLinear & (MemoSize - 1));
        _memoHead[slot] = headLinear;
        _memoGeneration[slot] = generation;
        _memoBlock[slot] = block;
    }

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

        /// <summary>
        /// A built block survived a generation change; it is offered to the
        /// caller for lazy revalidation (byte re-check against the current
        /// mapping). The verdict itself is untouched until Refresh/NoteStale.
        /// </summary>
        Stale,
    }

    /// <summary>Gets or sets the callback fired whenever the cache drops every block (raw chaining clears its target table).</summary>
    public Action? Cleared { get; set; }

    /// <summary>
    /// Looks up a head. Unseen heads are recorded as pending; pending heads
    /// become eligible at <paramref name="buildThreshold"/> visits. A
    /// generation change (decode-cache flush / remap) hands a stale block to
    /// the caller for lazy revalidation and otherwise keeps the visit
    /// history: hot code re-forms promptly instead of re-warming from zero,
    /// which matters because the guest rewrites page tables thousands of
    /// times per 100M steps.
    /// </summary>
    public Verdict Lookup(uint headLinear, ulong generation, int buildThreshold, out JitBlock? block)
    {
        block = null;
        if (!_heads.TryGetValue(headLinear, out Entry entry))
        {
            _heads[headLinear] = new Entry { Generation = generation, Visits = 1 };
            PendingVisits++;
            return Verdict.Pending;
        }

        if (entry.Generation != generation)
        {
            if (entry.Block is not null)
            {
                block = entry.Block;
                return Verdict.Stale;
            }

            // Pending/rejected verdicts carry no decoded state; only the
            // rejection outcome is generation-bound. Keep the visit count.
            entry.Generation = generation;
            entry.Rejected = false;
            entry.RetryAtVisits = 0;
        }

        if (entry.Block is not null)
        {
            block = entry.Block;
            Memoize(headLinear, generation, entry.Block);
            return Verdict.Hit;
        }

        if (entry.Rejected)
        {
            return Verdict.Rejected;
        }

        int visits = entry.Visits + 1;
        entry.Visits = visits;
        _heads[headLinear] = entry;
        if (visits >= buildThreshold && visits >= entry.RetryAtVisits)
        {
            return Verdict.Absent;
        }

        PendingVisits++;
        return Verdict.Pending;
    }

    /// <summary>Stamps a lazily-revalidated block as current-generation.</summary>
    public void Refresh(uint headLinear, ulong generation, JitBlock block)
    {
        _heads[headLinear] = new Entry { Generation = generation, Block = block };
        Memoize(headLinear, generation, block);
    }

    /// <summary>Clears a stale verdict (block or rejection), keeping the visit history.</summary>
    public void NoteStale(uint headLinear)
    {
        if (_heads.TryGetValue(headLinear, out Entry entry))
        {
            entry.Block = null;
            entry.Rejected = false;
            entry.RetryAtVisits = 0;
            _heads[headLinear] = entry;
        }
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
            Memoize(headLinear, generation, block);
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
        Array.Clear(_memoBlock);
        Array.Clear(_memoHead);
        Array.Clear(_memoGeneration);
        Cleared?.Invoke();
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
            Cleared?.Invoke();
            return;
        }

        foreach (uint head in drop)
        {
            _heads.Remove(head);
        }
    }

    /// <summary>Counts heads that are neither built nor rejected (diagnostic).</summary>
    public int CountPendingHeads()
    {
        int pending = 0;
        foreach (Entry entry in _heads.Values)
        {
            if (entry.Block is null && !entry.Rejected)
            {
                pending++;
            }
        }

        return pending;
    }

    private struct Entry
    {
        public ulong Generation { get; set; }

        public JitBlock? Block { get; set; }

        public bool Rejected { get; set; }

        public int Visits { get; set; }

        public int RetryAtVisits { get; set; }
    }
}
