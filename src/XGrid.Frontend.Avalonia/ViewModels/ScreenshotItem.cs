using System;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.Input;
using XGrid.Frontend.Avalonia.Models;
using XGrid.Frontend.Avalonia.Services;

namespace XGrid.Frontend.Avalonia.ViewModels;

/// <summary>
/// One capture in the Library's screenshot strip: a thumbnail decoded from the
/// PNG, the capture time, and the commands to open the file or its folder.
/// </summary>
public sealed class ScreenshotItem : IDisposable
{
    private const int ThumbnailWidth = 320;

    private readonly MainWindowViewModel _shell;
    private Bitmap? _thumbnail;

    /// <summary>Creates a row for one capture.</summary>
    /// <param name="file">Capture path and time.</param>
    /// <param name="shell">Shell used for logging and toasts.</param>
    public ScreenshotItem(ScreenshotFile file, MainWindowViewModel shell)
    {
        ArgumentNullException.ThrowIfNull(shell);
        _shell = shell;
        Path = file.Path;
        CapturedAt = file.CapturedAt;
        _thumbnail = LoadThumbnail(file.Path);
        OpenCommand = new RelayCommand(Open);
    }

    /// <summary>Gets the capture path.</summary>
    public string Path { get; }

    /// <summary>Gets the capture time.</summary>
    public DateTime CapturedAt { get; }

    /// <summary>Gets the file name shown in the tooltip.</summary>
    public string FileName => System.IO.Path.GetFileName(Path);

    /// <summary>Gets the date and time shown under the thumbnail.</summary>
    public string CapturedText => CapturedAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    /// <summary>Gets the decoded thumbnail, or null when the PNG cannot be read.</summary>
    public Bitmap? Thumbnail => _thumbnail;

    /// <summary>Gets the command that opens the capture in the default viewer.</summary>
    public IRelayCommand OpenCommand { get; }

    /// <inheritdoc />
    public void Dispose()
    {
        _thumbnail?.Dispose();
        _thumbnail = null;
    }

    private static Bitmap? LoadThumbnail(string path)
    {
        try
        {
            using FileStream stream = File.OpenRead(path);
            return Bitmap.DecodeToWidth(stream, ThumbnailWidth);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                              or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    private void Open()
    {
        try
        {
            FolderLauncher.OpenFile(Path);
            _shell.AddLog(LogLevel.Info, "Opened screenshot " + FileName + ".");
        }
        catch (Exception exception) when (exception is IOException
                                              or UnauthorizedAccessException
                                              or InvalidOperationException
                                              or NotSupportedException
                                              or Win32Exception)
        {
            _shell.AddLog(LogLevel.Error, "Could not open the screenshot: " + exception.Message);
            _shell.ShowToast("Could not open the screenshot.");
        }
    }
}
