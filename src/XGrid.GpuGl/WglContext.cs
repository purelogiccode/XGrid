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
internal sealed partial class WglContext : IDisposable
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
        IntPtr address = WglGetProcAddress(name);
        long raw = address.ToInt64();
        if (address == IntPtr.Zero || (raw >= 1 && raw <= 3) || raw == -1)
        {
            return GetProcAddress(_opengl32, name);
        }

        return address;
    }

    /// <summary>Gets whether the window has been made visible (window mode).</summary>
    public bool IsVisible { get; private set; }

    /// <summary>
    /// Makes the hidden window visible for live presentation (opt-in
    /// <c>--window</c>): restyles it to a resizable overlapped window (the
    /// pixel format is set once and is kept), sizes the client area to the
    /// frame, centers it, shows it, and disables vsync
    /// (<c>wglSwapIntervalEXT(0)</c>; guest pacing is PIT/flip-stall, never
    /// host vsync). Never throws; returns false with a why when the desktop
    /// refuses (headless/session-0 degrades like a probe failure and the
    /// caller continues headless).
    /// </summary>
    public bool TryShowWindow(uint clientWidth, uint clientHeight, out string why)
    {
        why = "ok";
        if (IsVisible)
        {
            return true;
        }

        const int gwlStyle = -16;
        const uint wsOverlappedWindow = 0x00CF0000u;
        const uint swpNoZOrder = 0x0004u;
        const uint swpNoActivate = 0x0010u;
        const int swShow = 5;
        _ = SetWindowLongPtrW(_hwnd, gwlStyle, new IntPtr(unchecked(wsOverlappedWindow)));
        var rect = default(Rect);
        rect.Right = (int)clientWidth;
        rect.Bottom = (int)clientHeight;
        if (AdjustWindowRect(ref rect, wsOverlappedWindow, 0) == 0)
        {
            why = "AdjustWindowRect failed";
            return false;
        }

        int outerWidth = rect.Right - rect.Left;
        int outerHeight = rect.Bottom - rect.Top;
        int screenWidth = GetSystemMetrics(0);
        int screenHeight = GetSystemMetrics(1);
        int x = Math.Max(0, (screenWidth - outerWidth) / 2);
        int y = Math.Max(0, (screenHeight - outerHeight) / 2);
        if (SetWindowPos(_hwnd, IntPtr.Zero, x, y, outerWidth, outerHeight, swpNoZOrder | swpNoActivate) == 0)
        {
            why = "SetWindowPos failed";
            return false;
        }

        _ = ShowWindow(_hwnd, swShow);
        _ = UpdateWindow(_hwnd);

        IntPtr swapInterval = GetProcAddress("wglSwapIntervalEXT");
        if (swapInterval != IntPtr.Zero)
        {
            unsafe
            {
                ((delegate* unmanaged[Stdcall]<int, int>)swapInterval)(0);
            }
        }

        IsVisible = true;
        return true;
    }

    /// <summary>Presents the window's back buffer (window mode only).</summary>
    public void SwapBuffers()
    {
        _ = SwapBuffersNative(_dc);
    }

    /// <summary>Drains the window message queue so the window stays responsive (window mode only).</summary>
    public void PumpMessages()
    {
        if (_disposed)
        {
            return;
        }

        while (PeekMessageW(out Msg msg, IntPtr.Zero, 0u, 0u, PmRemove) != 0)
        {
            _ = TranslateMessage(ref msg);
            _ = DispatchMessageW(ref msg);
        }
    }

    /// <summary>Sets the window title (window mode only).</summary>
    public void SetTitle(string title)
    {
        _ = SetWindowTextW(_hwnd, title);
    }

    /// <summary>Gets the current client-area size in pixels (window mode only).</summary>
    public bool TryGetClientSize(out uint width, out uint height)
    {
        if (GetClientRect(_hwnd, out Rect rect) != 0)
        {
            width = (uint)Math.Max(0, rect.Right - rect.Left);
            height = (uint)Math.Max(0, rect.Bottom - rect.Top);
            return true;
        }

        width = 0;
        height = 0;
        return false;
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

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr GetModuleHandleW(string? lpModuleName);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr CreateWindowExW(
        uint dwExStyle, string lpClassName, string lpWindowName, uint dwStyle,
        int x, int y, int nWidth, int nHeight,
        IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [LibraryImport("user32.dll")]
    private static partial IntPtr GetDC(IntPtr hwnd);

    [LibraryImport("user32.dll")]
    private static partial int ReleaseDC(IntPtr hwnd, IntPtr dc);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hwnd);

    [LibraryImport("gdi32.dll")]
    private static partial int ChoosePixelFormat(IntPtr dc, ref PixelFormatDescriptor pixelFormat);

    [DllImport("gdi32.dll")]
    private static extern bool SetPixelFormat(IntPtr dc, int pixelFormat, ref PixelFormatDescriptor descriptor);

    [LibraryImport("opengl32.dll")]
    private static partial IntPtr wglCreateContext(IntPtr dc);

    [DllImport("opengl32.dll")]
    private static extern bool wglMakeCurrent(IntPtr dc, IntPtr hglrc);

    [DllImport("opengl32.dll")]
    private static extern bool wglDeleteContext(IntPtr hglrc);

    [LibraryImport("opengl32.dll", EntryPoint = "wglGetProcAddress", StringMarshalling = StringMarshalling.Custom,
        StringMarshallingCustomType = typeof(System.Runtime.InteropServices.Marshalling.AnsiStringMarshaller))]
    private static partial IntPtr WglGetProcAddress(string name);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Custom,
        StringMarshallingCustomType = typeof(System.Runtime.InteropServices.Marshalling.AnsiStringMarshaller))]
    private static partial IntPtr GetProcAddress(IntPtr module, string name);

    private const uint PmRemove = 0x0001u;

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg
    {
        public IntPtr Hwnd;
        public uint Message;
        public IntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public int PtX;
        public int PtY;
    }

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static partial IntPtr SetWindowLongPtrW(IntPtr hwnd, int index, IntPtr value);

    [LibraryImport("user32.dll")]
    private static partial int AdjustWindowRect(ref Rect rect, uint style, int menu);

    [LibraryImport("user32.dll")]
    private static partial int SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height,
        uint flags);

    [LibraryImport("user32.dll")]
    private static partial int ShowWindow(IntPtr hwnd, int cmdShow);

    [LibraryImport("user32.dll")]
    private static partial int UpdateWindow(IntPtr hwnd);

    [LibraryImport("user32.dll")]
    private static partial int GetSystemMetrics(int index);

    [LibraryImport("gdi32.dll", EntryPoint = "SwapBuffers")]
    private static partial int SwapBuffersNative(IntPtr dc);

    [LibraryImport("user32.dll", EntryPoint = "PeekMessageW")]
    private static partial int PeekMessageW(out Msg msg, IntPtr hwnd, uint filterMin, uint filterMax, uint removeMsg);

    [LibraryImport("user32.dll", EntryPoint = "TranslateMessage")]
    private static partial int TranslateMessage(ref Msg msg);

    [LibraryImport("user32.dll", EntryPoint = "DispatchMessageW")]
    private static partial IntPtr DispatchMessageW(ref Msg msg);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowTextW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int SetWindowTextW(IntPtr hwnd, string text);

    [LibraryImport("user32.dll")]
    private static partial int GetClientRect(IntPtr hwnd, out Rect rect);
}
