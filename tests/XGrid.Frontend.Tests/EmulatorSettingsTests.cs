using System;
using System.Collections.Generic;
using System.IO;
using XGrid.Frontend.Avalonia.Models;
using Xunit;

namespace XGrid.Frontend.Tests;

/// <summary>
/// Pins the settings model: the global graphics switches and the per-title
/// override merge (a null member inherits, a set member replaces).
/// </summary>
public sealed class EmulatorSettingsTests
{
    [Fact]
    public void GlobalGraphicsSwitchesDefaultToTheCliDefaults()
    {
        // The CLI flags are opt-in, so a fresh configuration must be too: the
        // resident vertex program, the legacy sampler and the software renderer.
        var settings = new EmulatorSettings();

        Assert.False(settings.FixedFunctionVertex);
        Assert.Equal("legacy", settings.TextureFilter);
        Assert.Equal("software", settings.Renderer);
        Assert.False(EmulatorSettings.IsGpuRenderer(settings.Renderer));
        Assert.Empty(settings.TitleOverrides);
    }

    [Fact]
    public void RendererSurvivesCloneAndNormalize()
    {
        var settings = new EmulatorSettings { Renderer = EmulatorSettings.RendererOpenGl };

        EmulatorSettings copy = settings.Clone();

        Assert.Equal(EmulatorSettings.RendererOpenGl, copy.Renderer);
        Assert.True(EmulatorSettings.IsGpuRenderer(copy.Renderer));
        Assert.NotSame(settings, copy);
    }

    [Fact]
    public void NormalizeRepairsAnUnknownRenderer()
    {
        // A hand-edited or newer config must never leave the session on a
        // renderer key nothing implements: anything but "opengl" is software.
        var settings = new EmulatorSettings { Renderer = "vulkan" };

        settings.Normalize();

        Assert.Equal(EmulatorSettings.RendererSoftware, settings.Renderer);
    }

    [Fact]
    public void RendererKeyComparisonsAreOrdinal()
    {
        Assert.True(EmulatorSettings.IsGpuRenderer("opengl"));
        Assert.False(EmulatorSettings.IsGpuRenderer("OpenGL"));
        Assert.False(EmulatorSettings.IsGpuRenderer(null));
        Assert.False(EmulatorSettings.IsGpuRenderer(string.Empty));
    }

    [Fact]
    public void NormalizeRepairsUnknownValues()
    {
        var settings = new EmulatorSettings
        {
            TextureFilter = "bilinear",
            RamMb = 32,
        };
        settings.TitleOverrides[@"relative\path.iso"] = new TitleOverride
        {
            TextureFilter = "nonsense",
            RamMb = 32,
            FixedFunctionVertex = true,
        };
        settings.TitleOverrides[string.Empty] = new TitleOverride { FixedFunctionVertex = true };
        settings.TitleOverrides["ignored.iso"] = new TitleOverride();

        settings.Normalize();

        Assert.Equal("legacy", settings.TextureFilter);
        Assert.Equal(64, settings.RamMb);

        // The empty key and the empty override are dropped; the remaining row
        // keeps only the value that survived validation.
        TitleOverride row = Assert.Single(settings.TitleOverrides).Value;
        Assert.True(row.FixedFunctionVertex);
        Assert.Null(row.TextureFilter);
        Assert.Null(row.RamMb);
    }

    [Fact]
    public void WithTitleOverrideInheritsEverythingWhenThereIsNoRow()
    {
        var settings = new EmulatorSettings { FixedFunctionVertex = true, TextureFilter = "registers", RamMb = 128 };

        EmulatorSettings effective = settings.WithTitleOverride(@"C:\games\no-override.iso");

        Assert.True(effective.FixedFunctionVertex);
        Assert.Equal("registers", effective.TextureFilter);
        Assert.Equal(128, effective.RamMb);
        Assert.NotSame(settings, effective);
    }

    [Fact]
    public void WithTitleOverrideAppliesOnlyTheMembersTheRowSets()
    {
        var settings = new EmulatorSettings { FixedFunctionVertex = true, TextureFilter = "registers", RamMb = 64 };

        // A path key must be rooted on the running host: the lookup normalizes it
        // with Path.GetFullPath, and a Windows literal is a relative path on Linux.
        string image = Path.Combine(Path.GetTempPath(), "xgrid-games", "spheremap.iso");
        settings.TitleOverrides[image] = new TitleOverride { FixedFunctionVertex = false };

        // The override dictionary is case-insensitive, so the launched path's
        // casing does not have to match the stored key.
        EmulatorSettings effective = settings.WithTitleOverride(image.ToUpperInvariant());

        // The row overrides FFP only; the filter and RAM inherit.
        Assert.False(effective.FixedFunctionVertex);
        Assert.Equal("registers", effective.TextureFilter);
        Assert.Equal(64, effective.RamMb);
    }

    [Fact]
    public void WithTitleOverrideAppliesTheFilterAndRamToo()
    {
        var settings = new EmulatorSettings();
        string image = Path.Combine(Path.GetTempPath(), "xgrid-games", "debug-kit.chd");
        settings.TitleOverrides[image] = new TitleOverride
        {
            TextureFilter = "registers",
            RamMb = 128,
        };

        EmulatorSettings effective = settings.WithTitleOverride(image);

        Assert.Equal("registers", effective.TextureFilter);
        Assert.Equal(128, effective.RamMb);
        Assert.False(effective.FixedFunctionVertex);
    }

    [Fact]
    public void WithTitleOverrideIgnoresANullPath()
    {
        var settings = new EmulatorSettings { FixedFunctionVertex = true };
        settings.TitleOverrides[@"C:\games\a.iso"] = new TitleOverride { FixedFunctionVertex = false };

        EmulatorSettings effective = settings.WithTitleOverride(null);

        Assert.True(effective.FixedFunctionVertex);
    }

    [Fact]
    public void CloneCopiesTheOverrideRowsIndependently()
    {
        var settings = new EmulatorSettings();
        settings.TitleOverrides[@"C:\games\a.iso"] = new TitleOverride { FixedFunctionVertex = true };

        EmulatorSettings copy = settings.Clone();
        copy.TitleOverrides[@"C:\games\a.iso"].FixedFunctionVertex = false;

        Assert.True(settings.TitleOverrides[@"C:\games\a.iso"].FixedFunctionVertex);
    }

    [Fact]
    public void TitleOverrideDescribesItself()
    {
        Assert.Equal("inherit everything", new TitleOverride().Describe());

        string text = new TitleOverride
        {
            FixedFunctionVertex = true,
            TextureFilter = "registers",
            RamMb = 128,
        }.Describe();

        Assert.Contains("fixed-function vertex", text, StringComparison.Ordinal);
        Assert.Contains("registers", text, StringComparison.Ordinal);
        Assert.Contains("128 MB", text, StringComparison.Ordinal);
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
