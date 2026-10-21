using System;
using System.Text.Json;
using XGrid.Frontend.Avalonia.Models;
using Xunit;

namespace XGrid.Frontend.Tests;

/// <summary>
/// Pins the renderer switch (Track B/P, decisions §220): the software
/// rasterizer is the default and every unknown persisted value repairs to it,
/// so a settings file written by a newer build can never select a renderer this
/// one does not have.
/// </summary>
public sealed class RendererSettingTests
{
    [Fact]
    public void FreshConfigurationUsesTheSoftwareRasterizer()
    {
        var settings = new EmulatorSettings();

        Assert.Equal("software", settings.GpuRenderer);
        Assert.False(settings.UsesGpuBackend);
        Assert.Equal(EmulatorSettings.SoftwareRenderer, settings.GpuRenderer);
    }

    [Fact]
    public void TheOpenGlKeyAsksForTheGpuBackend()
    {
        var settings = new EmulatorSettings { GpuRenderer = EmulatorSettings.OpenGlRenderer };

        Assert.Equal("gl", settings.GpuRenderer);
        Assert.True(settings.UsesGpuBackend);
    }

    [Fact]
    public void NormalizeRepairsAnUnknownRenderer()
    {
        var settings = new EmulatorSettings { GpuRenderer = "vulkan" };
        settings.Normalize();
        Assert.Equal(EmulatorSettings.SoftwareRenderer, settings.GpuRenderer);

        // A hand-edited file can carry a null/empty value; it must not throw
        // and must not select anything but the software rasterizer.
        settings.GpuRenderer = string.Empty;
        settings.Normalize();
        Assert.False(settings.UsesGpuBackend);
    }

    [Fact]
    public void NormalizeKeepsTheOpenGlRenderer()
    {
        var settings = new EmulatorSettings { GpuRenderer = "gl" };

        settings.Normalize();

        Assert.Equal("gl", settings.GpuRenderer);
        Assert.True(settings.UsesGpuBackend);
    }

    [Fact]
    public void CloneCopiesTheRendererIndependently()
    {
        var settings = new EmulatorSettings { GpuRenderer = "gl" };

        EmulatorSettings copy = settings.Clone();
        copy.GpuRenderer = EmulatorSettings.SoftwareRenderer;

        Assert.Equal("gl", settings.GpuRenderer);
        Assert.False(copy.UsesGpuBackend);
    }

    [Fact]
    public void TheRendererRoundTripsThroughJsonWithoutTheDerivedFlag()
    {
        var settings = new EmulatorSettings { GpuRenderer = "gl" };

        string json = JsonSerializer.Serialize(settings);
        EmulatorSettings? loaded = JsonSerializer.Deserialize<EmulatorSettings>(json);

        Assert.NotNull(loaded);
        Assert.Equal("gl", loaded.GpuRenderer);
        Assert.True(loaded.UsesGpuBackend);

        // The derived convenience flag is configuration, not state: persisting
        // it would let a stale file disagree with GpuRenderer.
        Assert.DoesNotContain("UsesGpuBackend", json, StringComparison.Ordinal);
    }

    [Fact]
    public void ATitleCanPickItsOwnRendererAndOtherwiseInherits()
    {
        var settings = new EmulatorSettings { GpuRenderer = "gl" };
        settings.TitleOverrides[@"C:\games\software-only.iso"] = new TitleOverride
        {
            GpuRenderer = EmulatorSettings.SoftwareRenderer,
        };
        settings.TitleOverrides[@"C:\games\inherits.iso"] = new TitleOverride { FixedFunctionVertex = true };

        EmulatorSettings pinned = settings.WithTitleOverride(@"C:\games\software-only.iso");
        EmulatorSettings inherited = settings.WithTitleOverride(@"C:\games\inherits.iso");

        Assert.Equal("software", pinned.GpuRenderer);
        Assert.False(pinned.UsesGpuBackend);
        Assert.Equal("gl", inherited.GpuRenderer);
        Assert.True(inherited.UsesGpuBackend);

        // Neither merge touched the stored configuration.
        Assert.Equal("gl", settings.GpuRenderer);
    }

    [Fact]
    public void NormalizeDropsAnUnknownOverrideRenderer()
    {
        var settings = new EmulatorSettings();
        settings.TitleOverrides[@"C:\games\a.iso"] = new TitleOverride
        {
            GpuRenderer = "vulkan",
            TextureFilter = "registers",
        };

        settings.Normalize();

        TitleOverride row = Assert.Single(settings.TitleOverrides).Value;
        Assert.Null(row.GpuRenderer);
        Assert.Equal("registers", row.TextureFilter);

        // An override with nothing set must read as a pure inherit, so a
        // dropped renderer cannot leave a phantom row behind.
        Assert.Equal("inherit everything", new TitleOverride().Describe());
    }

    [Fact]
    public void TitleOverrideDescribesTheRenderer()
    {
        string text = new TitleOverride { GpuRenderer = "gl" }.Describe();

        Assert.Contains("renderer: gl", text, StringComparison.Ordinal);
    }
}
