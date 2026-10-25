using System.Net;
using System.Text;

namespace GGCalendar.Core.Tests.Support;

/// <summary>A web site made of routes, for the tests of the subscriptions: nothing goes on the network and every request is recorded.</summary>
public sealed class FakeSite : HttpMessageHandler
{
    private readonly Dictionary<string, Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>> _routes = new();

    /// <summary>The URLs asked, in order, as they arrived (the tests look for secrets in them, never in messages).</summary>
    public List<Uri> Requests { get; } = [];

    public List<HttpRequestMessage> Messages { get; } = [];

    public FakeSite Route(string path, Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
    {
        _routes[path] = handler;
        return this;
    }

    public FakeSite Serve(string path, string body, string? etag = null, string? lastModified = null) =>
        Route(path, (req, _) =>
        {
            var tag = etag is null ? null : new System.Net.Http.Headers.EntityTagHeaderValue(etag);
            if (tag is not null && req.Headers.IfNoneMatch.Any(t => t.Tag == tag.Tag)) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotModified));
            if (lastModified is not null && req.Headers.IfModifiedSince is { } since && since >= DateTimeOffset.Parse(lastModified))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotModified));

            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "text/calendar") };
            if (tag is not null) response.Headers.ETag = tag;
            if (lastModified is not null) response.Content.Headers.LastModified = DateTimeOffset.Parse(lastModified);
            return Task.FromResult(response);
        });

    public FakeSite Status(string path, HttpStatusCode code) =>
        Route(path, (_, _) => Task.FromResult(new HttpResponseMessage(code)));

    public FakeSite Redirect(string path, string to, HttpStatusCode code = HttpStatusCode.Found) =>
        Route(path, (_, _) =>
        {
            var response = new HttpResponseMessage(code);
            response.Headers.Location = new Uri(to, UriKind.RelativeOrAbsolute);
            return Task.FromResult(response);
        });

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Requests.Add(request.RequestUri!);
        Messages.Add(request);
        if (_routes.TryGetValue(request.RequestUri!.AbsolutePath, out var route)) return await route(request, ct);
        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }
}

/// <summary>A body that says nothing about its length (like a chunked response) and gives <paramref name="length"/> bytes.</summary>
public sealed class UnknownLengthContent(long length) : HttpContent
{
    protected override async Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context)
    {
        var chunk = new byte[64 * 1024];
        Array.Fill(chunk, (byte)'A');
        for (long sent = 0; sent < length; sent += chunk.Length) await stream.WriteAsync(chunk.AsMemory(0, (int)Math.Min(chunk.Length, length - sent)));
    }

    protected override bool TryComputeLength(out long len)
    {
        len = 0;
        return false;
    }
}

/// <summary>A body that declares a length and fails if anyone reads it: proves that a too large file is refused before it is read.</summary>
public sealed class DeclaredLengthContent(long length) : HttpContent
{
    protected override Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context) =>
        throw new InvalidOperationException("the body must not be read");

    protected override Task<Stream> CreateContentReadStreamAsync() => throw new InvalidOperationException("the body must not be read");

    protected override bool TryComputeLength(out long len)
    {
        len = length;
        return true;
    }
}
