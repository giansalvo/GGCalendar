using System.Text.Json.Nodes;
using GGCalendar.App.ViewModels;
using GGCalendar.Core.Models;
using GGCalendar.Core.Tests.Support;

namespace GGCalendar.Core.Tests;

/// <summary>
/// Moving an event, or a whole series, from one calendar to another (T28-T31, issue #65), on the fake Google: the event is deleted at the origin and
/// created at the destination once, a second sync sends nothing, and a reopened app (a new view model on the same data) finds it in one place only.
/// The fake has one account with several calendars: T30 and T31 move between two calendars of that account.
/// </summary>
public class EventMoveTests
{
    private const string Local = "local-1";
    private const string Primary = FakeGoogle.LocalCalendarId;
    private static readonly DateTime Day = new(2026, 11, 10, 9, 0, 0, DateTimeKind.Utc);

    private static async Task<(SyncRig Rig, string SecondRemote, string Second)> SetupAsync()
    {
        var rig = new SyncRig();
        rig.Store.UpsertCalendar(new CalendarInfo(Local, MainViewModel.LocalAccountName, "Personale", "#1A73E8"));
        var remote = rig.Google.AddCalendar("Secondo");
        await rig.SyncOkAsync();
        return (rig, remote, FakeGoogle.Account + "|" + remote);
    }

    private static CalendarEvent NewEvent(string calendarId, string title) =>
        new("ev-" + title, calendarId, title, Day.ToLocalTime(), Day.ToLocalTime().AddHours(1)); // the standard reminder: a notification 30 minutes before

    /// <summary>Syncs twice: the first one sends the move, the second one must send nothing (no writes, no new request that changes data).</summary>
    private static async Task SyncTwiceAsync(SyncRig rig)
    {
        await rig.SyncOkAsync();
        var writes = rig.Google.Writes;
        await rig.SyncOkAsync();
        Assert.Equal(writes, rig.Google.Writes);
    }

    private static CalendarEvent InOnePlace(SyncRig rig, string title, string calendarId)
    {
        var found = rig.Store.GetAllEvents().Where(e => e.Title == title).ToList();
        var only = Assert.Single(found);
        Assert.Equal(calendarId, only.CalendarId);
        return only;
    }

    private static (string Method, int Minutes) ReminderOf(JsonObject onGoogle)
    {
        var first = onGoogle["reminders"]!["overrides"]![0]!;
        return (first["method"]!.GetValue<string>(), first["minutes"]!.GetValue<int>());
    }

    /// <summary>The app is closed and opened again (a new view model on the same data) and it syncs once more.</summary>
    private static async Task<MainViewModel> ReopenAsync(SyncRig rig)
    {
        var vm = new MainViewModel(rig.Store);
        await rig.SyncOkAsync();
        return vm;
    }

    [Fact]
    [Trait("TestCase", "T28")]
    public async Task T28_AnEventMovedFromALocalCalendarToGoogleExistsThereOnce()
    {
        var (rig, _, _) = await SetupAsync();
        using var _ = rig;
        var vm = new MainViewModel(rig.Store);
        var ev = NewEvent(Local, "Riunione");
        vm.AddEvent(ev);
        Assert.Equal(0, rig.Google.EventsIn(FakeGoogle.RemoteCalendar)); // a calendar of this PC sends nothing

        vm.ChangeCalendar(ev, Primary);
        await SyncTwiceAsync(rig);

        var onGoogle = Assert.Single(rig.Google.EventsOfCalendar(FakeGoogle.RemoteCalendar));
        Assert.Equal("Riunione", onGoogle["summary"]!.GetValue<string>());
        Assert.Equal(("popup", 30), ReminderOf(onGoogle));
        InOnePlace(rig, "Riunione", Primary);

        await ReopenAsync(rig);
        Assert.Single(rig.Google.EventsOfCalendar(FakeGoogle.RemoteCalendar));
        InOnePlace(rig, "Riunione", Primary);
    }

    [Fact]
    [Trait("TestCase", "T29")]
    public async Task T29_AnEventMovedFromGoogleToALocalCalendarIsGoneFromGoogleAndStaysHere()
    {
        var (rig, _, _) = await SetupAsync();
        using var _ = rig;
        var remoteId = rig.Google.Seed("Riunione", Day);
        await rig.SyncOkAsync();
        var vm = new MainViewModel(rig.Store);
        var ev = vm.AllEvents.Single(e => e.Title == "Riunione");

        vm.ChangeCalendar(ev, Local);
        await SyncTwiceAsync(rig);

        Assert.False(rig.Google.Has(remoteId), "L'evento spostato esiste ancora su Google.");
        Assert.Equal(0, rig.Google.EventsIn(FakeGoogle.RemoteCalendar));
        var kept = InOnePlace(rig, "Riunione", Local);
        Assert.Null(rig.Store.GetRemoteInfo(kept.Id)?.RemoteId); // nothing links it to Google any more

        await ReopenAsync(rig);
        Assert.Equal(0, rig.Google.EventsIn(FakeGoogle.RemoteCalendar));
        InOnePlace(rig, "Riunione", Local);
        Assert.DoesNotContain(rig.Store.GetPending(), p => p.EventId == kept.Id); // and nothing is queued for it
    }

