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

            return new GameEntry(path, title, format, info.Length);
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
