using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using GGCalendar.Core.Models;
using GGCalendar.Core.Localization;

namespace GGCalendar.Providers.Google;

public sealed record GoogleCalendarEntry(string Id, string Name, string ColorHex, bool IsPrimary, bool CanWrite, IReadOnlyList<EventReminder> DefaultReminders, bool IsOwner = false);

public sealed record GoogleSyncPage(IReadOnlyList<JsonElement> Items, string? NextSyncToken);

public sealed class GoogleApiException(HttpStatusCode status, string message, string? reason = null, TimeSpan? retryAfter = null) : Exception(message)
{
    public HttpStatusCode Status { get; } = status;

    /// <summary>The first "reason" of Google's error body (rateLimitExceeded, notFound...), if there is one.</summary>
    public string? Reason { get; } = reason;

    /// <summary>How long Google asked to wait before trying again (Retry-After), if it said.</summary>
    public TimeSpan? RetryAfter { get; } = retryAfter;

    private static readonly HashSet<string> LimitReasons = new(StringComparer.OrdinalIgnoreCase)
    {
        "rateLimitExceeded", "userRateLimitExceeded", "quotaExceeded", "dailyLimitExceeded", "calendarUsageLimitsExceeded",
    };

    /// <summary>
    /// Google is refusing for now, not for good: a limit on the speed or the quota (403 with one of the limit reasons, or 429) or a
    /// problem on its side (5xx). The same request can succeed later, so it must not be dropped.
    /// </summary>
    public bool IsTemporary =>
        Status == HttpStatusCode.TooManyRequests || (int)Status >= 500 || (Status == HttpStatusCode.Forbidden && Reason is not null && LimitReasons.Contains(Reason));
}

/// <summary>Thin Calendar v3 client for one account; refreshes the access token on demand.</summary>
public sealed class GoogleCalendarClient
{
    private const string Base = "https://www.googleapis.com/calendar/v3";
    private const string BaseHost = "https://www.googleapis.com";
    private const string BatchUrl = "https://www.googleapis.com/batch/calendar/v3";

    private readonly HttpClient _http;
    private readonly string _clientId;
    private readonly string _clientSecret;
    private readonly string _refreshToken;
    private GoogleTokens? _tokens;

    public GoogleCalendarClient(HttpClient http, GoogleClientConfig config, string refreshToken)
    {
        _http = http;
        _clientId = config.ClientId;
        _clientSecret = config.ClientSecret;
        _refreshToken = refreshToken;
    }

    private async Task<string> TokenAsync(CancellationToken ct)
    {
        if (_tokens is null || _tokens.ExpiresAt <= DateTimeOffset.UtcNow)
            _tokens = await GoogleAuth.RefreshAsync(_http, _clientId, _clientSecret, _refreshToken, ct);
        return _tokens.AccessToken;
    }

