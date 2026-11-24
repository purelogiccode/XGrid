using System;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using Serilog;

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
/// top-down BGRA pixels. Windows uses a named file mapping; Unix hosts use a
/// POSIX shared-memory object with the same layout. The latter backs the
/// headless run path, while the shell's integrated presentation path remains
/// Windows-only.
/// </remarks>
public sealed partial class XemuDisplaySink : IDisposable
{
    private const uint Magic = 0x53444758; // 'XGDS'
    private const int HeaderSize = 32;
    private const int MaxDimension = 16384;

    private const int OpenReadOnly = 0;
    private const int SeekEnd = 2;
    private const int ProtectionRead = 1;
    private const int MapShared = 1;

    private static readonly IntPtr MapFailed = new(-1);

    private readonly string _name;
    private readonly XemuDisplayFrame _frame = new();
    private MemoryMappedFile? _mapping;
    private MemoryMappedViewAccessor? _view;
    private IntPtr _unixAddress;
    private nuint _unixLength;
    private string? _unixName;
    private bool _disposed;

    /// <summary>
    /// Gets a value indicating whether the shell's integrated presentation path
    /// is available on this platform. The core publishes frames on Unix too,
    /// but the shell only presents them itself on Windows; elsewhere the normal
    /// UI keeps the core's own SDL window.
    /// </summary>
    public static bool IsSupported => OperatingSystem.IsWindows();

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
        string id = Guid.NewGuid().ToString("N");
        return OperatingSystem.IsWindows()
            ? "Local\\XGridDisplay-" + id
            // POSIX names are limited to 31 characters on macOS and need a
            // leading slash on every implementation.
            : "/xgrid-disp-" + id[..16];
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

