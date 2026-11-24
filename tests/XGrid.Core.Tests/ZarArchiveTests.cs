using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;
using XGrid.Core.Xemu;
using Xunit;

namespace XGrid.Core.Tests;

/// <summary>
/// Pins the ZArchive directory reader the shell uses before launching: an
/// archive that contains an .iso entry is a disc, one that stores the game as a
/// file tree is not. Synthetic archives pin the entry-form shape everywhere;
/// the samples under data/ (git-ignored) add the real-tool case when present.
/// </summary>
public sealed class ZarArchiveTests
{
    [Fact]
    public void SyntheticEntryArchiveReportsItsDiscImage()
    {
        string path = Path.Combine(Path.GetTempPath(), "xgrid-zar-" + Guid.NewGuid().ToString("N") + ".zar");
        try
        {
            WriteArchive(path, [("hello.iso", 655360UL)]);

            Assert.True(ZarArchive.TryOpen(path, out ZarArchive? archive));
            Assert.NotNull(archive);
            Assert.True(archive.HasDiscImage);

            ZarEntry? disc = archive.FindDiscImage();
            Assert.NotNull(disc);
            Assert.Equal("hello.iso", disc.Value.Name, ignoreCase: true);
            Assert.Equal(655360UL, disc.Value.Size);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void SyntheticFileTreeArchiveHasNoDiscImage()
    {
        string path = Path.Combine(Path.GetTempPath(), "xgrid-zar-" + Guid.NewGuid().ToString("N") + ".zar");
        try
        {
            WriteArchive(path, [("default.xbe", 1024UL), ("data/music.wma", 2048UL)]);

            Assert.True(ZarArchive.TryOpen(path, out ZarArchive? archive));
            Assert.NotNull(archive);
            Assert.NotEmpty(archive.Entries);
            Assert.False(archive.HasDiscImage);
        }
        finally
        {
            File.Delete(path);
        }
    }

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
        Assert.True(archive.HasDiscImage);

        ZarEntry? disc = archive.FindDiscImage();
        Assert.NotNull(disc);
        Assert.Equal("hello.iso", disc.Value.Name, ignoreCase: true);
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
        Assert.NotEmpty(archive.Entries);
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

    /// <summary>
    /// Writes a minimal ZArchive with the footer, name table and tree the
    /// reader parses. The block payload is never read by the shell, so this
    /// pins the entry-form directory shape without a packer in the build.
    /// </summary>
    private static void WriteArchive(string path, IReadOnlyList<(string Name, ulong Size)> entries)
    {
        const uint magic = 0x169F52D6;
        const uint version = 0x61BF3A01;
        const int footerSize = 144;
        const int treeEntrySize = 16;
        const int namesOffset = 32;

        var names = new List<byte>();
        var tree = new byte[treeEntrySize * entries.Count];
        for (int index = 0; index < entries.Count; index++)
        {
            int nameOffset = names.Count;
            byte[] name = Encoding.Latin1.GetBytes(entries[index].Name);
            Assert.InRange(name.Length, 1, 127);
            names.Add((byte)name.Length);
            names.AddRange(name);

            int offset = index * treeEntrySize;
            BinaryPrimitives.WriteUInt32BigEndian(tree.AsSpan(offset), 0x8000_0000u | (uint)nameOffset);
            BinaryPrimitives.WriteUInt32BigEndian(tree.AsSpan(offset + 4), 0);
            BinaryPrimitives.WriteUInt32BigEndian(
                tree.AsSpan(offset + 8), (uint)(entries[index].Size & 0xFFFF_FFFFu));
            BinaryPrimitives.WriteUInt32BigEndian(
                tree.AsSpan(offset + 12), (uint)((entries[index].Size >> 32) << 16));
        }

        int treeOffset = namesOffset + names.Count;
        int footerOffset = treeOffset + tree.Length;
        int total = footerOffset + footerSize;

        var file = new byte[total];
        names.CopyTo(file, namesOffset);
        tree.CopyTo(file, treeOffset);

        Span<byte> footer = file.AsSpan(footerOffset, footerSize);
        BinaryPrimitives.WriteUInt64BigEndian(footer[32..], namesOffset);
        BinaryPrimitives.WriteUInt64BigEndian(footer[40..], (ulong)names.Count);
        BinaryPrimitives.WriteUInt64BigEndian(footer[48..], (ulong)treeOffset);
        BinaryPrimitives.WriteUInt64BigEndian(footer[56..], (ulong)tree.Length);
        BinaryPrimitives.WriteUInt64BigEndian(footer[128..], (ulong)total);
        BinaryPrimitives.WriteUInt32BigEndian(footer[136..], version);
        BinaryPrimitives.WriteUInt32BigEndian(footer[140..], magic);

        File.WriteAllBytes(path, file);
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
