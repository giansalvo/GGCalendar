using GGCalendar.Core.Models;

namespace GGCalendar.Core.Reminders;

public enum PendingKind { Planned, Snoozed }

/// <summary>
/// An alarm that has not rung yet: <see cref="Key"/> identifies it (the key of its reminder, or the one of the snooze),
/// <see cref="FireAt"/> is when it rings. A snoozed alarm of a hidden calendar still rings (like on Android).
/// </summary>
public sealed record PendingAlarm(PendingKind Kind, string Key, DateTime FireAt, DueReminder Reminder, bool CalendarHidden)
{
    public CalendarEvent Event => Reminder.Event;
}

/// <summary>The alarms waiting to ring (issue #18): the planned ones of the visible calendars and the snoozed ones, soonest first.</summary>
public static class PendingAlarms
{
    /// <summary>How far ahead the planned alarms are listed.</summary>
    public static readonly TimeSpan Horizon = TimeSpan.FromDays(60);

    /// <summary>The most alarms listed.</summary>
    public const int MaxListed = 300;

    public static string SnoozeKey(DateTime at, string reminderKey) => $"snooze|{at.Ticks}|{reminderKey}";

    /// <summary>The events (occurrences) that exist: a snoozed reminder of an event that was deleted or moved does not come back (as on Android).</summary>
    public static HashSet<(string Id, DateTime Start)> Occurrences(IEnumerable<CalendarEvent> events) =>
        events.Where(e => !e.IsCancelled).Select(e => (e.Id, e.Start)).ToHashSet();

    /// <param name="events">The events (and occurrences of series) that can have an alarm in the next <see cref="Horizon"/>.</param>
    /// <param name="fired">Keys of the reminders already shown or cancelled.</param>
    /// <param name="snoozed">The reminders put off, with the time they come back.</param>
    /// <param name="isVisible">Whether the calendar with the given id is shown; only planned alarms of shown calendars are listed.</param>
    public static IReadOnlyList<PendingAlarm> Build(
        IEnumerable<CalendarEvent> events,
        IReadOnlyDictionary<string, DateTime> fired,
        IEnumerable<(DateTime At, DueReminder Reminder)> snoozed,
        DateTime now,
        Func<string, bool> isVisible)
    {
        var list = new List<PendingAlarm>();
        var all = events as IReadOnlyCollection<CalendarEvent> ?? events.ToList();
        var withAlarms = all.Where(e => !e.IsCancelled && e.Reminders.Any(r => r.Method == ReminderMethod.Alarm) && isVisible(e.CalendarId));
        foreach (var due in ReminderCalculator.Due(withAlarms, now, now + Horizon))
            if (due.Reminder.Method == ReminderMethod.Alarm && !fired.ContainsKey(due.Key))
                list.Add(new PendingAlarm(PendingKind.Planned, due.Key, due.FireAt, due, CalendarHidden: false));

        var existing = Occurrences(all);
        foreach (var (at, reminder) in snoozed)
            if (reminder.Reminder.Method == ReminderMethod.Alarm && existing.Contains((reminder.Event.Id, reminder.Event.Start)))
                list.Add(new PendingAlarm(PendingKind.Snoozed, SnoozeKey(at, reminder.Key), at < now ? now : at, reminder, !isVisible(reminder.Event.CalendarId)));

        return list.OrderBy(a => a.FireAt).ThenBy(a => a.Key, StringComparer.Ordinal).Take(MaxListed).ToList();
    }
}
