using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace XGrid.Frontend.Avalonia.Services;

/// <summary>An HTTP failure with an optional status code.</summary>
public class CoverHttpException : Exception
{
    /// <summary>Creates an empty exception.</summary>
    public CoverHttpException()
    {
    }

    /// <summary>Creates an exception with a message.</summary>
    /// <param name="message">Failure message.</param>
    public CoverHttpException(string message)
        : base(message)
    {
    }

    /// <summary>Creates an exception with a message and inner exception.</summary>
    /// <param name="message">Failure message.</param>
    /// <param name="innerException">Inner exception.</param>
    public CoverHttpException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Downloads the cover catalog and content-addressed images. File URIs are
/// supported for offline catalogs and tests; remote catalogs use HTTP or HTTPS.
/// </summary>
public sealed class CoverDownloader : IDisposable
{
    private const int MaxAttempts = 3;

    private readonly HttpClient _http;
    private readonly bool _ownsClient;

    /// <summary>Creates a downloader.</summary>
    /// <param name="httpClient">Optional HTTP client; a configured client is created when omitted.</param>
    public CoverDownloader(HttpClient? httpClient = null)
    {
        if (httpClient is null)
        {
            var handler = new SocketsHttpHandler
            {
                AutomaticDecompression = DecompressionMethods.All,
                ConnectTimeout = TimeSpan.FromSeconds(10),
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            };
            _http = new HttpClient(handler)
            {
                Timeout = Timeout.InfiniteTimeSpan,
            };
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("XGrid-CoverService/1.0");
            _ownsClient = true;
        }
        else
        {
            _http = httpClient;
            _ownsClient = false;
        }
    }

    /// <summary>Requests the catalog, using a conditional request when an ETag is supplied.</summary>
    /// <param name="uri">Catalog URI.</param>
    /// <param name="etag">Optional cached ETag.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The response, including a 304 result.</returns>
    public async Task<CoverCatalogHttpResult> GetCatalogAsync(
        Uri uri,
        string? etag,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (uri.IsFile)
        {
            var file = new FileInfo(uri.LocalPath);
            if (!file.Exists)
            {
                throw new CoverHttpException("The cover catalog file does not exist.");
            }

            if (file.Length > CoverDefaults.MaxCatalogBytes)
            {
                throw new InvalidDataException("The cover catalog exceeds the maximum allowed size.");
            }

            string json = await File.ReadAllTextAsync(uri.LocalPath, cancellationToken).ConfigureAwait(false);
            return new CoverCatalogHttpResult(false, json.TrimStart('\uFEFF'), string.Empty);
        }

        EnsureRemoteScheme(uri);
        return await ExecuteWithRetryAsync(async timeoutToken =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            if (!string.IsNullOrWhiteSpace(etag))
            {
                request.Headers.TryAddWithoutValidation("If-None-Match", etag);
            }

            using HttpResponseMessage response = await _http
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutToken)
                .ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotModified)
            {
                return new CoverCatalogHttpResult(true, string.Empty, etag ?? string.Empty);
            }

            await EnsureSuccessAsync(response, "cover catalog").ConfigureAwait(false);
            string json = await ReadLimitedStringAsync(
                    response,
                    CoverDefaults.MaxCatalogBytes,
                    timeoutToken)
                .ConfigureAwait(false);
            return new CoverCatalogHttpResult(
                false,
                json,
                response.Headers.ETag?.Tag ?? string.Empty);
        }, TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Downloads one image into the content-addressed cache.</summary>
    /// <param name="uri">Image URI.</param>
    /// <param name="id">Expected SHA-256 identifier.</param>
    /// <param name="store">Cache store.</param>
    /// <param name="progress">Optional byte progress.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The cached image path.</returns>
    public async Task<string> DownloadImageAsync(
        Uri uri,
        string id,
        CoverStore store,
        IProgress<long>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(uri);
        ArgumentNullException.ThrowIfNull(store);
        if (store.HasImage(id))
        {
            return store.GetImagePath(id);
        }

        if (uri.IsFile)
        {
            await using var file = new FileStream(
                uri.LocalPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                64 * 1024,
                useAsync: true);
            return await store.SaveImageAsync(id, file, cancellationToken, progress).ConfigureAwait(false);
        }

        EnsureRemoteScheme(uri);
        return await ExecuteWithRetryAsync(async timeoutToken =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("image/webp"));
            using HttpResponseMessage response = await _http
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutToken)
                .ConfigureAwait(false);
            await EnsureSuccessAsync(response, "cover image").ConfigureAwait(false);
            if (response.Content.Headers.ContentLength is > CoverDefaults.MaxImageBytes)
            {
                throw new InvalidDataException("The cover image exceeds the maximum allowed size.");
            }

            await using Stream stream = await response.Content
                .ReadAsStreamAsync(timeoutToken)
                .ConfigureAwait(false);
            return await store.SaveImageAsync(id, stream, timeoutToken, progress).ConfigureAwait(false);
        }, TimeSpan.FromSeconds(120), cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_ownsClient)
        {
            _http.Dispose();
        }
    }

    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        string description)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        string message =
            $"The {description} request returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).";
        if (response.StatusCode is HttpStatusCode.RequestTimeout
            or HttpStatusCode.TooManyRequests
            or HttpStatusCode.InternalServerError
            or HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.GatewayTimeout)
        {
            throw new TransientCoverHttpException(message);
        }

        throw new CoverHttpException(message);
    }

    private static async Task<string> ReadLimitedStringAsync(
        HttpResponseMessage response,
        long maximumBytes,
        CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength is { } contentLength && contentLength > maximumBytes)
        {
            throw new InvalidDataException("The cover catalog exceeds the maximum allowed size.");
        }

        await using Stream stream = await response.Content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        using var buffer = new MemoryStream();
        byte[] rented = new byte[64 * 1024];
        int read;
        while ((read = await stream.ReadAsync(rented, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > maximumBytes)
            {
                throw new InvalidDataException("The cover catalog exceeds the maximum allowed size.");
            }

            buffer.Write(rented, 0, read);
        }

        return System.Text.Encoding.UTF8.GetString(buffer.ToArray()).TrimStart('\uFEFF');
    }

    private static async Task<T> ExecuteWithRetryAsync<T>(
        Func<CancellationToken, Task<T>> action,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        for (int attempt = 1; ; attempt++)
        {
            using var attemptTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            attemptTimeout.CancelAfter(timeout);
            try
            {
                return await action(attemptTimeout.Token).ConfigureAwait(false);
            }
            catch (TransientCoverHttpException exception) when (attempt < MaxAttempts)
            {
                Log.Debug(exception, "Transient cover download failure; retrying attempt {Attempt}", attempt + 1);
                await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt), cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException exception) when (attempt < MaxAttempts)
            {
                Log.Debug(exception, "Cover download failed; retrying attempt {Attempt}", attempt + 1);
                await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt), cancellationToken).ConfigureAwait(false);
            }
            catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested
                                                          && attempt < MaxAttempts)
            {
                Log.Debug(exception, "Cover download timed out; retrying attempt {Attempt}", attempt + 1);
                await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static void EnsureRemoteScheme(Uri uri)
    {
        if (uri.Scheme is not ("http" or "https"))
        {
            throw new InvalidDataException($"Unsupported cover URL scheme '{uri.Scheme}'.");
        }
    }

    private sealed class TransientCoverHttpException(string message) : CoverHttpException(message);
}
