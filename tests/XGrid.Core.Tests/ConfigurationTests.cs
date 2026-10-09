using System;
using System.IO;
using XGrid.Core.Configuration;
using XGrid.Core.Emulation;
using Xunit;

namespace XGrid.Core.Tests;

public sealed class ConfigurationTests
{
    [Fact]
    public void Defaults_AreExpected()
    {
        var configuration = new EmulatorConfiguration();

        Assert.Null(configuration.System.Bios);
        Assert.Null(configuration.System.Mcpx);
        Assert.Null(configuration.System.HardDisk);
        Assert.Equal(GraphicsBackend.Vulkan, configuration.Graphics.Backend);
        Assert.Equal(1, configuration.Graphics.ResolutionScale);
        Assert.True(configuration.Graphics.Vsync);
        Assert.Equal(CpuBackend.Interpreter, configuration.Cpu.Backend);
        Assert.False(configuration.Cpu.JitEnabled);
        Assert.True(configuration.Audio.Enabled);
        Assert.Equal("Default", configuration.Input.Backend);
    }

    [Fact]
    public void Save_Then_Load_RoundTrips()
    {
        var original = new EmulatorConfiguration
        {
            System = new SystemConfiguration
            {
                Bios = "bios.bin",
                Mcpx = "mcpx.bin",
                HardDisk = "xbox_hdd.img",
            },
            Graphics = new GraphicsConfiguration
            {
                Backend = GraphicsBackend.Null,
                ResolutionScale = 4,
                Vsync = false,
            },
            Cpu = new CpuConfiguration
            {
                Backend = CpuBackend.Jit,
                JitEnabled = true,
            },
            Audio = new AudioConfiguration { Enabled = false },
            Input = new InputConfiguration { Backend = "Keyboard" },
        };

        string json = ConfigurationLoader.Save(original);
        EmulatorConfiguration loaded = ConfigurationLoader.Load(json);

        Assert.Equal(original.System.Bios, loaded.System.Bios);
        Assert.Equal(original.System.Mcpx, loaded.System.Mcpx);
        Assert.Equal(original.System.HardDisk, loaded.System.HardDisk);
        Assert.Equal(original.Graphics.Backend, loaded.Graphics.Backend);
        Assert.Equal(original.Graphics.ResolutionScale, loaded.Graphics.ResolutionScale);
        Assert.Equal(original.Graphics.Vsync, loaded.Graphics.Vsync);
        Assert.Equal(original.Cpu.Backend, loaded.Cpu.Backend);
        Assert.Equal(original.Cpu.JitEnabled, loaded.Cpu.JitEnabled);
        Assert.Equal(original.Audio.Enabled, loaded.Audio.Enabled);
        Assert.Equal(original.Input.Backend, loaded.Input.Backend);
    }

    [Fact]
    public void Load_PlanSample_ParsesValues()
    {
        const string json = """
                            {
                              "system": {
                                "bios": "bios.bin",
                                "mcpx": "mcpx.bin",
                                "hardDisk": "xbox_hdd.img"
                              },
                              "graphics": {
                                "backend": "Vulkan",
                                "resolutionScale": 1,
                                "vsync": true
                              },
                              "cpu": {
                                "backend": "Jit",
                                "jitEnabled": true
                              },
                              "audio": {
                                "enabled": true
                              },
                              "input": {
                                "backend": "Default"
                              }
                            }
                            """;

        EmulatorConfiguration configuration = ConfigurationLoader.Load(json);

        Assert.Equal("bios.bin", configuration.System.Bios);
        Assert.Equal("mcpx.bin", configuration.System.Mcpx);
        Assert.Equal("xbox_hdd.img", configuration.System.HardDisk);
        Assert.Equal(GraphicsBackend.Vulkan, configuration.Graphics.Backend);
        Assert.Equal(CpuBackend.Jit, configuration.Cpu.Backend);
        Assert.True(configuration.Cpu.JitEnabled);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(17)]
    public void Load_InvalidResolutionScale_Throws(int scale)
    {
        string json = $"{{\"graphics\":{{\"resolutionScale\":{scale}}}}}";

        Assert.Throws<ConfigurationException>(() => ConfigurationLoader.Load(json));
    }

    [Fact]
    public void Load_NullSection_Throws()
    {
        Assert.Throws<ConfigurationException>(() => ConfigurationLoader.Load("{\"system\":null}"));
    }

    [Fact]
    public void Load_EmptyInputBackend_Throws()
    {
        Assert.Throws<ConfigurationException>(() => ConfigurationLoader.Load("{\"input\":{\"backend\":\" \"}}"));
    }

    [Fact]
    public void Load_MalformedJson_Throws()
    {
        Assert.Throws<ConfigurationException>(() => ConfigurationLoader.Load("{ not json"));
    }

    [Fact]
    public void SaveFile_Then_LoadFile_RoundTrips()
    {
        string path = Path.Combine(Path.GetTempPath(), $"xgrid-config-{Guid.NewGuid():N}.json");
        try
        {
            ConfigurationLoader.SaveFile(path, new EmulatorConfiguration());
            EmulatorConfiguration loaded = ConfigurationLoader.LoadFile(path);

            Assert.Equal(GraphicsBackend.Vulkan, loaded.Graphics.Backend);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Options_ZeroCyclesPerStep_Throws()
    {
        Assert.Throws<ConfigurationException>(() => new Emulator(
            new EmulatorConfiguration(),
            new EmulatorOptions { CyclesPerStep = 0 }));
    }
}
