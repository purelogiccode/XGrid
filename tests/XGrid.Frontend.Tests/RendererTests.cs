using System;
using XGrid.Frontend.Avalonia.Models;
using XGrid.Frontend.Avalonia.Services;
using XGrid.Host;
using Xunit;

namespace XGrid.Frontend.Tests;

/// <summary>
/// Pins the renderer switch's decision layer: the software rasterizer is the
/// default and the portable answer, the OpenGL backend is selected only where a
/// WGL context can exist, and an unknown persisted value repairs itself.
/// </summary>
public sealed class RendererPolicyTests
{
    [Fact]
    public void ANewConfigurationRunsTheSoftwareRasterizer()
    {
        var settings = new EmulatorSettings();

        Assert.Equal(RendererPolicy.Software, settings.Renderer);
        Assert.Equal(RendererChoice.Software, RendererPolicy.Decide(settings.Renderer, platformSupportsOpenGl: true));
    }

    [Fact]
    public void OpenGlIsChosenOnlyWhereThePlatformCanProvideIt()
    {
        Assert.Equal(RendererChoice.OpenGl, RendererPolicy.Decide(RendererPolicy.OpenGl, platformSupportsOpenGl: true));
        Assert.Equal(RendererChoice.OpenGlUnavailable, RendererPolicy.Decide(RendererPolicy.OpenGl, platformSupportsOpenGl: false));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("vulkan")]
    [InlineData("GL")]
    [InlineData("gl")]
    [InlineData("software")]
    public void UnknownOrDifferentValuesDecideToSoftware(string? requested)
    {
        Assert.Equal(RendererChoice.Software, RendererPolicy.Decide(requested, platformSupportsOpenGl: true));
        Assert.Equal(RendererPolicy.Software, RendererPolicy.Normalize(requested));
    }

    [Fact]
    public void NormalizeRepairsThePersistedRenderer()
    {
        var settings = new EmulatorSettings { Renderer = "d3d12" };

        settings.Normalize();

        Assert.Equal(RendererPolicy.Software, settings.Renderer);
    }

    [Fact]
    public void CloneCopiesTheRenderer()
    {
        var settings = new EmulatorSettings { Renderer = RendererPolicy.OpenGl };

        EmulatorSettings copy = settings.Clone();

        Assert.Equal(RendererPolicy.OpenGl, copy.Renderer);
        copy.Renderer = RendererPolicy.Software;
        Assert.Equal(RendererPolicy.OpenGl, settings.Renderer);
    }

    [Fact]
    public void DescribeNamesBothRenderers()
    {
        Assert.Equal("Software rasterizer", RendererPolicy.Describe(RendererPolicy.Software));
        Assert.Equal("OpenGL backend", RendererPolicy.Describe(RendererPolicy.OpenGl));
    }
}

/// <summary>
/// Pins the side-effect half of the renderer switch against a real machine: the
/// software choice never touches GL, and the OpenGL choice either attaches the
/// host backend or (probe-gated) says why it fell back.
/// </summary>
public sealed class GraphicsBackendHostTests
{
    [Fact]
    public void TheMachineExposesTheGpuMemoryAdapterABackendNeeds()
    {
        var machine = new XboxMachine(64u * 1024u * 1024u);

        Assert.NotNull(machine.Nv2A.GpuMemory);
    }

    [Fact]
    public void SoftwareSelectionNeverAttachesABackend()
    {
        var machine = new XboxMachine(64u * 1024u * 1024u);

        bool attached = GraphicsBackendHost.TryAttach(machine, new EmulatorSettings(), out string detail);

        Assert.False(attached);
        Assert.Null(machine.Nv2A.Backend);
        Assert.StartsWith("Software rasterizer", detail, StringComparison.Ordinal);

        // Detection is idempotent: detaching an unattached machine is a no-op.
        GraphicsBackendHost.Detach(machine);
        Assert.Null(machine.Nv2A.Backend);
    }

    [Fact]
    public void OpenGlSelectionAttachesTheHostBackendWhereThePlatformProvidesIt()
    {
        if (!OperatingSystem.IsWindows())
        {
            // The pure policy test above pins the non-Windows answer; creating a
            // WGL context off Windows is not a supported path.
            return;
        }

        var machine = new XboxMachine(64u * 1024u * 1024u);
        var settings = new EmulatorSettings { Renderer = RendererPolicy.OpenGl };

        bool attached = GraphicsBackendHost.TryAttach(machine, settings, out string detail);
        if (!attached)
        {
            // Probe-gated by design (no GL driver, no desktop session): the
            // machine stays on the software rasterizer and the detail says why.
            Assert.Null(machine.Nv2A.Backend);
            Assert.StartsWith("Software rasterizer (OpenGL probe failed:", detail, StringComparison.Ordinal);
            return;
        }

        Assert.NotNull(machine.Nv2A.Backend);
        Assert.StartsWith("OpenGL backend", detail, StringComparison.Ordinal);

        // With a backend attached the frame source is the backend readback, and
        // there is none before the first flip: the session shows nothing rather
        // than mixing software pixels into a backend frame.
        Assert.False(machine.Nv2A.TryCopyDisplayFrame(out _, out _, out _));

        GraphicsBackendHost.Detach(machine);
        Assert.Null(machine.Nv2A.Backend);
    }
}
