using GGCalendar.Core.Models;
using GGCalendar.Data;
using Xunit;

namespace GGCalendar.Core.Tests;

public class CalendarStoreTests
{
    private static CalendarEvent Ev(string id, DateTime start, int hours = 1, string? rrule = null) =>
        new(id, "c1", "Titolo " + id, start, start.AddHours(hours), Location: "Roma")
        {
            Recurrence = rrule,
            Attendees = [new EventAttendee("a@b.it", true)],
            Reminders = [new EventReminder(ReminderMethod.Alarm, 15, "C:\\x.mp3")],
            Visibility = EventVisibility.Private,
        };

    private static CalendarStore Create()
    {
        var store = new CalendarStore(":memory:");
        store.UpsertCalendar(new CalendarInfo("c1", "Locale", "Personale", "#1A73E8"));
        return store;
    }

    [Fact]
    public void EventRoundTripKeepsAllFields()
    {
        var store = Create();
        var ev = Ev("e1", new DateTime(2026, 5, 4, 9, 0, 0));
        store.UpsertEvent(ev);
        var back = Assert.Single(store.GetAllEvents());
        Assert.Equal(ev.Title, back.Title);
        Assert.Equal(ev.Start, back.Start);
        Assert.Equal(EventVisibility.Private, back.Visibility);
        Assert.Equal(ReminderMethod.Alarm, back.Reminders[0].Method);
        Assert.Equal("a@b.it", back.Attendees[0].Email);
    }

    [Fact]
    public void UpsertReplacesAndDeleteRemoves()
    {
        var store = Create();
        store.UpsertEvent(Ev("e1", new DateTime(2026, 5, 4, 9, 0, 0)));
        store.UpsertEvent(Ev("e1", new DateTime(2026, 5, 5, 9, 0, 0)));
        Assert.Equal(new DateTime(2026, 5, 5, 9, 0, 0), Assert.Single(store.GetAllEvents()).Start);
        store.DeleteEvent("e1");
        Assert.Empty(store.GetAllEvents());
    }

    [Fact]
    public void RangeQueryReturnsOverlappingAndRecurring()
    {
        var store = Create();
        store.UpsertEvent(Ev("in", new DateTime(2026, 5, 4, 9, 0, 0)));
        store.UpsertEvent(Ev("out", new DateTime(2026, 6, 4, 9, 0, 0)));
        store.UpsertEvent(Ev("rec", new DateTime(2026, 1, 1, 9, 0, 0), rrule: "FREQ=WEEKLY"));
        var ids = store.GetEvents(new DateTime(2026, 5, 4), new DateTime(2026, 5, 11)).Select(e => e.Id).Order().ToList();
        Assert.Equal(["in", "rec"], ids);
    }

    [Fact]
    public void CalendarVisibilityPersistsAndDeleteCascades()
    {
        var store = Create();
        store.SetCalendarVisible("c1", false);
        Assert.False(Assert.Single(store.GetCalendars()).IsVisible);
        store.UpsertEvent(Ev("e1", DateTime.Today));
        store.Enqueue("e1", "c1", OutboxOperation.Update, Ev("e1", DateTime.Today));
        store.SetSyncToken("c1", "tok");
        store.DeleteCalendar("c1");
        Assert.Empty(store.GetAllEvents());
        Assert.Empty(store.GetPending());
        Assert.Null(store.GetSyncToken("c1"));
    }

    [Fact]
    public void OutboxIsOrderedAndCompletable()
    {
        var store = Create();
        var ev = Ev("e1", DateTime.Today);
        var first = store.Enqueue("e1", "c1", OutboxOperation.Create, ev);
        store.Enqueue("e1", "c1", OutboxOperation.Delete);
        Assert.Equal([OutboxOperation.Create, OutboxOperation.Delete], store.GetPending().Select(p => p.Operation));
        store.Complete(first);
        Assert.Equal(OutboxOperation.Delete, Assert.Single(store.GetPending()).Operation);
    }

    [Fact]
    public void PendingQueriesAndDiscard()
    {
        var store = Create();
        var ev = Ev("e1", DateTime.Today);
        var first = store.Enqueue("e1", "c1", OutboxOperation.Update, ev);
        Assert.True(store.HasPending("e1"));
        Assert.False(store.HasPendingAfter("e1", first));
        store.Enqueue("e1", "c1", OutboxOperation.Update, ev);
        store.Enqueue("e2", "c1", OutboxOperation.Update, ev);
        Assert.True(store.HasPendingAfter("e1", first));
        store.DiscardPending("e1");
        Assert.False(store.HasPending("e1"));
        Assert.Equal("e2", Assert.Single(store.GetPending()).EventId);
    }

