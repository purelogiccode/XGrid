using System;
using System.Collections.Generic;
using XGrid.Core.Xemu;
using Xunit;

namespace XGrid.Core.Tests;

/// <summary>
/// Pins the shell-to-core presentation filter contract. The core owns the
/// pixel work; the shell only sends one of these values.
/// </summary>
public sealed class XemuPresentationFilterTests
{
    [Theory]
    [InlineData(XemuPresentationFilter.Nearest, 0)]
    [InlineData(XemuPresentationFilter.Bilinear, 1)]
    [InlineData(XemuPresentationFilter.Scanline, 2)]
    [InlineData(XemuPresentationFilter.Crt, 3)]
    public void ToCoreValue_MapsEverySupportedFilter(string key, int expected)
    {
        Assert.Equal(expected, XemuPresentationFilter.ToCoreValue(key));
    }

    [Fact]
    public void NormalizeRepairsAnUnknownFilter()
    {
        Assert.Equal(XemuPresentationFilter.Nearest, XemuPresentationFilter.Normalize(null));
        Assert.Equal(XemuPresentationFilter.Nearest, XemuPresentationFilter.Normalize("vhs"));
        Assert.Equal(XemuPresentationFilter.Crt, XemuPresentationFilter.Normalize(XemuPresentationFilter.Crt));
        Assert.Equal(-1, XemuPresentationFilter.ToCoreValue("vhs"));
    }

    [Fact]
    public void EveryFilterNormalizesToItselfAndHasAUniqueCoreValue()
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var values = new HashSet<int>();
        foreach ((string key, string label) in XemuPresentationFilter.All)
        {
            Assert.True(keys.Add(key), "Duplicate filter key: " + key);
            Assert.False(string.IsNullOrWhiteSpace(label));
            Assert.Equal(key, XemuPresentationFilter.Normalize(key));
            Assert.True(values.Add(XemuPresentationFilter.ToCoreValue(key)));
        }
    }
}
