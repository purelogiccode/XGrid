using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
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
        return await RecordLaunchAsync(options, Client, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Records a launch with an injected HTTP client (tests and tools).</summary>
    /// <param name="options">The statistics endpoint and API key.</param>
    /// <param name="client">HTTP client to use.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns><see langword="true"/> when the service recorded the hit.</returns>
    internal static async Task<bool> RecordLaunchAsync(
        DiagnosticsOptions options,
        HttpClient client,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(client);
        if (!options.StatisticsEnabled)
        {
            return false;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, options.StatisticsEndpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.StatisticsApiKey);
            request.Content = JsonContent.Create(
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["applicationId"] = options.ApplicationId,
                    ["version"] = options.Version,
                });
            using HttpResponseMessage response = await client
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
}
