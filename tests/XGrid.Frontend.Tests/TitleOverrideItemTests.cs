using System;
using System.Linq;
using XGrid.Frontend.Avalonia.Models;
using XGrid.Frontend.Avalonia.ViewModels;
using Xunit;

namespace XGrid.Frontend.Tests;

/// <summary>
/// Pins the settings row for a per-title override: the "inherit" entries map
/// to null members, persisted values survive the round trip, and the renderer
/// choices follow what the selected core reports.
/// </summary>
public sealed class TitleOverrideItemTests
{
    [Fact]
    public void InheritSelectionsMapToNullMembers()
    {
        var item = new TitleOverrideItem(
            "title:4D530002",
            new TitleOverride(),
            _ => { },
            ["OPENGL", "NULL"]);

        TitleOverride value = item.ToOverride();

        Assert.Null(value.RamMb);
        Assert.Null(value.Renderer);
        Assert.Null(value.Filtering);
        Assert.Null(value.Filter);
        Assert.True(value.IsEmpty);
    }

    [Fact]
    public void PersistedValuesRoundTripThroughTheRow()
    {
        var item = new TitleOverrideItem(
            @"C:\games\a.iso",
            new TitleOverride
            {
                RamMb = 128,
                Renderer = GraphicsSettings.RendererVulkan,
                Filtering = GraphicsSettings.FilterNearest,
                Filter = "crt",
            },
            _ => { },
            ["OPENGL", "VULKAN", "NULL"]);

        TitleOverride value = item.ToOverride();

        Assert.Equal(128, value.RamMb);
        Assert.Equal(GraphicsSettings.RendererVulkan, value.Renderer);
        Assert.Equal(GraphicsSettings.FilterNearest, value.Filtering);
        Assert.Equal("crt", value.Filter);
    }

    [Fact]
    public void RendererOptionsFollowTheCoreBuild()
    {
        var item = new TitleOverrideItem(
            @"C:\games\a.iso",
            new TitleOverride(),
            _ => { },
            ["OPENGL", "NULL"]);

        Assert.Equal(
            ["", "OPENGL", "NULL"],
            item.RendererOptions.Select(option => option.Key),
            StringComparer.Ordinal);
        Assert.Equal(
            ["", "linear", "nearest"],
            item.FilteringOptions.Select(option => option.Key),
            StringComparer.Ordinal);
        Assert.Equal(
            ["", "nearest", "bilinear", "scanline", "crt"],
            item.FilterOptions.Select(option => option.Key),
            StringComparer.Ordinal);
    }

    [Fact]
    public void AnUnknownPersistedValueFallsBackToInherit()
    {
        var item = new TitleOverrideItem(
            @"C:\games\a.iso",
            new TitleOverride { Renderer = GraphicsSettings.RendererVulkan },
            _ => { },
            ["OPENGL", "NULL"]);

        Assert.Equal(string.Empty, item.SelectedRenderer.Key);
        Assert.Null(item.ToOverride().Renderer);
    }
}
