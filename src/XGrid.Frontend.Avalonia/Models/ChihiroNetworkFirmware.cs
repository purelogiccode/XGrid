using System;
using System.Buffers.Binary;
using System.Globalization;
using System.IO;
using System.Text;

namespace XGrid.Frontend.Avalonia.Models;

/// <summary>
/// Reads the version field from a Chihiro network-board firmware image. The
/// VxWorks image carries its version after the <c>FIRM_VERSION is here!</c>
/// marker as a little-endian word, printed high byte first.
/// </summary>
public static class ChihiroNetworkFirmware
{
    private static readonly byte[] Marker = Encoding.ASCII.GetBytes("FIRM_VERSION is here!");

    /// <summary>
    /// Returns the version formatted like <c>13.05(06.21)</c>, or null when
    /// the image is missing or has no version field.
    /// </summary>
    /// <param name="path">Candidate network firmware file.</param>
    /// <returns>The formatted version, or null.</returns>
    public static string? ReadVersion(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        byte[] image;
        try
        {
            image = File.ReadAllBytes(path);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }

        int at = image.AsSpan().IndexOf(Marker);
        if (at < 0)
        {
            return null;
        }

        at = (at + Marker.Length + 1 + 3) & ~3;
        if (at + sizeof(uint) > image.Length)
        {
            return null;
        }

        uint word = BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(at));
        if (word == 0)
        {
            return null;
        }

        return string.Format(
            CultureInfo.InvariantCulture,
            "{0:X}.{1:X2}({2:X2}.{3:X2})",
            (word >> 24) & 0xFF,
            (word >> 16) & 0xFF,
            (word >> 8) & 0xFF,
            word & 0xFF);
    }
}
