using System;
using Avalonia.Controls;

namespace GGCalendar.App.ViewModels;

/// <summary>What the small calendar on the left shows with each view, and the day a choice in it leads to (issue #103).</summary>
public static class MiniCalendarZoom
{
    /// <summary>The months of a year with the month view, the years with the year view, the days of a month with the other views.</summary>
    public static CalendarMode RestingMode(int viewModeIndex) => viewModeIndex switch
    {
        2 => CalendarMode.Year,
        3 => CalendarMode.Decade,
        _ => CalendarMode.Month,
    };

    /// <summary>
    /// The day reached by choosing a month (<paramref name="resting"/> = Year) or a year (Decade) in the small calendar:
    /// the day of <paramref name="selected"/> is kept (and, for a year, its month), made shorter when the month is.
    /// With the days (Month) the choice is the day itself.
    /// </summary>
    /// <param name="shown">The date the small calendar holds right after the choice: it carries the month or the year chosen.</param>
    public static DateTime Chosen(DateTime selected, DateTime shown, CalendarMode resting) => resting switch
    {
        CalendarMode.Year => SameDay(selected, shown.Year, shown.Month),
        CalendarMode.Decade => SameDay(selected, shown.Year, selected.Month),
        _ => shown.Date,
    };

    private static DateTime SameDay(DateTime day, int year, int month) => new(year, month, Math.Min(day.Day, DateTime.DaysInMonth(year, month)));
}
