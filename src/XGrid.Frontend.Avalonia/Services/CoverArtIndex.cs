using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Serilog;

namespace XGrid.Frontend.Avalonia.Services;

/// <summary>
/// Resolves local cover art for library entries. Covers live in the user's
/// shared covers folder, named after the title ID or the title; images beside
/// the disc are deliberately ignored.
/// </summary>
/// <remarks>
/// The index is built once per library scan. Building it per entry would walk
/// the covers folder thousands of times on a large library.
/// </remarks>
public sealed class CoverArtIndex
{
    /// <summary>The image extensions a cover may use.</summary>
    public static IReadOnlyList<string> Extensions { get; } = [".png", ".jpg", ".jpeg", ".webp", ".bmp"];

    private static readonly StringComparer NameComparer = StringComparer.Ordinal;

    private readonly string _directory;
    private readonly IReadOnlyDictionary<string, string> _byName;

    /// <summary>An index with no shared folder; sibling covers still resolve.</summary>
    public static CoverArtIndex Empty { get; } =
        new(string.Empty, new Dictionary<string, string>(NameComparer));

    private CoverArtIndex(string directory, IReadOnlyDictionary<string, string> byName)
    {
        _directory = directory;
        _byName = byName;
    }

    /// <summary>Gets the shared folder used when the shell has no configured one.</summary>
    /// <returns>The <c>covers</c> folder under the shell data directory.</returns>
    public static string DefaultDirectory()
    {
        return Path.Combine(SettingsStore.ResolveDirectory(), "covers");
    }

    /// <summary>Builds an index over the top-level images in a covers folder.</summary>
    /// <param name="directory">Covers folder, or null/empty when no covers are available.</param>
    /// <returns>An index that can be reused for every entry in one scan.</returns>
    public static CoverArtIndex Create(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return Empty;
        }

        var files = new List<string>();
        try
        {
            foreach (string file in Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly))
            {
                if (IsImage(file))
                {
                    files.Add(file);
                }
            }
        }
        catch (IOException exception)
        {
            Log.Debug(exception, "The covers folder disappeared during the scan");
        }
        catch (UnauthorizedAccessException exception)
        {
            Log.Debug(exception, "The covers folder is not readable");
        }

        // A deterministic order keeps the same cover when two files normalize
        // to the same name on a case-sensitive file system.
        files.Sort(StringComparer.Ordinal);
        var byName = new Dictionary<string, string>(NameComparer);
        foreach (string file in files)
        {
            string key = Normalize(Path.GetFileNameWithoutExtension(file));
            if (key.Length > 0 && !byName.ContainsKey(key))
            {
                byName[key] = file;
            }
        }

        return new CoverArtIndex(directory, byName);
    }

    /// <summary>Finds the best local cover for one entry.</summary>
    /// <param name="gamePath">Disc or XBE path.</param>
    /// <param name="title">Display title of the entry.</param>
    /// <param name="titleId">XBE certificate title ID, or 0 when unknown.</param>
    /// <returns>The cover path, or null when no local image matches.</returns>
    public string? Find(string gamePath, string title, uint titleId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gamePath);

        if (_directory.Length == 0)
        {
            return null;
        }

        if (titleId != 0)
        {
            string id = titleId.ToString("X8", System.Globalization.CultureInfo.InvariantCulture);
            foreach (string extension in Extensions)
            {
                string candidate = Path.Combine(_directory, id + extension);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        string key = Normalize(title);
        return key.Length > 0 && _byName.TryGetValue(key, out string? cover) ? cover : null;
    }

    /// <summary>
    /// Returns the normalized form used for title matching: annotation groups
    /// (<c>(USA)</c>, <c>[!]</c>) removed, <c>&amp;</c> read as "and", and only
    /// letters and digits kept.
    /// </summary>
    /// <param name="value">File stem or display title.</param>
    /// <returns>The comparison key.</returns>
    public static string Normalize(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var text = new StringBuilder(value.Length);
        int depth = 0;
        foreach (char c in value)
        {
            if (c is '(' or '[' or '{')
            {
                depth++;
                continue;
            }

            if (c is ')' or ']' or '}')
            {
                if (depth > 0)
                {
                    depth--;
                }

                continue;
            }

            if (depth > 0)
            {
                continue;
            }

            if (c == '&')
            {
                text.Append("and");
                continue;
            }

            if (char.IsLetterOrDigit(c))
            {
                text.Append(char.ToLowerInvariant(c));
            }
        }

        return text.ToString();
    }

    /// <summary>Returns true when a path has a supported cover extension.</summary>
    /// <param name="path">Candidate image path.</param>
    /// <returns>True for PNG, JPEG, WebP and BMP.</returns>
    public static bool IsImage(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string extension = Path.GetExtension(path);
        foreach (string candidate in Extensions)
        {
            if (extension.Equals(candidate, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

}
