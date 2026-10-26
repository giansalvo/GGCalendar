using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace GGCalendar.Core.Tests.Support;

/// <summary>
/// A tiny in-memory Google Calendar for the sync tests: one account with one calendar ("primary"), the OAuth token
/// endpoint, the calendar list and the events (list with or without sync token, get, insert, patch with If-Match, delete), and the
/// calendars themselves: other calendars can be added (owned, or read-only like the holidays), created (POST calendars), recoloured
/// (PATCH calendarList) and deleted (DELETE calendars, never the primary one or one the person does not own).
/// PATCH follows the JSON merge semantics Google uses: a null removes the field, an object is merged.
/// </summary>
public sealed class FakeGoogle : HttpMessageHandler
{
    public const string Account = "me@example.com";
    /// <summary>As on Google, the primary calendar has the email of the account as its id.</summary>
    public const string RemoteCalendar = Account;
    public const string LocalCalendarId = Account + "|" + RemoteCalendar;

    private sealed class Stored(JsonObject body)
    {
        public JsonObject Body { get; } = body;
        public long Version { get; set; }
        public bool Deleted { get; set; }
    }

    private Dictionary<string, Stored> _events = [];

    private sealed class CalendarRec(string id, string summary, string color, string role, bool primary, Dictionary<string, Stored> events)
    {
        public string Id { get; } = id;
        public string Summary { get; set; } = summary;

        /// <summary>The name only the person sees (summaryOverride): when there is one, the list shows it and not the real name.</summary>
        public string? PersonalName { get; set; }
        public string Color { get; set; } = color;
        public string Role { get; set; } = role;
        public bool Primary { get; } = primary;
        public Dictionary<string, Stored> Events { get; } = events;
    }

    private readonly List<CalendarRec> _calendars = [];
    private int _nextCalendar = 1;
    private long _version;
    private int _nextId = 1;

    public FakeGoogle() => _calendars.Add(new CalendarRec(RemoteCalendar, "Principale", "#1A73E8", "owner", true, _events));

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

    /// <summary>Every request fails as if there were no network (an exception, no answer).</summary>
    public bool Offline { get; set; }

    /// <summary>The tokens revoked through the revoke address of Google (when an account is disconnected).</summary>
    public List<string> RevokedTokens { get; } = [];

    /// <summary>What Google answers to a revoke (400 = the token was already invalid, which counts as revoked; 500 = it could not be done).</summary>
    public HttpStatusCode RevokeStatus { get; set; } = HttpStatusCode.OK;

    /// <summary>The bodies received to create calendars (POST calendars), in order.</summary>
    public List<string> CreatedBodies { get; } = [];

    /// <summary>The colours set on calendars (PATCH calendarList): the id, the query and the body.</summary>
    public List<(string Id, string Query, string Body)> ColorPatches { get; } = [];

    /// <summary>The renames received (PATCH calendars): the id and the new name.</summary>
    public List<(string Id, string Summary)> Renames { get; } = [];

    /// <summary>The ids of the calendars deleted so far.</summary>
    public List<string> DeletedCalendars { get; } = [];

    /// <summary>Called with the remote id of an event just before a PATCH is applied (e.g. to simulate a local edit during a sync).</summary>
    public Action<string>? OnPatch { get; set; }

    // Test helpers: what the "server" holds

    /// <summary>Adds a calendar to the list of the account. A role other than "owner" (for example "reader") makes it one that cannot be deleted.</summary>
    public string AddCalendar(string summary, string role = "owner", string color = "#9FE1E7", string? id = null)
    {
        id ??= $"cal{_nextCalendar++}@group.calendar.google.com";
        _calendars.Add(new CalendarRec(id, summary, color, role, false, []));
        return id;
    }

    public IReadOnlyList<string> CalendarIds => _calendars.Select(c => c.Id).ToList();

    public bool HasCalendar(string id) => _calendars.Any(c => c.Id == id);

