namespace GGCalendar.Core.Models;

public sealed record CalendarInfo(string Id, string AccountName, string Name, string ColorHex, bool IsVisible = true);

/// <summary>For all-day events Start is the first day at 00:00 and End is exclusive (00:00 of the day after the last).</summary>
public sealed record CalendarEvent(
    string Id,
    string CalendarId,
    string Title,
    DateTime Start,
    DateTime End,
    bool IsAllDay = false,
    string Location = "",
    string Description = "");
