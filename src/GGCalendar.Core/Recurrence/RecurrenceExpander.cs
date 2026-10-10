using System;
using System.Collections.Generic;
using System.Linq;
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

        var excluded = master.ExcludedStarts.Count > 0 ? master.ExcludedStarts.ToHashSet() : null;
        var duration = master.End - master.Start;
        var emitted = 0;
        foreach (var start in Starts(master.Start, rule))
        {
            if (start >= to) yield break;
            if (rule.Until is { } until && start.Date > until.Date) yield break;
            if (rule.Count is { } count && emitted >= count) yield break;
            emitted++;
            if (excluded?.Contains(start) == true) continue;
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
                    var offset = ((int)date.DayOfWeek - (int)rule.WeekStart + 7) % 7;
                    var weekStart = date.AddDays(-offset).AddDays(7L * i * interval);
                    var days = rule.Days.Count > 0 ? rule.Days : [first.DayOfWeek];
                    var ordered = days.Select(d => weekStart.AddDays(((int)d - (int)rule.WeekStart + 7) % 7)).OrderBy(d => d);
                    foreach (var d in ordered)
                        if (d >= date) yield return d + time;
                    break;
                }

                case RecurrenceFrequency.Monthly:
                {
                    var month = new DateTime(date.Year, date.Month, 1).AddMonths(i * interval);
                    if (DayInMonth(month.Year, month.Month, date.Day, rule) is { } d && d >= date) yield return d + time;
                    break;
                }

                default:
                {
                    var year = date.Year + i * interval;
                    if (year > 9998) yield break;
                    if (DayInMonth(year, date.Month, date.Day, rule) is { } d && d >= date) yield return d + time;
                    break;
                }
            }
        }
    }

    /// <summary>The date of the occurrence inside the given month, or null when the month has no such day.</summary>
    private static DateTime? DayInMonth(int year, int month, int masterDay, RecurrenceRule rule)
    {
        var dim = DateTime.DaysInMonth(year, month);
        if (rule.NthWeekday is { } nth)
        {
            if (nth.Ordinal > 0)
            {
                var firstOfMonth = new DateTime(year, month, 1);
                var day = 1 + ((int)nth.Day - (int)firstOfMonth.DayOfWeek + 7) % 7 + 7 * (nth.Ordinal - 1);
                return day <= dim ? new DateTime(year, month, day) : null;
            }

            var last = new DateTime(year, month, dim);
            var back = ((int)last.DayOfWeek - (int)nth.Day + 7) % 7 + 7 * (-nth.Ordinal - 1);
            var result = dim - back;
            return result >= 1 ? new DateTime(year, month, result) : null;
        }

        var target = rule.MonthDay is { } md ? (md > 0 ? md : dim + md + 1) : masterDay;
        return target >= 1 && target <= dim ? new DateTime(year, month, target) : null;
    }
}
