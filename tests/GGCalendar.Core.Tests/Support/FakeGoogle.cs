using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace GGCalendar.Core.Tests.Support;

/// <summary>
/// A tiny in-memory Google Calendar for the sync tests: one account with one calendar ("primary"), the OAuth token
/// endpoint, the calendar list and the events (list with or without sync token, get, insert, patch with If-Match, delete).
/// PATCH follows the JSON merge semantics Google uses: a null removes the field, an object is merged.
/// </summary>
public sealed class FakeGoogle : HttpMessageHandler
{
    public const string Account = "me@example.com";
    public const string RemoteCalendar = "primary";
    public const string LocalCalendarId = Account + "|" + RemoteCalendar;

    private sealed class Stored(JsonObject body)
    {
        public JsonObject Body { get; } = body;
        public long Version { get; set; }
        public bool Deleted { get; set; }
    }

    private readonly Dictionary<string, Stored> _events = [];
    private long _version;
    private int _nextId = 1;

    /// <summary>Every request received, as "METHOD path?query".</summary>
    public List<string> Requests { get; } = [];

    /// <summary>When true, any list request with a sync token is answered 410 Gone (the token expired).</summary>
    public bool ExpireSyncTokens { get; set; }

    /// <summary>After this many writes (insert, patch, delete) Google answers 403 rateLimitExceeded, as it does for a burst of requests. Null = no limit.</summary>
    public int? LimitAfterWrites { get; set; }

    /// <summary>The next write is answered with this status and reason (once), without being applied.</summary>
    public (HttpStatusCode Status, string Reason)? NextWriteFails { get; set; }

    /// <summary>The next insert is stored but its answer is lost (a network error reaches the app), as when the connection drops after Google created the event.</summary>
    public bool LoseNextInsertAnswer { get; set; }

    /// <summary>Writes (insert, patch, delete) received so far, including the refused ones.</summary>
    public int Writes { get; private set; }

    /// <summary>Called with the remote id of an event just before a PATCH is applied (e.g. to simulate a local edit during a sync).</summary>
    public Action<string>? OnPatch { get; set; }

    // Test helpers: what the "server" holds

    public string Seed(string summary, DateTime utcStart, string? colorId = null, string? id = null)
    {
        id ??= "g" + _nextId++;
        var body = new JsonObject
        {
            ["id"] = id,
            ["summary"] = summary,
            ["start"] = new JsonObject { ["dateTime"] = Iso(utcStart) },
            ["end"] = new JsonObject { ["dateTime"] = Iso(utcStart.AddHours(1)) },
        };
        if (colorId is not null) body["colorId"] = colorId;
        Store(id, body);
        return id;
    }

    /// <summary>Changes the version (etag) of an event without changing what the user sees, as Google does for a Meet or a guest reply.</summary>
    public void Touch(string id) => Store(id, _events[id].Body);

    public void SetSummary(string id, string summary)
    {
        _events[id].Body["summary"] = summary;
        Store(id, _events[id].Body);
    }

    public void Remove(string id)
    {
        _events[id].Deleted = true;
        _events[id].Version = ++_version;
    }

    public bool Has(string id) => _events.TryGetValue(id, out var e) && !e.Deleted;

    public IReadOnlyList<string> Ids => _events.Where(e => !e.Value.Deleted).Select(e => e.Key).ToList();

    public string? Summary(string id) => _events[id].Body["summary"]?.GetValue<string>();

    public string? ColorId(string id) => _events[id].Body["colorId"]?.GetValue<string>();

    public string? FindBySummary(string summary) =>
        _events.FirstOrDefault(e => !e.Value.Deleted && e.Value.Body["summary"]?.GetValue<string>() == summary).Key;

    private static string Iso(DateTime utc) => DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private void Store(string id, JsonObject body)
    {
        _version++;
        body["etag"] = $"\"{_version}\"";
        _events[id] = new Stored(body) { Version = _version };
    }

    // The HTTP surface

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var url = request.RequestUri!;
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add($"{request.Method} {url.AbsolutePath}{url.Query}");

        if (url.Host == "oauth2.googleapis.com") return Reply(HttpStatusCode.OK, """{"access_token":"at","expires_in":3600}""");
        if (url.AbsolutePath == "/batch/calendar/v3") return await BatchAsync(request, body!, cancellationToken);

        var path = url.AbsolutePath.Replace("/calendar/v3/", "");
        if (path == "users/me/calendarList")
        {
            return Reply(HttpStatusCode.OK, $$"""
                {"items":[{"id":"{{RemoteCalendar}}","summary":"Principale","backgroundColor":"#1A73E8","primary":true,"accessRole":"owner","defaultReminders":[]}]}
                """);
        }

        var parts = path.Split('/');
        if (parts is not ["calendars", _, "events", ..]) return Reply(HttpStatusCode.NotFound, "{}");
        var id = parts.Length > 3 ? Uri.UnescapeDataString(parts[3]) : null;
        if (request.Method != HttpMethod.Get)
        {
            Writes++;
            if (NextWriteFails is { } failure)
            {
                NextWriteFails = null;
                return Reply(failure.Status, ErrorBody(failure.Status, failure.Reason));
            }

            if (LimitAfterWrites is { } limit && Writes > limit) return Reply(HttpStatusCode.Forbidden, ErrorBody(HttpStatusCode.Forbidden, "rateLimitExceeded"));
        }

        var query = System.Web.HttpUtility.ParseQueryString(url.Query);

