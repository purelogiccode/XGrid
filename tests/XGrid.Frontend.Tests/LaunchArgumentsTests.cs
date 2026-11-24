using XGrid.Frontend.Avalonia;
using Xunit;

namespace XGrid.Frontend.Tests;

/// <summary>
/// Pins the shell's own command-line switches: autostart, the three fullscreen
/// spellings, and the pass-through of arguments Avalonia still needs.
/// </summary>
public sealed class LaunchArgumentsTests
{
    [Theory]
    [InlineData("-fullscreen")]
    [InlineData("--fullscreen")]
    [InlineData("-f")]
    [InlineData("-F")]
    public void Parse_RecognizesTheFullscreenSpellings(string argument)
    {
        LaunchArguments options = LaunchArguments.Parse([argument]);

        Assert.True(options.Fullscreen);
        Assert.False(options.Autostart);
        Assert.Empty(options.Remaining);
    }

    [Fact]
    public void Parse_CombinesAutostartAndFullscreen()
    {
        LaunchArguments options = LaunchArguments.Parse(["-f", "--autostart"]);

        Assert.True(options.Autostart);
        Assert.True(options.Fullscreen);
        Assert.Empty(options.Remaining);
    }

    [Fact]
    public void Parse_PassesUnknownArgumentsThrough()
    {
        LaunchArguments options = LaunchArguments.Parse(["one", "--theme", "dark", "-f"]);

        Assert.True(options.Fullscreen);
        Assert.Equal(["one", "--theme", "dark"], options.Remaining);
    }

    [Fact]
    public void Parse_IsCaseInsensitive()
    {
        LaunchArguments options = LaunchArguments.Parse(["--AUTOSTART", "--FullScreen"]);

        Assert.True(options.Autostart);
        Assert.True(options.Fullscreen);
    }
}
