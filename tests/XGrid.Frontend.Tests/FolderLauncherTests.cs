using System;
using XGrid.Frontend.Avalonia.Services;
using Xunit;

namespace XGrid.Frontend.Tests;

/// <summary>Pins the guards of the folder menu entries; opening is left to the desktop.</summary>
public sealed class FolderLauncherTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Open_RejectsABlankPathWithoutLaunching(string path)
    {
        Assert.Throws<ArgumentException>(() => FolderLauncher.Open(path));
    }
}
