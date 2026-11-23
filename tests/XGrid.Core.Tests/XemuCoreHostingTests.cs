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
            ["-audiodev", "none"],
            XemuCoreProcess.BuildExtraArguments(new XemuLaunchSettings { AudioBackend = "none" }));

        Assert.Equal(
            ["-audiodev", "dsound"],
            XemuCoreProcess.BuildExtraArguments(new XemuLaunchSettings { AudioBackend = "dsound" }));

        Assert.Equal(
            ["-audiodev", "wav,path=C:\\capture.wav"],
            XemuCoreProcess.BuildExtraArguments(
                new XemuLaunchSettings { AudioBackend = "wav", AudioWavPath = "C:\\capture.wav" }));
    }

    [Fact]
    public void BuildExtraArguments_LeavesAutoToTheCore()
    {
        Assert.Empty(XemuCoreProcess.BuildExtraArguments(new XemuLaunchSettings()));
    }

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
    }
}