    private async Task<JsonElement> SendAsync(HttpMethod method, string url, JsonNode? body, CancellationToken ct, string? ifMatch = null)
    {
        using var req = new HttpRequestMessage(method, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await TokenAsync(ct));
        if (ifMatch is not null) req.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        if (body is not null) req.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");

        using var res = await _http.SendAsync(req, ct);
        var text = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode)
            throw new GoogleApiException(res.StatusCode, $"Google Calendar: {(int)res.StatusCode} {text}", ReasonOf(text), RetryAfterOf(res));
        return string.IsNullOrWhiteSpace(text) ? default : JsonDocument.Parse(text).RootElement.Clone();
    }

    private static string? ReasonOf(string text)
    {
        try
        {
            using var doc = JsonDocument.Parse(text);
            return doc.RootElement.TryGetProperty("error", out var error) && error.TryGetProperty("errors", out var errors)
                && errors.ValueKind == JsonValueKind.Array && errors.GetArrayLength() > 0 && errors[0].TryGetProperty("reason", out var reason)
                ? reason.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static TimeSpan? RetryAfterOf(HttpResponseMessage response)
    {
        var after = response.Headers.RetryAfter;
        if (after?.Delta is { } delta) return delta;
        return after?.Date is { } date && date > DateTimeOffset.UtcNow ? date - DateTimeOffset.UtcNow : null;
    }

    public async Task<string> GetAccountEmailAsync(CancellationToken ct)
    {
        // The primary calendar id is the account email.
        var e = await SendAsync(HttpMethod.Get, $"{Base}/calendars/primary", null, ct);
        return e.GetProperty("id").GetString()!;
    }

    public async Task<IReadOnlyList<GoogleCalendarEntry>> ListCalendarsAsync(CancellationToken ct)
    {
        var list = new List<GoogleCalendarEntry>();
        string? page = null;
        do
        {
            var url = $"{Base}/users/me/calendarList?minAccessRole=reader" + (page is null ? "" : "&pageToken=" + Uri.EscapeDataString(page));
            var r = await SendAsync(HttpMethod.Get, url, null, ct);
            foreach (var c in r.GetProperty("items").EnumerateArray())
            {
                var role = c.TryGetProperty("accessRole", out var ar) ? ar.GetString() : "reader";
                var name = c.TryGetProperty("summaryOverride", out var so) ? so.GetString() : c.GetProperty("summary").GetString();
                list.Add(new GoogleCalendarEntry(
                    c.GetProperty("id").GetString()!, name ?? "",
                    c.TryGetProperty("backgroundColor", out var bc) ? bc.GetString()! : "#1A73E8",
                    c.TryGetProperty("primary", out var p) && p.GetBoolean(),
                    role is "owner" or "writer",
                    GoogleEventMapper.ParseDefaultReminders(c),
                    role == "owner"));
            }

            page = r.TryGetProperty("nextPageToken", out var np) ? np.GetString() : null;
        }
        while (page is not null);
        return list;
    }

    /// <summary>Creates a secondary calendar of the account (calendars.insert); returns its id. The colour is set apart: see <see cref="SetCalendarColorAsync"/>.</summary>
    public async Task<string> CreateCalendarAsync(string name, string timeZone, CancellationToken ct)
    {
        var body = new JsonObject { ["summary"] = name, ["timeZone"] = timeZone };
        var created = await SendAsync(HttpMethod.Post, $"{Base}/calendars", body, ct);
        return created.GetProperty("id").GetString()!;
    }

    /// <summary>Sets the colour of a calendar in the list of the account (calendars.insert cannot): background as given, text readable on it.</summary>
    public Task SetCalendarColorAsync(string calendarId, string colorHex, CancellationToken ct) => UpdateCalendarListEntryAsync(calendarId, colorHex, false, ct);

    /// <summary>
    /// Changes the entry of a calendar in the list of the account (calendarList.patch): the colour (background and text) and/or the personal name
    /// removed. A personal name, when there is one, hides the real name of the calendar from the person: it is removed when the calendar is renamed.
    /// </summary>
    public async Task UpdateCalendarListEntryAsync(string calendarId, string? colorHex, bool clearPersonalName, CancellationToken ct)
    {
        var body = new JsonObject();
        if (colorHex is not null)
        {
            body["backgroundColor"] = colorHex;
            body["foregroundColor"] = CalendarColors.TextFor(colorHex);
        }

        if (clearPersonalName) body["summaryOverride"] = null; // a null removes the field in a patch
        await SendAsync(HttpMethod.Patch, $"{Base}/users/me/calendarList/{Uri.EscapeDataString(calendarId)}?colorRgbFormat=true", body, ct);
    }

    /// <summary>Renames a calendar (calendars.patch): its real name, seen by everybody it is shared with. Only its owner can.</summary>
    public async Task RenameCalendarAsync(string calendarId, string name, CancellationToken ct) =>
        await SendAsync(HttpMethod.Patch, $"{Base}/calendars/{Uri.EscapeDataString(calendarId)}", new JsonObject { ["summary"] = name }, ct);

    /// <summary>
    /// Deletes a secondary calendar with all its events (calendars.delete): one request whatever the number of events, and for everybody it is
    /// shared with. Guests of its events are not told. Already gone (404, 410) is not an error.
    /// </summary>
    public async Task DeleteCalendarAsync(string calendarId, CancellationToken ct)
    {
        try
        {
            await SendAsync(HttpMethod.Delete, $"{Base}/calendars/{Uri.EscapeDataString(calendarId)}", null, ct);
        }
        catch (GoogleApiException ex) when (ex.Status is HttpStatusCode.NotFound or HttpStatusCode.Gone)
        {
            // Already deleted remotely.
        }
    }

    /// <summary>
    /// Lists events. Without a sync token it fetches everything; with one, only the changes (including cancellations).
    /// A 410 means the token expired: the caller must do a full sync.
    /// </summary>
    public async Task<GoogleSyncPage> ListEventsAsync(string calendarId, string? syncToken, CancellationToken ct)
    {
        var items = new List<JsonElement>();
        string? page = null;
        string? next = null;
        do
        {
            var q = new List<string> { "singleEvents=false", "maxResults=250", "showDeleted=true" };
            if (syncToken is not null) q.Add("syncToken=" + Uri.EscapeDataString(syncToken));
            if (page is not null) q.Add("pageToken=" + Uri.EscapeDataString(page));
            var r = await SendAsync(HttpMethod.Get, $"{Base}/calendars/{Uri.EscapeDataString(calendarId)}/events?" + string.Join("&", q), null, ct);
            items.AddRange(r.GetProperty("items").EnumerateArray());
            page = r.TryGetProperty("nextPageToken", out var np) ? np.GetString() : null;
            if (r.TryGetProperty("nextSyncToken", out var st)) next = st.GetString();
        }
        while (page is not null);
        return new GoogleSyncPage(items, next);
    }

    /// <summary>
    /// Creates an event. The id chosen by the app is sent along (Google accepts 5-1024 characters among 0-9 and a-v), so that if the
    /// answer is lost and the same request is sent again Google answers 409 (already there) instead of making a second event.
    /// </summary>
    public Task<JsonElement> InsertAsync(string calendarId, CalendarEvent ev, CancellationToken ct, bool withClientId = true)
    {
        var body = GoogleEventMapper.ToGoogle(ev, ev.AddMeet && ev.MeetLink is null);
        if (withClientId && IsValidClientId(ev.Id)) body["id"] = ev.Id;
        return SendAsync(HttpMethod.Post, EventsUrl(calendarId, ev, null), body, ct);
    }

    private static bool IsValidClientId(string id) => id.Length is >= 5 and <= 1024 && id.All(c => c is (>= '0' and <= '9') or (>= 'a' and <= 'v'));

    /// <summary>
    /// Updates an event. With <paramref name="etag"/> the update only succeeds if the event has not changed on Google since
    /// that version; otherwise Google answers 412 (<see cref="HttpStatusCode.PreconditionFailed"/>).
    /// </summary>
    public Task<JsonElement> PatchAsync(string calendarId, string remoteId, CalendarEvent ev, CancellationToken ct, string? etag = null) =>
        SendAsync(HttpMethod.Patch, EventsUrl(calendarId, ev, remoteId), GoogleEventMapper.ToGoogle(ev, ev.AddMeet && ev.MeetLink is null), ct, etag);

    /// <summary>The answer to one call of a batch: its status, its body (default if none) and, for a refusal, the reason and the wait Google asked for.</summary>
    public sealed record BatchAnswer(int Status, JsonElement Body, string? Reason)
    {
        public bool IsSuccess => Status is >= 200 and < 300;

        /// <summary>Google is refusing for now (limit of speed or quota, or a problem on its side): the same call can succeed later.</summary>
        public bool IsTemporary => new GoogleApiException((HttpStatusCode)Status, "", Reason).IsTemporary;
    }

    /// <summary>The most calls Google takes in one batch request.</summary>
    public const int MaxBatchSize = 50;

    /// <summary>
    /// Creates up to <see cref="MaxBatchSize"/> events with one request (the batch endpoint of the Calendar API): much faster than one
    /// request each, and Google does not refuse the speed that it refuses for single requests. The answers come back in the order of
    /// <paramref name="events"/>; a call can fail without the others failing.
    /// </summary>
    public async Task<IReadOnlyList<BatchAnswer>> InsertBatchAsync(string calendarId, IReadOnlyList<CalendarEvent> events, CancellationToken ct)
    {
        if (events.Count is 0 or > MaxBatchSize) throw new ArgumentOutOfRangeException(nameof(events));
        var boundary = "batch_" + Guid.NewGuid().ToString("N");
        var sb = new StringBuilder();
        for (var i = 0; i < events.Count; i++)
        {
            var ev = events[i];
            var body = GoogleEventMapper.ToGoogle(ev, ev.AddMeet && ev.MeetLink is null);
            if (IsValidClientId(ev.Id)) body["id"] = ev.Id;
            var path = EventsUrl(calendarId, ev, null)[BaseHost.Length..];
            sb.Append($"--{boundary}\r\nContent-Type: application/http\r\nContent-ID: <item{i}>\r\n\r\n");
            sb.Append($"POST {path}\r\nContent-Type: application/json\r\n\r\n{body.ToJsonString()}\r\n");
        }

        sb.Append($"--{boundary}--\r\n");
        using var req = new HttpRequestMessage(HttpMethod.Post, BatchUrl);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await TokenAsync(ct));
        req.Content = new StringContent(sb.ToString(), Encoding.UTF8);
        req.Content.Headers.ContentType = MediaTypeHeaderValue.Parse($"multipart/mixed; boundary={boundary}");
        using var res = await _http.SendAsync(req, ct);
        var text = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode) throw new GoogleApiException(res.StatusCode, $"Google Calendar: {(int)res.StatusCode} {text}", ReasonOf(text), RetryAfterOf(res));
        var replyBoundary = res.Content.Headers.ContentType?.Parameters.FirstOrDefault(p => p.Name == "boundary")?.Value?.Trim('"')
            ?? throw new GoogleApiException(res.StatusCode, L.T("google.batch_unrecognized"));
        return ParseBatch(text, replyBoundary, events.Count);
    }

    /// <summary>Splits the multipart answer of a batch into one answer per call, in the order of the calls (matched by Content-ID).</summary>
    internal static IReadOnlyList<BatchAnswer> ParseBatch(string text, string boundary, int count)
    {
        var answers = new BatchAnswer?[count];
        foreach (var part in text.Split("--" + boundary).Skip(1))
        {
            var id = System.Text.RegularExpressions.Regex.Match(part, @"Content-ID:\s*<response-item(\d+)>", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            var status = System.Text.RegularExpressions.Regex.Match(part, @"HTTP/1\.[01]\s+(\d{3})");
            if (!id.Success || !status.Success || !int.TryParse(id.Groups[1].Value, out var index) || index >= count) continue;
            // The body follows the headers of the inner response: after the second blank line of the part.
            var afterStatus = part[status.Index..];
            var blank = afterStatus.IndexOf("\r\n\r\n", StringComparison.Ordinal);
            var raw = blank < 0 ? "" : afterStatus[(blank + 4)..].Trim();
            JsonElement body = default;
            string? reason = null;
            if (raw.Length > 0)
            {
                try
                {
                    body = JsonDocument.Parse(raw).RootElement.Clone();
                    reason = ReasonOf(raw);
                }
                catch (JsonException)
                {
                    // not JSON: keep only the status
                }
            }

            answers[index] = new BatchAnswer(int.Parse(status.Groups[1].Value), body, reason);
        }

        // A call without an answer is a problem on Google's side: treat it as a temporary failure so it is tried again.
        return answers.Select(a => a ?? new BatchAnswer(503, default, "backendError")).ToList();
    }

    public Task<JsonElement> GetEventAsync(string calendarId, string remoteId, CancellationToken ct) =>
        SendAsync(HttpMethod.Get, $"{Base}/calendars/{Uri.EscapeDataString(calendarId)}/events/{Uri.EscapeDataString(remoteId)}", null, ct);

    public async Task DeleteAsync(string calendarId, string remoteId, CancellationToken ct)
    {
        try
        {
            await SendAsync(HttpMethod.Delete, $"{Base}/calendars/{Uri.EscapeDataString(calendarId)}/events/{Uri.EscapeDataString(remoteId)}?sendUpdates=all", null, ct);
        }
        catch (GoogleApiException ex) when (ex.Status is HttpStatusCode.NotFound or HttpStatusCode.Gone)
        {
            // Already deleted remotely.
        }
    }

    private static string EventsUrl(string calendarId, CalendarEvent ev, string? remoteId)
    {
        var url = $"{Base}/calendars/{Uri.EscapeDataString(calendarId)}/events" + (remoteId is null ? "" : "/" + Uri.EscapeDataString(remoteId));
        return url + $"?conferenceDataVersion=1&supportsAttachments=true&sendUpdates={(ev.SendInvites ? "all" : "none")}";
    }
}
