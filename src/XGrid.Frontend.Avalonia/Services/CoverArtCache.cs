using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Serilog;

namespace XGrid.Frontend.Avalonia.Services;

/// <summary>
/// Decodes cover images once per file on background threads. Filtering the
/// library reuses the same bitmaps instead of re-reading every cover.
/// </summary>
public sealed class CoverArtCache : IDisposable
{
    private const int CoverWidth = 192;
    private const long MaxCoverBytes = 32L * 1024 * 1024;

    private readonly ConcurrentDictionary<string, Task<Bitmap?>> _covers =
        new(StringComparer.OrdinalIgnoreCase);

    private bool _disposed;

    /// <summary>Loads the decoded cover for a path, or null when it cannot be read.</summary>
    /// <param name="path">Cover image path, or null.</param>
    /// <returns>The shared bitmap, or null.</returns>
    public Task<Bitmap?> LoadAsync(string? path)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (string.IsNullOrWhiteSpace(path))
        {
            return Task.FromResult<Bitmap?>(null);
        }

        return _covers.GetOrAdd(path, static value => Task.Run(() => Load(value)));
    }

    /// <summary>Releases every decoded cover.</summary>
    public void Clear()
    {
        foreach (Task<Bitmap?> task in _covers.Values)
        {
            if (task.IsCompletedSuccessfully)
            {
                task.Result?.Dispose();
            }
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
            Log.Debug(exception, "Could not decode cover art {Path}", path);
            return null;
        }
    }
}
