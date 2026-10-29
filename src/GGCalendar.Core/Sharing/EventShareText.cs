using System.Globalization;
using System.Text;
using GGCalendar.Core.Localization;
using GGCalendar.Core.Models;

namespace GGCalendar.Core.Sharing;

/// <summary>
/// The text of an event to send to someone (issue #45): title, when, place, link, description, number of guests and calendar,
/// in the language of the app. The addresses of the guests are not written (privacy: only how many they are).
/// Lines are separated by "\n".
/// </summary>
public static class EventShareText
{
    private static CultureInfo Culture => L.Culture;

    /// <param name="ev">The event or the occurrence chosen.</param>
    /// <param name="recurrence">What the repeat rule says ("Every week on Friday"), or null when the event does not repeat.</param>
    /// <param name="secondZoneText">The start in the second time zone the user has set, when there is one.</param>
    public static string Build(CalendarEvent ev, string? recurrence, string calendarName, bool use24Hour, string? secondZoneText = null)
    {
        var lines = new List<string>
        {
            string.IsNullOrWhiteSpace(ev.Title) ? L.T("common.untitled") : ev.Title.Trim(),
            When(ev, use24Hour),
        };
        if (!string.IsNullOrWhiteSpace(secondZoneText)) lines.Add(secondZoneText.Trim());
        if (!string.IsNullOrWhiteSpace(recurrence)) lines.Add(L.T("share.repeats", recurrence.Trim()));
        if (!string.IsNullOrWhiteSpace(ev.Location)) lines.Add(L.T("share.location", ev.Location.Trim()));
        if (!string.IsNullOrWhiteSpace(ev.MeetLink)) lines.Add(L.T("share.meet", ev.MeetLink.Trim()));

        var description = Description(ev.Description);
        if (description.Length > 0)
        {
            lines.Add("");
            lines.Add(description);
        }

        if (ev.Attendees.Count > 0)
        {
            lines.Add("");
            lines.Add(L.N("share.guests", ev.Attendees.Count));
        }

        if (!string.IsNullOrWhiteSpace(calendarName)) lines.Add(L.T("share.calendar", calendarName.Trim()));
        return string.Join("\n", lines);
    }

    /// <summary>"Friday 30 October 2026, 15:00 – 16:00"; an all-day event has no time; a longer one says from which day to which.</summary>
    public static string When(CalendarEvent ev, bool use24Hour)
    {
        string Time(DateTime t) => t.ToString(use24Hour ? "HH:mm" : "h:mm tt", Culture);

        if (ev.IsAllDay)
        {
            var last = ev.End.Date.AddDays(-1);
            return last <= ev.Start.Date ? Day(ev.Start) : L.T("share.from_to", Day(ev.Start, capital: false), Day(last, capital: false));
        }

        return ev.End.Date == ev.Start.Date || ev.End == ev.Start.Date.AddDays(1)
            ? $"{Day(ev.Start)}, {Time(ev.Start)} – {Time(ev.End)}"
            : $"{Day(ev.Start)}, {Time(ev.Start)} – {Day(ev.End)}, {Time(ev.End)}";
    }

    private static string Day(DateTime d, bool capital = true)
    {
        var text = d.ToString("dddd d MMMM yyyy", Culture);
        return text.Length == 0 || !capital ? text : char.ToUpper(text[0], Culture) + text[1..];
    }

    /// <summary>The description as the reader sees it; the address of a link is added in brackets when the text of the link is not the address.</summary>
    private static string Description(string? description)
    {
        var builder = new StringBuilder();
        foreach (var run in DescriptionText.Parse(description))
        {
            builder.Append(run.Text);
            if (run.Link is { } link && !string.Equals(run.Text.Trim(), link.OriginalString, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(run.Text.Trim(), link.AbsoluteUri, StringComparison.OrdinalIgnoreCase))
                builder.Append(" (").Append(link.OriginalString).Append(')');
        }

        return builder.ToString().Trim();
    }
}
