using System;
using System.Collections.Generic;
using GGCalendar.Core.Models;

namespace GGCalendar.Core.Recurrence;

public static class RecurrenceExpander
{
    private const int MaxIterations = 20000;

    /// <summary>
    /// Returns the occurrences of <paramref name="master"/> overlapping [<paramref name="from"/>, <paramref name="to"/>).
    /// Non-repeating events and rules this model cannot represent are returned as the single master event.
    /// Every occurrence keeps the master's Id and has <see cref="CalendarEvent.OccurrenceIndex"/> set.
    /// </summary>
    public static IEnumerable<CalendarEvent> Expand(CalendarEvent master, DateTime from, DateTime to)
    {
        var rule = RecurrenceRule.Parse(master.Recurrence);
        if (rule is null)
        {
            if (master.Start < to && master.End > from) yield return master;
            yield break;
        }

        var duration = master.End - master.Start;
        var emitted = 0;
        foreach (var start in Starts(master.Start, rule))
        {
            if (start >= to) yield break;
            if (rule.Until is { } until && start.Date > until.Date) yield break;
            if (rule.Count is { } count && emitted >= count) yield break;
            emitted++;
            if (start + duration > from)
                yield return master with { Start = start, End = start + duration, OccurrenceIndex = emitted - 1 };
        }
    }

    private static IEnumerable<DateTime> Starts(DateTime first, RecurrenceRule rule)
    {
        var time = first.TimeOfDay;
        var date = first.Date;
        var interval = rule.Interval;

        for (var i = 0; i < MaxIterations; i++)
        {
            switch (rule.Frequency)
            {
                case RecurrenceFrequency.Daily:
                    yield return date.AddDays((long)i * interval) + time;
                    break;

                case RecurrenceFrequency.Weekly:
                {
                    var weekStart = date.AddDays(-(((int)date.DayOfWeek + 6) % 7)).AddDays(7L * i * interval);
                    var days = rule.Days.Count > 0 ? rule.Days : [first.DayOfWeek];
                    var ordered = new List<DateTime>();
                    foreach (var d in days) ordered.Add(weekStart.AddDays(((int)d + 6) % 7));
                    ordered.Sort();
                    foreach (var d in ordered)
                        if (d >= date) yield return d + time;
                    break;
                }

                case RecurrenceFrequency.Monthly:
                {
                    var monthStart = new DateTime(date.Year, date.Month, 1).AddMonths(i * interval);
                    if (date.Day <= DateTime.DaysInMonth(monthStart.Year, monthStart.Month))
                        yield return new DateTime(monthStart.Year, monthStart.Month, date.Day) + time;
                    break;
                }

                default:
                {
                    var year = date.Year + i * interval;
                    if (year > 9998) yield break;
                    if (date.Day <= DateTime.DaysInMonth(year, date.Month))
                        yield return new DateTime(year, date.Month, date.Day) + time;
                    break;
                }
            }
        }
    }
}
