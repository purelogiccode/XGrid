using System;
using System.Collections.Generic;
using System.IO;
using Avalonia.Media.Imaging;

namespace XGrid.Frontend.Avalonia.Services;

/// <summary>
/// Decodes cover images once per library scan. Filtering the library rebuilds
/// the visible rows, so a per-row decode would re-read every cover on each
/// keystroke.
/// </summary>
public sealed class CoverArtCache : IDisposable
{
    private const int CoverWidth = 160;
    private const long MaxCoverBytes = 32L * 1024 * 1024;

    private readonly Dictionary<string, Bitmap?> _covers = new(StringComparer.Ordinal);
    private bool _disposed;

    /// <summary>Gets the decoded cover for a path, or null when it cannot be read.</summary>
    /// <param name="path">Cover image path, or null.</param>
    /// <returns>The shared bitmap, or null.</returns>
    public Bitmap? Get(string? path)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        if (!_covers.TryGetValue(path, out Bitmap? cover))
        {
            cover = Load(path);
            _covers[path] = cover;
        }

        return cover;
    }

    /// <summary>Releases every decoded cover.</summary>
    public void Clear()
    {
        foreach (Bitmap? cover in _covers.Values)
        {
            cover?.Dispose();
        }

        _covers.Clear();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Clear();
        _disposed = true;
    }

    private static Bitmap? Load(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > MaxCoverBytes)
            {
                return null;
            }

            using FileStream stream = File.OpenRead(path);
            return Bitmap.DecodeToWidth(stream, CoverWidth);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                              or ArgumentException or NotSupportedException
                                              or InvalidOperationException)
        {
            return null;
        }
    }
}
