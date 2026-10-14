using System;
using System.Runtime.InteropServices;

namespace XGrid.GpuGl;

/// <summary>
/// Minimal hidden-window WGL context for the CLI's headless GL backend
/// (no windowing package — platform interop only, per the single-dependency
/// rule: Silk.NET.OpenGL is the only NuGet package). Probe.c pattern: the
/// caller must run a behavioral self-test after creation; a version string
/// is never trusted.
/// </summary>
internal sealed class WglContext : IDisposable
{
    private const int WsOverlapped = 0x00000000;
    private const uint PfdDrawToWindow = 0x00000004;
    private const uint PfdSupportOpenGl = 0x00000020;
    private const uint PfdDoubleBuffer = 0x00000001;
    private const byte PfdTypeRgba = 0;
    private const byte PfdMainPlane = 0;

    private readonly IntPtr _hwnd;
    private readonly IntPtr _dc;
    private readonly IntPtr _hglrc;
    private readonly IntPtr _opengl32;
    private bool _disposed;

    private WglContext(IntPtr hwnd, IntPtr dc, IntPtr hglrc, IntPtr opengl32)
    {
        _hwnd = hwnd;
        _dc = dc;
        _hglrc = hglrc;
        _opengl32 = opengl32;
    }

    /// <summary>Creates the hidden window + WGL context on the calling thread.</summary>
    public static bool TryCreate(out WglContext? context, out string why)
    {
        context = null;
        why = "ok";
        if (!OperatingSystem.IsWindows())
        {
            why = "WGL context requires Windows";
            return false;
        }

        IntPtr instance = GetModuleHandleW(null);
        IntPtr hwnd = CreateWindowExW(
            0, "STATIC", "XGridGpuGl", WsOverlapped, 0, 0, 1, 1,
            IntPtr.Zero, IntPtr.Zero, instance, IntPtr.Zero);
        if (hwnd == IntPtr.Zero)
        {
            why = "CreateWindowExW failed";
            return false;
        }

        IntPtr dc = GetDC(hwnd);
        if (dc == IntPtr.Zero)
        {
            DestroyWindow(hwnd);
            why = "GetDC failed";
            return false;
        }

        var pfd = default(PixelFormatDescriptor);
        pfd.nSize = (ushort)Marshal.SizeOf<PixelFormatDescriptor>();
        pfd.nVersion = 1;
        pfd.dwFlags = PfdDrawToWindow | PfdSupportOpenGl | PfdDoubleBuffer;
        pfd.iPixelType = PfdTypeRgba;
        pfd.cColorBits = 32;
        pfd.cAlphaBits = 8;
        pfd.cDepthBits = 24;
        pfd.cStencilBits = 8;
        pfd.iLayerType = (sbyte)PfdMainPlane;
        int pixelFormat = ChoosePixelFormat(dc, ref pfd);
        if (pixelFormat == 0 || !SetPixelFormat(dc, pixelFormat, ref pfd))
        {
            _ = ReleaseDC(hwnd, dc);
            DestroyWindow(hwnd);
            why = "SetPixelFormat failed";
            return false;
        }

        IntPtr hglrc = wglCreateContext(dc);
        if (hglrc == IntPtr.Zero)
        {
            _ = ReleaseDC(hwnd, dc);
            DestroyWindow(hwnd);
            why = "wglCreateContext failed";
            return false;
        }

        if (!wglMakeCurrent(dc, hglrc))
        {
            wglDeleteContext(hglrc);
            _ = ReleaseDC(hwnd, dc);
            DestroyWindow(hwnd);
            why = "wglMakeCurrent failed";
            return false;
        }

        IntPtr opengl32 = GetModuleHandleW("opengl32.dll");
        context = new WglContext(hwnd, dc, hglrc, opengl32);
        return true;
    }

    /// <summary>wglGetProcAddress with an opengl32 GetProcAddress fallback.</summary>
    public IntPtr GetProcAddress(string name)
    {
        IntPtr address = wglGetProcAddress(name);
        long raw = address.ToInt64();
        if (address == IntPtr.Zero || (raw >= 1 && raw <= 3) || raw == -1)
        {
            return GetProcAddress(_opengl32, name);
        }

        return address;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _ = wglMakeCurrent(IntPtr.Zero, IntPtr.Zero);
        wglDeleteContext(_hglrc);
        _ = ReleaseDC(_hwnd, _dc);
        _ = DestroyWindow(_hwnd);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PixelFormatDescriptor
    {
        public ushort nSize;
        public ushort nVersion;
        public uint dwFlags;
        public byte iPixelType;
        public byte cColorBits;
        public byte cRedBits;
        public byte cRedShift;
        public byte cGreenBits;
        public byte cGreenShift;
        public byte cBlueBits;
        public byte cBlueShift;
        public byte cAlphaBits;
        public byte cAlphaShift;
        public byte cAccumBits;
        public byte cAccumRedBits;
        public byte cAccumGreenBits;
        public byte cAccumBlueBits;
        public byte cAccumAlphaBits;
        public byte cDepthBits;
        public byte cStencilBits;
        public byte cAuxBuffers;
        public sbyte iLayerType;
        public byte bReserved;
        public uint dwLayerMask;
        public uint dwVisibleMask;
        public uint dwDamageMask;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandleW(string? lpModuleName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowExW(
        uint dwExStyle, string lpClassName, string lpWindowName, uint dwStyle,
        int x, int y, int nWidth, int nHeight,
        IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hwnd);

    [DllImport("gdi32.dll")]
    private static extern int ChoosePixelFormat(IntPtr dc, ref PixelFormatDescriptor pixelFormat);

    [DllImport("gdi32.dll")]
    private static extern bool SetPixelFormat(IntPtr dc, int pixelFormat, ref PixelFormatDescriptor descriptor);

    [DllImport("opengl32.dll")]
    private static extern IntPtr wglCreateContext(IntPtr dc);

    [DllImport("opengl32.dll")]
    private static extern bool wglMakeCurrent(IntPtr dc, IntPtr hglrc);

    [DllImport("opengl32.dll")]
    private static extern bool wglDeleteContext(IntPtr hglrc);

    [DllImport("opengl32.dll", CharSet = CharSet.Ansi)]
    private static extern IntPtr wglGetProcAddress(string name);

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true)]
    private static extern IntPtr GetProcAddress(IntPtr module, string name);
}
