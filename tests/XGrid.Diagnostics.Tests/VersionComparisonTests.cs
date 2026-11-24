using Xunit;

namespace XGrid.Diagnostics.Tests;

public sealed class VersionComparisonTests
{
    [Theory]
    [InlineData("v1.2.4", "1.2.3", true)]
    [InlineData("1.2.3", "1.2.3", false)]
    [InlineData("1.2.2", "1.2.3", false)]
    [InlineData("v2", "1.9.9", true)]
    [InlineData("1.2.3-beta", "1.2.2", true)]
    [InlineData("1.2.3+build", "1.2.3", false)]
    [InlineData("not-a-version", "1.0.0", false)]
    public void IsNewer_ComparesNumericCores(string candidate, string current, bool expected)
    {
        Assert.Equal(expected, VersionComparison.IsNewer(candidate, current));
    }

    [Fact]
    public void TryParse_AcceptsPrefixesAndPrerelease()
    {
        Assert.True(VersionComparison.TryParse("v10.20.30-rc.1", out System.Version? version));
        Assert.Equal(new System.Version(10, 20, 30, 0), version);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("v")]
    [InlineData("1.2.3.4.5")]
    [InlineData("-1.2.3")]
    [InlineData("1.-2.3")]
    [InlineData("1.2.x")]
    [InlineData("999999999999.1")]
    public void TryParse_RejectsInvalidVersions(string? text)
    {
        Assert.False(VersionComparison.TryParse(text, out System.Version? version));
        Assert.Null(version);
    }

    [Fact]
    public void TryParse_AcceptsFourNumericParts()
    {
        Assert.True(VersionComparison.TryParse("1.2.3.4", out System.Version? version));
        Assert.Equal(new System.Version(1, 2, 3, 4), version);
    }

    [Theory]
    [InlineData("v1.0.0", null, false)]
    [InlineData(null, "1.0.0", false)]
    [InlineData("1.0.0", "not-a-version", false)]
    [InlineData("1.10.0", "1.9.0", true)]
    public void IsNewer_ReturnsFalseWhenEitherSideIsInvalid(string? candidate, string? current, bool expected)
    {
        Assert.Equal(expected, VersionComparison.IsNewer(candidate, current));
    }
}
