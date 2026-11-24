using System;
using System.IO.MemoryMappedFiles;

namespace XGrid.Core.Xemu;

/// <summary>
/// Publishes the shell's pad state to the core. The core reads port 1 from this
/// section when it is started with <c>XGRID_INPUT_SHM</c>, which is the only
/// input path that works while the shell owns presentation (no SDL window).
/// </summary>
/// <remarks>
/// Layout is defined in <c>xemu/xgrid-core/input/xgrid-input.h</c>: magic,
/// xemu's XID button mask, four stick axes, two triggers and a sequence word.
/// </remarks>
public sealed class XemuInputSink : IDisposable
{
    private const uint Magic = 0x49444758; // 'XGDI'
    private const int Size = 32;

    private readonly string _name;
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
    /// <param name="buttons">xemu's XID button mask (see the core header).</param>
    /// <param name="lx">Left stick X, -32768..32767.</param>
    /// <param name="ly">Left stick Y, -32768..32767.</param>
    /// <param name="rx">Right stick X, -32768..32767.</param>
    /// <param name="ry">Right stick Y, -32768..32767.</param>
    /// <param name="lt">Left trigger, 0..32767.</param>
    /// <param name="rt">Right trigger, 0..32767.</param>
    public void Write(uint buttons, int lx, int ly, int rx, int ry, int lt, int rt)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!EnsureOpen())
        {
            return;
        }

        MemoryMappedViewAccessor view = _view!;
        view.Write(0, Magic);
        view.Write(4, buttons);
        view.Write(8, lx);
        view.Write(12, ly);
        view.Write(16, rx);
        view.Write(20, ry);
        view.Write(24, lt);
        view.Write(28, rt);
        // The core reads all eight words at once.
        view.Flush();
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
