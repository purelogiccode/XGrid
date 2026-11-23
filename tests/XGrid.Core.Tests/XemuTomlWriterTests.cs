using System;
using System.Collections.Generic;
using System.IO;
using XGrid.Core.Xemu;
using Xunit;

namespace XGrid.Core.Tests;

/// <summary>
/// Pins the generated core configuration. Every key and value type here was
/// verified against the xemu 0.8.139 binary, which rejects an integer for
/// <c>sys.mem_limit</c> (it is the string enum '64'/'128') and derives the
/// guest RAM as (index + 1) * 64 MB.
/// </summary>
public sealed class XemuTomlWriterTests
{
    [Fact]
    public void Build_WritesTheFirmwareAndMediaPathsAsLiteralStrings()
    {
        XemuLaunchSettings settings = CreateSettings();

        string toml = XemuTomlWriter.Build(settings);

        Assert.Contains("[sys.files]", toml, StringComparison.Ordinal);
        Assert.Contains("bootrom_path = 'C:\\firmware\\mcpx_1.0.bin'", toml, StringComparison.Ordinal);
        Assert.Contains("flashrom_path = 'C:\\firmware\\Complex_4627.bin'", toml, StringComparison.Ordinal);
        Assert.Contains("eeprom_path = 'C:\\firmware\\eeprom.bin'", toml, StringComparison.Ordinal);
        Assert.Contains("hdd_path = 'C:\\media\\xbox_hdd.qcow2'", toml, StringComparison.Ordinal);
        Assert.Contains("dvd_path = 'C:\\media\\game.iso'", toml, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(64, "64")]
    [InlineData(128, "128")]
    public void Build_UsesTheStringEnumForTheRamLimit(int ramMb, string expected)
    {
        XemuLaunchSettings settings = CreateSettings(ramMb);

        string toml = XemuTomlWriter.Build(settings);

        Assert.Contains($"mem_limit = '{expected}'", toml, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_DisablesTheWelcomeOverlayUpdateCheckAndMenuBar()
    {
        string toml = XemuTomlWriter.Build(CreateSettings());

        // The welcome overlay clears the core's autostart flag, and the shell
        // (never the core) owns configuration and messages.
        Assert.Contains("show_welcome = false", toml, StringComparison.Ordinal);
        Assert.Contains("[general.updates]", toml, StringComparison.Ordinal);
        Assert.Contains("check = false", toml, StringComparison.Ordinal);
        Assert.Contains("show_menubar = false", toml, StringComparison.Ordinal);
        Assert.Contains("show_notifications = false", toml, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_OmitsAnEmptyDiscInsteadOfWritingAnEmptyPath()
    {
        XemuLaunchSettings settings = CreateSettings() with { DvdPath = string.Empty };

        string toml = XemuTomlWriter.Build(settings);

        Assert.DoesNotContain("dvd_path", toml, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_WritesApostrophesAndNewlinesAsBasicStrings()
    {
        XemuLaunchSettings settings = CreateSettings() with
        {
            HddPath = "C:\\it's a \\ disk\"\nnew.qcow2",
        };

        string toml = XemuTomlWriter.Build(settings);

        // A literal string cannot carry an apostrophe, so the value switches to
        // the basic form and escapes the backslashes and the quote.
        Assert.Contains("hdd_path = \"C:\\\\it's a \\\\ disk\\\"\\nnew.qcow2\"", toml, StringComparison.Ordinal);
        Assert.DoesNotContain("hdd_path = '", toml, StringComparison.Ordinal);
    }

    [Fact]
    public void String_PrefersTheLiteralFormForOrdinaryWindowsPaths()
    {
        Assert.Equal("'C:\\firmware\\mcpx_1.0.bin'", XemuTomlWriter.String("C:\\firmware\\mcpx_1.0.bin"));
    }

    [Fact]
    public void Validate_ReportsEveryMissingInputAtOnce()
    {
        var settings = new XemuLaunchSettings();

        IReadOnlyList<string> errors = settings.Validate();

        Assert.Equal(6, errors.Count);
        Assert.Contains(errors, error => error.Contains("core executable", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("MCPX boot ROM", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("flash ROM", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("EEPROM", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("hard-disk image", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("working directory", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_AcceptsACompleteConfiguration()
    {
        using var files = new TemporaryFiles();

        Assert.Empty(files.Settings.Validate());
        files.Settings.EnsureValid();
    }

    [Fact]
    public void Validate_RejectsAnUnsupportedRamSizeAvPackAndWindow()
    {
        using var files = new TemporaryFiles();
        XemuLaunchSettings settings = files.Settings with
        {
            RamMb = 96,
            AvPack = "magnavox",
            WindowWidth = 32,
            WindowHeight = 32,
        };

        IReadOnlyList<string> errors = settings.Validate();

        Assert.Equal(3, errors.Count);
        Assert.Contains(errors, error => error.Contains("64 or 128", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("AV pack", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("64x64", StringComparison.Ordinal));
    }

    [Fact]
    public void EnsureValid_ThrowsOneExceptionCarryingEveryProblem()
    {
        var settings = new XemuLaunchSettings();

        XemuConfigurationException error = Assert.Throws<XemuConfigurationException>(settings.EnsureValid);

        Assert.Equal(settings.Validate().Count, error.Errors.Count);
    }

    private static XemuLaunchSettings CreateSettings(int ramMb = 64)
    {
        return new XemuLaunchSettings
        {
            CorePath = "C:\\core\\xemu.exe",
            BootRomPath = "C:\\firmware\\mcpx_1.0.bin",
            FlashRomPath = "C:\\firmware\\Complex_4627.bin",
            EepromPath = "C:\\firmware\\eeprom.bin",
            HddPath = "C:\\media\\xbox_hdd.qcow2",
            DvdPath = "C:\\media\\game.iso",
            RamMb = ramMb,
            WorkingDirectory = "C:\\shell\\xemu",
        };
    }

    /// <summary>Creates the files a valid configuration points at and removes them afterwards.</summary>
    private sealed class TemporaryFiles : IDisposable
    {
        private readonly string _directory;

        internal TemporaryFiles()
        {
            _directory = Path.Combine(Path.GetTempPath(), "xgrid-xemu-tests-" + Guid.NewGuid().ToString("N"));
            _ = Directory.CreateDirectory(_directory);
            Settings = new XemuLaunchSettings
            {
                CorePath = Touch("xemu.exe"),
                BootRomPath = Touch("mcpx_1.0.bin"),
                FlashRomPath = Touch("Complex_4627.bin"),
                EepromPath = Touch("eeprom.bin"),
                HddPath = Touch("xbox_hdd.qcow2"),
                DvdPath = Touch("game.iso"),
                WorkingDirectory = _directory,
            };
        }

        internal XemuLaunchSettings Settings { get; }

        public void Dispose()
        {
            Directory.Delete(_directory, recursive: true);
        }

        private string Touch(string name)
        {
            string path = Path.Combine(_directory, name);
            File.WriteAllBytes(path, []);
            return path;
        }
    }
}
