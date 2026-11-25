using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace XGrid.Frontend.Avalonia.Services;

/// <summary>
/// Captures a window's client area from the Windows desktop. The embedded core
/// window paints with its own swapchain, which a visual-tree render cannot see;
/// a screen capture includes that native child exactly as DWM composites it.
/// </summary>
internal static partial class WindowCapture
{
    private const uint SrcCopy = 0x00CC0020;
    private const uint CaptureBlt = 0x40000000;
    private const uint DibRgbColors = 0;
    private const int BiRgb = 0;

    /// <summary>Gets a value indicating whether desktop capture is available.</summary>
    public static bool IsSupported => OperatingSystem.IsWindows();

    /// <summary>
    /// Captures the client area of a window into a top-down BGRA bitmap.
    /// </summary>
    /// <param name="window">Top-level window handle.</param>
    /// <param name="size">Client size in physical pixels.</param>
    /// <returns>The capture, or null when the desktop could not be read.</returns>
    public static WriteableBitmap? CaptureClient(IntPtr window, PixelSize size)
    {
        if (!OperatingSystem.IsWindows() || window == IntPtr.Zero
            || size.Width <= 0 || size.Height <= 0)
        {
            return null;
        }

        IntPtr screen = IntPtr.Zero;
        IntPtr memory = IntPtr.Zero;
        IntPtr dib = IntPtr.Zero;
        IntPtr previous = IntPtr.Zero;
        try
        {
            screen = GetDC(IntPtr.Zero);
            if (screen == IntPtr.Zero)
            {
                return null;
            }

            memory = CreateCompatibleDC(screen);
            if (memory == IntPtr.Zero)
            {
                return null;
            }

            var info = new BitmapInfo
            {
                Header = new BitmapInfoHeader
                {
                    Size = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
                    Width = size.Width,
                    // A negative height asks for top-down rows, the order the
                    // shell bitmap and PNG encoder use.
                    Height = -size.Height,
                    Planes = 1,
                    BitCount = 32,
                    Compression = BiRgb,
                },
            };

            dib = CreateDIBSection(memory, ref info, DibRgbColors, out IntPtr bits, IntPtr.Zero, 0);
            if (dib == IntPtr.Zero || bits == IntPtr.Zero)
            {
                return null;
            }

            previous = SelectObject(memory, dib);
            var origin = default(Point);
            if (!ClientToScreen(window, ref origin))
            {
                return null;
            }

            if (!BitBlt(
                    memory,
                    0,
                    0,
                    size.Width,
                    size.Height,
                    screen,
                    origin.X,
                    origin.Y,
                    SrcCopy | CaptureBlt))
            {
                return null;
            }

            var capture = new WriteableBitmap(
                size,
                new Vector(96, 96),
                PixelFormat.Bgra8888,
                AlphaFormat.Opaque);
            try
            {
                int rowBytes = size.Width * 4;
                byte[] row = new byte[rowBytes];
                using (ILockedFramebuffer buffer = capture.Lock())
                {
                    for (int y = 0; y < size.Height; y++)
                    {
                        Marshal.Copy(IntPtr.Add(bits, y * rowBytes), row, 0, rowBytes);
                        Marshal.Copy(row, 0, IntPtr.Add(buffer.Address, y * buffer.RowBytes), rowBytes);
                    }
                }

                return capture;
            }
            catch
            {
                capture.Dispose();
                throw;
            }
        }
        finally
        {
            if (previous != IntPtr.Zero && memory != IntPtr.Zero)
            {
                _ = SelectObject(memory, previous);
            }

            if (dib != IntPtr.Zero)
            {
                _ = DeleteObject(dib);
            }

            if (memory != IntPtr.Zero)
            {
                _ = DeleteDC(memory);
            }

            if (screen != IntPtr.Zero)
            {
                _ = ReleaseDC(IntPtr.Zero, screen);
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ClrUsed;
        public uint ClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public BitmapInfoHeader Header;
        public uint Colors;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial IntPtr GetDC(IntPtr window);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial int ReleaseDC(IntPtr window, IntPtr deviceContext);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ClientToScreen(IntPtr window, ref Point point);

    [LibraryImport("gdi32.dll", SetLastError = true)]
    private static partial IntPtr CreateCompatibleDC(IntPtr deviceContext);

    [LibraryImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteDC(IntPtr deviceContext);

    [LibraryImport("gdi32.dll", SetLastError = true)]
    private static partial IntPtr CreateDIBSection(
        IntPtr deviceContext,
        ref BitmapInfo info,
        uint usage,
        out IntPtr bits,
        IntPtr section,
        uint offset);

    [LibraryImport("gdi32.dll", SetLastError = true)]
    private static partial IntPtr SelectObject(IntPtr deviceContext, IntPtr value);

    [LibraryImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteObject(IntPtr value);

    [LibraryImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool BitBlt(
        IntPtr destination,
        int x,
        int y,
        int width,
        int height,
        IntPtr source,
        int sourceX,
        int sourceY,
        uint operation);
}
