using System;
using System.IO;
using XGrid.Core.Xemu;
using XGrid.Frontend.Avalonia.Models;
using XGrid.Frontend.Avalonia.Services;
using Xunit;

namespace XGrid.Frontend.Tests;

/// <summary>
/// Pins the shell-to-core launch mapping (decision A): the shell's settings and
/// firmware layout become one core configuration, and a launch that cannot
/// work says why before a process is started.
/// </summary>
public sealed class XemuLaunchPlanTests : IDisposable
{
    private readonly string _root;

    public XemuLaunchPlanTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "xgrid-xemu-plan-" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(_root);
        _ = Directory.CreateDirectory(Path.Combine(_root, "firmware"));
        _ = Directory.CreateDirectory(Path.Combine(_root, "media"));
        _ = Directory.CreateDirectory(Path.Combine(_root, "work"));
        Touch(Path.Combine(_root, "firmware", "xemu.exe"));
        Touch(Path.Combine(_root, "firmware", "mcpx_1.0.bin"));
        Touch(Path.Combine(_root, "firmware", "Complex_4627.bin"));
        Touch(Path.Combine(_root, "firmware", "eeprom.bin"));
        Touch(Path.Combine(_root, "firmware", "xbox_hdd.qcow2"));
        Touch(Path.Combine(_root, "media", "game.iso"));
        Touch(Path.Combine(_root, "media", "other.iso"));
    }

    [Fact]
    public void Create_CopiesTheConfiguredFirmwareAndRam()
    {
        EmulatorSettings settings = Settings();

        XemuLaunchSettings launch = XemuLaunchPlan.Create(
            settings, null, 640, 480, Path.Combine(_root, "work"));

        Assert.Equal(Path.Combine(_root, "firmware", "xemu.exe"), launch.CorePath);
        Assert.Equal(Path.Combine(_root, "firmware", "Complex_4627.bin"), launch.FlashRomPath);
        Assert.Equal(Path.Combine(_root, "media", "game.iso"), launch.DvdPath);
        Assert.Equal(128, launch.RamMb);
        Assert.Equal(640, launch.WindowWidth);
        Assert.Equal(480, launch.WindowHeight);
        Assert.Empty(launch.Validate());
    }

    [Fact]
    public void Create_PrefersTheDiscPassedForThisLaunch()
    {
        XemuLaunchSettings launch = XemuLaunchPlan.Create(
            Settings(),
            Path.Combine(_root, "media", "other.iso"),
            1280,
            720,
            Path.Combine(_root, "work"));

        Assert.Equal(Path.Combine(_root, "media", "other.iso"), launch.DvdPath);
        Assert.Equal(1280, launch.WindowWidth);
    }

    [Fact]
    public void Create_FillsUnsetFirmwareFromTheCoreDirectory()
    {
        var settings = new EmulatorSettings
        {
            BiosPath = Path.Combine(_root, "firmware", "Complex_4627.bin"),
            XemuCorePath = Path.Combine(_root, "firmware", "xemu.exe"),
        };

        XemuLaunchSettings launch = XemuLaunchPlan.Create(
            settings, null, 640, 480, Path.Combine(_root, "work"));

        // The development layout keeps every image beside the core, so an
        // empty shell setting still produces a complete launch.
        Assert.Equal(Path.Combine(_root, "firmware", "mcpx_1.0.bin"), launch.BootRomPath);
        Assert.Equal(Path.Combine(_root, "firmware", "eeprom.bin"), launch.EepromPath);
        Assert.Equal(Path.Combine(_root, "firmware", "xbox_hdd.qcow2"), launch.HddPath);
        Assert.Empty(launch.Validate());
    }

    [Fact]
    public void DescribeProblem_ReportsTheFirstBlockingReason()
    {
        string work = Path.Combine(_root, "work");

        // A fresh configuration has no BIOS, which blocks before anything else.
        Assert.Contains(
            "BIOS",
            XemuLaunchPlan.DescribeProblem(new EmulatorSettings(), null, work),
            StringComparison.OrdinalIgnoreCase);

        Assert.Null(XemuLaunchPlan.DescribeProblem(Settings(), null, work));
    }

    [Fact]
    public void DescribeProblem_ReportsAMissingStorageImage()
    {
        EmulatorSettings settings = Settings(Path.Combine(_root, "media", "absent.qcow2"));

        string? problem = XemuLaunchPlan.DescribeProblem(
            settings, null, Path.Combine(_root, "work"));

        Assert.NotNull(problem);
        Assert.Contains("hard-disk", problem, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DescribeProblem_WantsADiscFileThatExists()
    {
        EmulatorSettings settings = Settings();
        string missing = Path.Combine(_root, "media", "absent.iso");

        string? problem = XemuLaunchPlan.DescribeProblem(
            settings, missing, Path.Combine(_root, "work"));

        Assert.NotNull(problem);
        Assert.Contains("does not exist", problem, StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
    }

    private EmulatorSettings Settings(string? hddPath = null)
    {
        return new EmulatorSettings
        {
            BiosPath = Path.Combine(_root, "firmware", "Complex_4627.bin"),
            McpxRomPath = Path.Combine(_root, "firmware", "mcpx_1.0.bin"),
            EepromPath = Path.Combine(_root, "firmware", "eeprom.bin"),
            HddPath = hddPath ?? Path.Combine(_root, "firmware", "xbox_hdd.qcow2"),
            DvdPath = Path.Combine(_root, "media", "game.iso"),
            RamMb = 128,
            XemuCorePath = Path.Combine(_root, "firmware", "xemu.exe"),
        };
    }

    private static void Touch(string path)
    {
        File.WriteAllBytes(path, []);
    }
}
