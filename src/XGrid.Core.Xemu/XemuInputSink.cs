using System;
using System.IO.MemoryMappedFiles;

namespace XGrid.Core.Xemu;

/// <summary>
/// Publishes the shell's pad states to the core. The core reads one record per
/// Xbox port from this section when it is started with <c>XGRID_INPUT_SHM</c>,
/// which is the only input path that works while the shell owns presentation
/// (no SDL window).
/// </summary>
/// <remarks>
/// Layout is defined in <c>xemu/xgrid-core/input/xgrid-input.h</c>: a 16-byte
/// header (magic, version, pad count, reserved) followed by four 32-byte pad
/// records (button mask, four stick axes, two triggers and a sequence word).
/// </remarks>
public sealed class XemuInputSink : IDisposable
{
    /// <summary>The number of pads the shell publishes, one per Xbox port.</summary>
    public const int PadCount = 4;

    private const uint Magic = 0x49444758; // 'XGDI'
    private const uint Version = 2;
    private const uint HeaderSize = 16;
    private const uint PadStride = 32;
    private const int Size = (int)(HeaderSize + (PadStride * PadCount));

    private readonly string _name;
    private readonly uint[] _sequence = new uint[PadCount];
    private MemoryMappedFile? _mapping;
    private MemoryMappedViewAccessor? _view;
    private bool _disposed;

    /// <summary>Creates a writer for a named section.</summary>
    /// <param name="name">Section name the core was started with.</param>
    public XemuInputSink(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _name = name;
    }

    /// <summary>Creates a section name for one launch.</summary>
    /// <returns>A session-local name the core and shell agree on.</returns>
    public static string CreateName()
    {
        return "Local\\XGridInput-" + Guid.NewGuid().ToString("N");
    }

    /// <summary>Publishes one pad state.</summary>
    /// <param name="pad">Xbox port index, 0 to <see cref="PadCount"/> - 1.</param>
    /// <param name="buttons">xemu's XID button mask (see the core header).</param>
    /// <param name="lx">Left stick X, -32768..32767.</param>
    /// <param name="ly">Left stick Y, -32768..32767.</param>
    /// <param name="rx">Right stick X, -32768..32767.</param>
    /// <param name="ry">Right stick Y, -32768..32767.</param>
    /// <param name="lt">Left trigger, 0..32767.</param>
    /// <param name="rt">Right trigger, 0..32767.</param>
    public void Write(int pad, uint buttons, int lx, int ly, int rx, int ry, int lt, int rt)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentOutOfRangeException.ThrowIfNegative(pad);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(pad, PadCount);

        if (!EnsureOpen())
        {
            return;
        }

        MemoryMappedViewAccessor view = _view!;
        uint offset = HeaderSize + ((uint)pad * PadStride);
        _sequence[pad]++;
        view.Write(0, Magic);
        view.Write(4, Version);
        view.Write(8, (uint)PadCount);
        view.Write(12, 0u);
        view.Write(offset, buttons);
        view.Write(offset + 4, lx);
        view.Write(offset + 8, ly);
        view.Write(offset + 12, rx);
        view.Write(offset + 16, ry);
        view.Write(offset + 20, lt);
        view.Write(offset + 24, rt);
        view.Write(offset + 28, _sequence[pad]);
        // The section is pagefile-backed and shared with the core, so the
        // writes are already coherent; FlushViewOfFile would only add latency.
    }

    /// <summary>Publishes a neutral state for every pad.</summary>
    public void WriteNeutral()
    {
        for (int pad = 0; pad < PadCount; pad++)
        {
            Write(pad, 0, 0, 0, 0, 0, 0, 0);
        }
    }

    /// <summary>Releases the section.</summary>
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

        _mapping = MemoryMappedFile.CreateNew(_name, Size);
        _view = _mapping.CreateViewAccessor(0, Size, MemoryMappedFileAccess.ReadWrite);
        return true;
    }
}
