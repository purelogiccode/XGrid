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
        Assert.Contains("hdd_format = 'qcow2'", toml, StringComparison.Ordinal);
        Assert.Contains("dvd_path = 'C:\\media\\game.iso'", toml, StringComparison.Ordinal);
        Assert.Contains(
            "snapshot_store_path = 'C:\\shell\\xemu\\xbox_snapshots.qcow2'",
            toml,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Build_WritesTheChihiroMachineAndFirmware()
    {
        XemuLaunchSettings settings = CreateSettings(128) with
        {
            Machine = XemuLaunchSettings.MachineChihiro,
            ChihiroBiosPath = "C:\\firmware\\chihiro_xbox_bios.bin",
            ChihiroMediaboardPath = "C:\\firmware\\fpr21042_m29w160et.bin",
            ChihiroIc10Path = "C:\\firmware\\ic10_g24lc64.bin",
            ChihiroIc11Path = "C:\\firmware\\ic11_24lc024.bin",
            ChihiroPc20Path = "C:\\firmware\\pc20_g24lc64.bin",
            ChihiroNetFirmwarePath = "C:\\firmware\\ver1305.bin",
            ChihiroSnapshotStorePath = "C:\\data\\xemu\\chihiro_snapshots.qcow2",
            SnapshotStorePath = string.Empty,
            HddFormat = "auto",
            ChihiroFreePlay = true,
            ChihiroRegion = "jp",
            ChihiroDimmSize = "512",
            ChihiroBoardType = "type3",
            ChihiroForceFeedback = true,
            ChihiroFfbStrength = 150,
            ChihiroFfbInvert = true,
            ChihiroWheelWeight = 70,
            ChihiroWheelAutocenter = false,
            ChihiroWheelAutocenterStrength = 80,
            ChihiroPointerDevices = true,
            ChihiroPointerP1 = "XGrid Test Light Gun 1",
            ChihiroPointerP2 = "XGrid Test Light Gun 2",
        };

        string toml = XemuTomlWriter.Build(settings);

        Assert.Contains("default_machine = 'chihiro'", toml, StringComparison.Ordinal);
        Assert.Contains("[chihiro.roms]", toml, StringComparison.Ordinal);
        Assert.Contains("bios_path = 'C:\\firmware\\chihiro_xbox_bios.bin'", toml, StringComparison.Ordinal);
        Assert.Contains("mediaboard_path = 'C:\\firmware\\fpr21042_m29w160et.bin'", toml, StringComparison.Ordinal);
        Assert.Contains("ic10_path = 'C:\\firmware\\ic10_g24lc64.bin'", toml, StringComparison.Ordinal);
        Assert.Contains("ic11_path = 'C:\\firmware\\ic11_24lc024.bin'", toml, StringComparison.Ordinal);
        Assert.Contains("pc20_path = 'C:\\firmware\\pc20_g24lc64.bin'", toml, StringComparison.Ordinal);
        Assert.Contains("net_firmware_path = 'C:\\firmware\\ver1305.bin'", toml, StringComparison.Ordinal);
        Assert.Contains(
            "snapshot_store_path = 'C:\\data\\xemu\\chihiro_snapshots.qcow2'",
            toml,
            StringComparison.Ordinal);
        Assert.Contains("[chihiro.settings]", toml, StringComparison.Ordinal);
        Assert.Contains("freeplay = true", toml, StringComparison.Ordinal);
        Assert.Contains("region = 'jp'", toml, StringComparison.Ordinal);
        Assert.Contains("dimm_size = '512'", toml, StringComparison.Ordinal);
        Assert.Contains("board_type = 'type3'", toml, StringComparison.Ordinal);
        Assert.Contains("force_feedback = true", toml, StringComparison.Ordinal);
        Assert.Contains("ffb_strength = 150", toml, StringComparison.Ordinal);
        Assert.Contains("ffb_invert = true", toml, StringComparison.Ordinal);
        Assert.Contains("wheel_weight = 70", toml, StringComparison.Ordinal);
        Assert.Contains("wheel_autocenter = false", toml, StringComparison.Ordinal);
        Assert.Contains("wheel_autocenter_strength = 80", toml, StringComparison.Ordinal);
        Assert.Contains("pointer_devices = true", toml, StringComparison.Ordinal);
        Assert.Contains("pointer_p1 = 'XGrid Test Light Gun 1'", toml, StringComparison.Ordinal);
        Assert.Contains("pointer_p2 = 'XGrid Test Light Gun 2'", toml, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_OmitsTheChihiroNetworkFirmwareWhenUnset()
    {
        XemuLaunchSettings settings = CreateSettings(128) with
        {
            Machine = XemuLaunchSettings.MachineChihiro,
        };

        string toml = XemuTomlWriter.Build(settings);

        Assert.DoesNotContain("net_firmware_path", toml, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_WritesTheChihiroJvsBindings()
    {
        XemuLaunchSettings settings = CreateSettings(128) with
        {
            Machine = XemuLaunchSettings.MachineChihiro,
            ChihiroJvs = new ChihiroJvsSettings
            {
                Profile = "gs",
                Coin = 35,
                Hotd3Trigger = 1003,
                GsChange = 15,
                GundamLUp = 20,
                GundamRTrigger = 230,
                GundamPedal = 46,
            },
        };

        string toml = XemuTomlWriter.Build(settings);

        Assert.Contains("[chihiro.jvs]", toml, StringComparison.Ordinal);
        Assert.Contains("profile = 'gs'", toml, StringComparison.Ordinal);
        Assert.Contains("coin = 35", toml, StringComparison.Ordinal);
        Assert.Contains("[chihiro.jvs.hotd3]", toml, StringComparison.Ordinal);
        Assert.Contains("trigger = 1003", toml, StringComparison.Ordinal);
        Assert.Contains("[chihiro.jvs.gs]", toml, StringComparison.Ordinal);
        Assert.Contains("change = 15", toml, StringComparison.Ordinal);
        Assert.Contains("[chihiro.jvs.gundam]", toml, StringComparison.Ordinal);
        Assert.Contains("l_up = 20", toml, StringComparison.Ordinal);
        Assert.Contains("r_trigger = 230", toml, StringComparison.Ordinal);
        Assert.Contains("pedal = 46", toml, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_WritesTheChihiroPlayer2Bindings()
    {
        XemuLaunchSettings settings = CreateSettings(128) with
        {
            Machine = XemuLaunchSettings.MachineChihiro,
            ChihiroJvsP2 = new ChihiroJvsP2Settings
            {
                Start = 89,
                Coin = 91,
                CardIn = 20,
                AimSpeed = 125,
                GsTrigger = 1003,
                GsReload = 44,
            },
        };

        string toml = XemuTomlWriter.Build(settings);

        Assert.Contains("[chihiro.jvs_p2]", toml, StringComparison.Ordinal);
        Assert.Contains("start = 89", toml, StringComparison.Ordinal);
        Assert.Contains("coin = 91", toml, StringComparison.Ordinal);
        Assert.Contains("card_in = 20", toml, StringComparison.Ordinal);
        Assert.Contains("aim_speed = 125", toml, StringComparison.Ordinal);
        Assert.Contains("gs_trigger = 1003", toml, StringComparison.Ordinal);
        Assert.Contains("gs_reload = 44", toml, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_WritesTheChihiroCardReaders()
    {
        XemuLaunchSettings settings = CreateSettings(128) with
        {
            Machine = XemuLaunchSettings.MachineChihiro,
            ChihiroCardReader = new ChihiroCardReaderSettings
            {
                Enabled = false,
                Hw210Slot1 = "C:\\cards\\p1.bin",
                Hw210Slot2 = "C:\\cards\\p2.bin",
                Hw210Gundam = "C:\\cards\\gundam.bin",
                Crp1231Mt1 = "C:\\cards\\mt1.bin",
                Crp1231Mt2 = "C:\\cards\\mt2.bin",
            },
        };

        string toml = XemuTomlWriter.Build(settings);

        Assert.Contains("[chihiro.card_reader]", toml, StringComparison.Ordinal);
        Assert.Contains("enable = false", toml, StringComparison.Ordinal);
        Assert.Contains("[chihiro.card_reader.hw210]", toml, StringComparison.Ordinal);
        Assert.Contains("slot1 = 'C:\\cards\\p1.bin'", toml, StringComparison.Ordinal);
        Assert.Contains("slot2 = 'C:\\cards\\p2.bin'", toml, StringComparison.Ordinal);
        Assert.Contains("gundam = 'C:\\cards\\gundam.bin'", toml, StringComparison.Ordinal);
        Assert.Contains("[chihiro.card_reader.crp1231]", toml, StringComparison.Ordinal);
        Assert.Contains("mt1 = 'C:\\cards\\mt1.bin'", toml, StringComparison.Ordinal);
        Assert.Contains("mt2 = 'C:\\cards\\mt2.bin'", toml, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_WritesTheChihiroCabinetLink()
    {
        XemuLaunchSettings settings = CreateSettings(128) with
        {
            Machine = XemuLaunchSettings.MachineChihiro,
            ChihiroLinkEnabled = true,
            ChihiroLinkCabinets = 3,
            ChihiroLinkCabinet = 2,
            ChihiroLinkHost = "10.0.0.1",
            ChihiroLinkPort = 9200,
        };

        string toml = XemuTomlWriter.Build(settings);

        Assert.Contains("[chihiro.link]", toml, StringComparison.Ordinal);
        Assert.Contains("enable = true", toml, StringComparison.Ordinal);
        Assert.Contains("cabinets = 3", toml, StringComparison.Ordinal);
        Assert.Contains("cabinet = 2", toml, StringComparison.Ordinal);
        Assert.Contains("host = '10.0.0.1'", toml, StringComparison.Ordinal);
        Assert.Contains("port = 9200", toml, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_OmitsTheChihiroCabinetLinkWhenDisabled()
    {
        XemuLaunchSettings settings = CreateSettings(128) with
        {
            Machine = XemuLaunchSettings.MachineChihiro,
            ChihiroLinkEnabled = false,
        };

        string toml = XemuTomlWriter.Build(settings);

        Assert.DoesNotContain("[chihiro.link]", toml, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_WritesTheScreenshotDirectoryWhenSet()
    {
        XemuLaunchSettings settings = CreateSettings() with
        {
            ScreenshotDirectory = "C:\\data\\screenshots",
        };

        string toml = XemuTomlWriter.Build(settings);

        Assert.Contains("screenshot_dir = 'C:\\data\\screenshots'", toml, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_OmitsTheScreenshotDirectoryWhenUnset()
    {
        string toml = XemuTomlWriter.Build(CreateSettings());

        Assert.DoesNotContain("screenshot_dir", toml, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_WritesTheShaderCacheDirectoryWhenSet()
    {
        XemuLaunchSettings settings = CreateSettings() with
        {
            ShaderCacheDirectory = "C:\\data\\xemu\\d3d11-shader-cache",
        };

        string toml = XemuTomlWriter.Build(settings);

        Assert.Contains(
            "shader_cache_dir = 'C:\\data\\xemu\\d3d11-shader-cache'",
            toml,
            StringComparison.Ordinal);
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
    public void Build_WritesGraphicsAndAudioSettings()
    {
        XemuLaunchSettings settings = CreateSettings() with
        {
            Renderer = "VULKAN",
            SurfaceScale = 3,
            Filtering = "nearest",
            StartupSize = "1920x1080",
            VSync = true,
            FullscreenOnStartup = true,
            FullscreenExclusive = true,
            AspectRatio = "16x9",
            Fit = "stretch",
            AvPack = "composite",
            AudioDsp = true,
            AudioDspJit = true,
            Hrtf = false,
            VolumeLimit = 0.75,
            VoiceWorkers = 4,
        };

        string toml = XemuTomlWriter.Build(settings);

        Assert.Contains("renderer = 'VULKAN'", toml, StringComparison.Ordinal);
        Assert.Contains("surface_scale = 3", toml, StringComparison.Ordinal);
        Assert.Contains("filtering = 'nearest'", toml, StringComparison.Ordinal);
        Assert.Contains("startup_size = '1920x1080'", toml, StringComparison.Ordinal);
        Assert.Contains("fullscreen_on_startup = true", toml, StringComparison.Ordinal);
        Assert.Contains("fullscreen_exclusive = true", toml, StringComparison.Ordinal);
        Assert.Contains("aspect_ratio = '16x9'", toml, StringComparison.Ordinal);
        Assert.Contains("fit = 'stretch'", toml, StringComparison.Ordinal);
        Assert.Contains("avpack = 'composite'", toml, StringComparison.Ordinal);
        Assert.Contains("use_dsp_jit = true", toml, StringComparison.Ordinal);
        Assert.Contains("hrtf = false", toml, StringComparison.Ordinal);
        Assert.Contains("volume_limit = 0.75", toml, StringComparison.Ordinal);
        Assert.Contains("num_workers = 4", toml, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_RejectsInvalidGraphicsAndAudioValues()
    {
        using var files = new TemporaryFiles();
        XemuLaunchSettings settings = files.Settings with
        {
            Renderer = "DIRECTX",
            SurfaceScale = 12,
            Filtering = "anisotropic",
            StartupSize = "800x600",
            AspectRatio = "21x9",
            Fit = "zoom",
            VolumeLimit = 2.0,
            VoiceWorkers = 99,
            AudioBackend = "asio",
        };

        IReadOnlyList<string> errors = settings.Validate();

        Assert.Contains(errors, error => error.Contains("renderer", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(errors, error => error.Contains("Surface scale", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(errors, error => error.Contains("filtering", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(errors, error => error.Contains("startup size", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(errors, error => error.Contains("aspect ratio", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(errors, error => error.Contains("fit mode", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(errors, error => error.Contains("volume", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(errors, error => error.Contains("worker", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(errors, error => error.Contains("audio backend", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_RejectsAnAudioDeviceWithAComma()
    {
        using var files = new TemporaryFiles();
        XemuLaunchSettings settings = files.Settings with
        {
            AudioBackend = "pa",
            AudioDevice = "sink,other",
        };

        IReadOnlyList<string> errors = settings.Validate();

        Assert.Contains(errors, error => error.Contains("audio device", StringComparison.OrdinalIgnoreCase));
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
    public void Build_BindsTheKeyboardControllerToPortOneWhenTheShellSuppliesThePad()
    {
        XemuLaunchSettings settings = CreateSettings() with { InputSinkName = "xgrid-input-test" };

        string toml = XemuTomlWriter.Build(settings);

        // The core binds a controller only when the port's device id matches, so
        // the binding is `port1`; `port1_driver` is the USB device type and keeps
        // its Duke default. Writing the driver instead left port 1 unbound, and
        // the shell's pad was never read.
        Assert.Contains("[input.bindings]", toml, StringComparison.Ordinal);
        Assert.Contains("port1 = 'keyboard'", toml, StringComparison.Ordinal);
        Assert.DoesNotContain("port1_driver", toml, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_OmitsTheInputBindingWithoutAnInputSink()
    {
        string toml = XemuTomlWriter.Build(CreateSettings());

        Assert.DoesNotContain("[input.bindings]", toml, StringComparison.Ordinal);
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
            HddFormat = "qcow2",
            SnapshotStorePath = "C:\\shell\\xemu\\xbox_snapshots.qcow2",
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
