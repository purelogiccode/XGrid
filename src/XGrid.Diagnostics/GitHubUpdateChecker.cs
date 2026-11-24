using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace XGrid.Diagnostics;

/// <summary>Describes a GitHub release that may be newer than the running build.</summary>
public sealed record GitHubUpdateInfo
{
    /// <summary>Gets the running version.</summary>
    public string CurrentVersion { get; init; } = string.Empty;

    /// <summary>Gets the latest published version.</summary>
    public string LatestVersion { get; init; } = string.Empty;

    /// <summary>Gets the release name, when GitHub provides one.</summary>
    public string? ReleaseName { get; init; }

    /// <summary>Gets the page that lets the user download the release.</summary>
    public Uri ReleaseUrl { get; init; } = new("https://github.com/purelogiccode/XGrid/releases", UriKind.Absolute);

    /// <summary>Gets a value indicating whether the latest release is newer than the running build.</summary>
    public bool UpdateAvailable => VersionComparison.IsNewer(LatestVersion, CurrentVersion);
}

/// <summary>Checks the GitHub repository for a newer release or tag.</summary>
public static class GitHubUpdateChecker
{
    private static readonly HttpClient Client = new()
    {
        Timeout = TimeSpan.FromSeconds(8),
    };

    /// <summary>Queries the configured repository for its latest release.</summary>
    /// <param name="options">The repository, version and optional token.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The release, or <see langword="null"/> when the check cannot run.</returns>
    public static async Task<GitHubUpdateInfo?> CheckAsync(
        DiagnosticsOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!options.UpdateChecksEnabled)
        {
            return null;
        }

        try
        {
            GitHubUpdateInfo? release = await CheckReleasesAsync(options, cancellationToken).ConfigureAwait(false);
            return release ?? await CheckTagsAsync(options, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is HttpRequestException
                                              or TaskCanceledException
                                              or InvalidOperationException
                                              or JsonException)
        {
            Log.Debug(exception, "The GitHub update check did not complete");
            return null;
        }
    }

    private static async Task<GitHubUpdateInfo?> CheckReleasesAsync(
        DiagnosticsOptions options,
        CancellationToken cancellationToken)
    {
        using JsonDocument? document = await GetJsonAsync(options, options.UpdateApiUrl, cancellationToken)
            .ConfigureAwait(false);
        if (document is null)
        {
            return null;
        }

        JsonElement root = document.RootElement;
        if (root.TryGetProperty("draft", out JsonElement draft) && draft.GetBoolean())
        {
            return null;
        }

        if (root.TryGetProperty("prerelease", out JsonElement prerelease) && prerelease.GetBoolean())
        {
            return null;
        }

        string? tag = ReadString(root, "tag_name");
        if (string.IsNullOrWhiteSpace(tag))
        {
            return null;
        }

        return new GitHubUpdateInfo
        {
            CurrentVersion = options.Version,
            LatestVersion = tag,
            ReleaseName = ReadString(root, "name"),
            ReleaseUrl = ReadUri(root, "html_url") ?? ReleasesPage(options),
        };
    }

    private static async Task<GitHubUpdateInfo?> CheckTagsAsync(
        DiagnosticsOptions options,
        CancellationToken cancellationToken)
    {
        using JsonDocument? document = await GetJsonAsync(options, options.TagsApiUrl, cancellationToken)
            .ConfigureAwait(false);
        if (document is null || document.RootElement.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (JsonElement tag in document.RootElement.EnumerateArray())
        {
            string? name = ReadString(tag, "name");
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            return new GitHubUpdateInfo
            {
                CurrentVersion = options.Version,
                LatestVersion = name,
                ReleaseUrl = ReleasesPage(options),
            };
        }

        return null;
    }

    private static async Task<JsonDocument?> GetJsonAsync(
        DiagnosticsOptions options,
        Uri uri,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.Add(
            new ProductInfoHeaderValue(Sanitize(options.ApplicationName), Sanitize(options.Version)));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        if (!string.IsNullOrWhiteSpace(options.GitHubToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.GitHubToken);
        }

        using HttpResponseMessage response = await Client
            .SendAsync(request, cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            Log.Debug(
                "The GitHub update check returned HTTP {StatusCode} for {Uri}",
                (int)response.StatusCode,
                uri);
            return null;
        }

        using var stream = await response.Content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        return await JsonDocument.ParseAsync(stream, default, cancellationToken).ConfigureAwait(false);
    }

    private static string? ReadString(JsonElement element, string property)
    {
        return element.TryGetProperty(property, out JsonElement value)
               && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static Uri? ReadUri(JsonElement element, string property)
    {
        string? text = ReadString(element, property);
        return Uri.TryCreate(text, UriKind.Absolute, out Uri? uri) ? uri : null;
    }

    private static Uri ReleasesPage(DiagnosticsOptions options)
    {
        return new Uri(options.RepositoryUrl.ToString().TrimEnd('/') + "/releases", UriKind.Absolute);
    }

    private static string Sanitize(string value)
    {
        Span<char> buffer = stackalloc char[value.Length];
        int length = 0;
        foreach (char character in value)
        {
            if (char.IsLetterOrDigit(character) || character is '.' or '-' or '_')
            {
                buffer[length++] = character;
            }
        }

        return length == 0 ? "XGrid" : new string(buffer[..length]);
    }
}
