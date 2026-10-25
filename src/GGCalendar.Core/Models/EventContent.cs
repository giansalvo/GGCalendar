namespace GGCalendar.Core.Models;

public static class EventContent
{
    /// <summary>
    /// True when two versions of an event look the same to the user. Ignores what a provider changes on its own:
    /// the Meet link (created asynchronously), guests' replies, reminders, ids, and <paramref name="ignoreGuest"/>
    /// (the calendar owner, whom Google adds to the guest list as organizer).
    /// </summary>
    public static bool Same(CalendarEvent a, CalendarEvent b, string? ignoreGuest = null) =>
        a.Title == b.Title
        && a.Start == b.Start
        && a.End == b.End
        && a.IsAllDay == b.IsAllDay
        && a.Location == b.Location
        && a.Description == b.Description
        && (a.Recurrence ?? "") == (b.Recurrence ?? "")
        && a.ExcludedStarts.ToHashSet().SetEquals(b.ExcludedStarts)
        && Guests(a, ignoreGuest).SetEquals(Guests(b, ignoreGuest))
        && a.Availability == b.Availability
        && a.Visibility == b.Visibility
        && string.Equals(a.ColorHex, b.ColorHex, StringComparison.OrdinalIgnoreCase)
        && a.IsCancelled == b.IsCancelled;

    private static HashSet<string> Guests(CalendarEvent e, string? ignore) =>
        e.Attendees.Where(x => !x.Email.Equals(ignore, StringComparison.OrdinalIgnoreCase))
            .Select(x => x.Email.ToLowerInvariant() + (x.IsOptional ? "?" : "")).ToHashSet();
}
