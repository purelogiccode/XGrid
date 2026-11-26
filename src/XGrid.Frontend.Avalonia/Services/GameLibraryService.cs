using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Serilog;
using XGrid.Frontend.Avalonia.Models;

namespace XGrid.Frontend.Avalonia.Services;

/// <summary>Scans configured folders for Xbox disc images and executables.</summary>
public static class GameLibraryService
{
    private const int MaxEntries = 4096;

    /// <summary>Scans the given folders recursively (missing folders are skipped).</summary>
    /// <param name="folders">Library roots.</param>
    /// <param name="coversDirectory">Shared covers folder, or null for sibling images only.</param>
    /// <param name="resolveTitleIds">
    /// True to open each image and read its XBE certificate. The library page
    /// passes false so adding a large folder is a directory scan; the title id
    /// is resolved when a game launches.
    /// </param>
    /// <param name="coverOptions">Cover matching options, or null for the defaults.</param>
    public static List<GameEntry> Scan(
        IEnumerable<string> folders,
        string? coversDirectory = null,
        bool resolveTitleIds = true,
        CoverMatchOptions? coverOptions = null)
    {
        ArgumentNullException.ThrowIfNull(folders);
        StringComparer pathComparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        var seen = new HashSet<string>(pathComparer);
        var entries = new List<GameEntry>();
        LocalCoverIndex covers = LocalCoverIndex.Create(
            coversDirectory,
            coverOptions?.StripAnnotations ?? true);
        foreach (string folder in folders)
        {
            if (entries.Count >= MaxEntries)
            {
                break;
            }

            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            {
                continue;
            }

            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.System | FileAttributes.ReparsePoint,
            };

            try
            {
                foreach (string file in Directory.EnumerateFiles(folder, "*", options))
                {
                    if (entries.Count >= MaxEntries)
                    {
                        break;
                    }

                    if (!IsGameCandidate(file) || !seen.Add(Path.GetFullPath(file)))
                    {
                        continue;
                    }

                    GameEntry? entry = GameEntry.FromFile(file, covers, resolveTitleIds, coverOptions);
                    if (entry is not null)
                    {
                        entries.Add(entry);
                    }
                }
            }
            catch (IOException exception)
            {
                Log.Debug(exception, "A library folder disappeared during the scan: {Folder}", folder);
            }
            catch (UnauthorizedAccessException exception)
            {
                Log.Debug(exception, "A library folder is not readable: {Folder}", folder);
            }
        }

        entries.Sort(static (left, right) =>
            string.Compare(left.Title, right.Title, StringComparison.CurrentCultureIgnoreCase));
        return entries;
    }

    /// <summary>Returns true when a path has a supported library extension.</summary>
    public static bool IsGameCandidate(string path)
    {
        string extension = Path.GetExtension(path);
        if (extension.Equals(".cci", StringComparison.OrdinalIgnoreCase))
        {
            // A split set is one game: list the first part and let the core
            // discover the rest from it.
            return IsPrimaryCciPart(path);
        }

        return extension.Equals(".iso", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".xiso", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".chd", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".zar", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".xbe", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPrimaryCciPart(string path)
    {
        string name = Path.GetFileNameWithoutExtension(path);
        int separator = name.LastIndexOf('.');
        if (separator <= 0 || separator == name.Length - 1)
        {
            return true;
        }

        return !int.TryParse(
                   name.AsSpan(separator + 1),
                   NumberStyles.None,
                   CultureInfo.InvariantCulture,
                   out int part)
               || part == 1;
    }
}
