using System;
using Xunit;

namespace XGrid.Cpu.Tests;

/// <summary>
/// D7 pending-head census: the heads that never reach the build threshold are
/// counted by fetch source and visit count, with the run's own measured
/// formation cost applied to them. This is the measurement the boot task
/// needed (the unbuilt population is 99.6-100% RAM code on the gate titles),
/// so the shape of the line is pinned here.
/// </summary>
public sealed class JitAttributionTests
{
    /// <summary>
    /// mov eax,0 / inc eax / cmp eax,0x20 / jne -8 / hlt - a short loop with
    /// several heads, all below the (huge) build threshold.
    /// </summary>
    private static readonly byte[] Loop =
    [
        0xB8, 0x00, 0x00, 0x00, 0x00,
        0x40,
        0x3D, 0x20, 0x00, 0x00, 0x00,
        0x75, 0xF8,
        0xF4,
    ];

    [Fact]
    public void PendingCensus_CountsTheUnbuiltHeadsByFetchSource()
    {
        XboxCpu cpu = CreateAttributed(buildThreshold: 1_000_000);
        cpu.Execute(400);

        string report = cpu.FormatJitAttribution();
        int pendingStart = report.IndexOf("Jit attribution pending ", StringComparison.Ordinal);
        Assert.True(pendingStart >= 0, report);
        string pending = report[pendingStart..];

        Assert.Contains("bySource[ram=n:", pending, StringComparison.Ordinal);
        Assert.Contains("unknown=n:0/v:0", pending, StringComparison.Ordinal);
        Assert.Contains("visits[1-3=", pending, StringComparison.Ordinal);
        Assert.Contains("formMsPerBlock=", pending, StringComparison.Ordinal);
        Assert.Contains("buildAllMs=", pending, StringComparison.Ordinal);

        int heads = ReadCell(pending, "pending heads=");
        int visits = ReadCell(pending, " visits=");
        int ramHeads = ReadCell(pending, "bySource[ram=n:");
        Assert.True(heads > 0, report);
        Assert.True(heads == ramHeads, report);
        Assert.True(visits >= heads, report);
    }

    [Fact]
    public void PendingCensus_IsEmptyWithoutTheAttribution()
    {
        XboxCpu cpu = CreateAttributed(buildThreshold: 1_000_000);
        cpu.JitTiming = false;
        cpu.Execute(400);

        Assert.Equal(string.Empty, cpu.FormatJitAttribution());
    }

    [Fact]
    public void PendingCensus_ShrinksWhenTheHeadsAreBuilt()
    {
        XboxCpu low = CreateAttributed(buildThreshold: 1);
        low.Execute(400);
        int builtHeads = ReadCell(low.FormatJitAttribution(), "pending heads=");

        XboxCpu high = CreateAttributed(buildThreshold: 1_000_000);
        high.Execute(400);
        int pendingHeads = ReadCell(high.FormatJitAttribution(), "pending heads=");

        Assert.True(
            pendingHeads > builtHeads,
            $"threshold 1: {builtHeads} pending; threshold 1e6: {pendingHeads} pending");
    }

    private static XboxCpu CreateAttributed(int buildThreshold)
    {
        XboxCpu cpu = CpuHarness.Create(Loop);
        cpu.DecodeCacheEnabled = true;
        cpu.SpecExecutionEnabled = true;
        cpu.JitEnabled = true;
        cpu.JitTiming = true;
        cpu.JitBuildThreshold = buildThreshold;
        cpu.JitRawBlocks = true;
        return cpu;
    }

    private static int ReadCell(string report, string prefix)
    {
        int start = report.IndexOf(prefix, StringComparison.Ordinal);
        Assert.True(start >= 0, $"'{prefix}' missing from: {report}");
        start += prefix.Length;
        int end = start;
        while (end < report.Length && char.IsAsciiDigit(report[end]))
        {
            end++;
        }

        Assert.True(end > start, $"'{prefix}' has no number in: {report}");
        return int.Parse(report.AsSpan(start, end - start), System.Globalization.CultureInfo.InvariantCulture);
    }
}
