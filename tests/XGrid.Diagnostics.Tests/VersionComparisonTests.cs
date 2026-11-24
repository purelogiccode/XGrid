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
}
