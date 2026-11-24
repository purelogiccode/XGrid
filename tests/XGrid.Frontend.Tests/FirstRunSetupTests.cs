using System;
using System.IO;
using XGrid.Frontend.Avalonia.Models;
using XGrid.Frontend.Avalonia.Services;
using Xunit;

namespace XGrid.Frontend.Tests;

/// <summary>
/// Pins the first-run wizard's settings mapping: entered paths must exist,
/// blanks keep the current configuration, and the game folder is added once.
/// </summary>
public sealed class FirstRunSetupTests : IDisposable
{
    private readonly string _root;

    public FirstRunSetupTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "xgrid-first-run-" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(_root);
    }

    [Fact]
    public void ApplySetsTheChosenPathsAndFolder()
    {
        string bios = Touch("bios.bin");
        string eeprom = Touch("eeprom.bin");
        string hdd = Touch("xbox_hdd.qcow2");
        string games = Path.Combine(_root, "games");
        var settings = new EmulatorSettings();

        FirstRunSetup.Apply(settings, bios, eeprom, hdd, games);

        Assert.Equal(bios, settings.BiosPath);
        Assert.Equal(eeprom, settings.EepromPath);
        Assert.Equal(hdd, settings.HddPath);
        Assert.Equal([games], settings.LibraryFolders);
    }

    [Fact]
    public void ApplyKeepsExistingValuesForBlankChoices()
    {
        var settings = new EmulatorSettings
        {
            BiosPath = "existing-bios",
            EepromPath = "existing-eeprom",
            HddPath = "existing-hdd",
            LibraryFolders = ["existing-games"],
        };

        FirstRunSetup.Apply(settings, null, "  ", string.Empty, "");

        Assert.Equal("existing-bios", settings.BiosPath);
        Assert.Equal("existing-eeprom", settings.EepromPath);
        Assert.Equal("existing-hdd", settings.HddPath);
        Assert.Equal(["existing-games"], settings.LibraryFolders);
    }

    [Fact]
    public void ApplyTrimsPathsAndDoesNotDuplicateTheFolder()
    {
        string games = Path.Combine(_root, "games");
        var settings = new EmulatorSettings { LibraryFolders = [games] };

        FirstRunSetup.Apply(settings, "  bios.bin  ", null, null, "  " + games.ToUpperInvariant() + " ");

        Assert.Equal("bios.bin", settings.BiosPath);
        _ = Assert.Single(settings.LibraryFolders);
    }

    [Fact]
    public void ValidateRequiresEveryEnteredPathToExist()
    {
        string existing = Touch("exists.bin");
        string missing = Path.Combine(_root, "absent.bin");

        Assert.Null(FirstRunSetup.Validate(null, null, null));
        Assert.Null(FirstRunSetup.Validate(existing, existing, existing));
        Assert.Equal("The BIOS image was not found.", FirstRunSetup.Validate(missing, null, null));
        Assert.Equal("The EEPROM image was not found.", FirstRunSetup.Validate(existing, missing, null));
        Assert.Equal("The hard-disk image was not found.", FirstRunSetup.Validate(existing, existing, missing));
    }

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
    }

    private string Touch(string name)
    {
        string path = Path.Combine(_root, name);
        File.WriteAllBytes(path, []);
        return path;
    }
}
