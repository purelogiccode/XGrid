using System;
using System.IO;
using XGrid.Core.Xemu;
using Xunit;

namespace XGrid.Core.Tests;

/// <summary>
/// Pins the in-process hosting seam: the DLL lookup, the shared extra
/// arguments, and the one-machine-per-process contract.
/// </summary>
public sealed class XemuCoreHostingTests : IDisposable
{
    private readonly string _root;

    public XemuCoreHostingTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "xgrid-core-hosting-" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(_root);
    }

    [Fact]
    public void TryLoad_ReturnsNullWithoutTheCoreLibrary()
    {
        string core = Path.Combine(_root, "xemu.exe");
        File.WriteAllBytes(core, []);

        Assert.Null(XemuInProcessCore.TryLoad(core, out string libraryPath));
        Assert.Empty(libraryPath);
    }

    [Fact]
    public void TryLoad_ReturnsNullForAnEmptyCorePath()
    {
        Assert.Null(XemuInProcessCore.TryLoad(string.Empty, out string libraryPath));
        Assert.Empty(libraryPath);
    }

    [Fact]
    public void BuildExtraArguments_MapsTheAudioBackend()
    {
        Assert.Equal(
            ["-audio", "none"],
            XemuCoreProcess.BuildExtraArguments(new XemuLaunchSettings { AudioBackend = "none" }));

        Assert.Equal(
            ["-audio", "dsound"],
            XemuCoreProcess.BuildExtraArguments(new XemuLaunchSettings { AudioBackend = "dsound" }));

        Assert.Equal(
            ["-audio", "wav,path=C:\\capture.wav"],
            XemuCoreProcess.BuildExtraArguments(
                new XemuLaunchSettings { AudioBackend = "wav", AudioWavPath = "C:\\capture.wav" }));

        Assert.Equal(
            ["-audio", "dsound,device={c847f520-6c79-4ae1-ab1e-b9cc32469df8}"],
            XemuCoreProcess.BuildExtraArguments(new XemuLaunchSettings
            {
                AudioBackend = "dsound",
                AudioDevice = "{c847f520-6c79-4ae1-ab1e-b9cc32469df8}",
            }));

        Assert.Equal(
            ["-audio", "pa,out.name=alsa_output.usb"],
            XemuCoreProcess.BuildExtraArguments(new XemuLaunchSettings
            {
                AudioBackend = "pa",
                AudioDevice = "alsa_output.usb",
            }));

        // The null sink never takes a device, even if an old setting kept one.
        Assert.Equal(
            ["-audio", "none"],
            XemuCoreProcess.BuildExtraArguments(new XemuLaunchSettings
            {
                AudioBackend = "none",
                AudioDevice = "{c847f520-6c79-4ae1-ab1e-b9cc32469df8}",
            }));
    }

    [Fact]
    public void BuildExtraArguments_LeavesAutoToTheCore()
    {
        Assert.Empty(XemuCoreProcess.BuildExtraArguments(new XemuLaunchSettings()));
    }

    [Fact]
    public void HostSharedSections_AreWindowsOnly()
    {
        // The core's display and input sinks use Windows named sections. Other
        // hosts must not advertise them, because the core keeps its SDL window.
        Assert.Equal(OperatingSystem.IsWindows(), XemuDisplaySink.IsSupported);
        Assert.Equal(OperatingSystem.IsWindows(), XemuInputSink.IsSupported);
    }

    [Fact]
    public void WindowEmbedding_IsWindowsOnly()
    {
        // Reparenting needs a host window handle and Win32; the separate core
        // process owns its window everywhere else.
        Assert.Equal(OperatingSystem.IsWindows(), XemuWindowEmbedder.IsSupported);
    }

    [Fact]
    public void ShellPadSection_PublishesFourPlayerPorts()
    {
        // The core binds one shell controller per Xbox port, so port 3 must be
        // a valid record even when no gamepad is assigned to it.
        Assert.Equal(4, XemuInputSink.PadCount);

        using var sink = new XemuInputSink("Local\\XGridPadTest-" + Guid.NewGuid().ToString("N"));
        sink.Write(3, 0, 0, 0, 0, 0, 0, 0);
    }

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
    }
}
