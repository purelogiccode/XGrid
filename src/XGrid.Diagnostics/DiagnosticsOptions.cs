using System;
using System.IO;

namespace XGrid.Diagnostics;

/// <summary>
/// Configuration for application logging, bug reporting, usage statistics and
/// update checks. The defaults target the public XGrid services and can be
/// overridden with environment variables for local testing.
/// </summary>
public sealed record DiagnosticsOptions
{
    /// <summary>The API key used by the centralized bug-report service.</summary>
    public const string DefaultBugReportApiKey =
        "hjh7yu6t56tyr540o9u8767676r5674534453235264c75b6t7ggghgg76trf564e";

    /// <summary>The API key used by the ApplicationStats service.</summary>
    public const string DefaultStatisticsApiKey = DefaultBugReportApiKey;

    /// <summary>Gets or sets the display name sent with diagnostics.</summary>
    public string ApplicationName { get; init; } = "XGrid";

    /// <summary>Gets or sets the stable identifier sent to the statistics service.</summary>
    public string ApplicationId { get; init; } = "xgrid";

    /// <summary>Gets or sets the product version shown in reports.</summary>
    public string Version { get; init; } = "0.0.0";

    /// <summary>Gets or sets the GitHub repository owner.</summary>
    public string RepositoryOwner { get; init; } = "purelogiccode";

    /// <summary>Gets or sets the GitHub repository name.</summary>
    public string RepositoryName { get; init; } = "XGrid";

    /// <summary>Gets or sets the bug-report submission endpoint.</summary>
    public Uri BugReportEndpoint { get; init; } =
        new("https://www.purelogiccode.com/bugreport/api/send-bug-report", UriKind.Absolute);

    /// <summary>Gets or sets the usage statistics endpoint.</summary>
    public Uri StatisticsEndpoint { get; init; } =
        new("https://www.purelogiccode.com/ApplicationStats/stats", UriKind.Absolute);

    /// <summary>Gets or sets the API key used for bug reports.</summary>
    public string BugReportApiKey { get; init; } = DefaultBugReportApiKey;

    /// <summary>Gets or sets the API key used for usage statistics.</summary>
    public string StatisticsApiKey { get; init; } = DefaultStatisticsApiKey;

    /// <summary>Gets or sets a value indicating whether warnings and errors are submitted as bug reports.</summary>
    public bool BugReportsEnabled { get; init; } = true;

    /// <summary>Gets or sets a value indicating whether a usage hit is recorded at startup.</summary>
    public bool StatisticsEnabled { get; init; } = true;

    /// <summary>Gets or sets a value indicating whether GitHub is queried for a newer release.</summary>
    public bool UpdateChecksEnabled { get; init; } = true;

    /// <summary>Gets or sets an optional GitHub token for private repositories or a higher rate limit.</summary>
    public string? GitHubToken { get; init; }

    /// <summary>Gets or sets the directory that receives the rolling log files.</summary>
    public string? LogDirectory { get; init; }

    /// <summary>Gets the project repository URL.</summary>
    public Uri RepositoryUrl => new(
        $"https://github.com/{RepositoryOwner}/{RepositoryName}",
        UriKind.Absolute);

    /// <summary>Gets the release API URL used by the update check.</summary>
    public Uri UpdateApiUrl => new(
        $"https://api.github.com/repos/{RepositoryOwner}/{RepositoryName}/releases/latest",
        UriKind.Absolute);

    /// <summary>Gets the tags API URL used when the repository has no releases yet.</summary>
    public Uri TagsApiUrl => new(
        $"https://api.github.com/repos/{RepositoryOwner}/{RepositoryName}/tags",
        UriKind.Absolute);

    /// <summary>Gets the default log directory under the per-user local application data folder.</summary>
    public static string DefaultLogDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "XGrid",
            "logs");

    /// <summary>Creates the product defaults, honoring the XGRID_* environment overrides.</summary>
    /// <param name="version">The product version.</param>
    /// <returns>A configured options instance.</returns>
    public static DiagnosticsOptions CreateDefault(string version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(version);

        return new DiagnosticsOptions
        {
            Version = version,
            BugReportsEnabled = !IsDisabled("XGRID_DISABLE_BUG_REPORTS"),
            StatisticsEnabled = !IsDisabled("XGRID_DISABLE_STATISTICS"),
            UpdateChecksEnabled = !IsDisabled("XGRID_DISABLE_UPDATE_CHECK"),
            BugReportApiKey = ReadSetting("XGRID_BUGREPORT_API_KEY", DefaultBugReportApiKey),
            StatisticsApiKey = ReadSetting("XGRID_STATS_API_KEY", DefaultStatisticsApiKey),
            GitHubToken = NullIfEmpty(Environment.GetEnvironmentVariable("XGRID_GITHUB_TOKEN")),
            LogDirectory = NullIfEmpty(Environment.GetEnvironmentVariable("XGRID_LOG_DIRECTORY")),
        };
    }

    private static bool IsDisabled(string name)
    {
        string? value = Environment.GetEnvironmentVariable(name);
        return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
               || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }

    private static string ReadSetting(string name, string fallback)
    {
        string? value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    private static string? NullIfEmpty(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
