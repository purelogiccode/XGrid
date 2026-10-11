using XGrid.Core.Diagnostics;
using Xunit;

namespace XGrid.Core.Tests;

public sealed class EipHistogramTests
{
    [Fact]
    public void Sample_EmptyHistogramReportsZero()
    {
        var histogram = new EipHistogram();

        Assert.Equal(0UL, histogram.Total);
        Assert.Empty(histogram.Top(40));
    }

    [Fact]
    public void Sample_CountsPerAddress()
    {
        var histogram = new EipHistogram();
        histogram.Sample(0x2D3B9);
        histogram.Sample(0x2EA35);
        histogram.Sample(0x2D3B9);

        Assert.Equal(3UL, histogram.Total);
        var top = histogram.Top(40);
        Assert.Equal(2, top.Count);
        Assert.Equal(0x2D3B9U, top[0].Eip);
        Assert.Equal(2UL, top[0].Count);
        Assert.Equal(0x2EA35U, top[1].Eip);
        Assert.Equal(1UL, top[1].Count);
    }

    [Fact]
    public void Top_RespectsLimit()
    {
        var histogram = new EipHistogram();
        for (uint i = 0; i < 10; i++)
        {
            histogram.Sample(0x1000 + i);
        }

        Assert.Equal(3, histogram.Top(3).Count);
        Assert.Equal(10, histogram.Top(40).Count);
    }
}