    public string? CalendarSummary(string id) => _calendars.FirstOrDefault(c => c.Id == id)?.Summary;

    public string? CalendarColor(string id) => _calendars.FirstOrDefault(c => c.Id == id)?.Color;

    /// <summary>Puts an event in another calendar (not the primary one).</summary>
    public string SeedIn(string calendarId, string summary, DateTime utcStart)
    {
        var saved = _events;
        _events = _calendars.First(c => c.Id == calendarId).Events;
        try
        {
            return Seed(summary, utcStart);
        }
        finally
        {
            _events = saved;
        }
    }

    /// <summary>The name that the person alone sees on a calendar (it hides the real one in the list).</summary>
    public void SetPersonalName(string calendarId, string? name) => _calendars.First(c => c.Id == calendarId).PersonalName = name;

    /// <summary>The calendar is not the person's any more (another device gave it away): the role becomes writer.</summary>
    public void RemoveOwnership(string calendarId) => _calendars.First(c => c.Id == calendarId).Role = "writer";

    public string? PersonalNameOf(string calendarId) => _calendars.FirstOrDefault(c => c.Id == calendarId)?.PersonalName;

    public int EventsIn(string calendarId) => _calendars.First(c => c.Id == calendarId).Events.Count(e => !e.Value.Deleted);

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
        var saved = _events;
        try
        {
            return await SendCoreAsync(request, cancellationToken);
        }
        finally
        {
            _events = saved;
        }
    }

    /// <summary>Counts a write and refuses it when a test asked so (a limit, a failure); null when it goes on.</summary>
    private HttpResponseMessage? RefuseWrite()
    {
        Writes++;
        if (NextWriteFails is { } failure)
        {
            NextWriteFails = null;
            return Reply(failure.Status, ErrorBody(failure.Status, failure.Reason));
        }

        if (LimitAfterWrites is { } limit && Writes > limit) return Reply(HttpStatusCode.Forbidden, ErrorBody(HttpStatusCode.Forbidden, "rateLimitExceeded"));
        return null;
    }

    private async Task<HttpResponseMessage> SendCoreAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var url = request.RequestUri!;
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add($"{request.Method} {url.AbsolutePath}{url.Query}");
        if (Offline) throw new HttpRequestException("Nessuna connessione.");

        if (url.Host == "oauth2.googleapis.com" && url.AbsolutePath == "/revoke")
        {
            RevokedTokens.Add(Uri.UnescapeDataString((body ?? "").Replace("token=", "")));
            return Reply(RevokeStatus, "{}");
        }

        if (url.Host == "oauth2.googleapis.com") return Reply(HttpStatusCode.OK, """{"access_token":"at","expires_in":3600}""");
        if (url.AbsolutePath == "/batch/calendar/v3") return await BatchAsync(request, body!, cancellationToken);

        var path = url.AbsolutePath.Replace("/calendar/v3/", "");
        if (path == "users/me/calendarList")
        {
            var items = new JsonArray();
            foreach (var c in _calendars)
            {
                var item = new JsonObject { ["id"] = c.Id, ["summary"] = c.Summary, ["backgroundColor"] = c.Color, ["accessRole"] = c.Role, ["defaultReminders"] = new JsonArray() };
                if (c.Primary) item["primary"] = true;
                if (c.PersonalName is not null) item["summaryOverride"] = c.PersonalName;
                items.Add(item);
            }

            return Reply(HttpStatusCode.OK, new JsonObject { ["items"] = items }.ToJsonString());
        }

        var parts = path.Split('/');

        // The calendars themselves.
        if (parts is ["calendars"] && request.Method == HttpMethod.Post)
        {
            if (RefuseWrite() is { } refused) return refused;
            CreatedBodies.Add(body!);
            var sent = JsonNode.Parse(body!)!.AsObject();
            var created = AddCalendar(sent["summary"]?.GetValue<string>() ?? "");
            return Reply(HttpStatusCode.OK, new JsonObject { ["id"] = created, ["summary"] = sent["summary"]?.DeepClone(), ["timeZone"] = sent["timeZone"]?.DeepClone() }.ToJsonString());
        }

        if (parts is ["users", "me", "calendarList", _] && request.Method == HttpMethod.Patch)
        {
            if (RefuseWrite() is { } refused) return refused;
            var target = _calendars.FirstOrDefault(c => c.Id == Uri.UnescapeDataString(parts[3]));
            if (target is null) return Reply(HttpStatusCode.NotFound, "{}");
            ColorPatches.Add((target.Id, url.Query, body!));
            if (!url.Query.Contains("colorRgbFormat=true")) return Reply(HttpStatusCode.BadRequest, ErrorBody(HttpStatusCode.BadRequest, "invalid"));
            var patch = JsonNode.Parse(body!)!.AsObject();
            // As the real Google does with colours sent as RGB: any text colour but black and white is refused (found with the real Google: #1D1D1D gives 400).
            if (patch["foregroundColor"]?.GetValue<string>() is { } text && !text.Equals("#000000", StringComparison.OrdinalIgnoreCase) && !text.Equals("#FFFFFF", StringComparison.OrdinalIgnoreCase))
                return Reply(HttpStatusCode.BadRequest, ErrorBody(HttpStatusCode.BadRequest, "invalid"));
            if (patch["backgroundColor"]?.GetValue<string>() is { } color) target.Color = color;
            if (patch.ContainsKey("summaryOverride")) target.PersonalName = patch["summaryOverride"]?.GetValue<string>(); // a null removes it
            return Reply(HttpStatusCode.OK, "{}");
        }

        if (parts is ["calendars", _] && request.Method == HttpMethod.Patch)
        {
            if (RefuseWrite() is { } refused) return refused;
            var target = _calendars.FirstOrDefault(c => c.Id == Uri.UnescapeDataString(parts[1]));
            if (target is null) return Reply(HttpStatusCode.NotFound, "{}");
            if (target.Role != "owner") return Reply(HttpStatusCode.Forbidden, ErrorBody(HttpStatusCode.Forbidden, "forbidden"));
            var name = JsonNode.Parse(body!)!["summary"]?.GetValue<string>();
            if (name is not null)
            {
                target.Summary = name;
                Renames.Add((target.Id, name));
            }

            return Reply(HttpStatusCode.OK, "{}");
        }

        if (parts is ["calendars", _] && request.Method == HttpMethod.Delete)
        {
            if (RefuseWrite() is { } refused) return refused;
            var deleteId = Uri.UnescapeDataString(parts[1]);
            var target = _calendars.FirstOrDefault(c => c.Id == deleteId);
            if (target is null) return Reply(HttpStatusCode.NotFound, "{}");
            if (target.Primary) return Reply(HttpStatusCode.BadRequest, ErrorBody(HttpStatusCode.BadRequest, "invalid"));
            if (target.Role != "owner") return Reply(HttpStatusCode.Forbidden, ErrorBody(HttpStatusCode.Forbidden, "forbidden"));
            _calendars.Remove(target);
            DeletedCalendars.Add(deleteId);
            return Reply(HttpStatusCode.NoContent, "");
        }

        if (parts is not ["calendars", _, "events", ..]) return Reply(HttpStatusCode.NotFound, "{}");
        var calendar = _calendars.FirstOrDefault(c => c.Id == Uri.UnescapeDataString(parts[1]));
        if (calendar is null) return Reply(HttpStatusCode.NotFound, "{}");
        _events = calendar.Events;
        var id = parts.Length > 3 ? Uri.UnescapeDataString(parts[3]) : null;
        if (request.Method != HttpMethod.Get && RefuseWrite() is { } refusedWrite) return refusedWrite;

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
