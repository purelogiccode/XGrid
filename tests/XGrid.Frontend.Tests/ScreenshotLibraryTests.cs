using System;
using System.IO;
using System.Linq;
using XGrid.Frontend.Avalonia.Services;
using Xunit;

namespace XGrid.Frontend.Tests;

/// <summary>
/// Pins the Library's screenshot listing: only PNG captures, newest first,
/// capped at the strip's limit, and a missing folder is simply empty.
/// </summary>
public sealed class ScreenshotLibraryTests : IDisposable
{
    private readonly string _root;

    public ScreenshotLibraryTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "xgrid-shots-" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(_root);
    }

    [Fact]
    public void List_ReturnsNewestFirstAndIgnoresOtherFiles()
    {
        string older = Capture("screenshot-20260101-120000.png", new DateTime(2026, 1, 1, 12, 0, 0));
        string newer = Capture("screenshot-20260202-090000.png", new DateTime(2026, 2, 2, 9, 0, 0));
        _ = Capture("notes.txt", new DateTime(2026, 3, 3, 9, 0, 0));

        var files = ScreenshotLibrary.List(_root);

        Assert.Equal([newer, older], files.Select(file => file.Path), StringComparer.Ordinal);
    }

    [Fact]
    public void List_LimitsTheStrip()
    {
        for (int index = 0; index < 5; index++)
        {
            _ = Capture(
                "screenshot-" + index + ".png",
                new DateTime(2026, 1, 1, 12, index, 0));
        }

        var files = ScreenshotLibrary.List(_root, limit: 3);

        Assert.Equal(3, files.Count);
    }

    [Fact]
    public void List_TreatsAMissingFolderAsEmpty()
    {
        string missing = Path.Combine(_root, "absent");

        Assert.Empty(ScreenshotLibrary.List(missing));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void List_RejectsABlankFolder(string path)
    {
        Assert.Throws<ArgumentException>(() => ScreenshotLibrary.List(path));
    }

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
    }

    private string Capture(string name, DateTime timestamp)
    {
        string path = Path.Combine(_root, name);
        File.WriteAllBytes(path, []);
        File.SetLastWriteTime(path, timestamp);
        return path;
    }
}
