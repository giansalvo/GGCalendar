namespace GGCalendar.Core.Models;

public sealed record CalendarInfo(string Id, string AccountName, string Name, string ColorHex, bool IsVisible = true);

public sealed record CalendarEvent(
    string Id,
    string CalendarId,
    string Title,
    DateTime Start,
    DateTime End,
    bool IsAllDay = false);
