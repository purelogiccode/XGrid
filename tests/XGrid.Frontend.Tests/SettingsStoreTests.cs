using System;
using System.IO;
using XGrid.Core.Xemu;
using XGrid.Frontend.Avalonia.Models;
using XGrid.Frontend.Avalonia.Services;
using Xunit;

namespace XGrid.Frontend.Tests;

/// <summary>Pins settings persistence, repair and corrupt-file recovery.</summary>
public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "xgrid-settings-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Load_WhenTheFileIsMissing_ReturnsDefaultsAndMarksFirstRun()
    {
        var store = new SettingsStore(_directory);

        EmulatorSettings settings = store.Load();

        Assert.False(store.ExistedAtLoad);
        Assert.Equal(64, settings.RamMb);
        Assert.Empty(settings.DonationEmail);
        Assert.False(settings.HasDonated);
    }

    [Fact]
    public void Save_LeavesNoTemporaryFileBehind()
    {
        var store = new SettingsStore(_directory);

        store.Save(new EmulatorSettings());

        Assert.True(File.Exists(store.FilePath));
        Assert.False(File.Exists(store.FilePath + ".tmp"));
    }

    [Fact]
    public void SaveThenLoad_RoundTripsPathsAndDonationState()
    {
        var store = new SettingsStore(_directory);
        var settings = new EmulatorSettings
        {
            BiosPath = Path.Combine(_directory, "bios.bin"),
            HddPath = Path.Combine(_directory, "hdd.qcow2"),
            LibraryFolders = { Path.Combine(_directory, "games") },
            DonationEmail = " donor@example.com ",
            HasDonated = true,
            DonationLastCheckedUtc = new DateTime(2026, 11, 24, 12, 0, 0, DateTimeKind.Utc),
            PowerOnAtStartup = true,
            StayOpenAfterPowerOff = false,
            SaveStateOnExit = true,
            ResumeStateOnStart = true,
            ShowPoweredOffScreen = false,
            Audio = new AudioSettings
            {
                Backend = AudioBackends.Wav,
                Device = "test-device",
            },
        };

        store.Save(settings);
        EmulatorSettings loaded = store.Load();

        Assert.True(store.ExistedAtLoad);
        Assert.Equal(settings.BiosPath, loaded.BiosPath);
        Assert.Equal(settings.HddPath, loaded.HddPath);
        Assert.Equal(settings.LibraryFolders, loaded.LibraryFolders);
        Assert.Equal("donor@example.com", loaded.DonationEmail);
        Assert.True(loaded.HasDonated);
        Assert.Equal(settings.DonationLastCheckedUtc, loaded.DonationLastCheckedUtc);
        Assert.True(loaded.PowerOnAtStartup);
        Assert.False(loaded.StayOpenAfterPowerOff);
        Assert.True(loaded.SaveStateOnExit);
        Assert.True(loaded.ResumeStateOnStart);
        Assert.False(loaded.ShowPoweredOffScreen);
        Assert.Equal(AudioBackends.Wav, loaded.Audio.Backend);
        Assert.Equal("test-device", loaded.Audio.Device);
    }

    [Fact]
    public void Save_ClampsInvalidValuesBeforeWriting()
    {
        var store = new SettingsStore(_directory);
        var settings = new EmulatorSettings
        {
            RamMb = 512,
            Filter = "vhs",
            CoreHosting = "magic",
        };

        store.Save(settings);
        EmulatorSettings loaded = store.Load();

        Assert.Equal(64, loaded.RamMb);
        Assert.Equal("nearest", loaded.Filter);
        Assert.Equal(EmulatorSettings.CoreHostingInProcess, loaded.CoreHosting);
    }

    [Fact]
    public void Load_WhenJsonIsInvalid_BacksUpTheFileAndReturnsDefaults()
    {
        Directory.CreateDirectory(_directory);
        string path = Path.Combine(_directory, "settings.json");
        File.WriteAllText(path, "{ this is not json");

        var store = new SettingsStore(_directory);
        EmulatorSettings settings = store.Load();

        Assert.True(store.ExistedAtLoad);
        Assert.Equal(64, settings.RamMb);
        Assert.False(File.Exists(path));
        Assert.True(File.Exists(path + ".bak"));
    }

    [Fact]
    public void Load_WhenJsonIsNull_ReturnsDefaults()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "settings.json"), "null");

        var store = new SettingsStore(_directory);
        EmulatorSettings settings = store.Load();

        Assert.Equal(64, settings.RamMb);
        Assert.False(settings.HasDonated);
    }

    [Fact]
    public void Load_NormalizesNullSectionsAndBlankPaths()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(
            Path.Combine(_directory, "settings.json"),
            """
            {
              "Input": null,
              "Graphics": null,
              "Audio": null,
              "LibraryFolders": [ "", "  ", "games" ],
              "DonationEmail": "   "
            }
            """);

        var store = new SettingsStore(_directory);
        EmulatorSettings settings = store.Load();

        Assert.NotNull(settings.Input);
        Assert.NotNull(settings.Graphics);
        Assert.NotNull(settings.Audio);
        Assert.Equal(["games"], settings.LibraryFolders);
        Assert.Empty(settings.DonationEmail);
    }

    [Fact]
    public void Load_WhenCollectionsAreNull_RepairsThem()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(
            Path.Combine(_directory, "settings.json"),
            """
            {
              "LibraryFolders": null,
              "TitleOverrides": null
            }
            """);

        var store = new SettingsStore(_directory);
        EmulatorSettings settings = store.Load();

        Assert.NotNull(settings.LibraryFolders);
        Assert.Empty(settings.LibraryFolders);
        Assert.NotNull(settings.TitleOverrides);
        Assert.Empty(settings.TitleOverrides);
    }

    [Fact]
    public void Constructor_RejectsABlankDirectory()
    {
        Assert.Throws<ArgumentException>(() => new SettingsStore("   "));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
