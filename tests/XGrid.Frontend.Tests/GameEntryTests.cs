using System;
using System.IO;
using XGrid.Frontend.Avalonia.Models;
using Xunit;

namespace XGrid.Frontend.Tests;

/// <summary>
/// The library row's certificate title id (the Track P follow-up filed in the
/// session notes): an entry resolves the id the emulator's per-title override
/// keys on, and a container without one stays id-less.
/// </summary>
public sealed class GameEntryTests : IDisposable
{
    private readonly string _root;

    public GameEntryTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "xgrid-gameentry-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp folder must not fail a test run.
        }
    }

    [Fact]
    public void FromFile_ResolvesTheCertificateTitleIdOfALooseExecutable()
    {
        string path = WriteXbe("my-game.xbe", "My Game", 0x4D530002u);

        GameEntry? entry = GameEntry.FromFile(path);

        Assert.NotNull(entry);
        Assert.Equal(0x4D530002u, entry.TitleId);
        Assert.Equal("Title 4D530002", entry.TitleIdText);
        Assert.True(entry.HasDistinctTitleId);
        Assert.Equal("My Game", entry.Title);
    }

    [Fact]
    public void FromFile_KeepsTheSharedPlaceholderOutOfTheRow()
    {
        // nxdk/xgu samples never set a certificate id, so every one of them
        // carries the same placeholder - the row must not claim it is distinct.
        string path = WriteXbe("sample.xbe", "sample", 0xFFFF0002u);

        GameEntry? entry = GameEntry.FromFile(path);

        Assert.NotNull(entry);
        Assert.Equal(0xFFFF0002u, entry.TitleId);
        Assert.False(entry.HasDistinctTitleId);
        Assert.Equal("Title FFFF0002", entry.TitleIdText);
    }

    [Fact]
    public void FromFile_ReportsNoIdForAForeignFile()
    {
        string path = Path.Combine(_root, "not-a-game.iso");
        File.WriteAllBytes(path, new byte[4096]);

        GameEntry? entry = GameEntry.FromFile(path);

        Assert.NotNull(entry);
        Assert.Equal(0u, entry.TitleId);
        Assert.Equal(string.Empty, entry.TitleIdText);
        Assert.False(entry.HasDistinctTitleId);
    }

    private string WriteXbe(string name, string title, uint titleId)
    {
        string path = Path.Combine(_root, name);
        File.WriteAllBytes(path, BuildXbe(title, titleId));
        return path;
    }

    /// <summary>
    /// Minimal but well-formed XBE (the shape XbeImage.Load validates: magic,
    /// headers size, a one-entry section table inside the image) with the
    /// certificate at 0x200 carrying the title id at +0x08 and a UCS-2 title
    /// name at +0x0C.
    /// </summary>
    private static byte[] BuildXbe(string title, uint titleId)
    {
        const uint Base = 0x00010000u;
        const uint Cert = 0x200u;
        byte[] image = new byte[0x1000];
        WriteAscii(image, 0x00, "XBEH");
        WriteU32(image, 0x104, Base);
        WriteU32(image, 0x108, 0x688u);
        WriteU32(image, 0x110, 0x178u);
        WriteU32(image, 0x118, Base + Cert);
        WriteU32(image, 0x11C, 1u);
        WriteU32(image, 0x120, Base + 0x300u);
        WriteU32(image, 0x128, 0x11000u);
        WriteU32(image, 0x12C, Base + 0x380u);

        WriteU32(image, (int)Cert + 0x08, titleId);
        byte[] name = System.Text.Encoding.Unicode.GetBytes(title);
        Array.Copy(name, 0, image, (int)Cert + 0x0C, Math.Min(name.Length, 0x40));

        WriteU32(image, 0x300, 0x4u);
        WriteU32(image, 0x304, 0x11000u);
        WriteU32(image, 0x308, 0x200u);
        WriteU32(image, 0x30C, 0x400u);
        WriteU32(image, 0x310, 0x200u);
        WriteU32(image, 0x314, Base + 0x360u);
        WriteAscii(image, 0x360, ".text");
        return image;
    }

    private static void WriteAscii(byte[] image, int offset, string text)
    {
        for (int i = 0; i < text.Length; i++)
        {
            image[offset + i] = (byte)text[i];
        }
    }

    private static void WriteU32(byte[] image, int offset, uint value)
    {
        BitConverter.GetBytes(value).CopyTo(image, offset);
    }
}
