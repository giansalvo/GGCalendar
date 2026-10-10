using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using GGCalendar.Core.Models;

namespace GGCalendar.Providers.Google;

public sealed record GoogleCalendarEntry(string Id, string Name, string ColorHex, bool IsPrimary, bool CanWrite, IReadOnlyList<EventReminder> DefaultReminders);

public sealed record GoogleSyncPage(IReadOnlyList<JsonElement> Items, string? NextSyncToken);

public sealed class GoogleApiException(HttpStatusCode status, string message) : Exception(message)
{
    public HttpStatusCode Status { get; } = status;
}

/// <summary>Thin Calendar v3 client for one account; refreshes the access token on demand.</summary>
public sealed class GoogleCalendarClient
{
    private const string Base = "https://www.googleapis.com/calendar/v3";

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
        if (!res.IsSuccessStatusCode) throw new GoogleApiException(res.StatusCode, $"Google Calendar: {(int)res.StatusCode} {text}");
        return string.IsNullOrWhiteSpace(text) ? default : JsonDocument.Parse(text).RootElement.Clone();
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
                    GoogleEventMapper.ParseDefaultReminders(c)));
            }

            page = r.TryGetProperty("nextPageToken", out var np) ? np.GetString() : null;
        }
        while (page is not null);
        return list;
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

    public Task<JsonElement> InsertAsync(string calendarId, CalendarEvent ev, CancellationToken ct) =>
        SendAsync(HttpMethod.Post, EventsUrl(calendarId, ev, null), GoogleEventMapper.ToGoogle(ev, ev.AddMeet && ev.MeetLink is null), ct);

    public Task<JsonElement> PatchAsync(string calendarId, string remoteId, CalendarEvent ev, CancellationToken ct) =>
        SendAsync(HttpMethod.Patch, EventsUrl(calendarId, ev, remoteId), GoogleEventMapper.ToGoogle(ev, ev.AddMeet && ev.MeetLink is null), ct);

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
        return url + $"?conferenceDataVersion=1&sendUpdates={(ev.SendInvites ? "all" : "none")}";
    }
}
