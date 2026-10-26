using GGCalendar.App.Services;
using GGCalendar.Core.Models;
using GGCalendar.Core.Reminders;
using GGCalendar.Data;

namespace GGCalendar.Core.Tests;

/// <summary>The scheduler of the reminders with a clock the test moves (issue #18): cancelling alarms, hidden calendars, snoozes.</summary>
public sealed class ReminderSchedulerTests : IDisposable
{
    private static readonly DateTime T0 = new(2026, 10, 26, 9, 0, 0);

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "ggcal-sched-" + Guid.NewGuid().ToString("N")[..8]);
    private DateTime _now = T0;
    private readonly List<CalendarEvent> _events = [];
    private readonly HashSet<string> _hidden = [];
    private readonly List<DueReminder> _fired = [];

    public ReminderSchedulerTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try { Directory.Delete(_folder, true); } catch (IOException) { }
    }

    private ReminderStateStore Store => new(Path.Combine(_folder, "reminders.json"));

    private ReminderScheduler NewScheduler()
    {
        var scheduler = new ReminderScheduler(() => _events.ToList(), Store, id => !_hidden.Contains(id), () => _now);
        scheduler.Fired += _fired.Add;
        return scheduler;
    }

    private static EventReminder Alarm(int minutes) => new(ReminderMethod.Alarm, minutes);
    private static EventReminder Popup(int minutes) => new(ReminderMethod.Popup, minutes);

    private CalendarEvent Add(string id, TimeSpan startsIn, string calendar = "c", params EventReminder[] reminders)
    {
        var ev = new CalendarEvent(id, calendar, "Evento " + id, T0 + startsIn, T0 + startsIn + TimeSpan.FromHours(1)) { Reminders = reminders };
        _events.Add(ev);
        return ev;
    }

    private void Advance(TimeSpan span, ReminderScheduler scheduler)
    {
        _now += span;
        scheduler.Check();
    }

    [Fact]
    [Trait("TestCase", "T213")]
    public void T213_AnAlarmIsPendingUntilItRingsAndThenItIsNot()
    {
        Add("a", TimeSpan.FromMinutes(40), reminders: [Alarm(10)]);
        var scheduler = NewScheduler();
        Assert.Single(scheduler.Pending());

        var changes = 0;
        scheduler.PendingChanged += () => changes++;
        Advance(TimeSpan.FromMinutes(31), scheduler);

        Assert.Equal(["a"], _fired.Select(f => f.Event.Id).ToArray());
        Assert.Empty(scheduler.Pending());
        Assert.True(changes > 0);
    }

    [Fact]
    [Trait("TestCase", "T215")]
    public void T215_ACancelledAlarmNeverRingsAndTheEventStays()
    {
        var ev = Add("a", TimeSpan.FromMinutes(40), reminders: [Alarm(10)]);
        var scheduler = NewScheduler();
        var alarm = Assert.Single(scheduler.Pending());

        scheduler.Skip(alarm);

        Assert.Empty(scheduler.Pending());
        Advance(TimeSpan.FromMinutes(45), scheduler);
        Assert.Empty(_fired);
        Assert.Equal([ev], _events); // nothing was done to the event
    }

    [Fact]
    [Trait("TestCase", "T215")]
    public void T215_ACancelledAlarmStaysCancelledAfterARestart()
    {
        Add("a", TimeSpan.FromMinutes(40), reminders: [Alarm(10)]);
        var first = NewScheduler();
        first.Skip(Assert.Single(first.Pending()));
        first.Dispose();

        var second = NewScheduler();

        Assert.Empty(second.Pending());
        Advance(TimeSpan.FromMinutes(45), second);
        Assert.Empty(_fired);
    }

    [Fact]
    [Trait("TestCase", "T215")]
    public void T215_OnlyTheCancelledOccurrenceIsSkippedNotTheNextOnes()
    {
        var first = Add("serie", TimeSpan.FromMinutes(40), reminders: [Alarm(10)]);
        _events.Add(first with { Start = first.Start.AddDays(1), End = first.End.AddDays(1) });
        var scheduler = NewScheduler();
        Assert.Equal(2, scheduler.Pending().Count);

        scheduler.Skip(scheduler.Pending()[0]);

        var left = Assert.Single(scheduler.Pending());
        Assert.Equal(first.Start.AddDays(1).AddMinutes(-10), left.FireAt);
    }

    [Fact]
    [Trait("TestCase", "T215")]
    public void T215_ASnoozedAlarmIsForgottenWhenItIsCancelled()
    {
        var ev = Add("a", TimeSpan.FromMinutes(40), reminders: [Alarm(10)]);
        var scheduler = NewScheduler();
        Advance(TimeSpan.FromMinutes(31), scheduler);
        scheduler.Snooze(_fired.Single(), TimeSpan.FromMinutes(5));
        var snoozed = Assert.Single(scheduler.Pending());
        Assert.Equal(PendingKind.Snoozed, snoozed.Kind);
        Assert.Equal(_now.AddMinutes(5), snoozed.FireAt);

        scheduler.Skip(snoozed);

        Assert.Empty(scheduler.Pending());
        _fired.Clear();
        Advance(TimeSpan.FromMinutes(10), scheduler);
        Assert.Empty(_fired);
        Assert.Contains(ev, _events);
    }

    [Fact]
    [Trait("TestCase", "T214")]
    public void T214_ASnoozedAlarmComesBackAndRingsAgain()
    {
        Add("a", TimeSpan.FromMinutes(40), reminders: [Alarm(10)]);
        var scheduler = NewScheduler();
        Advance(TimeSpan.FromMinutes(31), scheduler);
        scheduler.Snooze(_fired.Single(), TimeSpan.FromMinutes(5));
        _fired.Clear();

        Advance(TimeSpan.FromMinutes(4), scheduler);
        Assert.Empty(_fired);
        Advance(TimeSpan.FromMinutes(2), scheduler);

        Assert.Equal(["a"], _fired.Select(f => f.Event.Id).ToArray());
        Assert.Empty(scheduler.Pending());
    }

    [Fact]
    [Trait("TestCase", "T214")]
    public void T214_ASnoozedAlarmSurvivesARestart()
    {
        Add("a", TimeSpan.FromMinutes(40), reminders: [Alarm(10)]);
        var first = NewScheduler();
        Advance(TimeSpan.FromMinutes(31), first);
        first.Snooze(_fired.Single(), TimeSpan.FromMinutes(5));
        first.Dispose();

        var second = NewScheduler();

        Assert.Equal(PendingKind.Snoozed, Assert.Single(second.Pending()).Kind);
    }

    [Fact]
    [Trait("TestCase", "T216")]
    public void T216_TheRemindersOfAHiddenCalendarDoNotRing()
    {
        Add("alarm", TimeSpan.FromMinutes(40), "hidden", Alarm(10));
        Add("popup", TimeSpan.FromMinutes(40), "hidden", Popup(10));
        Add("shown", TimeSpan.FromMinutes(40), "visible", Alarm(10));
        _hidden.Add("hidden");
        var scheduler = NewScheduler();

        Advance(TimeSpan.FromMinutes(31), scheduler);

        Assert.Equal(["shown"], _fired.Select(f => f.Event.Id).ToArray());
    }

    [Fact]
    [Trait("TestCase", "T216")]
    public void T216_AnAlarmPlannedWhenTheCalendarWasShownDoesNotRingIfItIsHiddenBeforeTheTime()
    {
        Add("a", TimeSpan.FromMinutes(40), "cal", Alarm(10));
        var scheduler = NewScheduler();
        Assert.Single(scheduler.Pending());

        _hidden.Add("cal");
        scheduler.VisibilityChanged();
        Assert.Empty(scheduler.Pending());
        Advance(TimeSpan.FromMinutes(31), scheduler);

        Assert.Empty(_fired);
    }

    [Fact]
    [Trait("TestCase", "T216")]
    public void T216_ShowingTheCalendarAgainBringsBackTheAlarmsStillToCome()
    {
        Add("a", TimeSpan.FromMinutes(40), "cal", Alarm(10));
        _hidden.Add("cal");
        var scheduler = NewScheduler();
        Assert.Empty(scheduler.Pending());

        var told = false;
        scheduler.PendingChanged += () => told = true;
        _hidden.Remove("cal");
        scheduler.VisibilityChanged();

        Assert.True(told);
        Assert.Single(scheduler.Pending());
        Advance(TimeSpan.FromMinutes(31), scheduler);
        Assert.Equal(["a"], _fired.Select(f => f.Event.Id).ToArray());
    }

    [Fact]
    [Trait("TestCase", "T216")]
    public void T216_AnAlarmThatPassedWhileTheCalendarWasHiddenDoesNotRingWhenItIsShownAgain()
    {
        Add("a", TimeSpan.FromMinutes(40), "cal", Alarm(10));
        _hidden.Add("cal");
        var scheduler = NewScheduler();
        Advance(TimeSpan.FromMinutes(31), scheduler);

        _hidden.Remove("cal");
        scheduler.VisibilityChanged();
        Advance(TimeSpan.FromMinutes(1), scheduler);

        Assert.Empty(_fired);
        Assert.Empty(scheduler.Pending());
    }

    [Fact]
    [Trait("TestCase", "T217")]
    public void T217_ASnoozedAlarmOfAHiddenCalendarRingsAnyway()
    {
        Add("a", TimeSpan.FromMinutes(40), "cal", Alarm(10));
        var scheduler = NewScheduler();
        Advance(TimeSpan.FromMinutes(31), scheduler);
        scheduler.Snooze(_fired.Single(), TimeSpan.FromMinutes(5));
        _fired.Clear();

        _hidden.Add("cal");
        scheduler.VisibilityChanged();
        var snoozed = Assert.Single(scheduler.Pending());
        Assert.True(snoozed.CalendarHidden);
        Advance(TimeSpan.FromMinutes(6), scheduler);

        Assert.Equal(["a"], _fired.Select(f => f.Event.Id).ToArray());
    }

    [Fact]
    [Trait("TestCase", "T225")]
    public void T225_WithTheNotificationsOffNothingRingsAndTheListIsToldWhenTheyChange()
    {
        Add("a", TimeSpan.FromMinutes(40), reminders: [Alarm(10)]);
        var scheduler = NewScheduler();
        var told = 0;
        scheduler.PendingChanged += () => told++;

        scheduler.Enabled = false;
        Advance(TimeSpan.FromMinutes(31), scheduler);
        scheduler.Enabled = true;

        Assert.Empty(_fired);
        Assert.Equal(2, told);
    }

    [Fact]
    [Trait("TestCase", "T213")]
    public void T213_AnEventAddedLaterShowsUpAfterTheCacheIsInvalidated()
    {
        var scheduler = NewScheduler();
        Assert.Empty(scheduler.Pending());

        Add("late", TimeSpan.FromHours(3), reminders: [Alarm(0)]);
        scheduler.Invalidate();

        Assert.Single(scheduler.Pending());
    }

    [Fact]
    [Trait("TestCase", "T214")]
    public void T214_ASnoozedAlarmOfAnEventDeletedMeanwhileDoesNotRing()
    {
        var ev = Add("a", TimeSpan.FromMinutes(40), reminders: [Alarm(10)]);
        var scheduler = NewScheduler();
        Advance(TimeSpan.FromMinutes(31), scheduler);
        scheduler.Snooze(_fired.Single(), TimeSpan.FromMinutes(5));
        _fired.Clear();

        _events.Remove(ev);
        scheduler.Invalidate();
        Assert.Empty(scheduler.Pending());
        Advance(TimeSpan.FromMinutes(6), scheduler);

        Assert.Empty(_fired);
    }
}
