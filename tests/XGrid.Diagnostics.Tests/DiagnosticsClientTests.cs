using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace XGrid.Diagnostics.Tests;

/// <summary>Pins the wire contracts of the outbound diagnostics clients.</summary>
public sealed class DiagnosticsClientTests
{
    [Fact]
    public async Task Statistics_PostsApplicationAndVersionWithBearerKey()
    {
        var handler = new QueueHandler(HttpStatusCode.OK, "{}");
        using var http = new HttpClient(handler);

        bool recorded = await ApplicationStatsClient.RecordLaunchAsync(
            CreateOptions(),
            http,
            CancellationToken.None);

        Assert.True(recorded);
        RecordedRequest request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://example.test/stats", request.Uri);
        Assert.Equal("Bearer stats-key", request.Authorization);
        Assert.Contains("\"applicationId\":\"xgrid\"", request.Body, StringComparison.Ordinal);
        Assert.Contains("\"version\":\"1.2.3\"", request.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Statistics_WhenDisabled_DoesNotSend()
    {
        var handler = new QueueHandler(HttpStatusCode.OK, "{}");
        using var http = new HttpClient(handler);

        bool recorded = await ApplicationStatsClient.RecordLaunchAsync(
            CreateOptions() with { StatisticsEnabled = false },
            http,
            CancellationToken.None);

        Assert.False(recorded);
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task Statistics_WhenTheServiceRejectsTheHit_ReturnsFalse(HttpStatusCode status)
    {
        using var http = new HttpClient(new QueueHandler(status, "{}"));

        bool recorded = await ApplicationStatsClient.RecordLaunchAsync(
            CreateOptions(),
            http,
            CancellationToken.None);

        Assert.False(recorded);
    }

    [Fact]
    public async Task Statistics_WhenTransportFails_ReturnsFalse()
    {
        using var http = new HttpClient(new ThrowingHandler());

        bool recorded = await ApplicationStatsClient.RecordLaunchAsync(
            CreateOptions(),
            http,
            CancellationToken.None);

        Assert.False(recorded);
    }

    [Fact]
    public async Task BugReport_PostsTheApiKeyAndReportFields()
    {
        var handler = new QueueHandler(HttpStatusCode.Accepted, "{}");
        using var http = new HttpClient(handler);
        var report = new BugReportRequest
        {
            Message = "message",
            ApplicationName = "XGrid",
            Version = "1.2.3",
            Environment = "Windows",
            StackTrace = "stack",
        };

        bool accepted = await BugReportClient.SendAsync(
            report,
            CreateOptions(),
            http,
            CancellationToken.None);

        Assert.True(accepted);
        RecordedRequest request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://example.test/bugreport", request.Uri);
        Assert.Equal("bug-key", request.ApiKey);
        Assert.Contains("\"message\":\"message\"", request.Body, StringComparison.Ordinal);
        Assert.Contains("\"stackTrace\":\"stack\"", request.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BugReport_WhenTheServiceRejectsTheReport_ReturnsFalse()
    {
        using var http = new HttpClient(new QueueHandler(HttpStatusCode.BadRequest, "{}"));

        bool accepted = await BugReportClient.SendAsync(
            new BugReportRequest { Message = "message" },
            CreateOptions(),
            http,
            CancellationToken.None);

        Assert.False(accepted);
    }

    [Fact]
    public async Task UpdateCheck_UsesTheLatestReleaseAndItsPage()
    {
        var handler = new QueueHandler(
            HttpStatusCode.OK,
            """
            {
              "tag_name": "v9.0.0",
              "name": "XGrid 9",
              "html_url": "https://github.com/purelogiccode/XGrid/releases/tag/v9.0.0",
              "draft": false,
              "prerelease": false
            }
            """);
        using var http = new HttpClient(handler);

        GitHubUpdateInfo? update = await GitHubUpdateChecker.CheckAsync(
            CreateOptions(),
            http,
            CancellationToken.None);

        Assert.NotNull(update);
        Assert.True(update.UpdateAvailable);
        Assert.Equal("v9.0.0", update.LatestVersion);
        Assert.Equal("XGrid 9", update.ReleaseName);
        Assert.Equal("https://github.com/purelogiccode/XGrid/releases/tag/v9.0.0", update.ReleaseUrl.ToString());
        Assert.Equal("https://api.github.com/repos/purelogiccode/XGrid/releases/latest", Assert.Single(handler.Requests).Uri);
    }

    [Fact]
    public async Task UpdateCheck_WhenTheReleaseIsOlder_ReportsNoUpdate()
    {
        using var http = new HttpClient(new QueueHandler(
            HttpStatusCode.OK,
            """{"tag_name":"v0.0.9","name":"old","draft":false,"prerelease":false}"""));

        GitHubUpdateInfo? update = await GitHubUpdateChecker.CheckAsync(
            CreateOptions(),
            http,
            CancellationToken.None);

        Assert.NotNull(update);
        Assert.False(update.UpdateAvailable);
    }

    [Fact]
    public async Task UpdateCheck_SkipsDraftAndPrereleaseThenFallsBackToTags()
    {
        var handler = new QueueHandler();
        handler.Enqueue(
            HttpStatusCode.OK,
            """{"tag_name":"v9.0.0","draft":true,"prerelease":false}""");
        handler.Enqueue(HttpStatusCode.OK, """[{"name":"v1.3.0"},{"name":"v1.2.0"}]""");
        using var http = new HttpClient(handler);

        GitHubUpdateInfo? update = await GitHubUpdateChecker.CheckAsync(
            CreateOptions(),
            http,
            CancellationToken.None);

        Assert.NotNull(update);
        Assert.Equal("v1.3.0", update.LatestVersion);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal("https://api.github.com/repos/purelogiccode/XGrid/tags", handler.Requests[1].Uri);
    }

    [Fact]
    public async Task UpdateCheck_WhenTheReleaseRequestFails_UsesTags()
    {
        var handler = new QueueHandler();
        handler.Enqueue(HttpStatusCode.NotFound, "{}");
        handler.Enqueue(HttpStatusCode.OK, """[{"name":"v2.0.0"}]""");
        using var http = new HttpClient(handler);

        GitHubUpdateInfo? update = await GitHubUpdateChecker.CheckAsync(
            CreateOptions(),
            http,
            CancellationToken.None);

        Assert.NotNull(update);
        Assert.Equal("v2.0.0", update.LatestVersion);
        Assert.Equal("https://github.com/purelogiccode/XGrid/releases", update.ReleaseUrl.ToString());
    }

    [Fact]
    public async Task UpdateCheck_PicksTheHighestTagWhenTheTagListIsNotSorted()
    {
        var handler = new QueueHandler();
        handler.Enqueue(HttpStatusCode.NotFound, "{}");
        handler.Enqueue(
            HttpStatusCode.OK,
            """[{"name":"v1.2.0"},{"name":"v2.0.0"},{"name":"v1.9.0"}]""");
        using var http = new HttpClient(handler);

        GitHubUpdateInfo? update = await GitHubUpdateChecker.CheckAsync(
            CreateOptions(),
            http,
            CancellationToken.None);

        Assert.NotNull(update);
        Assert.Equal("v2.0.0", update.LatestVersion);
    }

    [Fact]
    public async Task UpdateCheck_UsesTheFirstTagWhenNoTagParses()
    {
        var handler = new QueueHandler();
        handler.Enqueue(HttpStatusCode.NotFound, "{}");
        handler.Enqueue(HttpStatusCode.OK, """[{"name":"nightly"},{"name":"preview"}]""");
        using var http = new HttpClient(handler);

        GitHubUpdateInfo? update = await GitHubUpdateChecker.CheckAsync(
            CreateOptions(),
            http,
            CancellationToken.None);

        Assert.NotNull(update);
        Assert.Equal("nightly", update.LatestVersion);
    }

    [Fact]
    public async Task UpdateCheck_SendsTheOptionalGitHubToken()
    {
        var handler = new QueueHandler(HttpStatusCode.OK, """{"tag_name":"v1.2.3"}""");
        using var http = new HttpClient(handler);

        _ = await GitHubUpdateChecker.CheckAsync(
            CreateOptions() with { GitHubToken = "github-token" },
            http,
            CancellationToken.None);

        RecordedRequest request = Assert.Single(handler.Requests);
        Assert.Equal("Bearer github-token", request.Authorization);
        Assert.Contains("application/vnd.github+json", request.Accept, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UpdateCheck_WhenDisabled_DoesNotSend()
    {
        var handler = new QueueHandler(HttpStatusCode.OK, "{}");
        using var http = new HttpClient(handler);

        GitHubUpdateInfo? update = await GitHubUpdateChecker.CheckAsync(
            CreateOptions() with { UpdateChecksEnabled = false },
            http,
            CancellationToken.None);

        Assert.Null(update);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task UpdateCheck_WhenJsonIsMalformed_ReturnsNull()
    {
        using var http = new HttpClient(new QueueHandler(HttpStatusCode.OK, "not json"));

        GitHubUpdateInfo? update = await GitHubUpdateChecker.CheckAsync(
            CreateOptions(),
            http,
            CancellationToken.None);

        Assert.Null(update);
    }

    [Fact]
    public async Task UpdateCheck_WhenTheTagListIsEmpty_ReturnsNull()
    {
        var handler = new QueueHandler();
        handler.Enqueue(HttpStatusCode.NotFound, "{}");
        handler.Enqueue(HttpStatusCode.OK, "[]");
        using var http = new HttpClient(handler);

        GitHubUpdateInfo? update = await GitHubUpdateChecker.CheckAsync(
            CreateOptions(),
            http,
            CancellationToken.None);

        Assert.Null(update);
    }

    private static DiagnosticsOptions CreateOptions()
    {
        return new DiagnosticsOptions
        {
            ApplicationId = "xgrid",
            ApplicationName = "XGrid",
            Version = "1.2.3",
            BugReportEndpoint = new Uri("https://example.test/bugreport", UriKind.Absolute),
            BugReportApiKey = "bug-key",
            StatisticsEndpoint = new Uri("https://example.test/stats", UriKind.Absolute),
            StatisticsApiKey = "stats-key",
        };
    }

    private sealed record RecordedRequest(
        HttpMethod Method,
        string Uri,
        string? ApiKey,
        string? Authorization,
        string? Accept,
        string Body);

    private sealed class QueueHandler : HttpMessageHandler
    {
        private readonly Queue<(HttpStatusCode Status, string Body)> _responses = new();

        public QueueHandler()
        {
        }

        public QueueHandler(HttpStatusCode status, string body)
        {
            Enqueue(status, body);
        }

        public List<RecordedRequest> Requests { get; } = [];

        public void Enqueue(HttpStatusCode status, string body)
        {
            _responses.Enqueue((status, body));
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            Requests.Add(new RecordedRequest(
                request.Method,
                request.RequestUri?.ToString() ?? string.Empty,
                request.Headers.TryGetValues("X-API-KEY", out var keys) ? string.Join(",", keys) : null,
                request.Headers.Authorization?.ToString(),
                request.Headers.Accept.Count > 0 ? request.Headers.Accept.ToString() : null,
                body));

            (HttpStatusCode status, string responseBody) = _responses.Count > 0
                ? _responses.Dequeue()
                : (HttpStatusCode.NotFound, "{}");
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json"),
            };
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            throw new HttpRequestException("network down");
        }
    }
}
