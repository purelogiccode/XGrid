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
    public void FromFile_CanSkipTheTitleIdUntilLaunch()
    {
        string path = WriteXbe("lazy_game.xbe", "Lazy Game", 0x4D530002u);

        GameEntry? entry = GameEntry.FromFile(path, resolveTitleId: false);

        Assert.NotNull(entry);
        Assert.Equal(0u, entry.TitleId);
        Assert.Equal("lazy game", entry.Title);

        Assert.Equal(0x4D530002u, entry.ResolveTitleId());
        Assert.Equal("Title 4D530002", entry.TitleIdText);
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

    [Theory]
    [InlineData(".iso", GameFormat.Iso, true)]
    [InlineData(".xiso", GameFormat.Iso, true)]
    [InlineData(".chd", GameFormat.Chd, true)]
    [InlineData(".zar", GameFormat.Zar, true)]
    [InlineData(".xbe", GameFormat.Xbe, false)]
    public void FromFile_MapsContainersAndLaunchability(
        string extension,
        GameFormat expectedFormat,
        bool launchable)
    {
        string path = Path.Combine(_root, "some_game" + extension);
        File.WriteAllBytes(
            path,
            string.Equals(extension, ".xbe", StringComparison.Ordinal)
                ? BuildXbe("Some Game", 0x4D530002u)
                : [0]);

        GameEntry? entry = GameEntry.FromFile(path);

        Assert.NotNull(entry);
        Assert.Equal(expectedFormat, entry.Format);
        Assert.Equal(launchable, entry.IsLaunchable);
        Assert.Equal(
            string.Equals(extension, ".xbe", StringComparison.Ordinal) ? "Some Game" : "some game",
            entry.Title);
        Assert.Equal(_root, entry.Folder);
    }

    [Fact]
    public void FromFile_PrettifiesUnderscoresAndDots()
    {
        string path = Path.Combine(_root, "halo_2.combat.evolved.iso");
        File.WriteAllBytes(path, [0]);

        GameEntry? entry = GameEntry.FromFile(path);

        Assert.NotNull(entry);
        Assert.Equal("halo 2 combat evolved", entry.Title);
    }

    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(1023, "1023 B")]
    [InlineData(1024, "1.0 KB")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(1048576, "1.0 MB")]
    public void SizeText_FormatsBinaryUnits(long length, string expected)
    {
        string path = Path.Combine(_root, "size.iso");
        File.WriteAllBytes(path, new byte[length]);

        GameEntry entry = Assert.IsType<GameEntry>(GameEntry.FromFile(path));

        Assert.Equal(expected, entry.SizeText);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void FromFile_RejectsBlankPaths(string path)
    {
        Assert.Null(GameEntry.FromFile(path));
    }

    [Fact]
    public void FromFile_ReturnsNullForAMissingFile()
    {
        Assert.Null(GameEntry.FromFile(Path.Combine(_root, "missing.iso")));
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
        const uint baseAddress = 0x00010000u;
        const uint certOffset = 0x200u;
        byte[] image = new byte[0x1000];
        WriteAscii(image, 0x00, "XBEH");
        WriteU32(image, 0x104, baseAddress);
        WriteU32(image, 0x108, 0x688u);
        WriteU32(image, 0x110, 0x178u);
        WriteU32(image, 0x118, baseAddress + certOffset);
        WriteU32(image, 0x11C, 1u);
        WriteU32(image, 0x120, baseAddress + 0x300u);
        WriteU32(image, 0x128, 0x11000u);
        WriteU32(image, 0x12C, baseAddress + 0x380u);

        WriteU32(image, (int)certOffset + 0x08, titleId);
        byte[] name = System.Text.Encoding.Unicode.GetBytes(title);
        Array.Copy(name, 0, image, (int)certOffset + 0x0C, Math.Min(name.Length, 0x40));

        WriteU32(image, 0x300, 0x4u);
        WriteU32(image, 0x304, 0x11000u);
        WriteU32(image, 0x308, 0x200u);
        WriteU32(image, 0x30C, 0x400u);
        WriteU32(image, 0x310, 0x200u);
        WriteU32(image, 0x314, baseAddress + 0x360u);
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
