using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using XGrid.Host;

namespace XGrid.Frontend.Avalonia.ViewModels;

/// <summary>About page: build, runtime, and project information.</summary>
public sealed partial class AboutViewModel : PageViewModel
{
    /// <summary>Creates the about page.</summary>
    public AboutViewModel(MainWindowViewModel shell)
        : base(shell)
    {
        Version version = typeof(AboutViewModel).Assembly.GetName().Version ?? new Version(0, 0, 0);
        AppVersion = "v" + version.ToString(3);
        CoreVersion = typeof(XboxMachine).Assembly.GetName().Version?.ToString(3) ?? "unknown";
        RuntimeText = RuntimeInformation.FrameworkDescription;
        OperatingSystemText = RuntimeInformation.OSDescription + " · " + RuntimeInformation.OSArchitecture;
        RepositoryUrl = "https://github.com/purelogiccode/XGrid";
    }

    /// <summary>Gets the frontend assembly version.</summary>
    public string AppVersion { get; }

    /// <summary>Gets the emulator core assembly version.</summary>
    public string CoreVersion { get; }

    /// <summary>Gets the .NET runtime description.</summary>
    public string RuntimeText { get; }

    /// <summary>Gets the operating-system description.</summary>
    public string OperatingSystemText { get; }

    /// <summary>Gets the project repository URL.</summary>
    public string RepositoryUrl { get; }

    /// <summary>Gets the settings file path.</summary>
    public string SettingsPath => Shell.SettingsPath;

    /// <summary>Public command: opens the project repository.</summary>
    [RelayCommand]
    private Task OpenRepositoryAsync()
    {
        return Shell.Dialogs.OpenUriAsync(new Uri(RepositoryUrl, UriKind.Absolute));
    }

    /// <summary>Public command: opens the settings folder.</summary>
    [RelayCommand]
    private Task OpenSettingsFolderAsync()
    {
        return Shell.Dialogs.OpenFolderAsync(System.IO.Path.GetDirectoryName(SettingsPath) ?? string.Empty);
    }

    /// <summary>Public command: opens the settings page.</summary>
    [RelayCommand]
    private void OpenSettings()
    {
        Shell.NavigateTo(PageKind.Settings);
    }
}
