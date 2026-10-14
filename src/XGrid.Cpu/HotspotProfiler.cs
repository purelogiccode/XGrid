using System;
using System.Collections.Generic;
using System.IO;

namespace XGrid.Cpu;

/// <summary>
/// Bounded hotspot profiler for JIT scoping (§126, first half of the
/// recommended slice: profiler only, no decode cache). Counts per-mnemonic
/// executions (array indexed by mnemonic), per-PC executions for code heads
/// (taken-transfer targets), and fetch/translate counters (SoftTLB
/// hit/miss, cross-page splits, RAM-vs-MMIO data accesses).
///
/// Bounded following the §119 precedent: code heads are capped at
/// <see cref="MaxTrackedHeads"/> entries with <see cref="HeadsOverflow"/>
/// counting visits to heads beyond the cap. The profiler is host-side
/// measurement state: it is not part of the guest and is untouched by
/// <c>XboxCpu.Reset</c> and snapshot save/load.
/// </summary>
public sealed class HotspotProfiler : IHotspotObserver
{
    /// <summary>Maximum distinct code heads tracked (128, §119 precedent).</summary>
    public const int MaxTrackedHeads = 128;

    private readonly ulong[] _mnemonicCounts = new ulong[MaxMnemonicIndex() + 1];
    private readonly Dictionary<uint, ulong> _heads = new();

    /// <summary>Gets the total number of executed instructions observed.</summary>
    public ulong TotalInstructions { get; private set; }

    /// <summary>Gets the total number of taken-transfer head visits.</summary>
    public ulong TotalHeadVisits { get; private set; }

    /// <summary>Gets the number of head visits to heads beyond the tracking cap.</summary>
    public ulong HeadsOverflow { get; private set; }

    /// <summary>Gets the number of distinct code heads currently tracked.</summary>
    public int TrackedHeadCount => _heads.Count;

    /// <summary>Gets the total number of instruction-fetch windows observed.</summary>
    public ulong Fetches { get; private set; }

    /// <summary>Gets the number of fetch windows spanning a 4 KB page boundary.</summary>
    public ulong FetchCrossPage { get; private set; }

    /// <summary>Gets the number of SoftTLB lookup hits.</summary>
    public ulong TlbHits { get; private set; }

    /// <summary>Gets the number of SoftTLB lookup misses.</summary>
    public ulong TlbMisses { get; private set; }

    /// <summary>Gets the number of translated RAM data reads.</summary>
    public ulong DataReadsRam { get; private set; }

    /// <summary>Gets the number of translated RAM data writes.</summary>
    public ulong DataWritesRam { get; private set; }

    /// <summary>Gets the number of translated MMIO-region data reads.</summary>
    public ulong DataReadsMmio { get; private set; }

    /// <summary>Gets the number of translated MMIO-region data writes.</summary>
    public ulong DataWritesMmio { get; private set; }

    /// <summary>Gets the number of cross-page split data accesses.</summary>
    public ulong SplitAccesses { get; private set; }

    /// <summary>Gets the total number of translated data accesses (RAM + MMIO, reads + writes).</summary>
    public ulong TotalDataAccesses => DataReadsRam + DataWritesRam + DataReadsMmio + DataWritesMmio;

    /// <inheritdoc />
    public void Observe(in DecodedInstruction instruction, uint eipBefore, uint eipAfter)
    {
        TotalInstructions++;
        int index = (int)instruction.Mnemonic;
        if ((uint)index < (uint)_mnemonicCounts.Length)
        {
            _mnemonicCounts[index]++;
        }

        uint fallThrough = eipBefore + (uint)instruction.Length;
        if (eipAfter != fallThrough)
        {
            TotalHeadVisits++;
            if (_heads.TryGetValue(eipAfter, out ulong count))
            {
                _heads[eipAfter] = count + 1;
            }
            else if (_heads.Count < MaxTrackedHeads)
            {
                _heads[eipAfter] = 1;
            }
            else
            {
                HeadsOverflow++;
            }
        }
    }

    /// <inheritdoc />
    public void NoteFetch(bool crossesPage)
    {
        Fetches++;
        if (crossesPage)
        {
            FetchCrossPage++;
        }
    }

    /// <inheritdoc />
    public void NoteTlbHit()
    {
        TlbHits++;
    }

    /// <inheritdoc />
    public void NoteTlbMiss()
    {
        TlbMisses++;
    }

    /// <inheritdoc />
    public void NoteDataAccess(bool isWrite, bool isRam)
    {
        if (isRam)
        {
            if (isWrite)
            {
                DataWritesRam++;
            }
            else
            {
                DataReadsRam++;
            }
        }
        else
        {
            if (isWrite)
            {
                DataWritesMmio++;
            }
            else
            {
                DataReadsMmio++;
            }
        }
    }

    /// <inheritdoc />
    public void NoteSplitAccess()
    {
        SplitAccesses++;
    }

    /// <summary>Returns the execution count for one mnemonic (0 when out of range).</summary>
    public ulong CountFor(Mnemonic mnemonic)
    {
        int index = (int)mnemonic;
        return (uint)index < (uint)_mnemonicCounts.Length ? _mnemonicCounts[index] : 0;
    }

