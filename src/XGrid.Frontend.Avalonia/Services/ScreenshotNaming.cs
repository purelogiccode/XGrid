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
    /// <summary>Folder name the captures are written to, inside the shell's data folder.</summary>
    public const string FolderName = "screenshots";

    /// <summary>Builds the capture folder for a shell data folder.</summary>
    /// <param name="dataDirectory">The shell's per-user data folder.</param>
    /// <returns>The <c>screenshots</c> folder inside <paramref name="dataDirectory"/>.</returns>
    public static string Directory(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        return Path.Combine(dataDirectory, FolderName);
    }

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
