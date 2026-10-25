using GGCalendar.App.Services;
using GGCalendar.Core.Models;
using GGCalendar.Data;
using GGCalendar.Providers.Google;

namespace GGCalendar.Core.Tests.Support;

/// <summary>The real <see cref="GoogleSyncService"/> on an in-memory store, talking to a <see cref="FakeGoogle"/>.</summary>
public sealed class SyncRig : IDisposable
{
    private readonly string _accountsFile = Path.Combine(Path.GetTempPath(), $"ggcal-accounts-{Guid.NewGuid():N}.json");
    private readonly HttpClient _http;

    public SyncRig()
    {
        Google = new FakeGoogle();
        Store = new CalendarStore(":memory:");
        Store.UpsertCalendar(new CalendarInfo(FakeGoogle.LocalCalendarId, FakeGoogle.Account, "Principale", "#1A73E8"));

        var accounts = new GoogleAccountStore(_accountsFile);
        accounts.AddOrReplace(FakeGoogle.Account, "refresh-token");
        _http = new HttpClient(Google);
        Service = new GoogleSyncService(Store, accounts, _http);
        Service.Configure(new GoogleClientConfig("test.apps.googleusercontent.com", "secret"));
    }

    public FakeGoogle Google { get; }
    public CalendarStore Store { get; }
    public GoogleSyncService Service { get; }

    public Task<SyncResult> SyncAsync() => Service.SyncAllAsync(CancellationToken.None);

    /// <summary>The first sync is only ever expected to succeed: a failure would hide what a test wants to show.</summary>
    public async Task<SyncResult> SyncOkAsync()
    {
        var result = await SyncAsync();
        Assert.Empty(result.Errors);
        return result;
    }

    public static CalendarEvent NewEvent(string id, string title, string? colorHex = null) =>
        new(id, FakeGoogle.LocalCalendarId, title, new DateTime(2026, 10, 14, 9, 0, 0), new DateTime(2026, 10, 14, 10, 0, 0))
        {
            ColorHex = colorHex,
            Reminders = [],
        };

    /// <summary>Creates an event in the local store and queues it for Google, as the app does.</summary>
    public CalendarEvent AddLocal(CalendarEvent ev)
    {
        Store.UpsertEvent(ev);
        Store.Enqueue(ev.Id, ev.CalendarId, OutboxOperation.Create, ev);
        return ev;
    }

    /// <summary>Replaces an event in the local store and queues the change, with the previous version as its base.</summary>
    public CalendarEvent Edit(CalendarEvent before, CalendarEvent after)
    {
        Store.UpsertEvent(after);
        Store.Enqueue(after.Id, after.CalendarId, OutboxOperation.Update, after, before);
        return after;
    }

    public CalendarEvent? Local(string title) => Store.GetAllEvents().FirstOrDefault(e => e.Title == title);

    public void Dispose()
    {
        _http.Dispose();
        if (File.Exists(_accountsFile)) File.Delete(_accountsFile);
    }
}
