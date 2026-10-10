using System.Text;
using GGCalendar.Core.Models;

namespace GGCalendar.Core.Ics;

/// <summary>Minimal RFC 5545 writer used to export events as .ics files.</summary>
public static class IcsWriter
{
    public static string Write(IEnumerable<CalendarEvent> events, string productId = "-//GGCalendar//IT")
    {
        var sb = new StringBuilder();
        Line(sb, "BEGIN:VCALENDAR");
        Line(sb, "VERSION:2.0");
        Line(sb, $"PRODID:{productId}");
        Line(sb, "CALSCALE:GREGORIAN");

        var all = events.ToList();
        var uids = all.Where(x => x.SeriesMasterId is null).ToDictionary(x => x.Id, x => x.IcsUid ?? $"{x.Id}@ggcalendar");
        foreach (var e in all.Where(x => !x.IsCancelled))
        {
            var isException = e.SeriesMasterId is not null;
            Line(sb, "BEGIN:VEVENT");
            Line(sb, $"UID:{(isException ? uids.GetValueOrDefault(e.SeriesMasterId!, $"{e.SeriesMasterId}@ggcalendar") : uids[e.Id])}");
            Line(sb, $"DTSTAMP:{DateTime.UtcNow:yyyyMMdd'T'HHmmss'Z'}");
            if (isException && e.OriginalStart is { } original)
                Line(sb, e.IsAllDay ? $"RECURRENCE-ID;VALUE=DATE:{original:yyyyMMdd}" : $"RECURRENCE-ID:{original:yyyyMMdd'T'HHmmss}");
            if (e.IsAllDay)
            {
                Line(sb, $"DTSTART;VALUE=DATE:{e.Start:yyyyMMdd}");
                Line(sb, $"DTEND;VALUE=DATE:{e.End:yyyyMMdd}");
            }
            else
            {
                Line(sb, $"DTSTART:{e.Start:yyyyMMdd'T'HHmmss}");
                Line(sb, $"DTEND:{e.End:yyyyMMdd'T'HHmmss}");
            }

            Line(sb, "SUMMARY:" + Escape(e.Title));
            if (e.Location.Length > 0) Line(sb, "LOCATION:" + Escape(e.Location));
            var description = e.MeetLink is null ? e.Description : $"{e.Description}\nGoogle Meet: {e.MeetLink}".Trim();
            if (description.Length > 0) Line(sb, "DESCRIPTION:" + Escape(description));
            if (e.MeetLink is not null) Line(sb, "URL:" + e.MeetLink);
            if (e.Recurrence is { Length: > 0 })
            {
                Line(sb, "RRULE:" + e.Recurrence);
                var excluded = e.ExcludedStarts
                    .Concat(all.Where(x => x.IsCancelled && x.SeriesMasterId == e.Id && x.OriginalStart is not null).Select(x => x.OriginalStart!.Value))
                    .Distinct().OrderBy(d => d).ToList();
                foreach (var d in excluded)
                    Line(sb, e.IsAllDay ? $"EXDATE;VALUE=DATE:{d:yyyyMMdd}" : $"EXDATE:{d:yyyyMMdd'T'HHmmss}");
            }
            Line(sb, "TRANSP:" + (e.Availability == EventAvailability.Free ? "TRANSPARENT" : "OPAQUE"));
            if (e.Visibility != EventVisibility.Default)
                Line(sb, "CLASS:" + (e.Visibility == EventVisibility.Private ? "PRIVATE" : "PUBLIC"));
            foreach (var a in e.Attendees)
                Line(sb, $"ATTENDEE;ROLE={(a.IsOptional ? "OPT-PARTICIPANT" : "REQ-PARTICIPANT")}:mailto:{a.Email}");
            foreach (var r in e.Reminders)
            {
                Line(sb, "BEGIN:VALARM");
                Line(sb, "ACTION:" + (r.Method == ReminderMethod.Email ? "EMAIL" : r.Method == ReminderMethod.Alarm ? "AUDIO" : "DISPLAY"));
                Line(sb, $"TRIGGER:-PT{r.Minutes}M");
                Line(sb, "DESCRIPTION:" + Escape(e.Title));
                Line(sb, "END:VALARM");
            }

            Line(sb, "END:VEVENT");
        }

        Line(sb, "END:VCALENDAR");
        return sb.ToString();
    }

    private static string Escape(string s) =>
        s.Replace("\\", "\\\\").Replace(";", "\\;").Replace(",", "\\,").Replace("\r\n", "\\n").Replace("\n", "\\n");

    // Lines are folded at 75 characters; continuation lines start with a space.
    private static void Line(StringBuilder sb, string line)
    {
        while (line.Length > 75)
        {
            sb.Append(line, 0, 75).Append("\r\n ");
            line = line[75..];
        }

        sb.Append(line).Append("\r\n");
    }
}
