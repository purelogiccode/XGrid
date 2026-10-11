using System.Collections.Generic;

namespace XGrid.Core.Diagnostics;

/// <summary>
/// Samples guest EIP values into per-address buckets for coarse execution
/// profiling (e.g. identifying the menu-idle loop of a long boot run).
/// Sampling is driven by the caller at a fixed instruction cadence.
/// </summary>
public sealed class EipHistogram
{
    private readonly Dictionary<uint, ulong> _counts = new();

    /// <summary>Gets the total number of samples recorded.</summary>
    public ulong Total { get; private set; }

    /// <summary>Records one EIP sample.</summary>
    public void Sample(uint eip)
    {
        _counts.TryGetValue(eip, out ulong count);
        _counts[eip] = count + 1;
        Total++;
    }

    /// <summary>Returns up to <paramref name="count"/> buckets ordered by hits, descending.</summary>
    public IReadOnlyList<(uint Eip, ulong Count)> Top(int count)
    {
        var buckets = new List<(uint Eip, ulong Count)>(_counts.Count);
        foreach (var entry in _counts)
        {
            buckets.Add((entry.Key, entry.Value));
        }

        buckets.Sort((a, b) => b.Count.CompareTo(a.Count));
        if (buckets.Count > count)
        {
            buckets.RemoveRange(count, buckets.Count - count);
        }

        return buckets;
    }
}
