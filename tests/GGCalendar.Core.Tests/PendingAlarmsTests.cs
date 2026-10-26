using GGCalendar.Core.Models;
using GGCalendar.Core.Reminders;

namespace GGCalendar.Core.Tests;

/// <summary>The list of the alarms waiting to ring (issue #18): which alarms, in which order, how far ahead.</summary>
public class PendingAlarmsTests
{
    private static readonly DateTime Now = new(2026, 10, 26, 9, 0, 0);

    private static EventReminder Alarm(int minutes) => new(ReminderMethod.Alarm, minutes);
    private static EventReminder Popup(int minutes) => new(ReminderMethod.Popup, minutes);

    private static CalendarEvent Ev(string id, DateTime start, string calendar = "c", bool allDay = false, params EventReminder[] reminders) =>
        new(id, calendar, "Evento " + id, start, allDay ? start.AddDays(1) : start.AddHours(1), allDay) { Reminders = reminders };

    private static IReadOnlyList<PendingAlarm> Build(IEnumerable<CalendarEvent> events, Dictionary<string, DateTime>? fired = null,
        IEnumerable<(DateTime, DueReminder)>? snoozed = null, Func<string, bool>? visible = null) =>
        PendingAlarms.Build(events, fired ?? [], snoozed ?? [], Now, visible ?? (_ => true));

    [Fact]
    [Trait("TestCase", "T213")]
    public void T213_OnlyAlarmsAreListedNotPopupsOrEmails()
    {
        var ev = Ev("1", Now.AddHours(5), reminders: [Popup(30), Alarm(10), new EventReminder(ReminderMethod.Email, 60)]);

        var list = Build([ev]);

        var alarm = Assert.Single(list);
        Assert.Equal(Now.AddHours(5).AddMinutes(-10), alarm.FireAt);
        Assert.Equal(PendingKind.Planned, alarm.Kind);
        Assert.Equal(ReminderMethod.Alarm, alarm.Reminder.Reminder.Method);
    }

    [Fact]
    [Trait("TestCase", "T213")]
    public void T213_AlarmsAreSortedBySoonestAndAnEventWithManyAlarmsIsListedOnceForEach()
    {
        var late = Ev("late", Now.AddDays(3), reminders: [Alarm(0)]);
        var twice = Ev("twice", Now.AddHours(4), reminders: [Alarm(60), Alarm(10)]);

        var list = Build([late, twice]);

        Assert.Equal([Now.AddHours(3), Now.AddHours(4).AddMinutes(-10), Now.AddDays(3)], list.Select(a => a.FireAt).ToArray());
        Assert.Equal(["twice", "twice", "late"], list.Select(a => a.Event.Id).ToArray());
    }

    [Fact]
    [Trait("TestCase", "T213")]
    public void T213_TheListLooksSixtyDaysAheadAndNoFurther()
    {
        var inside = Ev("in", Now.AddDays(59).AddHours(20), reminders: [Alarm(0)]);
        var outside = Ev("out", Now.AddDays(61), reminders: [Alarm(0)]);

        Assert.Equal(["in"], Build([inside, outside]).Select(a => a.Event.Id).ToArray());
    }

    [Fact]
    [Trait("TestCase", "T213")]
    public void T213_NoMoreThanThreeHundredAlarmsAreListedAndTheSoonestComeFirst()
    {
        var events = Enumerable.Range(0, 350).Select(i => Ev("e" + i, Now.AddMinutes(30 + i), reminders: [Alarm(0)])).ToList();

        var list = Build(events);

        Assert.Equal(300, list.Count);
        Assert.Equal("e0", list[0].Event.Id);
        Assert.Equal("e299", list[^1].Event.Id);
    }

    [Fact]
    [Trait("TestCase", "T213")]
    public void T213_AnAlarmAlreadyShownOrCancelledIsNotListed()
    {
        var shown = Ev("shown", Now.AddHours(1), reminders: [Alarm(10)]);
        var kept = Ev("kept", Now.AddHours(2), reminders: [Alarm(10)]);
        var key = new DueReminder(shown, Alarm(10), shown.Start.AddMinutes(-10)).Key;

        var list = Build([shown, kept], new Dictionary<string, DateTime> { [key] = shown.Start.AddMinutes(-10) });

        Assert.Equal(["kept"], list.Select(a => a.Event.Id).ToArray());
    }

    [Fact]
    [Trait("TestCase", "T213")]
    public void T213_AnAlarmThatWouldHaveRungAlreadyIsNotListed()
    {
        var past = Ev("past", Now.AddMinutes(5), reminders: [Alarm(10)]); // it rang at 8:55
        var gone = Ev("gone", Now.AddHours(-3), reminders: [Alarm(10)]);

        Assert.Empty(Build([past, gone]));
    }

