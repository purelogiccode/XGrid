using System;
using System.Globalization;
using System.IO;
using XGrid.Xbe;

namespace XGrid.Frontend.Avalonia.Models;

/// <summary>The on-disk container of a game entry.</summary>
public enum GameFormat
{
    /// <summary>An XDVDFS ISO/XISO image.</summary>
    Iso,

    /// <summary>A compressed CHD disc image.</summary>
    Chd,

    /// <summary>A loose Xbox executable (inspectable, not bootable on its own).</summary>
    Xbe,
}

/// <summary>A game or executable discovered by the library scanner.</summary>
public sealed class GameEntry
{
    private GameEntry(string path, string title, GameFormat format, long sizeBytes)
    {
        Path = path;
        Title = title;
        Format = format;
        SizeBytes = sizeBytes;
    }

    /// <summary>Gets the full path to the image.</summary>
    public string Path { get; }

    /// <summary>Gets the display title.</summary>
    public string Title { get; }

    /// <summary>Gets the container format.</summary>
    public GameFormat Format { get; }

    /// <summary>Gets the image size in bytes.</summary>
    private long SizeBytes { get; }

    /// <summary>Gets a value indicating whether the entry can be inserted as a disc.</summary>
    public bool IsLaunchable => Format is GameFormat.Iso or GameFormat.Chd;

    /// <summary>Gets the short format label.</summary>
    public string FormatText => Format switch
    {
        GameFormat.Iso => "ISO",
        GameFormat.Chd => "CHD",
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
    public static GameEntry? FromFile(string path)
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
                ".xbe" => GameFormat.Xbe,
                _ => GameFormat.Iso,
            };

            string title = Prettify(System.IO.Path.GetFileNameWithoutExtension(path));
            if (format == GameFormat.Xbe)
            {
                title = ReadXbeTitle(path) ?? title;
            }

            return new GameEntry(path, title, format, info.Length)
            {
                // Covers .xbe directly and every launchable image; a container
                // the resolver cannot read (or a homebrew title with no
                // certificate) leaves the id at 0 / the shared placeholder.
                TitleId = XGrid.Host.DiscTitleId.TryRead(path, out uint titleId, out _) ? titleId : 0u,
            };
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
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
        catch (InvalidDataException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
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