    [Fact]
    [Trait("TestCase", "T30")]
    public async Task T30_AnEventMovedBetweenTwoGoogleCalendarsExistsOnlyInTheDestination()
    {
        var (rig, secondRemote, second) = await SetupAsync();
        using var _ = rig;
        var vm = new MainViewModel(rig.Store);
        var ev = NewEvent(Primary, "Riunione");
        vm.AddEvent(ev);
        await rig.SyncOkAsync();
        var before = Assert.Single(rig.Google.EventsOfCalendar(FakeGoogle.RemoteCalendar));

        vm.ChangeCalendar(ev, second);
        await SyncTwiceAsync(rig);

        Assert.Equal(0, rig.Google.EventsIn(FakeGoogle.RemoteCalendar));
        var after = Assert.Single(rig.Google.EventsOfCalendar(secondRemote));
        Assert.Equal(("Riunione", before["start"]!["dateTime"]!.GetValue<string>(), before["end"]!["dateTime"]!.GetValue<string>(), ("popup", 30)),
            (after["summary"]!.GetValue<string>(), after["start"]!["dateTime"]!.GetValue<string>(), after["end"]!["dateTime"]!.GetValue<string>(), ReminderOf(after)));
        InOnePlace(rig, "Riunione", second);

        await ReopenAsync(rig);
        Assert.Equal(0, rig.Google.EventsIn(FakeGoogle.RemoteCalendar)); // it does not come back to where it was
        Assert.Single(rig.Google.EventsOfCalendar(secondRemote));
        InOnePlace(rig, "Riunione", second);
    }

    [Fact]
    [Trait("TestCase", "T31")]
    public async Task T31_ASeriesWithAModifiedOccurrenceMovesWholeToTheOtherCalendar()
    {
        var (rig, secondRemote, second) = await SetupAsync();
        using var _ = rig;
        var master = rig.Google.SeedSeries("Serie", Day, 3);
        rig.Google.SetInstanceSummary(master, Day.AddDays(1), "Serie cambiata");
        await rig.SyncOkAsync();
        var vm = new MainViewModel(rig.Store);
        var series = vm.AllEvents.Single(e => e.Title == "Serie");
        var changed = vm.AllEvents.Single(e => e.Title == "Serie cambiata");
        Assert.Equal((series.Id, series.Start.AddDays(1)), (changed.SeriesMasterId, changed.OriginalStart));

        vm.ChangeCalendar(series, second);
        await SyncTwiceAsync(rig);

        Assert.Equal(0, rig.Google.EventsIn(FakeGoogle.RemoteCalendar)); // neither the series nor its modified occurrence stay
        void AssertAtDestination()
        {
            var onGoogle = rig.Google.EventsOfCalendar(secondRemote);
            Assert.Equal(2, onGoogle.Count);
            var newSeries = Assert.Single(onGoogle, e => e["recurrence"] is not null);
            Assert.Equal("Serie", newSeries["summary"]!.GetValue<string>());
            var newChanged = Assert.Single(onGoogle, e => e["recurringEventId"] is not null);
            Assert.Equal(("Serie cambiata", newSeries["id"]!.GetValue<string>()), (newChanged["summary"]!.GetValue<string>(), newChanged["recurringEventId"]!.GetValue<string>()));

            var inApp = rig.Store.GetAllEvents().Where(e => e.Title is "Serie" or "Serie cambiata").ToList();
            Assert.Equal(2, inApp.Count);
            Assert.All(inApp, e => Assert.Equal(second, e.CalendarId));
            var appSeries = inApp.Single(e => e.Title == "Serie");
            var appChanged = inApp.Single(e => e.Title == "Serie cambiata");
            Assert.Equal((appSeries.Id, appSeries.Start.AddDays(1)), (appChanged.SeriesMasterId, appChanged.OriginalStart)); // the same days and titles as before
        }

        AssertAtDestination();
        await ReopenAsync(rig);
        Assert.Equal(0, rig.Google.EventsIn(FakeGoogle.RemoteCalendar));
        AssertAtDestination();
    }
}
