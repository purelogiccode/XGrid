using System;
using System.Collections.Generic;
using System.IO;
using XGrid.Frontend.Avalonia.Models;

namespace XGrid.Frontend.Avalonia.Services;

/// <summary>Scans configured folders for Xbox disc images and executables.</summary>
public static class GameLibraryService
{
    private const int MaxEntries = 4096;

    /// <summary>Scans the given folders recursively (missing folders are skipped).</summary>
    public static List<GameEntry> Scan(IEnumerable<string> folders)
    {
        ArgumentNullException.ThrowIfNull(folders);
        StringComparer pathComparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        var seen = new HashSet<string>(pathComparer);
        var entries = new List<GameEntry>();
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

                    GameEntry? entry = GameEntry.FromFile(file);
                    if (entry is not null)
                    {
                        entries.Add(entry);
                    }
                }
            }
            catch (IOException)
            {
                // A folder disappearing mid-scan must not abort the scan.
            }
            catch (UnauthorizedAccessException)
            {
                // Ignore unreadable roots.
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
        return extension.Equals(".iso", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".chd", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".xbe", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Returns the prettified display title for a path (no I/O).</summary>
    public static string TitleFromPath(string path)
    {
        string name = Path.GetFileNameWithoutExtension(path);
        string cleaned = name.Replace('_', ' ').Replace('.', ' ').Trim();
        return cleaned.Length == 0 ? name : cleaned;
    }
}
