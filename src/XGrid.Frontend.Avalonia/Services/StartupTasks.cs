using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Serilog;
using XGrid.Diagnostics;
using XGrid.Frontend.Avalonia.ViewModels;
using XGrid.Frontend.Avalonia.Views;

namespace XGrid.Frontend.Avalonia.Services;

/// <summary>
/// Runs the non-blocking work that happens once per shell launch: the usage
/// statistics hit, the GitHub update check and the optional update prompt.
/// </summary>
public static class StartupTasks
{
    /// <summary>Records usage and checks for a newer release after the window opens.</summary>
    /// <param name="owner">The main window, used as the dialog owner.</param>
    /// <returns>A task that completes when the startup work is done.</returns>
    public static async Task RunAsync(Window owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        try
        {
            DiagnosticsOptions options = AppDiagnostics.Options;
            _ = await ApplicationStatsClient.RecordLaunchAsync(options).ConfigureAwait(true);

            GitHubUpdateInfo? update = await GitHubUpdateChecker.CheckAsync(options).ConfigureAwait(true);
            if (update is not { UpdateAvailable: true })
            {
                return;
            }

            Log.Information(
                "A newer release is available: {LatestVersion} (running {CurrentVersion})",
                update.LatestVersion,
                update.CurrentVersion);

            var prompt = new UpdatePromptWindow(new UpdatePromptViewModel(update));
            bool openRepository = await prompt.ShowDialog<bool>(owner).ConfigureAwait(true);
            if (!openRepository)
            {
                return;
            }

            var dialogs = new FileDialogService { Window = owner };
            await dialogs.OpenUriAsync(update.ReleaseUrl).ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            // Statistics and update checks are conveniences; they must never
            // interrupt a launch or produce a second bug report for themselves.
            Log.Debug(exception, "The startup network tasks did not complete");
        }
    }
}
