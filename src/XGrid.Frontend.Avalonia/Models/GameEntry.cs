using System;
using System.Globalization;
using System.IO;
using Serilog;
using XGrid.Frontend.Avalonia.Services;
using XGrid.Xbe;
using XGrid.XboxFs;

namespace XGrid.Frontend.Avalonia.Models;

/// <summary>The on-disk container of a game entry.</summary>
public enum GameFormat
{
    /// <summary>An XDVDFS ISO/XISO image.</summary>
    Iso,

    /// <summary>A compressed CHD disc image.</summary>
    Chd,

    /// <summary>A ZArchive (.zar) container holding a disc image.</summary>
    Zar,

    /// <summary>A loose Xbox executable (inspectable, not bootable on its own).</summary>
    Xbe,
}

/// <summary>A game or executable discovered by the library scanner.</summary>
public sealed class GameEntry
{
    private GameEntry(string path, string title, GameFormat format, long sizeBytes, string? coverPath)
    {
        Path = path;
        Title = title;
        Format = format;
        SizeBytes = sizeBytes;
        CoverPath = coverPath;
    }

    /// <summary>Gets the full path to the image.</summary>
    public string Path { get; }

    /// <summary>Gets the display title.</summary>
    public string Title { get; }

    /// <summary>Gets the local cover image path, or null when none was found.</summary>
    public string? CoverPath { get; }

    /// <summary>Gets the container format.</summary>
    public GameFormat Format { get; }

    /// <summary>Gets the image size in bytes.</summary>
    private long SizeBytes { get; }

    /// <summary>Gets a value indicating whether the entry can be inserted as a disc.</summary>
    public bool IsLaunchable => Format is GameFormat.Iso or GameFormat.Chd or GameFormat.Zar;

    /// <summary>Gets the short format label.</summary>
    public string FormatText => Format switch
    {
        GameFormat.Iso => "ISO",
        GameFormat.Chd => "CHD",
        GameFormat.Zar => "ZAR",
        _ => "XBE",
    };

    /// <summary>Gets the formatted size label.</summary>
    public string SizeText => FormatSize(SizeBytes);

    /// <summary>
    /// Gets the XBE certificate title id (the per-title configuration key),
    /// or 0 when the container has none to read.
    /// </summary>
    /// <remarks>
    /// Resolved at construction through <see cref="DiscTitleId.TryRead"/> - the
    /// same resolver the emulator uses to match a `title:XXXXXXXX` override -
    /// so a library row shows the identity a per-title setting will key on
    /// (filed as the Track P follow-up in the §224 session notes). Every homebrew sample
    /// that never sets a certificate id shares the placeholder below, which is
    /// why the row keeps the file name as the distinguishing text.
    /// </remarks>
    public uint TitleId { get; private init; }

    /// <summary>Gets the title id as the `title:XXXXXXXX` key fragment, or an empty string when unknown.</summary>
    public string TitleIdText => TitleId == 0
        ? string.Empty
        : "Title " + TitleId.ToString("X8", CultureInfo.InvariantCulture);

    /// <summary>Gets a value indicating whether this entry carries a distinct certificate title id.</summary>
    public bool HasDistinctTitleId => TitleId != 0 && TitleId != PlaceholderTitleId;

    private const uint PlaceholderTitleId = 0xFFFF0002u;

    /// <summary>Gets the containing folder.</summary>
    public string Folder => System.IO.Path.GetDirectoryName(Path) ?? string.Empty;

    /// <summary>Creates an entry for a discovered file, parsing direct XBE titles.</summary>
    /// <param name="path">Discovered image or executable.</param>
    /// <param name="covers">Cover index for the scan, or null for sibling covers only.</param>
    public static GameEntry? FromFile(string path, CoverArtIndex? covers = null)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            var info = new FileInfo(path);
            if (!info.Exists)
            {
                return null;
            }

            GameFormat format = System.IO.Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".iso" => GameFormat.Iso,
                ".chd" => GameFormat.Chd,
                ".zar" => GameFormat.Zar,
                ".xbe" => GameFormat.Xbe,
                _ => GameFormat.Iso,
            };

            string title = Prettify(System.IO.Path.GetFileNameWithoutExtension(path));
            if (format == GameFormat.Xbe)
            {
                title = ReadXbeTitle(path) ?? title;
            }

            // Covers .xbe directly and every launchable image; a container the
            // resolver cannot read (or a homebrew title with no certificate)
            // leaves the id at 0 / the shared placeholder.
            uint resolvedTitleId = DiscTitleId.TryRead(path, out uint titleId, out _) ? titleId : 0u;
            CoverArtIndex coverIndex = covers ?? CoverArtIndex.Empty;
            return new GameEntry(
                path,
                title,
                format,
                info.Length,
                coverIndex.Find(path, title, resolvedTitleId))
            {
                TitleId = resolvedTitleId,
            };
        }
        catch (IOException exception)
        {
            Log.Debug(exception, "Could not inspect disc {Path}", path);
            return null;
        }
        catch (UnauthorizedAccessException exception)
        {
            Log.Debug(exception, "Could not read disc {Path}", path);
            return null;
        }
    }

    private static string? ReadXbeTitle(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > 64L * 1024 * 1024)
            {
                return null;
            }

            XbeImage image = XbeImage.Load(File.ReadAllBytes(path));
            string title = image.TitleName.Trim();
            return title.Length == 0 ? null : title;
        }
        catch (InvalidDataException exception)
        {
            Log.Debug(exception, "The XBE header is not readable: {Path}", path);
            return null;
        }
        catch (IOException exception)
        {
            Log.Debug(exception, "Could not read the XBE {Path}", path);
            return null;
        }
        catch (UnauthorizedAccessException exception)
        {
            Log.Debug(exception, "Could not access the XBE {Path}", path);
            return null;
        }
    }

    private static string Prettify(string name)
    {
        string cleaned = name.Replace('_', ' ').Replace('.', ' ').Trim();
        return cleaned.Length == 0 ? name : cleaned;
    }

    private static string FormatSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return string.Format(CultureInfo.InvariantCulture, unit == 0 ? "{0:0} {1}" : "{0:0.0} {1}", value, units[unit]);
    }
}
