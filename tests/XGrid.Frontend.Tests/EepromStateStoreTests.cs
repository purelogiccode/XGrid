using System;
using System.IO;
using XGrid.Frontend.Avalonia.Services;
using Xunit;

namespace XGrid.Frontend.Tests;

/// <summary>
/// Pins the persisted EEPROM image store: the frontend keeps the console's
/// 256-byte user settings here so the guest's writes survive across runs, while
/// the configured firmware image stays a read-only seed.
/// </summary>
public sealed class EepromStateStoreTests : IDisposable
{
    private readonly string _folder =
        Path.Combine(Path.GetTempPath(), "xgrid-eeprom-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public void MissingImageLoadsAsFalseAndCreatesNothing()
    {
        var store = new EepromStateStore(_folder);

        Assert.False(store.Exists);
        Assert.False(store.TryLoad(out byte[] image));
        Assert.Empty(image);
        Assert.False(Directory.Exists(_folder));
    }

    [Fact]
    public void SaveThenLoadRoundTripsTheWholeImage()
    {
        byte[] image = new byte[EepromStateStore.ImageSize];
        for (int i = 0; i < image.Length; i++)
        {
            image[i] = (byte)(i * 7);
        }

        var store = new EepromStateStore(_folder);

        Assert.True(store.TrySave(image));
        Assert.True(store.Exists);
        Assert.Equal(Path.Combine(_folder, "eeprom.bin"), store.FilePath);

        Assert.True(store.TryLoad(out byte[] loaded));
        Assert.Equal(image, loaded);
    }

    [Fact]
    public void TruncatedImageIsRejected()
    {
        var store = new EepromStateStore(_folder);
        Directory.CreateDirectory(_folder);
        File.WriteAllBytes(store.FilePath, new byte[100]);

        Assert.False(store.TryLoad(out byte[] image));
        Assert.Empty(image);
    }

    [Fact]
    public void LongerFileIsClampedToTheDeviceSize()
    {
        var store = new EepromStateStore(_folder);
        byte[] raw = new byte[EepromStateStore.ImageSize + 64];
        raw[0] = 0x5A;
        raw[EepromStateStore.ImageSize + 10] = 0xFF;
        Directory.CreateDirectory(_folder);
        File.WriteAllBytes(store.FilePath, raw);

        Assert.True(store.TryLoad(out byte[] image));
        Assert.Equal(EepromStateStore.ImageSize, image.Length);
        Assert.Equal(0x5A, image[0]);
    }

    [Fact]
    public void ResetDeletesThePersistedImage()
    {
        var store = new EepromStateStore(_folder);
        Assert.True(store.TrySave(new byte[EepromStateStore.ImageSize]));
        Assert.True(store.Exists);

        Assert.True(store.TryReset());
        Assert.False(store.Exists);
        Assert.False(store.TryLoad(out _));

        // Resetting twice is not an error.
        Assert.True(store.TryReset());
    }

    [Fact]
    public void ShortBufferCannotBeSaved()
    {
        var store = new EepromStateStore(_folder);

        Assert.Throws<ArgumentException>(() => store.TrySave(new byte[16]));
    }
}
