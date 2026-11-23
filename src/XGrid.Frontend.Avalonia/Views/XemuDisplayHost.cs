using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using XGrid.Core.Xemu;
using XGrid.Frontend.Avalonia.Services;

namespace XGrid.Frontend.Avalonia.Views;

/// <summary>Reports the display area's native handle and its size in physical pixels.</summary>
/// <param name="handle">Host child window handle.</param>
/// <param name="width">Width in physical pixels.</param>
/// <param name="height">Height in physical pixels.</param>
public sealed class CoreDisplayGeometryEventArgs(IntPtr handle, int width, int height) : EventArgs
{
    /// <summary>Gets the host child window handle.</summary>
    public IntPtr Handle { get; } = handle;

    /// <summary>Gets the width in physical pixels.</summary>
    public int Width { get; } = width;

    /// <summary>Gets the height in physical pixels.</summary>
    public int Height { get; } = height;
}

/// <summary>
/// Native display surface for the xemu core. The control owns a bare child
/// window; the core window is reparented into it, so the core paints with its
/// own swapchain and the shell never touches a frame.
/// </summary>
public sealed class XemuDisplayHost : NativeControlHost
{
    private IntPtr _handle;
    private int _lastWidth;
    private int _lastHeight;

    /// <summary>Raised when the native handle or its size changes.</summary>
    public event EventHandler<CoreDisplayGeometryEventArgs>? GeometryChanged;

    /// <summary>Raised when the native surface goes away.</summary>
    public event EventHandler? GeometryLost;

    /// <summary>
    /// Publishes the current handle and size. The view calls this after it has
    /// subscribed, so an initial geometry that was recorded while the surface
    /// was being created is never lost to event ordering.
    /// </summary>
    public void PublishGeometry()
    {
        Publish(force: true);
    }

    /// <summary>Creates the control and keeps the published geometry current.</summary>
    public XemuDisplayHost()
    {
        AttachedToVisualTree += (_, _) => Publish(force: true);
        LayoutUpdated += (_, _) => Publish(force: false);
    }

    /// <inheritdoc />
    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        _handle = XemuDisplaySurface.Create(parent.Handle);
        FrontendTrace.Write($"display host created hwnd=0x{_handle.ToInt64():X} parent=0x{parent.Handle.ToInt64():X}");
        Publish(force: true);
        return new PlatformHandle(_handle, "HWND");
    }

    /// <inheritdoc />
    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        FrontendTrace.Write($"display host destroyed hwnd=0x{control.Handle.ToInt64():X}");
        _handle = IntPtr.Zero;
        _lastWidth = 0;
        _lastHeight = 0;
        GeometryLost?.Invoke(this, EventArgs.Empty);
        XemuDisplaySurface.Destroy(control.Handle);
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == BoundsProperty)
        {
            Publish(force: false);
        }
    }

    private void Publish(bool force)
    {
        if (_handle == IntPtr.Zero)
        {
            return;
        }

        double scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
        int width = Math.Max(1, (int)Math.Round(Bounds.Width * scaling));
        int height = Math.Max(1, (int)Math.Round(Bounds.Height * scaling));
        if (!force && width == _lastWidth && height == _lastHeight)
        {
            return;
        }

        _lastWidth = width;
        _lastHeight = height;
        GeometryChanged?.Invoke(this, new CoreDisplayGeometryEventArgs(_handle, width, height));
    }
}
