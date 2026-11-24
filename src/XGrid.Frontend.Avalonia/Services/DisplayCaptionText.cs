using System.Globalization;

namespace XGrid.Frontend.Avalonia.Services;

/// <summary>
/// Formats the caption over the console picture. The size is whatever the core
/// actually published, never a default, so a 1280x960 sink is not labelled
/// 640x480.
/// </summary>
public static class DisplayCaptionText
{
    /// <summary>
    /// Describes a presented frame as <c>WIDTH x HEIGHT - RENDERER</c>. A size
    /// the shell does not know (the native embedded host paints the picture
    /// itself) leaves the renderer alone.
    /// </summary>
    /// <param name="width">Frame width in pixels, or 0 when unknown.</param>
    /// <param name="height">Frame height in pixels, or 0 when unknown.</param>
    /// <param name="renderer">Renderer label, such as <c>OpenGL</c>.</param>
    /// <returns>The caption text.</returns>
    public static string Describe(int width, int height, string renderer)
    {
        string name = renderer ?? string.Empty;
        if (width <= 0 || height <= 0)
        {
            return name;
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{width} x {height} - {name}");
    }
}
