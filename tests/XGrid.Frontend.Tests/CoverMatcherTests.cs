using System;
using XGrid.Frontend.Avalonia.Services;
using Xunit;

namespace XGrid.Frontend.Tests;

/// <summary>Pins annotation stripping, normalization and fuzzy scoring.</summary>
public sealed class CoverMatcherTests
{
    [Theory]
    [InlineData("Halo - Combat Evolved (USA) [!]", "Halo - Combat Evolved")]
    [InlineData("007 - Agent Under Fire (Europe) (En,Fr,De)", "007 - Agent Under Fire")]
    [InlineData("Game [Disc 1] {Beta}", "Game")]
    [InlineData("Plain Title", "Plain Title")]
    public void StripAnnotations_RemovesBalancedGroups(string value, string expected)
    {
        Assert.Equal(expected, CoverMatcher.StripAnnotations(value));
    }

    [Theory]
    [InlineData("Halo - Combat Evolved (USA)", "halo combat evolved")]
    [InlineData("Tom & Jerry", "tom and jerry")]
    [InlineData("The Warriors", "warriors")]
    [InlineData("Warriors, The", "warriors")]
    [InlineData("Grand Theft Auto - San Andreas", "grand theft auto san andreas")]
    [InlineData("  ...  ", "")]
    public void Normalize_ProducesTheComparableTitle(string value, string expected)
    {
        Assert.Equal(expected, CoverMatcher.Normalize(value));
    }

    [Fact]
    public void Normalize_CanKeepAnnotations()
    {
        Assert.Equal("halo usa combat evolved", CoverMatcher.Normalize("Halo (USA) - Combat Evolved", false));
    }

    [Fact]
    public void ExpandAliases_AddsSubtitleSuffixes()
    {
        Assert.Contains("A - B - C", CoverMatcher.ExpandAliases("A - B - C"), StringComparer.Ordinal);
        Assert.Contains("B - C", CoverMatcher.ExpandAliases("A - B - C"), StringComparer.Ordinal);
        Assert.Contains("C", CoverMatcher.ExpandAliases("A - B - C"), StringComparer.Ordinal);
    }

    [Theory]
    [InlineData("Martha", "Marhta", 0.961)]
    [InlineData("Dixon", "Dicksonx", 0.813)]
    [InlineData("Jellyfish", "Smellyfish", 0.896)]
    public void CalculateJaroWinklerSimilarity_MatchesTheReferenceValues(
        string left,
        string right,
        double expected)
    {
        Assert.Equal(expected, CoverMatcher.CalculateJaroWinklerSimilarity(left, right), 0.001);
    }

    [Fact]
    public void Score_AcceptsRomanNumeralsForSequels()
    {
        Assert.True(CoverMatcher.Score("Grand Theft Auto III", "Grand Theft Auto 3") >= 0.90);
    }

    [Fact]
    public void Score_RejectsDifferentSequelNumbers()
    {
        Assert.True(CoverMatcher.Score("Halo", "Halo 2") < CoverDefaults.DefaultFuzzyThreshold);
    }

    [Fact]
    public void Score_IgnoresNumbersInEditionNames()
    {
        Assert.True(CoverMatcher.Score("Halo Beta 2", "Halo Beta 3") >= 0.90);
    }

    [Fact]
    public void Score_MatchesAnnotatedCoverNames()
    {
        Assert.Equal(
            1.0,
            CoverMatcher.Score("007 - Agent Under Fire", "007 - Agent Under Fire (Europe) (En,Fr,De)"));
    }

    [Fact]
    public void ParseAnnotations_ExtractsRegionsYearAndRank()
    {
        CoverAnnotations annotations = CoverMatcher.ParseAnnotations(
            "Grand Theft Auto - San Andreas (USA, Europe) (En,Fr,De) (2004) (Rev 1)");

        Assert.Equal("Grand Theft Auto - San Andreas", annotations.Title);
        Assert.Contains("USA", annotations.Regions, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("Europe", annotations.Regions, StringComparer.OrdinalIgnoreCase);
        Assert.Equal("2004", annotations.Year);
        Assert.Equal(0, annotations.Rank);
    }

    [Fact]
    public void ParseAnnotations_RecognizesDemoAndBetaRanks()
    {
        Assert.Equal(1, CoverMatcher.ParseAnnotations("Game (Demo) (USA)").Rank);
        Assert.Equal(2, CoverMatcher.ParseAnnotations("Game (Beta) (Japan)").Rank);
        Assert.Equal("Japan", Assert.Single(CoverMatcher.ParseAnnotations("Game (Beta) (Japan)").Regions));
    }
}
