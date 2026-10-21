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

        Assert.Equal("software", settings.Renderer);
        Assert.False(settings.UsesGpuBackend);
        Assert.Equal(EmulatorSettings.RendererSoftware, settings.Renderer);
    }

    [Fact]
    public void TheOpenGlKeyAsksForTheGpuBackend()
    {
        var settings = new EmulatorSettings { Renderer = EmulatorSettings.RendererOpenGl };

        Assert.Equal(EmulatorSettings.RendererOpenGl, settings.Renderer);
        Assert.True(settings.UsesGpuBackend);
    }

    [Fact]
    public void NormalizeRepairsAnUnknownRenderer()
    {
        var settings = new EmulatorSettings { Renderer = "vulkan" };
        settings.Normalize();
        Assert.Equal(EmulatorSettings.RendererSoftware, settings.Renderer);

        // A hand-edited file can carry a null/empty value; it must not throw
        // and must not select anything but the software rasterizer.
        settings.Renderer = string.Empty;
        settings.Normalize();
        Assert.False(settings.UsesGpuBackend);
    }

    [Fact]
    public void NormalizeKeepsTheOpenGlRenderer()
    {
        var settings = new EmulatorSettings { Renderer = EmulatorSettings.RendererOpenGl };

        settings.Normalize();

        Assert.Equal(EmulatorSettings.RendererOpenGl, settings.Renderer);
        Assert.True(settings.UsesGpuBackend);
    }

    [Fact]
    public void CloneCopiesTheRendererIndependently()
    {
        var settings = new EmulatorSettings { Renderer = EmulatorSettings.RendererOpenGl };

        EmulatorSettings copy = settings.Clone();
        copy.Renderer = EmulatorSettings.RendererSoftware;

        Assert.Equal(EmulatorSettings.RendererOpenGl, settings.Renderer);
        Assert.False(copy.UsesGpuBackend);
    }

    [Fact]
    public void TheRendererRoundTripsThroughJsonWithoutTheDerivedFlag()
    {
        var settings = new EmulatorSettings { Renderer = EmulatorSettings.RendererOpenGl };

        string json = JsonSerializer.Serialize(settings);
        EmulatorSettings? loaded = JsonSerializer.Deserialize<EmulatorSettings>(json);

        Assert.NotNull(loaded);
        Assert.Equal(EmulatorSettings.RendererOpenGl, loaded.Renderer);
        Assert.True(loaded.UsesGpuBackend);

        // The derived convenience flag is configuration, not state: persisting
        // it would let a stale file disagree with Renderer.
        Assert.DoesNotContain("UsesGpuBackend", json, StringComparison.Ordinal);
    }

    [Fact]
    public void ATitleCanPickItsOwnRendererAndOtherwiseInherits()
    {
        var settings = new EmulatorSettings { Renderer = EmulatorSettings.RendererOpenGl };
        settings.TitleOverrides[@"C:\games\software-only.iso"] = new TitleOverride
        {
            Renderer = EmulatorSettings.RendererSoftware,
        };
        settings.TitleOverrides[@"C:\games\inherits.iso"] = new TitleOverride { FixedFunctionVertex = true };

        EmulatorSettings pinned = settings.WithTitleOverride(@"C:\games\software-only.iso");
        EmulatorSettings inherited = settings.WithTitleOverride(@"C:\games\inherits.iso");

        Assert.Equal("software", pinned.Renderer);
        Assert.False(pinned.UsesGpuBackend);
        Assert.Equal(EmulatorSettings.RendererOpenGl, inherited.Renderer);
        Assert.True(inherited.UsesGpuBackend);

        // Neither merge touched the stored configuration.
        Assert.Equal(EmulatorSettings.RendererOpenGl, settings.Renderer);
    }

    [Fact]
    public void NormalizeDropsAnUnknownOverrideRenderer()
    {
        var settings = new EmulatorSettings();
        settings.TitleOverrides[@"C:\games\a.iso"] = new TitleOverride
        {
            Renderer = "vulkan",
            TextureFilter = "registers",
        };

        settings.Normalize();

        TitleOverride row = Assert.Single(settings.TitleOverrides).Value;
        Assert.Null(row.Renderer);
        Assert.Equal("registers", row.TextureFilter);

        // An override with nothing set must read as a pure inherit, so a
        // dropped renderer cannot leave a phantom row behind.
        Assert.Equal("inherit everything", new TitleOverride().Describe());
    }

    [Fact]
    public void TitleOverrideDescribesTheRenderer()
    {
        string text = new TitleOverride { Renderer = EmulatorSettings.RendererOpenGl }.Describe();

        Assert.Contains("renderer: " + EmulatorSettings.RendererOpenGl, text, StringComparison.Ordinal);
    }
}
