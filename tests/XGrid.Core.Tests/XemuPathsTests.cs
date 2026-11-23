using System;
using System.IO;
using XGrid.Core.Xemu;
using Xunit;

namespace XGrid.Core.Tests;

/// <summary>Pins the core lookup order used by the shell.</summary>
public sealed class XemuPathsTests
{
    [Fact]
    public void BuiltCoreRelativePath_PointsAtTheTrackedTreeOutput()
    {
        string expected = Path.Combine(
            "xemu",
            "dist",
            OperatingSystem.IsWindows() ? "xemu.exe" : "xemu");

        Assert.Equal(expected, XemuPaths.BuiltCoreRelativePath);
    }

    [Fact]
    public void FindCore_PrefersAnExplicitExistingPath()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "xgrid-xemu-paths",
            Guid.NewGuid().ToString("N"));
        string core = Path.Combine(directory, OperatingSystem.IsWindows() ? "xemu.exe" : "xemu");
        Directory.CreateDirectory(directory);
        File.WriteAllText(core, string.Empty);
        try
        {
            Assert.Equal(Path.GetFullPath(core), XemuPaths.FindCore(core));
        }
        finally
        {
            File.Delete(core);
            Directory.Delete(directory);
        }
    }
}
