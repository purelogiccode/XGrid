using XGrid.Frontend.Avalonia.Models;
using Xunit;

namespace XGrid.Frontend.Tests;

/// <summary>
/// Pins the core switch (decision A): the managed C# core stays the default and
/// the oracle, the xemu core is opt-in, and an unknown persisted value can never
/// select a core this build does not have.
/// </summary>
public sealed class CoreSettingTests
{
    [Fact]
    public void FreshConfigurationUsesTheManagedCore()
    {
        var settings = new EmulatorSettings();

        Assert.Equal(EmulatorSettings.CoreManaged, settings.Core);
        Assert.False(settings.UsesXemuCore);
    }

    [Fact]
    public void TheXemuKeySelectsTheCore()
    {
        var settings = new EmulatorSettings { Core = EmulatorSettings.CoreXemu };

        Assert.True(settings.UsesXemuCore);
        Assert.True(EmulatorSettings.IsXemuCore("xemu"));
        Assert.False(EmulatorSettings.IsXemuCore(null));
        Assert.False(EmulatorSettings.IsXemuCore("Managed"));
    }

    [Fact]
    public void NormalizeRepairsAnUnknownCore()
    {
        var settings = new EmulatorSettings { Core = "xemu-next" };
        settings.Normalize();

        Assert.Equal(EmulatorSettings.CoreManaged, settings.Core);
    }

    [Fact]
    public void CloneKeepsTheCoreAndItsPath()
    {
        var settings = new EmulatorSettings
        {
            Core = EmulatorSettings.CoreXemu,
            XemuCorePath = @"C:\core\xemu.exe",
        };

        EmulatorSettings copy = settings.Clone();

        Assert.Equal(EmulatorSettings.CoreXemu, copy.Core);
        Assert.Equal(@"C:\core\xemu.exe", copy.XemuCorePath);
        Assert.True(copy.UsesXemuCore);
    }
}