        if (id is null && request.Method == HttpMethod.Get) return List(query["syncToken"]);
        if (id is null && request.Method == HttpMethod.Post)
        {
            var sent = JsonNode.Parse(body!)!.AsObject();
            var created = sent["id"]?.GetValue<string>() ?? "g" + _nextId++;
            if (_events.ContainsKey(created)) return Reply(HttpStatusCode.Conflict, ErrorBody(HttpStatusCode.Conflict, "duplicate")); // an id is never reused, not even after a deletion
            var stored = new JsonObject { ["id"] = created };
            Merge(stored, sent);
            Store(created, stored);
            if (LoseNextInsertAnswer)
            {
                LoseNextInsertAnswer = false;
                throw new HttpRequestException("La connessione è stata interrotta.");
            }

            return Reply(HttpStatusCode.OK, stored.ToJsonString());
        }

        if (id is null || !_events.TryGetValue(id, out var current) || current.Deleted)
            return Reply(HttpStatusCode.NotFound, "{}");

        if (request.Method == HttpMethod.Get) return Reply(HttpStatusCode.OK, current.Body.ToJsonString());
        if (request.Method == HttpMethod.Delete)
        {
            Remove(id);
            return Reply(HttpStatusCode.NoContent, "");
        }

        if (request.Method == HttpMethod.Patch)
        {
            if (request.Headers.TryGetValues("If-Match", out var match) && match.First() != (string?)current.Body["etag"])
                return Reply(HttpStatusCode.PreconditionFailed, "{}");
            OnPatch?.Invoke(id);
            Merge(current.Body, JsonNode.Parse(body!)!.AsObject());
            Store(id, current.Body);
            return Reply(HttpStatusCode.OK, current.Body.ToJsonString());
        }

        return Reply(HttpStatusCode.MethodNotAllowed, "{}");
    }

    /// <summary>Batches received: each is one request that carries several calls.</summary>
    public int Batches { get; private set; }

    /// <summary>Runs each call of a multipart batch request as if it had arrived alone, and answers with a multipart body like Google's.</summary>
    private async Task<HttpResponseMessage> BatchAsync(HttpRequestMessage request, string body, CancellationToken cancellationToken)
    {
        Batches++;
        var boundary = request.Content!.Headers.ContentType!.Parameters.First(p => p.Name == "boundary").Value!.Trim('"');
        var reply = new StringBuilder();
        foreach (var part in body.Split("--" + boundary).Skip(1))
        {
            var id = System.Text.RegularExpressions.Regex.Match(part, @"Content-ID:\s*<item(\d+)>");
            var line = System.Text.RegularExpressions.Regex.Match(part, @"(GET|POST|PATCH|DELETE) (\S+)");
            if (!id.Success || !line.Success) continue;
            var blank = part.IndexOf("\r\n\r\n", part.IndexOf(line.Value, StringComparison.Ordinal), StringComparison.Ordinal);
            var json = blank < 0 ? "" : part[(blank + 4)..].Trim();
            using var sub = new HttpRequestMessage(new HttpMethod(line.Groups[1].Value), "https://www.googleapis.com" + line.Groups[2].Value);
            if (json.Length > 0) sub.Content = new StringContent(json, Encoding.UTF8, "application/json");
            using var answer = await SendAsync(sub, cancellationToken);
            var text = await answer.Content.ReadAsStringAsync(cancellationToken);
            reply.Append($"--response\r\nContent-Type: application/http\r\nContent-ID: <response-item{id.Groups[1].Value}>\r\n\r\n");
            reply.Append($"HTTP/1.1 {(int)answer.StatusCode} {answer.ReasonPhrase}\r\nContent-Type: application/json; charset=UTF-8\r\n\r\n{text}\r\n");
        }

        reply.Append("--response--\r\n");
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(reply.ToString(), Encoding.UTF8) };
        response.Content.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse("multipart/mixed; boundary=response");
        return response;
    }

    private HttpResponseMessage List(string? syncToken)
    {
        var items = new JsonArray();
        if (syncToken is null)
        {
            foreach (var e in _events.Values.Where(e => !e.Deleted)) items.Add(e.Body.DeepClone());
        }
        else
        {
            if (ExpireSyncTokens) return Reply(HttpStatusCode.Gone, "{}");
            var since = long.TryParse(syncToken.TrimStart('t'), out var v) ? v : 0;
            foreach (var e in _events.Values.Where(e => e.Version > since))
                items.Add(e.Deleted ? new JsonObject { ["id"] = e.Body["id"]!.GetValue<string>(), ["status"] = "cancelled" } : e.Body.DeepClone());
        }

        return Reply(HttpStatusCode.OK, new JsonObject { ["items"] = items, ["nextSyncToken"] = "t" + _version }.ToJsonString());
    }

    private static void Merge(JsonObject target, JsonObject patch)
    {
        foreach (var (key, value) in patch)
        {
            if (value is null) target.Remove(key);
            else if (value is JsonObject o)
            {
                if (target[key] is not JsonObject t) target[key] = t = new JsonObject();
                Merge(t, o);
            }
            else target[key] = value.DeepClone();
        }
    }

    private static string ErrorBody(HttpStatusCode status, string reason) =>
        new JsonObject
        {
            ["error"] = new JsonObject
            {
                ["errors"] = new JsonArray(new JsonObject { ["domain"] = "usageLimits", ["reason"] = reason, ["message"] = reason }),
                ["code"] = (int)status,
                ["message"] = reason,
            },
        }.ToJsonString();

    private static HttpResponseMessage Reply(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
}
