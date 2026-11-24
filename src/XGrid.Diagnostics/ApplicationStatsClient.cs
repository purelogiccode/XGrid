using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace XGrid.Diagnostics;

/// <summary>Records one usage hit with the ApplicationStats service.</summary>
public static class ApplicationStatsClient
{
    private static readonly HttpClient Client = new()
    {
        Timeout = TimeSpan.FromSeconds(8),
    };

    /// <summary>Records a launch for this application and version.</summary>
    /// <param name="options">The statistics endpoint and API key.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns><see langword="true"/> when the service recorded the hit.</returns>
    public static async Task<bool> RecordLaunchAsync(
        DiagnosticsOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!options.StatisticsEnabled)
        {
            return false;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, options.StatisticsEndpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.StatisticsApiKey);
            request.Content = JsonContent.Create(
                new StatisticsPayload
                {
                    ApplicationId = options.ApplicationId,
                    Version = options.Version,
                });
            using HttpResponseMessage response = await Client
                .SendAsync(request, cancellationToken)
                .ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                Log.Debug("Recorded usage statistics for {ApplicationId} {Version}", options.ApplicationId, options.Version);
                return true;
            }

            Log.Debug(
                "Usage statistics were not recorded: HTTP {StatusCode}",
                (int)response.StatusCode);
            return false;
        }
        catch (Exception exception) when (exception is HttpRequestException
                                              or TaskCanceledException
                                              or InvalidOperationException)
        {
            // Statistics are best-effort and must never affect a launch.
            Log.Debug(exception, "Usage statistics could not be sent");
            return false;
        }
    }

    private sealed record StatisticsPayload
    {
        [JsonPropertyName("applicationId")]
        public string ApplicationId { get; init; } = string.Empty;

        [JsonPropertyName("version")]
        public string Version { get; init; } = string.Empty;
    }
}
