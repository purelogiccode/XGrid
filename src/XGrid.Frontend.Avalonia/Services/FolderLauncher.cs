using System;
using System.Diagnostics;
using System.IO;

namespace XGrid.Frontend.Avalonia.Services;

/// <summary>
/// Opens a folder in the desktop's file manager, creating it first so a menu
/// entry never fails on a fresh install.
/// </summary>
public static class FolderLauncher
{
    /// <summary>Opens <paramref name="path"/> with the platform's file manager.</summary>
    /// <param name="path">Folder to show.</param>
    /// <exception cref="ArgumentException"><paramref name="path"/> is blank.</exception>
    public static void Open(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _ = Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();
    }
}
