using System;

namespace XGrid.Core.Xemu;

/// <summary>
/// A bare child window that owns the display rectangle. The shell's UI
/// framework creates and positions it, and the core window is reparented into
/// it; the core then paints directly, with no frame copies.
/// </summary>
public static class XemuDisplaySurface
{
    private const uint WsChild = 0x40000000;
    private const uint WsVisible = 0x10000000;
    private const uint WsClipChildren = 0x02000000;
    private const uint WsClipSiblings = 0x04000000;

    /// <summary>
    /// Creates a child window filling its parent's client area. The UI
    /// framework resizes it afterwards.
    /// </summary>
    /// <param name="parent">Parent window handle.</param>
    /// <returns>The child window handle.</returns>
    public static IntPtr Create(IntPtr parent)
    {
        if (parent == IntPtr.Zero)
        {
            throw new ArgumentException("The parent window handle is null.", nameof(parent));
        }

        IntPtr handle = Win32Window.CreateWindowExW(
            0,
            "STATIC",
            string.Empty,
            WsChild | WsVisible | WsClipChildren | WsClipSiblings,
            0,
            0,
            1,
            1,
            parent,
            IntPtr.Zero,
            IntPtr.Zero,
            IntPtr.Zero);

        if (handle == IntPtr.Zero)
        {
            throw new XemuConfigurationException(
                "The display window could not be created (error "
                + System.Runtime.InteropServices.Marshal.GetLastPInvokeError().ToString(System.Globalization.CultureInfo.InvariantCulture)
                + ").");
        }

        return handle;
    }

    /// <summary>Destroys a display window created by <see cref="Create"/>.</summary>
    /// <param name="handle">The window handle.</param>
    public static void Destroy(IntPtr handle)
    {
        if (handle != IntPtr.Zero && Win32Window.IsWindow(handle))
        {
            _ = Win32Window.DestroyWindow(handle);
        }
    }
}
