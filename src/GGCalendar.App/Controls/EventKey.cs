using System;
using GGCalendar.Core.Models;

namespace GGCalendar.App.Controls;

/// <summary>An event as drawn in a view: the occurrences of a series have the same id and differ by their start.</summary>
public readonly record struct EventKey(string Id, DateTime Start)
{
    public static EventKey Of(CalendarEvent ev) => new(ev.Id, ev.Start);
}
