using System;
using XGrid.Diagnostics;

namespace XGrid.Frontend.Avalonia.ViewModels;

/// <summary>Text shown by the update prompt.</summary>
public sealed class UpdatePromptViewModel
{
    /// <summary>Creates the text from a GitHub release.</summary>
    /// <param name="update">The release information.</param>
    public UpdatePromptViewModel(GitHubUpdateInfo update)
    {
        ArgumentNullException.ThrowIfNull(update);
        CurrentVersion = "You are running v" + update.CurrentVersion.TrimStart('v', 'V') + ".";
        LatestVersion = "The latest release is " + update.LatestVersion + ".";
        ReleaseName = string.IsNullOrWhiteSpace(update.ReleaseName)
            ? "Open the project releases page to download the new build."
            : update.ReleaseName;
    }

    /// <summary>Gets the heading.</summary>
    public string Heading { get; } = "A new version of XGrid is available";

    /// <summary>Gets the running-version line.</summary>
    public string CurrentVersion { get; }

    /// <summary>Gets the latest-release line.</summary>
    public string LatestVersion { get; }

    /// <summary>Gets the release title or download instruction.</summary>
    public string ReleaseName { get; }
}