    [Fact]
    public void OutboxKeepsTheBaseVersion()
    {
        var store = Create();
        var before = Ev("e1", DateTime.Today);
        store.Enqueue("e1", "c1", OutboxOperation.Update, before with { Title = "Nuovo" }, before);
        var item = Assert.Single(store.GetPending());
        Assert.Equal("Nuovo", CalendarStore.ReadSnapshot(item)!.Title);
        Assert.Equal(before.Title, CalendarStore.ReadBase(item)!.Title);
    }

    [Fact]
    public void Version3DatabaseGetsTheBaseColumn()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ggcal-v3-{Guid.NewGuid():N}.db");
        try
        {
            using (var c = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path};Pooling=False"))
            {
                c.Open();
                using var cmd = c.CreateCommand();
                cmd.CommandText = """
                    CREATE TABLE outbox (seq INTEGER PRIMARY KEY AUTOINCREMENT, event_id TEXT NOT NULL, calendar_id TEXT NOT NULL,
                        operation TEXT NOT NULL, payload TEXT, created_at TEXT NOT NULL);
                    INSERT INTO outbox (event_id, calendar_id, operation, payload, created_at) VALUES ('old', 'c1', 'Delete', 'r1', '2026-01-01');
                    PRAGMA user_version = 3;
                    """;
                cmd.ExecuteNonQuery();
            }

            var store = new CalendarStore(path);
            store.Enqueue("e1", "c1", OutboxOperation.Update, Ev("e1", DateTime.Today), Ev("e1", DateTime.Today));
            var pending = store.GetPending();
            Assert.Null(pending[0].Base);
            Assert.NotNull(pending[1].Base);
        }
        finally
        {
            foreach (var f in Directory.GetFiles(Path.GetTempPath(), Path.GetFileName(path) + "*")) File.Delete(f);
        }
    }

    [Fact]
    public void DeleteSeriesRemovesModifiedOccurrences()
    {
        var store = Create();
        var start = new DateTime(2026, 5, 4, 9, 0, 0);
        store.UpsertEvent(Ev("master", start, rrule: "FREQ=DAILY"));
        store.UpsertEvent(Ev("child", start.AddDays(1).AddHours(2)) with { SeriesMasterId = "master", OriginalStart = start.AddDays(1) });
        store.UpsertEvent(Ev("other", start));
        store.DeleteSeries("master");
        Assert.Equal("other", Assert.Single(store.GetAllEvents()).Id);
    }

    [Fact]
    public void ApplyRemoteChangesStoresEventsDeletesAndToken()
    {
        var store = Create();
        store.UpsertEvent(Ev("gone", DateTime.Today), "r-gone", "1");
        store.ApplyRemoteChanges("c1", [new RemoteEvent(Ev("new", DateTime.Today), "r-new", "7")], ["gone"], "tok");
        Assert.Equal("new", Assert.Single(store.GetAllEvents()).Id);
        Assert.Equal(("r-new", "7"), store.GetRemoteInfo("new"));
        Assert.Equal("tok", store.GetSyncToken("c1"));
    }

    [Fact]
    public void ReminderStateRoundTrip()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ggcal-rem-{Guid.NewGuid():N}.json");
        try
        {
            var ev = Ev("e1", new DateTime(2026, 5, 4, 9, 0, 0));
            var due = new Reminders.DueReminder(ev, ev.Reminders[0], ev.Start.AddMinutes(-15));
            var state = new ReminderState { LastCheck = new DateTime(2026, 5, 4, 8, 0, 0) };
            state.Fired[due.Key] = due.FireAt;
            state.Snoozed.Add(new SnoozedReminder(new DateTime(2026, 5, 4, 8, 50, 0), due));
            new ReminderStateStore(path).Save(state);

            var back = new ReminderStateStore(path).Load();
            Assert.Equal(state.LastCheck, back.LastCheck);
            Assert.Equal(due.FireAt, back.Fired[due.Key]);
            var snoozed = Assert.Single(back.Snoozed);
            Assert.Equal(due.Key, snoozed.Reminder.Key);
            Assert.Equal("Titolo e1", snoozed.Reminder.Event.Title);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void FileDatabasePersistsAcrossInstances()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ggcal-{Guid.NewGuid():N}.db");
        try
        {
            var a = new CalendarStore(path);
            a.UpsertCalendar(new CalendarInfo("c1", "Locale", "Personale", "#1A73E8"));
            a.UpsertEvent(Ev("e1", DateTime.Today));
            Assert.Single(new CalendarStore(path).GetAllEvents());
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            foreach (var f in Directory.GetFiles(Path.GetTempPath(), Path.GetFileName(path) + "*")) File.Delete(f);
        }
    }
}
