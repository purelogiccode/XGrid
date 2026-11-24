using System;
using System.Collections.Generic;
using System.IO;
using XGrid.Frontend.Avalonia.Models;
using XGrid.Frontend.Avalonia.ViewModels;
using Xunit;

namespace XGrid.Frontend.Tests;

/// <summary>
/// Pins the V2 per-title override keying: an override can be keyed by the XBE
/// certificate title id (<c>title:XXXXXXXX</c>) so it follows a title across
/// renamed, moved and re-wrapped images, with the V1 image path as the
/// fallback for images whose title id cannot be read (e.g. a CHD).
/// </summary>
public sealed class TitleOverrideKeyTests
{
    private const uint HaloTitleId = 0x4D530002u;

    [Fact]
    public void TitleIdKeyRoundTrips()
    {
        string key = EmulatorSettings.TitleIdKey(HaloTitleId);

        Assert.Equal("title:4D530002", key);
        Assert.True(EmulatorSettings.IsTitleIdKey(key));
        Assert.True(EmulatorSettings.TryParseTitleIdKey(key, out uint parsed));
        Assert.Equal(HaloTitleId, parsed);

        // Case-insensitive, like the dictionary.
        Assert.True(EmulatorSettings.TryParseTitleIdKey("TITLE:4d530002", out parsed));
        Assert.Equal(HaloTitleId, parsed);

        Assert.False(EmulatorSettings.IsTitleIdKey(@"C:\games\a.iso"));
        Assert.False(EmulatorSettings.TryParseTitleIdKey(@"C:\games\a.iso", out _));
        Assert.False(EmulatorSettings.TryParseTitleIdKey("title:nothex", out _));
    }

    [Fact]
    public void TitleKeyMatchesRegardlessOfImagePath()
    {
        var settings = new EmulatorSettings();
        settings.TitleOverrides[EmulatorSettings.TitleIdKey(HaloTitleId)] = new TitleOverride
        {
            RamMb = 128,
        };

        // A different folder and file name still resolve the title override.
        TitleOverride? found = settings.FindTitleOverride(@"D:\redump\Halo - Combat Evolved.iso", HaloTitleId);
        Assert.NotNull(found);
        Assert.Equal(128, found.RamMb);

        // Without a title id only the path key can match - and there is none.
        Assert.Null(settings.FindTitleOverride(@"D:\redump\Halo - Combat Evolved.iso"));
    }

    [Fact]
    public void TitleKeyWinsOverThePathKey()
    {
        var settings = new EmulatorSettings();
        settings.TitleOverrides[EmulatorSettings.TitleIdKey(HaloTitleId)] = new TitleOverride
        {
            RamMb = 128,
        };
        settings.TitleOverrides[EmulatorSettings.NormalizeTitleKey(@"C:\games\halo.iso")] = new TitleOverride
        {
            RamMb = 64,
        };

        EmulatorSettings effective = settings.WithTitleOverride(@"C:\games\halo.iso", HaloTitleId);

        Assert.Equal(128, effective.RamMb);
    }

    [Fact]
    public void PathKeyStillAppliesWhenTheTitleIdIsUnknown()
    {
        var settings = new EmulatorSettings();
        settings.TitleOverrides[EmulatorSettings.NormalizeTitleKey(@"C:\games\a.chd")] = new TitleOverride
        {
            RamMb = 128,
        };

        EmulatorSettings effective = settings.WithTitleOverride(@"C:\games\a.chd", titleId: null);

        Assert.Equal(128, effective.RamMb);
    }

    [Fact]
    public void NormalizeKeepsTitleKeysCanonicalAndDropsInvalidOnes()
    {
        var settings = new EmulatorSettings();
        settings.TitleOverrides["title:4d530002"] = new TitleOverride { RamMb = 128 };
        settings.TitleOverrides["title:zzzz"] = new TitleOverride { RamMb = 64 };
        settings.TitleOverrides[@"relative\image.iso"] = new TitleOverride { RamMb = 64 };

        settings.Normalize();

        Assert.True(settings.TitleOverrides.ContainsKey("title:4D530002"));
        Assert.True(settings.TitleOverrides.ContainsKey(Path.GetFullPath(@"relative\image.iso")));

        // An unparsable title key is not a path either: it stays as-is rather
        // than being expanded into a bogus full path.
        var keys = new List<string>(settings.TitleOverrides.Keys);
        Assert.DoesNotContain(keys, key => key.Contains("zzzz", StringComparison.Ordinal));
    }

    [Fact]
    public void OverrideRowShowsTheTitleIdentity()
    {
        var item = new TitleOverrideItem(
            EmulatorSettings.TitleIdKey(HaloTitleId),
            new TitleOverride { RamMb = 128 },
            _ => { },
            ["OPENGL", "NULL"],
            "HALO");

        Assert.Equal("HALO (Title 4D530002)", item.DisplayName);
        Assert.Equal("applies wherever this title id is launched", item.PathText);

        var unnamed = new TitleOverrideItem(
            EmulatorSettings.TitleIdKey(HaloTitleId),
            new TitleOverride(),
            _ => { },
            ["OPENGL", "NULL"]);
        Assert.Equal("Title 4D530002", unnamed.DisplayName);

        // The row key is a host path: the display name is Path.GetFileName, which
        // only recognises the running platform's separator, so a Windows literal
        // would not split on Linux.
        string pathRowKey = Path.Combine("games", "a.iso");
        var pathRow = new TitleOverrideItem(
            pathRowKey, new TitleOverride(), _ => { }, ["OPENGL", "NULL"]);
        Assert.Equal("a.iso", pathRow.DisplayName);
        Assert.Equal(pathRowKey, pathRow.PathText);
    }
}
