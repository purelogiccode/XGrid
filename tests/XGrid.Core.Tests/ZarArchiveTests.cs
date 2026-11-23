using System;
using System.IO;
using XGrid.Core.Xemu;
using Xunit;

namespace XGrid.Core.Tests;

/// <summary>
/// Pins the ZArchive directory reader the shell uses before launching: an
/// archive that contains an .iso entry is a disc, one that stores the game as a
/// file tree is not. The samples live under data/ (git-ignored), so a missing
/// sample skips the assertion rather than failing on a clean checkout.
/// </summary>
public sealed class ZarArchiveTests
{
    [Fact]
    public void EntryArchiveReportsItsDiscImage()
    {
        string? path = FindSample("hello-entry.zar");
        if (path is null)
        {
            return;
        }

        Assert.True(ZarArchive.TryOpen(path, out ZarArchive? archive));
        Assert.NotNull(archive);
        Assert.True(archive!.HasDiscImage);

        ZarEntry? disc = archive.FindDiscImage();
        Assert.NotNull(disc);
        Assert.Equal("hello.iso", disc!.Value.Name, ignoreCase: true);
        Assert.Equal(655360UL, disc.Value.Size);
    }

    [Fact]
    public void FileTreeArchiveHasNoDiscImage()
    {
        string? path = FindSample("hello.zar");
        if (path is null)
        {
            return;
        }

        Assert.True(ZarArchive.TryOpen(path, out ZarArchive? archive));
        Assert.NotNull(archive);
        Assert.NotEmpty(archive!.Entries);
        Assert.False(archive.HasDiscImage);
    }

    [Fact]
    public void ANonArchiveIsRejected()
    {
        string path = Path.Combine(Path.GetTempPath(), "xgrid-zar-" + Guid.NewGuid().ToString("N") + ".zar");
        File.WriteAllBytes(path, new byte[512]);
        try
        {
            Assert.False(ZarArchive.TryOpen(path, out _));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string? FindSample(string name)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, "data", "test-samples", "zar", name);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        return null;
    }
}
