using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace XGrid.Core.Xemu;

/// <summary>
/// Finds the window the core creates. xemu opens its SDL window on a worker
/// thread, so the handle is not available when the process starts; the locator
/// polls the process's visible top-level windows until the largest one appears.
/// </summary>
internal static unsafe partial class XemuWindowLocator
{
    /// <summary>
    /// Waits for the core's main window.
    /// </summary>
    /// <param name="processId">Core process id.</param>
    /// <param name="timeout">How long to wait.</param>
    /// <param name="cancelled">Optional cancellation check.</param>
    /// <returns>The window handle, or <see cref="IntPtr.Zero"/> on timeout.</returns>
    internal static IntPtr WaitForMainWindow(int processId, TimeSpan timeout, Func<bool>? cancelled = null)
    {
        long deadline = Stopwatch.GetTimestamp() + (long)(timeout.TotalSeconds * Stopwatch.Frequency);
        while (Stopwatch.GetTimestamp() < deadline)
        {
            if (cancelled is not null && cancelled())
            {
                return IntPtr.Zero;
            }

            IntPtr handle = Find(processId);
            if (handle != IntPtr.Zero)
            {
                return handle;
            }

            Thread.Sleep(50);
        }

        return IntPtr.Zero;
    }

    /// <summary>Returns the largest visible top-level window owned by a process.</summary>
    /// <param name="processId">Core process id.</param>
    /// <param name="requireVisible">False to accept a hidden window (the embedding case).</param>
    /// <returns>The window handle, or zero when the process owns no candidate window.</returns>
    internal static IntPtr Find(int processId, bool requireVisible = true)
    {
        var candidates = new List<IntPtr>(4);
        GCHandle state = GCHandle.Alloc(candidates);
        try
        {
            EnumWindows(&CollectWindow, GCHandle.ToIntPtr(state));
        }
        finally
        {
            state.Free();
        }

        IntPtr best = IntPtr.Zero;
        long bestArea = 0;
        foreach (IntPtr handle in candidates)
        {
            _ = Win32Window.GetWindowThreadProcessId(handle, out uint owner);
            if (owner != processId)
            {
                continue;
            }

            int style = Win32Window.GetWindowLongW(handle, Win32Window.GwlStyle);
            if (!Win32Window.IsEmbeddable(unchecked((uint)style))
                || (requireVisible && !Win32Window.IsWindowVisible(handle)))
            {
                continue;
            }

            if (!Win32Window.GetWindowRect(handle, out Win32Window.Rect rect))
            {
                continue;
            }

            long area = (long)(rect.Right - rect.Left) * (rect.Bottom - rect.Top);
            if (area > bestArea)
            {
                bestArea = area;
                best = handle;
            }
        }

        return best;
    }

    /// <summary>Finds the core window by its XGrid title, hidden or not.</summary>
    /// <param name="processId">Core process id.</param>
    /// <param name="title">Exact window title to match.</param>
    /// <returns>The handle, or zero.</returns>
    internal static IntPtr FindTitled(int processId, string title)
    {
        var candidates = new List<IntPtr>(4);
        GCHandle state = GCHandle.Alloc(candidates);
        try
        {
            EnumWindows(&CollectWindow, GCHandle.ToIntPtr(state));
        }
        finally
        {
            state.Free();
        }

        foreach (IntPtr handle in candidates)
        {
            _ = Win32Window.GetWindowThreadProcessId(handle, out uint owner);
            if (owner != processId)
            {
                continue;
            }

            if (string.Equals(ReadTitle(handle), title, StringComparison.Ordinal))
            {
                return handle;
            }
        }

        return IntPtr.Zero;
    }

    private static string ReadTitle(IntPtr handle)
    {
        int length = Win32Window.GetWindowTextLengthW(handle);
        if (length <= 0)
        {
            return string.Empty;
        }

        char* buffer = stackalloc char[length + 1];
        int copied = Win32Window.GetWindowTextW(handle, buffer, length + 1);
        return copied <= 0 ? string.Empty : new string(buffer, 0, copied);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvStdcall)])]
    private static int CollectWindow(IntPtr handle, nint state)
    {
        if (Win32Window.IsWindowVisible(handle)
            && GCHandle.FromIntPtr(state).Target is List<IntPtr> list)
        {
            list.Add(handle);
        }

        return 1;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    private static unsafe partial void EnumWindows(delegate* unmanaged[Stdcall]<IntPtr, nint, int> callback, nint state);
}
