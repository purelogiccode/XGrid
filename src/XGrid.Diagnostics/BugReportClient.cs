using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;

namespace XGrid.Diagnostics;

/// <summary>Submits bug reports to the centralized bug-report API.</summary>
public static class BugReportClient
{
    private static readonly HttpClient Client = new()
    {
        Timeout = TimeSpan.FromSeconds(12),
    };

    /// <summary>Sends one report.</summary>
    /// <param name="request">The report body.</param>
    /// <param name="options">The endpoint and API key.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns><see langword="true"/> when the service accepted the report.</returns>
    public static async Task<bool> SendAsync(
        BugReportRequest request,
        DiagnosticsOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(options);

        using var message = new HttpRequestMessage(HttpMethod.Post, options.BugReportEndpoint);
        message.Headers.TryAddWithoutValidation("X-API-KEY", options.BugReportApiKey);
        message.Content = JsonContent.Create(request);
        using HttpResponseMessage response = await Client
            .SendAsync(message, cancellationToken)
            .ConfigureAwait(false);
        return response.IsSuccessStatusCode;
    }
}
