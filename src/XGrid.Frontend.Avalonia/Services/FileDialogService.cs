using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;

namespace XGrid.Frontend.Avalonia.Services;

/// <summary>Host file and folder pickers backed by the main window's storage provider.</summary>
public sealed class FileDialogService
{
    /// <summary>Gets or sets the window whose storage provider is used.</summary>
    public Window? Window { get; set; }

    /// <summary>Opens a single-file picker and returns a local path, or null when cancelled.</summary>
    public async Task<string?> PickFileAsync(string title, IReadOnlyList<FilePickerFileType>? types = null)
    {
        if (Window?.StorageProvider is not { } storage)
        {
            return null;
        }

        IReadOnlyList<IStorageFile> files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = types,
        });

        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    /// <summary>Opens a folder picker and returns a local path, or null when cancelled.</summary>
    public async Task<string?> PickFolderAsync(string title)
    {
        if (Window?.StorageProvider is not { } storage)
        {
            return null;
        }

        IReadOnlyList<IStorageFolder> folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
        });

        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }

    /// <summary>Launches the platform file manager at a folder path.</summary>
    public async Task OpenFolderAsync(string path)
    {
        if (Window?.Launcher is not { } launcher || string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        await launcher.LaunchDirectoryInfoAsync(new System.IO.DirectoryInfo(path));
    }

    /// <summary>Opens a URI in the platform browser.</summary>
    public async Task OpenUriAsync(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (Window?.Launcher is { } launcher)
        {
            await launcher.LaunchUriAsync(uri);
        }
    }

    /// <summary>Copies text to the platform clipboard.</summary>
    public async Task CopyTextAsync(string text)
    {
        if (Window?.Clipboard is { } clipboard)
        {
            var data = new DataTransfer();
            data.Add(DataTransferItem.CreateText(text));
            await clipboard.SetDataAsync(data);
        }
    }

    /// <summary>Common firmware filter.</summary>
    public static IReadOnlyList<FilePickerFileType> FirmwareTypes { get; } =
    [
        new FilePickerFileType("Firmware images") { Patterns = ["*.bin", "*.rom", "*.bios"] },
        new FilePickerFileType("All files") { Patterns = ["*"] },
    ];

    /// <summary>Common disc-image filter.</summary>
    public static IReadOnlyList<FilePickerFileType> DiscTypes { get; } =
    [
        new FilePickerFileType("Xbox disc images") { Patterns = ["*.iso", "*.chd", "*.xbe"] },
        new FilePickerFileType("All files") { Patterns = ["*"] },
    ];

    /// <summary>Common hard-disk-image filter.</summary>
    public static IReadOnlyList<FilePickerFileType> DiskTypes { get; } =
    [
        new FilePickerFileType("Xbox disk images") { Patterns = ["*.qcow2", "*.img", "*.vhd", "*.bin"] },
        new FilePickerFileType("All files") { Patterns = ["*"] },
    ];

}