    [Fact]
    [Trait("TestCase", "T223")]
    public void T223_EachOccurrenceOfASeriesIsListedWithItsOwnAlarm()
    {
        var first = Ev("serie", Now.AddDays(1), reminders: [Alarm(15)]);
        var second = first with { Start = Now.AddDays(2), End = Now.AddDays(2).AddHours(1) };

        var list = Build([first, second]);

        Assert.Equal(2, list.Count);
        Assert.Equal([Now.AddDays(1).AddMinutes(-15), Now.AddDays(2).AddMinutes(-15)], list.Select(a => a.FireAt).ToArray());
        Assert.Equal(2, list.Select(a => a.Key).Distinct().Count());
    }

    [Fact]
    [Trait("TestCase", "T224")]
    public void T224_AnAlarmOfAnAllDayEventRingsRelativeToMidnight()
    {
        var day = Now.Date.AddDays(2);
        var ev = Ev("all", day, allDay: true, reminders: [Alarm(9 * 60)]); // the day before, at 15:00

        var alarm = Assert.Single(Build([ev]));

        Assert.Equal(day.AddHours(-9), alarm.FireAt);
    }

    [Fact]
    [Trait("TestCase", "T216")]
    public void T216_TheAlarmsOfHiddenCalendarsAreNotListed()
    {
        var shown = Ev("shown", Now.AddHours(2), "visible", reminders: [Alarm(0)]);
        var hidden = Ev("hidden", Now.AddHours(3), "hidden", reminders: [Alarm(0)]);

        var list = Build([shown, hidden], visible: id => id != "hidden");

        Assert.Equal(["shown"], list.Select(a => a.Event.Id).ToArray());
    }

    [Fact]
    [Trait("TestCase", "T214")]
    public void T214_ASnoozedAlarmIsListedWhenItComesBackAndTheOthersAreInOrder()
    {
        var planned = Ev("planned", Now.AddHours(2), reminders: [Alarm(0)]);
        var snoozedEvent = Ev("snoozed", Now.AddMinutes(-3), reminders: [Alarm(10)]);
        var snoozedDue = new DueReminder(snoozedEvent, Alarm(10), snoozedEvent.Start.AddMinutes(-10));
        var at = Now.AddMinutes(4);

        var list = Build([planned, snoozedEvent], snoozed: [(at, snoozedDue)]);

        Assert.Equal(["snoozed", "planned"], list.Select(a => a.Event.Id).ToArray());
        Assert.Equal((PendingKind.Snoozed, at), (list[0].Kind, list[0].FireAt));
        Assert.StartsWith("snooze|", list[0].Key);
        Assert.False(list[0].CalendarHidden);
    }

    [Fact]
    [Trait("TestCase", "T214")]
    public void T214_ASnoozedPopupIsNotListed()
    {
        var ev = Ev("p", Now.AddMinutes(1), reminders: [Popup(30)]);

        Assert.Empty(Build([], snoozed: [(Now.AddMinutes(3), new DueReminder(ev, Popup(30), Now))]));
    }

    [Fact]
    [Trait("TestCase", "T217")]
    public void T217_ASnoozedAlarmOfAHiddenCalendarIsListedAndMarkedAsHidden()
    {
        var ev = Ev("h", Now.AddMinutes(30), "hidden", reminders: [Alarm(10)]);
        var due = new DueReminder(ev, Alarm(10), ev.Start.AddMinutes(-10));

        var alarm = Assert.Single(Build([ev], snoozed: [(Now.AddMinutes(5), due)], visible: id => id != "hidden"));

        Assert.True(alarm.CalendarHidden);
    }

    [Fact]
    [Trait("TestCase", "T214")]
    public void T214_ASnoozedAlarmThatIsLateIsListedAsRingingNow()
    {
        var ev = Ev("late", Now.AddMinutes(-2), reminders: [Alarm(10)]);
        var due = new DueReminder(ev, Alarm(10), ev.Start.AddMinutes(-10));

        var alarm = Assert.Single(Build([ev], snoozed: [(Now.AddMinutes(-1), due)]));

        Assert.Equal(Now, alarm.FireAt);
    }

    [Fact]
    [Trait("TestCase", "T213")]
    public void T213_ACancelledOccurrenceHasNoAlarm()
    {
        var ev = Ev("x", Now.AddHours(2), reminders: [Alarm(0)]) with { IsCancelled = true };

        Assert.Empty(Build([ev]));
    }

    [Fact]
    [Trait("TestCase", "T214")]
    public void T214_ASnoozedAlarmOfAnEventThatIsGoneIsNotListed()
    {
        var gone = Ev("gone", Now.AddMinutes(30), reminders: [Alarm(10)]);
        var due = new DueReminder(gone, Alarm(10), gone.Start.AddMinutes(-10));

        Assert.Empty(Build([], snoozed: [(Now.AddMinutes(3), due)]));
        var moved = Build([gone with { Start = gone.Start.AddHours(1), End = gone.End.AddHours(1) }], snoozed: [(Now.AddMinutes(3), due)]);
        Assert.DoesNotContain(moved, a => a.Kind == PendingKind.Snoozed); // moved: it is another occurrence, with its own planned alarm
    }
}
