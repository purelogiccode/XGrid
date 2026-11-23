using System;
using System.IO;
using XGrid.Frontend.Avalonia.Services;
using Xunit;

namespace XGrid.Frontend.Tests;

/// <summary>
/// Pins the screenshot file names: chronological, stable, and never overwriting
/// a capture taken in the same second.
/// </summary>
public sealed class ScreenshotNamingTests
{
    [Fact]
    public void NextPath_NamesTheFileAfterTheCaptureTime()
    {
        string directory = Path.Combine(Path.GetTempPath(), "xgrid-shots-" + Guid.NewGuid().ToString("N"));

        string path = ScreenshotNaming.NextPath(directory, new DateTimeOffset(2026, 11, 23, 19, 42, 7, TimeSpan.Zero));

        Assert.Equal(Path.Combine(directory, "screenshot-20261123-194207.png"), path);
    }

    [Fact]
    public void NextPath_AvoidsOverwritingACaptureFromTheSameSecond()
    {
        string directory = Path.Combine(Path.GetTempPath(), "xgrid-shots-" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(directory);
        try
        {
            var timestamp = new DateTimeOffset(2026, 11, 23, 19, 42, 7, TimeSpan.Zero);
            string first = ScreenshotNaming.NextPath(directory, timestamp);
            File.WriteAllBytes(first, []);

            string second = ScreenshotNaming.NextPath(directory, timestamp);
            File.WriteAllBytes(second, []);

            string third = ScreenshotNaming.NextPath(directory, timestamp);
            Assert.Equal(Path.Combine(directory, "screenshot-20261123-194207-1.png"), second);
            Assert.Equal(Path.Combine(directory, "screenshot-20261123-194207-2.png"), third);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Directory_IsTheScreenshotsFolderInsideTheDataFolder()
    {
        string data = Path.Combine(Path.GetTempPath(), "xgrid-data");

        Assert.Equal(Path.Combine(data, "screenshots"), ScreenshotNaming.Directory(data));
    }

    [Fact]
    public void Directory_RejectsAnEmptyDataFolder()
    {
        Assert.Throws<ArgumentException>(() => ScreenshotNaming.Directory(" "));
    }
    [Fact]
    public void NextPath_RejectsAnEmptyDirectory()
    {
        Assert.Throws<ArgumentException>(() => ScreenshotNaming.NextPath(" ", DateTimeOffset.Now));
    }
}