        return OperatingSystem.IsWindows() ? TryReadWindows() : TryReadUnix();
    }

    /// <summary>
    /// Reads just the frame counter, without copying the pixels. Headless runs
    /// use this to measure presentation throughput; copying a full frame only
    /// to count it would compete with the core for memory bandwidth.
    /// </summary>
    /// <param name="counter">The core's latest frame counter.</param>
    /// <returns>True when a valid section header was read.</returns>
    public bool TryReadCounter(out ulong counter)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        counter = 0;

        if (!EnsureOpen())
        {
            return false;
        }

        return OperatingSystem.IsWindows()
            ? TryReadCounterWindows(out counter)
            : TryReadCounterUnix(out counter);
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
        ReleaseUnix(unlink: true);
    }

    private bool EnsureOpen()
    {
        if (OperatingSystem.IsWindows())
        {
            return EnsureOpenWindows();
        }

        // Windows uses a named mapping; every other host the core supports
        // publishes a POSIX shared-memory object.
        return EnsureOpenUnix();
    }

    private bool EnsureOpenWindows()
    {
        // Keep the platform check inline so the analyzer can see that the
        // Windows-only mapping APIs below are guarded.
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
        catch (FileNotFoundException exception)
        {
            Log.Debug(exception, "The display section has not been created yet: {Name}", _name);
            _mapping?.Dispose();
            _mapping = null;
            return false;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Log.Debug(exception, "The display section could not be opened: {Name}", _name);
            _mapping?.Dispose();
            _mapping = null;
            return false;
        }
    }

    private bool EnsureOpenUnix()
    {
        if (_unixAddress != IntPtr.Zero)
        {
            return true;
        }

        int descriptor = -1;
        try
        {
            descriptor = ShmOpen(_name, OpenReadOnly, 0);
            if (descriptor < 0)
            {
                return false;
            }

            long length = LSeek(descriptor, 0, SeekEnd);
            if (length < HeaderSize)
            {
                return false;
            }

            IntPtr address = MMap(
                IntPtr.Zero,
                (nuint)length,
                ProtectionRead,
                MapShared,
                descriptor,
                IntPtr.Zero);
            if (address == MapFailed)
            {
                return false;
            }

            _unixAddress = address;
            _unixLength = (nuint)length;
            _unixName = _name;
            return true;
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            Log.Debug(exception, "POSIX shared memory is not available on this host");
            return false;
        }
        finally
        {
            if (descriptor >= 0)
            {
                _ = Close(descriptor);
            }
        }
    }

    private bool TryReadCounterWindows(out ulong counter)
    {
        counter = 0;
        MemoryMappedViewAccessor view = _view!;
        uint magic = view.ReadUInt32(0);
        int width = (int)view.ReadUInt32(4);
        int height = (int)view.ReadUInt32(8);
        int stride = (int)view.ReadUInt32(12);
        counter = view.ReadUInt64(16);

        return magic == Magic
               && width is > 0 and <= MaxDimension
               && height is > 0 and <= MaxDimension
               && stride >= (long)width * 4;
    }

    private bool TryReadCounterUnix(out ulong counter)
    {
        counter = 0;
        IntPtr address = _unixAddress;
        uint magic = unchecked((uint)Marshal.ReadInt32(address, 0));
        int width = Marshal.ReadInt32(address, 4);
        int height = Marshal.ReadInt32(address, 8);
        int stride = Marshal.ReadInt32(address, 12);
        counter = unchecked((ulong)Marshal.ReadInt64(address, 16));

        return magic == Magic
               && width is > 0 and <= MaxDimension
               && height is > 0 and <= MaxDimension
               && stride >= (long)width * 4;
    }

    private bool TryReadWindows()
    {
        MemoryMappedViewAccessor view = _view!;
        uint magic = view.ReadUInt32(0);
        int width = (int)view.ReadUInt32(4);
        int height = (int)view.ReadUInt32(8);
        int stride = (int)view.ReadUInt32(12);
        ulong counter = view.ReadUInt64(16);

        if (magic != Magic
            || width is <= 0 or > MaxDimension
            || height is <= 0 or > MaxDimension
            || stride < (long)width * 4)
        {
            return false;
        }

        long length = (long)stride * height;
        if (length is <= 0 or > int.MaxValue)
        {
            return false;
        }

        if (HeaderSize + length > view.Capacity)
        {
            return false;
        }

        if (counter == _frame.Counter
            && width == _frame.Width
            && height == _frame.Height
            && _frame.Pixels.Length > 0)
        {
            // No new frame; keep the previous one so the caller can skip work.
            return true;
        }

        if (_frame.Pixels.Length < length)
        {
            _frame.Pixels = new byte[length];
        }

        if (view.ReadArray(HeaderSize, _frame.Pixels, 0, (int)length) != (int)length)
        {
            return false;
        }

        _frame.Width = width;
        _frame.Height = height;
        _frame.Counter = counter;
        return true;
    }

    private bool TryReadUnix()
    {
        IntPtr address = _unixAddress;
        uint magic = unchecked((uint)Marshal.ReadInt32(address, 0));
        int width = Marshal.ReadInt32(address, 4);
        int height = Marshal.ReadInt32(address, 8);
        int stride = Marshal.ReadInt32(address, 12);
        ulong counter = unchecked((ulong)Marshal.ReadInt64(address, 16));

        if (magic != Magic
            || width is <= 0 or > MaxDimension
            || height is <= 0 or > MaxDimension
            || stride < (long)width * 4)
        {
            return false;
        }

        long length = (long)stride * height;
        if (length is <= 0 or > int.MaxValue)
        {
            return false;
        }

        if (HeaderSize + length > (long)_unixLength)
        {
            // The core recreated the object with a larger geometry; remap on the
            // next poll instead of reading past the mapping.
            ReleaseUnix(unlink: false);
            return false;
        }

        if (counter == _frame.Counter
            && width == _frame.Width
            && height == _frame.Height
            && _frame.Pixels.Length > 0)
        {
            return true;
        }

        if (_frame.Pixels.Length < length)
        {
            _frame.Pixels = new byte[length];
        }

        Marshal.Copy(IntPtr.Add(address, HeaderSize), _frame.Pixels, 0, (int)length);
        _frame.Width = width;
        _frame.Height = height;
        _frame.Counter = counter;
        return true;
    }

    private void ReleaseUnix(bool unlink)
    {
        if (_unixAddress != IntPtr.Zero)
        {
            _ = Munmap(_unixAddress, _unixLength);
            _unixAddress = IntPtr.Zero;
            _unixLength = 0;
        }

        if (unlink && _unixName is { Length: > 0 } name)
        {
            _ = ShmUnlink(name);
        }

        _unixName = null;
    }

    [LibraryImport("libc", EntryPoint = "shm_open", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int ShmOpen(string name, int flags, uint mode);

    [LibraryImport("libc", EntryPoint = "shm_unlink", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int ShmUnlink(string name);

    [LibraryImport("libc", EntryPoint = "lseek", SetLastError = true)]
    private static partial long LSeek(int descriptor, long offset, int origin);

    [LibraryImport("libc", EntryPoint = "mmap", SetLastError = true)]
    private static partial IntPtr MMap(
        IntPtr address,
        nuint length,
        int protection,
        int flags,
        int descriptor,
        IntPtr offset);

    [LibraryImport("libc", EntryPoint = "munmap", SetLastError = true)]
    private static partial int Munmap(IntPtr address, nuint length);

    [LibraryImport("libc", EntryPoint = "close", SetLastError = true)]
    private static partial int Close(int descriptor);
}