    /// <summary>Returns up to <paramref name="count"/> mnemonics ordered by executions, descending.</summary>
    public IReadOnlyList<(Mnemonic Mnemonic, ulong Count)> TopMnemonics(int count)
    {
        var rows = new List<(Mnemonic Mnemonic, ulong Count)>();
        foreach (Mnemonic mnemonic in Enum.GetValues<Mnemonic>())
        {
            ulong hits = CountFor(mnemonic);
            if (hits != 0)
            {
                rows.Add((mnemonic, hits));
            }
        }

        rows.Sort((a, b) => b.Count.CompareTo(a.Count));
        if (rows.Count > count)
        {
            rows.RemoveRange(count, rows.Count - count);
        }

        return rows;
    }

    /// <summary>Returns up to <paramref name="count"/> code heads ordered by visits, descending.</summary>
    public IReadOnlyList<(uint Eip, ulong Count)> TopHeads(int count)
    {
        var rows = new List<(uint Eip, ulong Count)>(_heads.Count);
        foreach (var entry in _heads)
        {
            rows.Add((entry.Key, entry.Value));
        }

        rows.Sort((a, b) => b.Count.CompareTo(a.Count));
        if (rows.Count > count)
        {
            rows.RemoveRange(count, rows.Count - count);
        }

        return rows;
    }

    /// <summary>
    /// Writes the ranked report: summary rates, the top mnemonic rows, and
    /// the top code-head rows, as <c>section,key,count,share</c> lines.
    /// </summary>
    public void WriteReport(TextWriter writer, int topRows = 25)
    {
        ArgumentNullException.ThrowIfNull(writer);
        double total = TotalInstructions == 0 ? 1.0 : TotalInstructions;
        double translates = (TlbHits + TlbMisses) == 0 ? 1.0 : TlbHits + TlbMisses;
        double accesses = TotalDataAccesses == 0 ? 1.0 : TotalDataAccesses;

        writer.WriteLine(FormattableString.Invariant($"# hotspot top={topRows} total={TotalInstructions}"));
        writer.WriteLine(FormattableString.Invariant($"summary,totalInstructions,{TotalInstructions},1.000000"));
        writer.WriteLine(FormattableString.Invariant($"summary,headVisits,{TotalHeadVisits},{TotalHeadVisits / total:F6}"));
        writer.WriteLine(FormattableString.Invariant($"summary,trackedHeads,{TrackedHeadCount},{TrackedHeadCount / (double)MaxTrackedHeads:F6}"));
        writer.WriteLine(FormattableString.Invariant($"summary,headOverflowVisits,{HeadsOverflow},{HeadsOverflow / total:F6}"));
        writer.WriteLine(FormattableString.Invariant($"summary,fetches,{Fetches},{Fetches / total:F6}"));
        writer.WriteLine(FormattableString.Invariant($"summary,fetchCrossPage,{FetchCrossPage},{FetchCrossPage / (Fetches == 0 ? 1.0 : Fetches):F6}"));
        writer.WriteLine(FormattableString.Invariant($"summary,tlbHits,{TlbHits},{TlbHits / translates:F6}"));
        writer.WriteLine(FormattableString.Invariant($"summary,tlbMisses,{TlbMisses},{TlbMisses / translates:F6}"));
        writer.WriteLine(FormattableString.Invariant($"summary,dataReadsRam,{DataReadsRam},{DataReadsRam / accesses:F6}"));
        writer.WriteLine(FormattableString.Invariant($"summary,dataWritesRam,{DataWritesRam},{DataWritesRam / accesses:F6}"));
        writer.WriteLine(FormattableString.Invariant($"summary,dataReadsMmio,{DataReadsMmio},{DataReadsMmio / accesses:F6}"));
        writer.WriteLine(FormattableString.Invariant($"summary,dataWritesMmio,{DataWritesMmio},{DataWritesMmio / accesses:F6}"));
        writer.WriteLine(FormattableString.Invariant($"summary,splitAccesses,{SplitAccesses},{SplitAccesses / accesses:F6}"));

        foreach ((Mnemonic mnemonic, ulong hits) in TopMnemonics(topRows))
        {
            writer.WriteLine(FormattableString.Invariant($"mnemonic,{mnemonic},{hits},{hits / total:F6}"));
        }

        foreach ((uint eip, ulong hits) in TopHeads(topRows))
        {
            writer.WriteLine(FormattableString.Invariant($"head,0x{eip:X8},{hits},{hits / total:F6}"));
        }
    }

    /// <summary>Writes the ranked report to <paramref name="path"/>.</summary>
    public void SaveReport(string path, int topRows = 25)
    {
        using var writer = new StreamWriter(path);
        WriteReport(writer, topRows);
    }

    private static int MaxMnemonicIndex()
    {
        int max = 0;
        foreach (Mnemonic value in Enum.GetValues<Mnemonic>())
        {
            max = Math.Max(max, (int)value);
        }

        return max;
    }
}
