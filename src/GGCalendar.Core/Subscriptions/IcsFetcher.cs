using System.Net;
using System.Net.Http.Headers;
using System.Security.Authentication;
using GGCalendar.Core.Ics;

namespace GGCalendar.Core.Subscriptions;

public enum FetchStatus { Ok, NotModified, Failed }

public enum FetchError
{
    InvalidUrl, NoNetwork, Timeout, Tls, NotFound, AccessDenied, RateLimited, ServerError, HttpError,
    Empty, TooLarge, TooManyRedirects, RedirectNotHttps,
}

/// <summary>The outcome of one download. <see cref="Bytes"/> is set only for <see cref="FetchStatus.Ok"/>.</summary>
public sealed record FetchResult(FetchStatus Status, byte[]? Bytes = null, string? ETag = null, string? LastModified = null, FetchError? Error = null, int? HttpStatus = null, string? Detail = null)
{
    public static FetchResult Fail(FetchError error, int? http = null, string? detail = null) => new(FetchStatus.Failed, Error: error, HttpStatus: http, Detail: detail);
}

/// <summary>
/// Downloads a public calendar: https only, no credentials, no cookies, at most <see cref="MaxRedirects"/> redirects (each to https),
/// at most <see cref="IcsSources.MaxBytes"/> (checked on the declared length and while reading), a time limit. A conditional request
/// is made when the validators of the last download are given. Nothing it reports contains the address.
/// </summary>
public sealed class IcsFetcher : IDisposable
{
    public const int MaxRedirects = 5;

    private readonly HttpClient _http;

    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <param name="handler">For tests; the default handler follows no redirect by itself and keeps no cookies.</param>
    public IcsFetcher(HttpMessageHandler? handler = null)
    {
        handler ??= new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(15),
        };
        _http = new HttpClient(handler, disposeHandler: true) { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
    }

    public async Task<FetchResult> FetchAsync(Uri uri, string? etag = null, string? lastModified = null, CancellationToken ct = default)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(Timeout);
        try
        {
            return await FetchCoreAsync(uri, etag, lastModified, limit.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return FetchResult.Fail(FetchError.Timeout);
        }
        catch (HttpRequestException e)
        {
            return FetchResult.Fail(Classify(e), detail: Describe(e));
        }
        catch (IOException e) when (e.InnerException is AuthenticationException)
        {
            return FetchResult.Fail(FetchError.Tls);
        }
        catch (IOException)
        {
            return FetchResult.Fail(FetchError.NoNetwork);
        }
        catch (AuthenticationException)
        {
            return FetchResult.Fail(FetchError.Tls);
        }
    }

    private async Task<FetchResult> FetchCoreAsync(Uri uri, string? etag, string? lastModified, CancellationToken ct)
    {
        var current = uri;
        for (var hop = 0; ; hop++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, current);
            request.Headers.UserAgent.ParseAdd("GGCalendar");
            request.Headers.Accept.ParseAdd("text/calendar, text/plain;q=0.8, */*;q=0.5");
            if (!string.IsNullOrEmpty(etag) && EntityTagHeaderValue.TryParse(etag, out var tag)) request.Headers.IfNoneMatch.Add(tag);
            if (!string.IsNullOrEmpty(lastModified) && DateTimeOffset.TryParse(lastModified, out var since)) request.Headers.IfModifiedSince = since;

            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            var status = (int)response.StatusCode;

            if (status is 301 or 302 or 303 or 307 or 308)
            {
                if (hop >= MaxRedirects) return FetchResult.Fail(FetchError.TooManyRedirects);
                var location = response.Headers.Location;
                if (location is null) return FetchResult.Fail(FetchError.HttpError, status);
                var target = location.IsAbsoluteUri ? location : new Uri(current, location);
                var check = SubscriptionUrls.Check(target.OriginalString);
                if (check.Uri is null) return FetchResult.Fail(FetchError.RedirectNotHttps);
                current = check.Uri;
                continue;
            }

            if (status == 304) return new FetchResult(FetchStatus.NotModified, ETag: etag, LastModified: lastModified);
            if (status is not (>= 200 and < 300)) return FetchResult.Fail(HttpError(status), status);

            if (response.Content.Headers.ContentLength is { } declared && declared > IcsSources.MaxBytes)
                return FetchResult.Fail(FetchError.TooLarge);

            byte[]? bytes;
            await using (var stream = await response.Content.ReadAsStreamAsync(ct))
                bytes = await ReadLimitedAsync(stream, ct);
            if (bytes is null) return FetchResult.Fail(FetchError.TooLarge);
            if (bytes.All(b => b is 0x20 or 0x09 or 0x0A or 0x0D)) return FetchResult.Fail(FetchError.Empty);

            return new FetchResult(FetchStatus.Ok, bytes, response.Headers.ETag?.ToString(),
                response.Content.Headers.LastModified?.ToString("R"));
        }
    }

    private static FetchError HttpError(int status) => status switch
    {
        404 or 410 => FetchError.NotFound,
        401 or 403 => FetchError.AccessDenied,
        429 => FetchError.RateLimited,
        >= 500 => FetchError.ServerError,
        _ => FetchError.HttpError,
    };

    private static async Task<byte[]?> ReadLimitedAsync(Stream input, CancellationToken ct)
    {
        using var all = new MemoryStream();
        var buffer = new byte[16 * 1024];
        long total = 0;
        int n;
        while ((n = await input.ReadAsync(buffer, ct)) > 0)
        {
            total += n;
            if (total > IcsSources.MaxBytes) return null;
            all.Write(buffer, 0, n);
        }

        return all.ToArray();
    }

    /// <summary>The types of the exceptions and the error code of the request, for the person who looks for the cause (never the message: it may hold the address).</summary>
    private static string Describe(Exception e)
    {
        var parts = new List<string>();
        for (Exception? x = e; x is not null; x = x.InnerException) parts.Add(x.GetType().Name + (x is HttpRequestException h ? $"({h.HttpRequestError})" : x is System.Net.Sockets.SocketException so ? $"({so.SocketErrorCode})" : ""));
        return string.Join(" > ", parts);
    }

    private static FetchError Classify(HttpRequestException e)
    {
        for (Exception? x = e; x is not null; x = x.InnerException)
            if (x is AuthenticationException) return FetchError.Tls;
        return FetchError.NoNetwork;
    }

    public void Dispose() => _http.Dispose();
}
