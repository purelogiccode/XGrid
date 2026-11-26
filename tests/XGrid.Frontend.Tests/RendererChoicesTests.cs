using System;
using System.IO;
using XGrid.Core.Xemu;
using XGrid.Frontend.Avalonia.Services;
using Xunit;

namespace XGrid.Frontend.Tests;

/// <summary>
/// Pins what the settings page offers for a given core build. A build without
/// Vulkan must not list it: selecting it only makes the core log a fallback
/// while the shell keeps claiming Vulkan.
/// </summary>
public sealed class RendererChoicesTests : IDisposable
{
    private readonly string _directory;

    public RendererChoicesTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "xgrid-renderer-choices-" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(_directory);
    }

    [Fact]
    public void Keys_IncludeVulkanOnlyWhenTheBuildReportsIt()
    {
        CoreCapabilities without = Read("""{"renderers":["OPENGL","NULL"]}""");
        CoreCapabilities with = Read("""{"renderers":["OPENGL","VULKAN","NULL"]}""");

        Assert.Equal(
            [CoreCapabilities.RendererOpenGl, CoreCapabilities.RendererNull],
            RendererChoices.Keys(without));
        Assert.Equal(
            [CoreCapabilities.RendererOpenGl, CoreCapabilities.RendererVulkan, CoreCapabilities.RendererNull],
            RendererChoices.Keys(with));
    }

    [Fact]
    public void Keys_OfferDirect3D11OnlyWhenTheBuildReportsIt()
    {
        CoreCapabilities with = Read("""{"renderers":["D3D11","OPENGL","NULL"]}""");
        CoreCapabilities without = Read("""{"renderers":["OPENGL","NULL"]}""");

        Assert.Equal(
            [CoreCapabilities.RendererDirect3D11, CoreCapabilities.RendererOpenGl, CoreCapabilities.RendererNull],
            RendererChoices.Keys(with));
        Assert.DoesNotContain(CoreCapabilities.RendererDirect3D11, RendererChoices.Keys(without), StringComparer.Ordinal);
    }

    [Fact]
    public void Keys_KeepVulkanWhenTheCoreDoesNotReport()
    {
        // No manifest: the shell cannot prove Vulkan is missing, so an older
        // configuration that selected it is not silently rewritten here.
        Assert.Contains(
            CoreCapabilities.RendererVulkan,
            RendererChoices.Keys(null),
            StringComparer.Ordinal);
    }

    [Fact]
    public void Hint_SaysWhyVulkanIsNotOffered()
    {
        CoreCapabilities without = Read("""{"renderers":["OPENGL","NULL"]}""");
        CoreCapabilities with = Read("""{"renderers":["OPENGL","VULKAN","NULL"]}""");

        Assert.Contains("no Vulkan", RendererChoices.Hint(without), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Direct3D 11", RendererChoices.Hint(with), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("may fall back", RendererChoices.Hint(null), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Label_NamesEveryKey()
    {
        Assert.Equal("OpenGL", RendererChoices.Label(CoreCapabilities.RendererOpenGl));
        Assert.Equal("Vulkan", RendererChoices.Label(CoreCapabilities.RendererVulkan));
        Assert.Equal("Direct3D 11", RendererChoices.Label(CoreCapabilities.RendererDirect3D11));
        Assert.Contains("headless", RendererChoices.Label(CoreCapabilities.RendererNull), StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
    }

    private CoreCapabilities Read(string json)
    {
        File.WriteAllText(Path.Combine(_directory, CoreCapabilities.FileName), json);
        return CoreCapabilities.ReadFromDirectory(_directory)
               ?? throw new InvalidOperationException("the manifest did not parse");
    }
}
