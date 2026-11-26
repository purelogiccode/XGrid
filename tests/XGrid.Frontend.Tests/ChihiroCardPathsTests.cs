using System;
using System.IO;
using XGrid.Frontend.Avalonia.Services;
using Xunit;

namespace XGrid.Frontend.Tests;

/// <summary>Pins the Chihiro card-folder location.</summary>
public sealed class ChihiroCardPathsTests
{
    [Fact]
    public void ResolveCardsDirectory_UsesTheXemuPreferenceFolder()
    {
        string path = ChihiroCardPaths.ResolveCardsDirectory();

        Assert.True(Path.IsPathRooted(path));
        Assert.EndsWith(
            Path.Combine("xemu", "xemu", "cards"),
            path,
            StringComparison.OrdinalIgnoreCase);

        if (OperatingSystem.IsWindows())
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            Assert.StartsWith(appData, path, StringComparison.OrdinalIgnoreCase);
        }
    }
}
