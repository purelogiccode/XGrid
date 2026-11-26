using System;
using System.Buffers.Binary;
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
        Touch(Path.Combine(_root, "firmware", ChihiroSettings.BiosFileName));
        Touch(Path.Combine(_root, "firmware", ChihiroSettings.MediaboardFileName));
        Touch(Path.Combine(_root, "firmware", ChihiroSettings.Ic10FileName));
        Touch(Path.Combine(_root, "firmware", ChihiroSettings.Ic11FileName));
        Touch(Path.Combine(_root, "firmware", ChihiroSettings.Pc20FileName));
        Touch(Path.Combine(_root, "firmware", ChihiroSettings.NetFirmwareFileName));
        Touch(Path.Combine(_root, "media", "game.iso"));
        Touch(Path.Combine(_root, "media", "other.iso"));
        Touch(Path.Combine(_root, "media", "game.bin"));
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
        Assert.Equal(Path.Combine(_root, "screenshots"), launch.ScreenshotDirectory);
        Assert.Equal(Path.Combine(_root, "work", "d3d11-shader-cache"), launch.ShaderCacheDirectory);
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
    public void Create_UsesTheChihiroForANetbootImage()
    {
        EmulatorSettings settings = Settings();
        string image = Path.Combine(_root, "media", "game.bin");

        XemuLaunchSettings launch = XemuLaunchPlan.Create(
            settings, image, 640, 480, Path.Combine(_root, "work"));

        Assert.Equal(XemuLaunchSettings.MachineChihiro, launch.Machine);
        Assert.True(launch.IsChihiro);
        Assert.Equal(128, launch.RamMb);
        Assert.Equal(string.Empty, launch.HddPath);
        Assert.Equal(string.Empty, launch.InputSinkName);
        Assert.Equal(image, launch.DvdPath);
        Assert.Equal(Path.Combine(_root, "firmware", ChihiroSettings.BiosFileName), launch.ChihiroBiosPath);
        Assert.Equal(
            Path.Combine(_root, "firmware", ChihiroSettings.MediaboardFileName),
            launch.ChihiroMediaboardPath);
        Assert.Equal(
            Path.Combine(_root, "firmware", ChihiroSettings.NetFirmwareFileName),
            launch.ChihiroNetFirmwarePath);
        Assert.Empty(launch.Validate());
    }

    [Fact]
    public void Create_LeavesTheNetworkFirmwareEmptyWhenItIsNotPresent()
    {
        EmulatorSettings settings = Settings();
        string image = Path.Combine(_root, "media", "game.bin");
        File.Delete(Path.Combine(_root, "firmware", ChihiroSettings.NetFirmwareFileName));

        XemuLaunchSettings launch = XemuLaunchPlan.Create(
            settings, image, 640, 480, Path.Combine(_root, "work"));

        Assert.True(launch.IsChihiro);
        Assert.Equal(string.Empty, launch.ChihiroNetFirmwarePath);
        Assert.Empty(launch.Validate());
    }

    [Fact]
    public void DescribeProblem_ReportsMissingChihiroFirmware()
    {
        string directory = Path.Combine(_root, "empty-chihiro");
        _ = Directory.CreateDirectory(directory);
        EmulatorSettings settings = Settings();
        settings.Chihiro.Enabled = true;
        settings.Chihiro.FirmwareDirectory = directory;

        string? problem = XemuLaunchPlan.DescribeProblem(
            settings, Path.Combine(_root, "media", "game.bin"), Path.Combine(_root, "work"));

        Assert.NotNull(problem);
        Assert.Contains("Chihiro BIOS", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_CopiesGraphicsAndAudioSettings()
    {
        EmulatorSettings settings = Settings();
        settings.Graphics.Renderer = GraphicsSettings.RendererVulkan;
        settings.Graphics.SurfaceScale = 3;
        settings.Graphics.Filtering = GraphicsSettings.FilterNearest;
        settings.Graphics.StartupSize = "1920x1080";
        settings.Graphics.VSync = true;
        settings.Graphics.FullscreenOnStartup = true;
        settings.Graphics.AspectRatio = "16x9";
        settings.Graphics.Fit = "stretch";
        settings.Graphics.AvPack = "composite";
        settings.Audio.UseDsp = true;
        settings.Audio.UseDspJit = true;
        settings.Audio.Hrtf = false;
        settings.Audio.VolumeLimit = 0.75;
        settings.Audio.VoiceWorkers = 4;
        settings.Audio.Backend = AudioSettings.BackendWav;

        XemuLaunchSettings launch = XemuLaunchPlan.Create(
            settings, null, 640, 480, Path.Combine(_root, "work"));

        Assert.Equal("VULKAN", launch.Renderer);
        Assert.Equal(3, launch.SurfaceScale);
        Assert.Equal("nearest", launch.Filtering);
        Assert.Equal("1920x1080", launch.StartupSize);
        Assert.True(launch.VSync);
        Assert.True(launch.FullscreenOnStartup);
        Assert.Equal("16x9", launch.AspectRatio);
        Assert.Equal("stretch", launch.Fit);
        Assert.Equal("composite", launch.AvPack);
        Assert.True(launch.AudioDspJit);
        Assert.False(launch.Hrtf);
        Assert.Equal(0.75, launch.VolumeLimit);
        Assert.Equal(4, launch.VoiceWorkers);
        Assert.Equal(Path.Combine(_root, "work", "audio.wav"), launch.AudioWavPath);
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
    public void Create_CopiesTheCoreHostingMode()
    {
        EmulatorSettings settings = Settings();
        settings.CoreHosting = EmulatorSettings.CoreHostingProcess;

        XemuLaunchSettings launch = XemuLaunchPlan.Create(
            settings, null, 640, 480, Path.Combine(_root, "work"));

        Assert.Equal(EmulatorSettings.CoreHostingProcess, launch.HostMode);
    }

    [Fact]
    public void Create_StartsOpenGlWhenTheCoreWasBuiltWithoutVulkan()
    {
        // The build writes its renderer list beside the core; the shell must not
        // hand a Vulkan renderer to a binary that cannot start one.
        File.WriteAllText(
            Path.Combine(_root, "firmware", CoreCapabilities.FileName),
            """{"version":"0.8.139","renderers":["OPENGL","NULL"]}""");
        EmulatorSettings settings = Settings();
        settings.Graphics.Renderer = GraphicsSettings.RendererVulkan;

        XemuLaunchSettings launch = XemuLaunchPlan.Create(
            settings, null, 640, 480, Path.Combine(_root, "work"));

        Assert.Equal(GraphicsSettings.RendererOpenGl, launch.Renderer);
    }

    [Fact]
    public void Create_KeepsVulkanWhenTheCoreReportsIt()
    {
        File.WriteAllText(
            Path.Combine(_root, "firmware", CoreCapabilities.FileName),
            """{"version":"0.8.139","renderers":["OPENGL","VULKAN","NULL"]}""");
        EmulatorSettings settings = Settings();
        settings.Graphics.Renderer = GraphicsSettings.RendererVulkan;

        XemuLaunchSettings launch = XemuLaunchPlan.Create(
            settings, null, 640, 480, Path.Combine(_root, "work"));

        Assert.Equal(GraphicsSettings.RendererVulkan, launch.Renderer);
    }

    [Fact]
    public void Create_StartsOpenGlWhenTheCoreHasNoDirect3D11()
    {
        File.WriteAllText(
            Path.Combine(_root, "firmware", CoreCapabilities.FileName),
            """{"version":"0.8.139","renderers":["OPENGL","NULL"]}""");
        EmulatorSettings settings = Settings();
        settings.Graphics.Renderer = GraphicsSettings.RendererDirect3D11;

        XemuLaunchSettings launch = XemuLaunchPlan.Create(
            settings, null, 640, 480, Path.Combine(_root, "work"));

        Assert.Equal(GraphicsSettings.RendererOpenGl, launch.Renderer);
    }

    [Fact]
    public void Create_KeepsDirect3D11WhenTheCoreReportsIt()
    {
        File.WriteAllText(
            Path.Combine(_root, "firmware", CoreCapabilities.FileName),
            """{"version":"0.8.139","renderers":["D3D11","OPENGL","NULL"]}""");
        EmulatorSettings settings = Settings();
        settings.Graphics.Renderer = GraphicsSettings.RendererDirect3D11;

        XemuLaunchSettings launch = XemuLaunchPlan.Create(
            settings, null, 640, 480, Path.Combine(_root, "work"));

        Assert.Equal(GraphicsSettings.RendererDirect3D11, launch.Renderer);
    }

    [Fact]
    public void ResolveRenderer_LeavesTheOtherRenderersAlone()
    {
        File.WriteAllText(
            Path.Combine(_root, "firmware", CoreCapabilities.FileName),
            """{"renderers":["OPENGL","NULL"]}""");
        CoreCapabilities capabilities = CoreCapabilities.ReadFromDirectory(Path.Combine(_root, "firmware"))!;

        Assert.Equal(
            GraphicsSettings.RendererNull,
            XemuLaunchPlan.ResolveRenderer(GraphicsSettings.RendererNull, capabilities));
        Assert.Equal(
            GraphicsSettings.RendererOpenGl,
            XemuLaunchPlan.ResolveRenderer(GraphicsSettings.RendererOpenGl, capabilities));
        Assert.Equal(
            GraphicsSettings.RendererVulkan,
            XemuLaunchPlan.ResolveRenderer(GraphicsSettings.RendererVulkan, null));
    }

    [Fact]
    public void Create_DefaultsToTheInProcessCore()
    {
        XemuLaunchSettings launch = XemuLaunchPlan.Create(
            Settings(), null, 640, 480, Path.Combine(_root, "work"));

        Assert.Equal(EmulatorSettings.CoreHostingInProcess, launch.HostMode);
    }

    [Fact]
    public void Create_UsesNativePresentationByDefaultAndKeepsThePadSink()
    {
        XemuLaunchSettings launch = XemuLaunchPlan.Create(
            Settings(), null, 640, 480, Path.Combine(_root, "work"));

        // The shell never requests a frame section: the core owns the picture.
        Assert.Empty(launch.DisplaySinkName);
        if (XemuInputSink.IsSupported)
        {
            Assert.NotEmpty(launch.InputSinkName);
        }
        else
        {
            Assert.Empty(launch.InputSinkName);
        }
    }

    [Fact]
    public void DescribeProblem_ReportsTheFirstBlockingReason()
    {
        string work = Path.Combine(_root, "work");

        // With a core present, a configuration without a BIOS is refused for that
        // reason; the fixture core keeps this independent of the build layout.
        var noBios = new EmulatorSettings
        {
            XemuCorePath = Path.Combine(_root, "firmware", "xemu.exe"),
            BiosPath = string.Empty,
        };
        Assert.Contains(
            "BIOS",
            XemuLaunchPlan.DescribeProblem(noBios, null, work),
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

    [Fact]
    public void DescribeProblem_AcceptsAFileTreeZar()
    {
        // The core synthesizes an XDVDFS volume for a file-tree archive, so the
        // shell must not pre-reject it as it did before the synthesizer landed.
        string archive = Path.Combine(_root, "media", "tree.zar");
        WriteFileTreeArchive(archive);

        string? problem = XemuLaunchPlan.DescribeProblem(
            Settings(), archive, Path.Combine(_root, "work"));

        Assert.Null(problem);
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

    /// <summary>
    /// Writes a minimal file-tree ZArchive: a root directory and no files. The
    /// shell only needs a valid directory to classify the shape; the core builds
    /// the XDVDFS volume at open time.
    /// </summary>
    private static void WriteFileTreeArchive(string path)
    {
        const uint magic = 0x169F52D6;
        const uint version = 0x61BF3A01;
        const int footerSize = 144;
        const int treeEntrySize = 16;
        const int namesOffset = 32;
        const int treeOffset = 64;

        byte[] names = [0];
        var tree = new byte[treeEntrySize];
        BinaryPrimitives.WriteUInt32BigEndian(tree, 0x7FFF_FFFFu);
        int footerOffset = treeOffset + tree.Length;
        int total = footerOffset + footerSize;

        var file = new byte[total];
        names.CopyTo(file, namesOffset);
        tree.CopyTo(file, treeOffset);

        Span<byte> footer = file.AsSpan(footerOffset, footerSize);
        BinaryPrimitives.WriteUInt64BigEndian(footer[32..], namesOffset);
        BinaryPrimitives.WriteUInt64BigEndian(footer[40..], (ulong)names.Length);
        BinaryPrimitives.WriteUInt64BigEndian(footer[48..], treeOffset);
        BinaryPrimitives.WriteUInt64BigEndian(footer[56..], (ulong)tree.Length);
        BinaryPrimitives.WriteUInt64BigEndian(footer[128..], (ulong)total);
        BinaryPrimitives.WriteUInt32BigEndian(footer[136..], version);
        BinaryPrimitives.WriteUInt32BigEndian(footer[140..], magic);

        File.WriteAllBytes(path, file);
    }
}
