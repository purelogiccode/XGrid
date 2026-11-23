using System;
using System.Globalization;
using System.IO;

namespace XGrid.Frontend.Avalonia.Services;

/// <summary>
/// Names screenshot files so they sort chronologically and never overwrite an
/// earlier capture taken in the same second.
/// </summary>
public static class ScreenshotNaming
{
    /// <summary>Builds the first free path for a capture taken at <paramref name="timestamp"/>.</summary>
    /// <param name="directory">Folder that receives the capture.</param>
    /// <param name="timestamp">Capture time.</param>
    /// <returns>An absolute or relative path ending in <c>.png</c>.</returns>
    public static string NextPath(string directory, DateTimeOffset timestamp)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        string stem = "screenshot-"
                      + timestamp.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        string path = Path.Combine(directory, stem + ".png");
        for (int index = 1; File.Exists(path); index++)
        {
            path = Path.Combine(
                directory,
                stem + "-" + index.ToString(CultureInfo.InvariantCulture) + ".png");
        }

        return path;
    }
}
