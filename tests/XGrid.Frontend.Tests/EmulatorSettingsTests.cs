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

        // The empty key, the empty override and the row whose only value was
        // invalid are all dropped.
        Assert.Empty(settings.TitleOverrides);
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
        Assert.Equal(GraphicsSettings.RendererOpenGl, effective.Graphics.Renderer);
        Assert.Equal(GraphicsSettings.FilterLinear, effective.Graphics.Filtering);
        Assert.Equal("nearest", effective.Filter);
    }

    [Fact]
    public void WithTitleOverrideAppliesTheGraphicsMembers()
    {
        var settings = new EmulatorSettings();
        settings.Graphics.Renderer = GraphicsSettings.RendererVulkan;
        settings.Graphics.Filtering = GraphicsSettings.FilterLinear;
        settings.Filter = "bilinear";

        string image = Path.Combine(Path.GetTempPath(), "xgrid-games", "panzer.iso");
        settings.TitleOverrides[image] = new TitleOverride
        {
            Renderer = GraphicsSettings.RendererOpenGl,
            Filtering = GraphicsSettings.FilterNearest,
            Filter = "crt",
        };

        EmulatorSettings effective = settings.WithTitleOverride(image);

        Assert.Equal(GraphicsSettings.RendererOpenGl, effective.Graphics.Renderer);
        Assert.Equal(GraphicsSettings.FilterNearest, effective.Graphics.Filtering);
        Assert.Equal("crt", effective.Filter);

        // The source settings keep their global values.
        Assert.Equal(GraphicsSettings.RendererVulkan, settings.Graphics.Renderer);
        Assert.Equal("bilinear", settings.Filter);
    }

    [Fact]
    public void WithTitleOverrideIgnoresUnknownGraphicsValues()
    {
        var settings = new EmulatorSettings();
        string image = Path.Combine(Path.GetTempPath(), "xgrid-games", "broken.iso");
        settings.TitleOverrides[image] = new TitleOverride
        {
            Renderer = "D3D12",
            Filtering = "trilinear",
            Filter = "vhs",
        };

        EmulatorSettings effective = settings.WithTitleOverride(image);

        Assert.Equal(GraphicsSettings.RendererOpenGl, effective.Graphics.Renderer);
        Assert.Equal(GraphicsSettings.FilterLinear, effective.Graphics.Filtering);
        Assert.Equal("nearest", effective.Filter);
    }

    [Fact]
    public void NormalizeDropsUnknownOverrideGraphics()
    {
        var settings = new EmulatorSettings();
        settings.TitleOverrides[@"C:\games\a.iso"] = new TitleOverride
        {
            Renderer = "d3d12",
            Filtering = "trilinear",
            Filter = "vhs",
        };
        settings.TitleOverrides[@"C:\games\b.iso"] = new TitleOverride
        {
            Renderer = GraphicsSettings.RendererVulkan,
            Filtering = GraphicsSettings.FilterNearest,
            Filter = "crt",
        };

        settings.Normalize();

        TitleOverride row = Assert.Single(settings.TitleOverrides).Value;
        Assert.Equal(GraphicsSettings.RendererVulkan, row.Renderer);
        Assert.Equal(GraphicsSettings.FilterNearest, row.Filtering);
        Assert.Equal("crt", row.Filter);
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
        Assert.Equal(
            "128 MB, OpenGL, nearest texture filtering, CRT mask",
            new TitleOverride
            {
                RamMb = 128,
                Renderer = GraphicsSettings.RendererOpenGl,
                Filtering = GraphicsSettings.FilterNearest,
                Filter = "crt",
            }.Describe());
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
