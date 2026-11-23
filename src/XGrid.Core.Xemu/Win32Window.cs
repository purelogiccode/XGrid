using System;
using System.Runtime.InteropServices;

namespace XGrid.Core.Xemu;

/// <summary>
/// The Win32 calls needed to host a foreign top-level window inside the shell:
/// reparenting, style surgery, geometry and message delivery.
/// </summary>
/// <remarks>
/// Host-only interop (a window manager facility), inventoried in
/// <c>docs/porting-policy.md</c> section 2. No emulator semantics live here.
/// </remarks>
internal static partial class Win32Window
{
    internal const int GwlStyle = -16;
    internal const int GwlExStyle = -20;

    internal const uint WsPopup = 0x80000000;
    internal const uint WsChild = 0x40000000;
    internal const uint WsVisible = 0x10000000;
    internal const uint WsClipSiblings = 0x04000000;
    internal const uint WsClipChildren = 0x02000000;
    internal const uint WsBorder = 0x00800000;
    internal const uint WsDlgFrame = 0x00400000;
    internal const uint WsCaption = 0x00C00000;
    internal const uint WsSysMenu = 0x00080000;
    internal const uint WsThickFrame = 0x00040000;
    internal const uint WsMinimizeBox = 0x00020000;
    internal const uint WsMaximizeBox = 0x00010000;

    internal const uint WsExAppWindow = 0x00040000;
    internal const uint WsExWindowEdge = 0x00000100;
    internal const uint WsExClientEdge = 0x00000200;
    internal const uint WsExDlgModalFrame = 0x00000001;
    internal const uint WsExToolWindow = 0x00000080;

    internal const uint SwpNoZOrder = 0x0004;
    internal const uint SwpNoActivate = 0x0010;
    internal const uint SwpFrameChanged = 0x0020;
    internal const uint SwpShowWindow = 0x0040;

    internal const int SwShow = 5;

    internal const int SwHide = 0;

    internal const uint WmClose = 0x0010;

    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

    [LibraryImport("user32.dll")]
    internal static partial IntPtr GetParent(IntPtr hWnd);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetWindowPos(
        IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial int GetWindowLongW(IntPtr hWnd, int index);

    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial int SetWindowLongW(IntPtr hWnd, int index, int value);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetMenu(IntPtr hWnd, IntPtr hMenu);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ShowWindow(IntPtr hWnd, int command);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool IsWindow(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool IsWindowVisible(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    internal static partial IntPtr SetFocus(IntPtr hWnd);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool PostMessageW(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam);

    [LibraryImport("user32.dll")]
    internal static partial uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowTextLengthW")]
    internal static partial int GetWindowTextLengthW(IntPtr hWnd);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowTextW")]
    internal static unsafe partial int GetWindowTextW(IntPtr hWnd, char* text, int maxCount);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetClientRect(IntPtr hWnd, out Rect rect);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetWindowRect(IntPtr hWnd, out Rect rect);

    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect
    {
        internal int Left;
        internal int Top;
        internal int Right;
        internal int Bottom;
    }

    [LibraryImport("user32.dll", EntryPoint = "CreateWindowExW", SetLastError = true,
        StringMarshalling = StringMarshalling.Utf16)]
    internal static partial IntPtr CreateWindowExW(
        uint exStyle,
        string className,
        string windowName,
        uint style,
        int x,
        int y,
        int width,
        int height,
        IntPtr parent,
        IntPtr menu,
        IntPtr instance,
        IntPtr param);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DestroyWindow(IntPtr hWnd);

    internal static bool IsEmbeddable(uint style)
    {
        return (style & WsVisible) != 0 && (style & WsChild) == 0;
    }
}
