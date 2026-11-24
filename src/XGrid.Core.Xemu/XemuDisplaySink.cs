using System;
using System.IO;
using System.IO.MemoryMappedFiles;

namespace XGrid.Core.Xemu;

/// <summary>One frame read from the core's shared display section.</summary>
/// <remarks>The pixel buffer is reused between reads; copy it before the next call.</remarks>
public sealed class XemuDisplayFrame
{
    /// <summary>Gets the frame width in pixels.</summary>
    public int Width { get; internal set; }

    /// <summary>Gets the frame height in pixels.</summary>
    public int Height { get; internal set; }

    /// <summary>Gets the BGRA pixel data, top row first.</summary>
    public byte[] Pixels { get; internal set; } = [];

    /// <summary>Gets the core's frame counter for this image.</summary>
    public ulong Counter { get; internal set; }
}

/// <summary>
/// Reads the frames the core publishes when it runs with the host display sink
/// (<c>XGRID_DISPLAY_SHM</c>). The section is re-opened automatically when the
/// core recreates it after a geometry change.
/// </summary>
/// <remarks>
/// Layout is defined in <c>xemu/xgrid-core/display/xgrid-display.h</c>: a
/// 32-byte header (magic, width, height, stride, frame counter) followed by
/// top-down BGRA pixels.
/// </remarks>
public sealed class XemuDisplaySink : IDisposable
{
    private const uint Magic = 0x53444758; // 'XGDS'
    private const int HeaderSize = 32;

    private readonly string _name;
    private readonly XemuDisplayFrame _frame = new();
    private MemoryMappedFile? _mapping;
    private MemoryMappedViewAccessor? _view;
    private bool _disposed;

    /// <summary>Creates a reader for a named section.</summary>
    /// <param name="name">Section name the core was started with.</param>
    public XemuDisplaySink(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _name = name;
    }

    /// <summary>Creates a section name for one launch.</summary>
    /// <returns>A session-local name the core and shell agree on.</returns>
    public static string CreateName()
    {
        return "Local\\XGridDisplay-" + Guid.NewGuid().ToString("N");
    }

    /// <summary>
    /// Reads the latest frame. Returns false while the core has not published
    /// one yet (or between a resize and the next frame).
    /// </summary>
    /// <param name="frame">The frame, valid until the next call.</param>
    /// <returns>True when a complete frame was read.</returns>
    public bool TryRead(out XemuDisplayFrame frame)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        frame = _frame;

        if (!EnsureOpen())
        {
            return false;
        }

        MemoryMappedViewAccessor view = _view!;
        uint magic = view.ReadUInt32(0);
        int width = (int)view.ReadUInt32(4);
        int height = (int)view.ReadUInt32(8);
        int stride = (int)view.ReadUInt32(12);
        ulong counter = view.ReadUInt64(16);

        if (magic != Magic || width <= 0 || height <= 0 || stride < width * 4)
        {
            return false;
        }

        if (counter == _frame.Counter && width == _frame.Width && height == _frame.Height && _frame.Pixels.Length > 0)
        {
            // No new frame; keep the previous one so the caller can skip work.
            return true;
        }

        int length = stride * height;
        if (_frame.Pixels.Length < length)
        {
            _frame.Pixels = new byte[length];
        }

        if (view.ReadArray(HeaderSize, _frame.Pixels, 0, length) != length)
        {
            return false;
        }

        _frame.Width = width;
        _frame.Height = height;
        _frame.Counter = counter;
        return true;
    }

    /// <summary>Releases the mapping.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _view?.Dispose();
        _view = null;
        _mapping?.Dispose();
        _mapping = null;
    }

    private bool EnsureOpen()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        if (_view is not null)
        {
            return true;
        }

        try
        {
            _mapping = MemoryMappedFile.OpenExisting(_name, MemoryMappedFileRights.Read);
            _view = _mapping.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
            return true;
        }
        catch (FileNotFoundException)
        {
            _mapping?.Dispose();
            _mapping = null;
            return false;
        }
    }
}
