using System;
using System.Collections.Generic;
using System.IO;
using XGrid.Storage;
using XGrid.Xbe;

namespace XGrid.XboxFs;

/// <summary>
/// Resolves the XBE certificate title id of a disc image or a loose
/// executable: the identity a per-title configuration wants, because it
/// follows the title across paths and containers instead of the file name.
/// </summary>
/// <remarks>
/// A plain XDVDFS ISO is read through <see cref="XdvdfsFileSystem"/> (its root
/// <c>default.xbe</c>), a loose <c>.xbe</c> is read directly, and a CHD is read
/// through <see cref="XdvdfsSectorReader"/>, which walks the same volume layout
/// over the compressed image's sector stream (<see cref="XdvdfsFileSystem"/>
/// itself only opens ISO/CSO paths).
/// </remarks>
public static class DiscTitleId
{
    /// <summary>Largest executable the resolver will read out of an image.</summary>
    private const long MaxExecutableBytes = 256L * 1024 * 1024;

    /// <summary>The root executable name on every Xbox disc.</summary>
    private const string RootExecutableName = "default.xbe";

    /// <summary>
    /// Tries to read the title id (and certificate title name) of an image.
    /// </summary>
    /// <param name="imagePath">ISO/XISO, CHD or loose XBE path.</param>
    /// <param name="titleId">XBE certificate title id on success.</param>
    /// <param name="titleName">Certificate title name on success (may be empty).</param>
    /// <returns>True when a title id was read; false for any unsupported or unreadable image.</returns>
    public static bool TryRead(string? imagePath, out uint titleId, out string titleName)
    {
        titleId = 0;
        titleName = string.Empty;
        if (string.IsNullOrWhiteSpace(imagePath))
        {
            return false;
        }

        string extension = Path.GetExtension(imagePath);
        try
        {
            if (extension.Equals(".xbe", StringComparison.OrdinalIgnoreCase))
            {
                return TryReadExecutable(File.ReadAllBytes(imagePath), out titleId, out titleName);
            }

            if (extension.Equals(".iso", StringComparison.OrdinalIgnoreCase))
            {
                using XdvdfsFileSystem disc = XdvdfsFileSystem.Open(imagePath);
                byte[]? executable = ReadRootExecutable(disc);
                return executable is not null && TryReadExecutable(executable, out titleId, out titleName);
            }

            if (extension.Equals(".chd", StringComparison.OrdinalIgnoreCase))
            {
                using IDvdImage image = ChdDvdImage.Open(imagePath);
                return TryReadImage(image, out titleId, out titleName);
            }
        }
        catch (Exception exception) when (exception is IOException
                                              or UnauthorizedAccessException
                                              or InvalidDataException
                                              or NotSupportedException
                                              or ArgumentException
                                              or InvalidOperationException)
        {
            // An unreadable image simply has no title id; the caller keys by path.
        }

        return false;
    }

    /// <summary>
    /// Tries to read the title id out of an already-open disc image (any
    /// <see cref="IDvdImage"/>, e.g. a compressed CHD).
    /// </summary>
    /// <param name="image">Sector source of the disc image.</param>
    /// <param name="titleId">XBE certificate title id on success.</param>
    /// <param name="titleName">Certificate title name on success (may be empty).</param>
    /// <returns>True when the root executable was found and its certificate read.</returns>
    public static bool TryReadImage(IDvdImage image, out uint titleId, out string titleName)
    {
        ArgumentNullException.ThrowIfNull(image);
        titleId = 0;
        titleName = string.Empty;
        try
        {
            return XdvdfsSectorReader.TryReadRootFile(image, RootExecutableName, MaxExecutableBytes, out byte[] xbe)
                && TryReadExecutable(xbe, out titleId, out titleName);
        }
        catch (Exception exception) when (exception is IOException
                                              or InvalidDataException
                                              or NotSupportedException
                                              or ArgumentException
                                              or InvalidOperationException)
        {
        }

        return false;
    }

    /// <summary>Tries to parse a title id out of an in-memory XBE image.</summary>
    /// <param name="image">Complete XBE file bytes.</param>
    /// <param name="titleId">XBE certificate title id on success.</param>
    /// <param name="titleName">Certificate title name on success.</param>
    public static bool TryReadExecutable(ReadOnlySpan<byte> image, out uint titleId, out string titleName)
    {
        titleId = 0;
        titleName = string.Empty;
        try
        {
            XbeImage parsed = XbeImage.Load(image);
            titleId = parsed.TitleId;
            titleName = parsed.TitleName.Trim();
            return titleId != 0;
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }

    /// <summary>Reads the disc's root <c>default.xbe</c>, or null when absent or too large to inspect.</summary>
    private static byte[]? ReadRootExecutable(XdvdfsFileSystem disc)
    {
        IReadOnlyList<XdvdfsFileSystem.Entry> root = disc.ListDirectory("/");
        foreach (XdvdfsFileSystem.Entry entry in root)
        {
            if (entry.IsDirectory ||
                !string.Equals(entry.Name, RootExecutableName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return entry.Size <= 0 || entry.Size > MaxExecutableBytes ? null : disc.ReadFile(entry);
        }

        return null;
    }
}
