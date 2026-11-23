using System;
using System.Collections.Generic;
using System.IO;
using XGrid.Frontend.Avalonia.Models;
using Xunit;

namespace XGrid.Frontend.Tests;

/// <summary>
/// Pins the shell settings model: the xemu launch inputs and the per-title
/// override merge (a null member inherits, a set member replaces).
/// </summary>
public sealed class EmulatorSettingsTests
{
    [Fact]
    public void DefaultsAreRetailHardwareAndNoOverrides()
    {
        var settings = new EmulatorSettings();

        Assert.Equal(64, settings.RamMb);
        Assert.Equal("nearest", settings.Filter);
        Assert.Empty(settings.TitleOverrides);
        Assert.False(settings.IsBiosReady);
    }

    [Fact]
    public void NormalizeRepairsUnknownValues()
    {
        var settings = new EmulatorSettings
        {
            Filter = "vhs",
            RamMb = 32,
        };
        settings.TitleOverrides[@"relative\path.iso"] = new TitleOverride
        {
            RamMb = 32,
        };
        settings.TitleOverrides[string.Empty] = new TitleOverride { RamMb = 64 };
        settings.TitleOverrides["ignored.iso"] = new TitleOverride();

        settings.Normalize();

        Assert.Equal("nearest", settings.Filter);
        Assert.Equal(64, settings.RamMb);

        // The empty key and the empty override are dropped; the remaining row
        // keeps only the value that survived validation.
        TitleOverride row = Assert.Single(settings.TitleOverrides).Value;
        Assert.Null(row.RamMb);
    }

    [Fact]
    public void WithTitleOverrideInheritsEverythingWhenThereIsNoRow()
    {
        var settings = new EmulatorSettings { RamMb = 128 };

        EmulatorSettings effective = settings.WithTitleOverride(@"C:\games\no-override.iso");

        Assert.Equal(128, effective.RamMb);
        Assert.NotSame(settings, effective);
    }

    [Fact]
    public void WithTitleOverrideAppliesOnlyTheMembersTheRowSets()
    {
        var settings = new EmulatorSettings { RamMb = 64 };

        // A path key must be rooted on the running host: the lookup normalizes it
        // with Path.GetFullPath, and a Windows literal is a relative path on Linux.
        string image = Path.Combine(Path.GetTempPath(), "xgrid-games", "spheremap.iso");
        settings.TitleOverrides[image] = new TitleOverride { RamMb = 128 };

        // The override dictionary is case-insensitive, so the launched path's
        // casing does not have to match the stored key.
        EmulatorSettings effective = settings.WithTitleOverride(image.ToUpperInvariant());

        Assert.Equal(128, effective.RamMb);
    }

    [Fact]
    public void WithTitleOverrideIgnoresANullPath()
    {
        var settings = new EmulatorSettings { RamMb = 128 };
        settings.TitleOverrides[@"C:\games\a.iso"] = new TitleOverride { RamMb = 64 };

        EmulatorSettings effective = settings.WithTitleOverride(null);

        Assert.Equal(128, effective.RamMb);
    }

    [Fact]
    public void CloneCopiesTheOverrideRowsIndependently()
    {
        var settings = new EmulatorSettings();
        settings.TitleOverrides[@"C:\games\a.iso"] = new TitleOverride { RamMb = 128 };

        EmulatorSettings copy = settings.Clone();
        copy.TitleOverrides[@"C:\games\a.iso"].RamMb = 64;

        Assert.Equal(128, settings.TitleOverrides[@"C:\games\a.iso"].RamMb);
    }

    [Fact]
    public void TitleOverrideDescribesItself()
    {
        Assert.Equal("inherit everything", new TitleOverride().Describe());
        Assert.Equal("128 MB", new TitleOverride { RamMb = 128 }.Describe());
    }

    [Fact]
    public void NormalizeTitleKeyProducesOneKeyPerImage()
    {
        string a = EmulatorSettings.NormalizeTitleKey(@"C:\games\a.iso");
        string b = EmulatorSettings.NormalizeTitleKey(@"c:\GAMES\A.ISO");

        Assert.Equal(Path.GetFullPath(@"C:\games\a.iso"), a);

        // Windows path comparison is case-insensitive at the dictionary level.
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { [a] = 1 };
        Assert.True(map.ContainsKey(b));
    }
}
