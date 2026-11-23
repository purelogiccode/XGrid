using System;
using System.Runtime.InteropServices;

namespace XGrid.Core.Xemu;

/// <summary>
/// Embeds the core's window inside a shell window: reparents it, strips the
/// top-level chrome, and keeps it filling the host's client area.
/// </summary>
/// <remarks>
/// Reparenting (rather than copying frames) is the whole performance argument:
/// the core keeps its own OpenGL context and swapchain, and the shell adds no
/// per-frame work at all. The embedded window is a real child, so the core
/// receives the keyboard when the user clicks the display, with no key
/// translation layer.
/// </remarks>
public sealed class XemuWindowEmbedder : IDisposable
{
    private IntPtr _host;
    private IntPtr _child;
    private int _width;
    private int _height;
    private bool _disposed;

    /// <summary>Gets a value indicating whether a core window is currently hosted.</summary>
    public bool IsEmbedded => _child != IntPtr.Zero;

    /// <summary>
    /// Reparents <paramref name="child"/> into <paramref name="host"/> and
    /// makes it fill the host's client area.
    /// </summary>
    /// <param name="host">Shell window or child control that owns the display area.</param>
    /// <param name="child">The core's top-level window.</param>
    /// <param name="widthPx">Initial width in physical pixels.</param>
    /// <param name="heightPx">Initial height in physical pixels.</param>
    public void Embed(IntPtr host, IntPtr child, int widthPx, int heightPx)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (host == IntPtr.Zero)
        {
            throw new ArgumentException("The host window handle is null.", nameof(host));
        }

        if (child == IntPtr.Zero)
        {
            throw new ArgumentException("The core window handle is null.", nameof(child));
        }

        if (_child != IntPtr.Zero)
        {
            Detach();
        }

        // A child window cannot own a menu; clear it before the style change so
        // the client area is the whole window.
        _ = Win32Window.SetMenu(child, IntPtr.Zero);

        uint style = unchecked((uint)Win32Window.GetWindowLongW(child, Win32Window.GwlStyle));
        style &= ~(Win32Window.WsPopup
                   | Win32Window.WsCaption
                   | Win32Window.WsThickFrame
                   | Win32Window.WsMinimizeBox
                   | Win32Window.WsMaximizeBox
                   | Win32Window.WsSysMenu
                   | Win32Window.WsBorder
                   | Win32Window.WsDlgFrame);
        style |= Win32Window.WsChild | Win32Window.WsVisible | Win32Window.WsClipChildren | Win32Window.WsClipSiblings;
        _ = Win32Window.SetWindowLongW(child, Win32Window.GwlStyle, unchecked((int)style));

        uint exStyle = unchecked((uint)Win32Window.GetWindowLongW(child, Win32Window.GwlExStyle));
        exStyle &= ~(Win32Window.WsExAppWindow
                     | Win32Window.WsExWindowEdge
                     | Win32Window.WsExClientEdge
                     | Win32Window.WsExDlgModalFrame
                     | Win32Window.WsExToolWindow);
        _ = Win32Window.SetWindowLongW(child, Win32Window.GwlExStyle, unchecked((int)exStyle));

        // SetParent answers the previous parent, which is null for a top-level
        // window, so the result only means failure when the last error is set.
        // The authoritative check is the parent afterwards.
        Marshal.SetLastPInvokeError(0);
        _ = Win32Window.SetParent(child, host);
        if (Win32Window.GetParent(child) != host)
        {
            throw new XemuConfigurationException(
                "The core window could not be reparented into the shell (error "
                + Marshal.GetLastPInvokeError().ToString(System.Globalization.CultureInfo.InvariantCulture)
                + ").");
        }

        _host = host;
        _child = child;
        _width = Math.Max(1, widthPx);
        _height = Math.Max(1, heightPx);
        ApplyGeometry();
        _ = Win32Window.ShowWindow(child, Win32Window.SwShow);
        _ = Win32Window.SetFocus(child);
    }

    /// <summary>Resizes the embedded window to the display rectangle.</summary>
    /// <param name="widthPx">Width in physical pixels.</param>
    /// <param name="heightPx">Height in physical pixels.</param>
    public void Resize(int widthPx, int heightPx)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_child == IntPtr.Zero)
        {
            return;
        }

        int width = Math.Max(1, widthPx);
        int height = Math.Max(1, heightPx);
        if (width == _width && height == _height)
        {
            return;
        }

        _width = width;
        _height = height;
        ApplyGeometry();
    }

    /// <summary>
    /// Returns the core window to the desktop. Called before the core exits so
    /// a window that outlives the shell is never a child of a destroyed host.
    /// </summary>
    public void Detach()
    {
        if (_child == IntPtr.Zero)
        {
            return;
        }

        IntPtr child = _child;
        _child = IntPtr.Zero;
        _host = IntPtr.Zero;
        if (Win32Window.IsWindow(child))
        {
            _ = Win32Window.SetParent(child, IntPtr.Zero);
        }
    }

    /// <summary>Releases the embedding without closing the core window.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Detach();
    }

    private void ApplyGeometry()
    {
        _ = Win32Window.SetWindowPos(
            _child,
            IntPtr.Zero,
            0,
            0,
            _width,
            _height,
            Win32Window.SwpNoZOrder | Win32Window.SwpNoActivate | Win32Window.SwpFrameChanged | Win32Window.SwpShowWindow);
    }
}
