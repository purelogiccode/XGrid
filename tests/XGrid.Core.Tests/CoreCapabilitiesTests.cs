using System;
using System.IO;
using XGrid.Core.Xemu;
using Xunit;

namespace XGrid.Core.Tests;

/// <summary>
/// Pins the manifest the core build writes beside the emulator. The shell hides
/// renderers this file says are missing, so an unreadable manifest must read as
/// "unknown" instead of throwing.
/// </summary>
public sealed class CoreCapabilitiesTests : IDisposable
{
    private readonly string _directory;

    public CoreCapabilitiesTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "xgrid-core-capabilities-" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(_directory);
    }

    [Fact]
    public void Read_ReportsTheRenderersTheBuildLists()
    {
        string core = Core();
        Manifest("""{"core":"xemu","version":"0.8.139","renderers":["OPENGL","NULL"]}""");

        CoreCapabilities? capabilities = CoreCapabilities.Read(core);

        Assert.NotNull(capabilities);
        Assert.Equal("0.8.139", capabilities.Version);
        Assert.False(capabilities.SupportsVulkan);
        Assert.True(capabilities.Has(CoreCapabilities.RendererOpenGl));
        Assert.True(capabilities.Has(CoreCapabilities.RendererNull));
    }

    [Fact]
    public void Read_ReportsVulkanWhenTheBuildHasIt()
    {
        Manifest("""{"version":"0.8.139","renderers":["OPENGL","VULKAN","NULL"]}""");

        CoreCapabilities? capabilities = CoreCapabilities.ReadFromDirectory(_directory);

        Assert.NotNull(capabilities);
        Assert.True(capabilities.SupportsVulkan);
    }

    [Fact]
    public void Read_ReportsDirect3D11WhenTheBuildHasIt()
    {
        Manifest("""{"version":"0.8.139","renderers":["D3D11","OPENGL","NULL"]}""");

        CoreCapabilities? capabilities = CoreCapabilities.ReadFromDirectory(_directory);

        Assert.NotNull(capabilities);
        Assert.True(capabilities.Has(CoreCapabilities.RendererDirect3D11));
    }

    [Fact]
    public void Read_NormalizesLowercaseRendererKeys()
    {
        Manifest("""{"version":"0.8.139","renderers":["opengl","vulkan","null"]}""");

        CoreCapabilities? capabilities = CoreCapabilities.ReadFromDirectory(_directory);

        Assert.NotNull(capabilities);
        Assert.True(capabilities.SupportsVulkan);
        Assert.True(capabilities.Has(CoreCapabilities.RendererOpenGl));
    }

    [Fact]
    public void Read_WithoutAManifestReportsNothing()
    {
        Assert.Null(CoreCapabilities.ReadFromDirectory(_directory));
        Assert.Null(CoreCapabilities.Read(Path.Combine(_directory, "xemu.exe")));
        Assert.Null(CoreCapabilities.Read(null));
    }

    [Fact]
    public void Read_WithAMalformedManifestReportsNothing()
    {
        Manifest("{ this is not json");

        Assert.Null(CoreCapabilities.ReadFromDirectory(_directory));
    }

    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
    }

    private string Core()
    {
        string path = Path.Combine(_directory, OperatingSystem.IsWindows() ? "xemu.exe" : "xemu");
        File.WriteAllBytes(path, []);
        return path;
    }

    private void Manifest(string json)
    {
        File.WriteAllText(Path.Combine(_directory, CoreCapabilities.FileName), json);
    }
}
