using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using GGCalendar.Data;
using GGCalendar.Providers.Google;

namespace GGCalendar.Core.Tests.Support;

/// <summary>
/// Google Calendar asked directly (plain REST calls) with the test account that the app has connected: an answer that does
/// not depend on the app's own code. It only ever touches events whose title starts with the test prefix.
/// </summary>
public sealed class TestGoogle : IDisposable
{
    private readonly HttpClient _http = new();
    private readonly string _events = "https://www.googleapis.com/calendar/v3/calendars/" + Uri.EscapeDataString(AppDriver.TestAccount) + "/events";

    private TestGoogle() { }

    public static async Task<TestGoogle> ConnectAsync()
    {
        var settings = new SettingsStore().Load();
        var config = GoogleClientConfig.Resolve(settings.GoogleClientId, settings.GoogleClientSecret);
        var account = new GoogleAccountStore().Load().FirstOrDefault(a => a.Email.Equals(AppDriver.TestAccount, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"L'account di prova {AppDriver.TestAccount} non è collegato all'app.");
        var tokens = await GoogleAuth.RefreshAsync(new HttpClient(), config.ClientId, config.ClientSecret,
            GoogleAccountStore.Unprotect(account.ProtectedRefreshToken), CancellationToken.None);
        var google = new TestGoogle();
        google._http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        return google;
    }

    private async Task<JsonElement> SendAsync(HttpMethod method, string url, JsonNode? body = null)
    {
        for (var attempt = 0; ; attempt++)
        {
            using var request = new HttpRequestMessage(method, url);
            if (body is not null) request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
            using var response = await _http.SendAsync(request);
            var text = await response.Content.ReadAsStringAsync();
            // Google refuses a burst of writes for a while (403 rateLimitExceeded): wait and try again.
            if ((int)response.StatusCode == 403 && text.Contains("rateLimitExceeded") && attempt < 8)
            {
                await Task.Delay(TimeSpan.FromSeconds(20));
                continue;
            }

            if (!response.IsSuccessStatusCode) throw new HttpRequestException($"Google {(int)response.StatusCode}: {text}");
            return string.IsNullOrWhiteSpace(text) ? default : JsonDocument.Parse(text).RootElement.Clone();
        }
    }

    public static string? RemoteIdOf(string localEventId) => new CalendarStore().GetRemoteInfo(localEventId)?.RemoteId;

    public Task<JsonElement> GetAsync(string remoteId) => SendAsync(HttpMethod.Get, $"{_events}/{Uri.EscapeDataString(remoteId)}");

    /// <summary>Events (series masters and modified occurrences, not expanded) whose text matches <paramref name="text"/>.</summary>
    public async Task<List<JsonElement>> FindAsync(string text)
    {
        var items = new List<JsonElement>();
        string? page = null;
        do
        {
            var result = await SendAsync(HttpMethod.Get,
                $"{_events}?singleEvents=false&showDeleted=false&maxResults=250&q={Uri.EscapeDataString(text)}" + (page is null ? "" : "&pageToken=" + Uri.EscapeDataString(page)));
            items.AddRange(result.GetProperty("items").EnumerateArray());
            page = result.TryGetProperty("nextPageToken", out var next) ? next.GetString() : null;
        }
        while (page is not null);

        return items;
    }

    /// <summary>The occurrences of a series in [from, to) as Google expands them (cancelled ones are left out).</summary>
    public async Task<List<JsonElement>> InstancesAsync(string seriesRemoteId, DateTime from, DateTime to)
    {
        static string Utc(DateTime d) => Uri.EscapeDataString(DateTime.SpecifyKind(d, DateTimeKind.Local).ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"));
        var result = await SendAsync(HttpMethod.Get,
            $"{_events}/{Uri.EscapeDataString(seriesRemoteId)}/instances?timeMin={Utc(from)}&timeMax={Utc(to)}&maxResults=250&showDeleted=false");
        return result.GetProperty("items").EnumerateArray().ToList();
    }

    /// <summary>True when the event no longer exists on Google (deleted: Google may still answer with status "cancelled").</summary>
    public async Task<bool> IsGoneAsync(string remoteId)
    {
        try
        {
            var e = await GetAsync(remoteId);
            return e.TryGetProperty("status", out var s) && s.GetString() == "cancelled";
        }
        catch (HttpRequestException ex) when (ex.Message.Contains("404") || ex.Message.Contains("410"))
        {
            return true;
        }
    }

    /// <summary>Changes fields of an event with a PATCH (JSON merge: a null removes the field).</summary>
    public Task<JsonElement> PatchAsync(string remoteId, JsonObject fields) =>
        SendAsync(HttpMethod.Patch, $"{_events}/{Uri.EscapeDataString(remoteId)}", fields);

    public Task DeleteAsync(string remoteId) => SendAsync(HttpMethod.Delete, $"{_events}/{Uri.EscapeDataString(remoteId)}");

    /// <summary>Deletes every event of the test calendar whose title starts with the test prefix (left by an interrupted run). Returns how many.</summary>
    public async Task<int> DeleteLeftoversAsync()
    {
        var count = 0;
        foreach (var item in await FindAsync(AppDriver.TestPrefix))
        {
            if (!item.TryGetProperty("summary", out var title) || title.GetString()?.StartsWith(AppDriver.TestPrefix, StringComparison.Ordinal) != true) continue;
            if (item.TryGetProperty("recurringEventId", out _)) continue; // an occurrence goes away with its series
            try
            {
                await DeleteAsync(item.GetProperty("id").GetString()!);
                count++;
            }
            catch (HttpRequestException ex) when (ex.Message.Contains("410") || ex.Message.Contains("404"))
            {
                // already gone
            }
        }

        return count;
    }

    // Calendars (never the primary one, and only those the test made: their name starts with the test prefix)

    private const string Calendars = "https://www.googleapis.com/calendar/v3";

    /// <summary>The calendars of the test account: id, name, role.</summary>
    public async Task<List<(string Id, string Name, string Role)>> ListCalendarsAsync()
    {
        var list = new List<(string, string, string)>();
        var answer = await SendAsync(HttpMethod.Get, Calendars + "/users/me/calendarList");
        foreach (var c in answer.GetProperty("items").EnumerateArray())
            list.Add((c.GetProperty("id").GetString()!, c.GetProperty("summary").GetString() ?? "", c.TryGetProperty("accessRole", out var r) ? r.GetString() ?? "" : ""));
        return list;
    }

    public async Task<string?> FindCalendarAsync(string name) => (await ListCalendarsAsync()).FirstOrDefault(c => c.Name == name).Id;

    public async Task<int> CountEventsAsync(string calendarId)
    {
        var answer = await SendAsync(HttpMethod.Get, $"{Calendars}/calendars/{Uri.EscapeDataString(calendarId)}/events?maxResults=2500&singleEvents=false");
        return answer.GetProperty("items").GetArrayLength();
    }

    public Task<JsonElement> InsertEventInAsync(string calendarId, string title, DateTime utcStart) =>
        SendAsync(HttpMethod.Post, $"{Calendars}/calendars/{Uri.EscapeDataString(calendarId)}/events", new JsonObject
        {
            ["summary"] = title,
            ["start"] = new JsonObject { ["dateTime"] = utcStart.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture) },
            ["end"] = new JsonObject { ["dateTime"] = utcStart.AddHours(1).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture) },
        });

    /// <summary>Deletes the calendars of the test account that the tests made (owned, with the test prefix in the name). Returns how many.</summary>
    public async Task<int> DeleteLeftoverCalendarsAsync()
    {
        var count = 0;
        foreach (var (id, name, role) in await ListCalendarsAsync())
        {
            if (role != "owner" || !name.StartsWith(AppDriver.TestPrefix, StringComparison.Ordinal) || id.Equals(AppDriver.TestAccount, StringComparison.OrdinalIgnoreCase)) continue;
            await SendAsync(HttpMethod.Delete, $"{Calendars}/calendars/{Uri.EscapeDataString(id)}");
            count++;
        }

        return count;
    }

    public void Dispose() => _http.Dispose();
}
