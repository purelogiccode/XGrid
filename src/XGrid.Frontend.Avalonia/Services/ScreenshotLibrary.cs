using System;
using System.Collections.Generic;
using System.IO;

namespace XGrid.Frontend.Avalonia.Services;

/// <summary>One capture in the screenshots folder.</summary>
/// <param name="Path">Absolute or relative path to the PNG.</param>
/// <param name="CapturedAt">The file's last write time, in local time.</param>
public readonly record struct ScreenshotFile(string Path, DateTime CapturedAt);

/// <summary>
/// Lists the screenshots the shell has written, newest first, so the Library
/// can show them without exposing the data folder layout.
/// </summary>
public static class ScreenshotLibrary
{
    /// <summary>The number of captures the Library shows at once.</summary>
    public const int DefaultLimit = 24;

    /// <summary>
    /// Reads the capture folder. A missing folder is an empty library; a file
    /// that disappears while the folder is being read is skipped.
    /// </summary>
    /// <param name="directory">The <c>screenshots</c> folder.</param>
    /// <param name="limit">Maximum number of captures to return.</param>
    public static IReadOnlyList<ScreenshotFile> List(string directory, int limit = DefaultLimit)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        if (!Directory.Exists(directory))
        {
            return [];
        }

        var files = new List<ScreenshotFile>();
        foreach (string path in Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly))
        {
            if (!path.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                files.Add(new ScreenshotFile(path, File.GetLastWriteTime(path)));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // A capture removed between the listing and the stat.
            }
        }

        files.Sort(static (left, right) => right.CapturedAt.CompareTo(left.CapturedAt));
        return files.Count <= limit ? files : files.GetRange(0, limit);
    }
}
