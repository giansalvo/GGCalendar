using GGCalendar.App.Services;
using GGCalendar.Core.Models;
using GGCalendar.Core.Tests.Support;

namespace GGCalendar.Core.Tests;

/// <summary>A limit of requests of Google while the events are downloaded (a full download of a big account reaches it): calendars gone from Google go anyway, and the sync tries again by itself.</summary>
public class SyncPullLimitTests
{
    private static string Local(string remote) => FakeGoogle.Account + "|" + remote;

    [Fact]
    [Trait("TestCase", "T254")]
    public async Task T254_ACalendarDeletedOnGoogleGoesAwayEvenIfTheDownloadOfAnotherCalendarIsRefusedByTheLimit()
    {
        using var rig = new SyncRig();
        var gone = rig.Google.AddCalendar("TEST GGCAL");
        var other = rig.Google.AddCalendar("Altro");
        rig.Google.SeedIn(gone, "Evento nel calendario che sparisce", new DateTime(2027, 1, 5, 9, 0, 0, DateTimeKind.Utc));
        await rig.SyncOkAsync();
        Assert.Contains(rig.Store.GetCalendars(), c => c.Id == Local(gone));
        Assert.Contains(rig.Store.GetAllEvents(), e => e.Title == "Evento nel calendario che sparisce");

        rig.Google.RemoveCalendar(gone);
        rig.Google.QuotaOnEventList.Add(other); // Google refuses the events of the other calendar: the limit per minute

        var result = await rig.SyncAsync();

        Assert.DoesNotContain(rig.Store.GetCalendars(), c => c.Id == Local(gone)); // it went anyway
        Assert.DoesNotContain(rig.Store.GetAllEvents(), e => e.Title == "Evento nel calendario che sparisce");
        Assert.Contains(rig.Store.GetCalendars(), c => c.Id == Local(other)); // the one that is still there stays
        Assert.True(result.Changed);
    }

    [Fact]
    [Trait("TestCase", "T255")]
    public async Task T255_TheLimitWhileDownloadingGivesAClearMessageAndTheSyncIsTriedAgainAfterAWhile()
    {
        using var rig = new SyncRig();
        var now = new DateTimeOffset(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);
        rig.Service.Clock = () => now;
        await rig.SyncOkAsync();
        var title = "Inviato nonostante il limite";
        rig.AddLocal(new CalendarEvent("n", FakeGoogle.LocalCalendarId, title, new DateTime(2027, 2, 2, 9, 0, 0), new DateTime(2027, 2, 2, 10, 0, 0)) { Reminders = [] });
        rig.Google.QuotaOnEventList.Add(FakeGoogle.RemoteCalendar);

        var result = await rig.SyncAsync();

        var error = Assert.Single(result.Errors);
        Assert.Equal("sync.account_limited", error.Key); // a clear message, not the raw text of Google
        Assert.Contains(FakeGoogle.Account, error.Text);
        Assert.NotNull(result.RetryAt); // the app knows when to try again
        Assert.True(result.RetryAt > now);
        Assert.NotNull(rig.Google.FindBySummary(title)); // what was queued was sent before the download failed

        rig.Google.QuotaOnEventList.Clear();
        now = result.RetryAt!.Value.AddSeconds(5); // the time Google was asked to wait has passed
        await rig.SyncOkAsync();
        Assert.Equal(0, rig.Service.CountQueuedEvents());
    }
}
