using XGrid.Frontend.Avalonia.Services;
using Xunit;

namespace XGrid.Frontend.Tests;

/// <summary>
/// Pins the display caption to the frame that was actually presented. The core
/// publishes 1280x960, so a caption that says 640x480 describes a picture that
/// is not on screen.
/// </summary>
public sealed class DisplayCaptionTextTests
{
    [Theory]
    [InlineData(1280, 960, "OpenGL", "1280 x 960 - OpenGL")]
    [InlineData(640, 480, "OpenGL", "640 x 480 - OpenGL")]
    [InlineData(1920, 1080, "Vulkan", "1920 x 1080 - Vulkan")]
    public void Describe_UsesThePresentedFrameSize(int width, int height, string renderer, string expected)
    {
        Assert.Equal(expected, DisplayCaptionText.Describe(width, height, renderer));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1280, 0)]
    [InlineData(0, 960)]
    [InlineData(-1, 960)]
    public void Describe_FallsBackToTheRendererWhenTheSizeIsUnknown(int width, int height)
    {
        Assert.Equal("OpenGL", DisplayCaptionText.Describe(width, height, "OpenGL"));
    }
}
