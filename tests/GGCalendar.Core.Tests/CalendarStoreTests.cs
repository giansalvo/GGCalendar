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
        store.DeleteCalendar("c1");
        Assert.Empty(store.GetAllEvents());
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
