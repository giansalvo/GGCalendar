using System.Globalization;
using System.Text;
using GGCalendar.Core.Models;

namespace GGCalendar.Core.Ics;

/// <summary>
/// RFC 5545 writer for the .ics files the app exports. A timed event that does not repeat is written in UTC; a series (and its
/// modified occurrences) keeps the time zone of the PC with TZID, so that every occurrence stays at the same local time across
/// daylight saving changes. No VTIMEZONE component is written: Google Calendar, Apple and Outlook resolve the IANA name. Lines
/// end with CRLF and are folded at 75 bytes (UTF-8), never inside a character.
/// </summary>
public static class IcsWriter
{
    private const string UtcStamp = "yyyyMMdd'T'HHmmss'Z'";
    private const string LocalStamp = "yyyyMMdd'T'HHmmss";
    private const string DateStamp = "yyyyMMdd";

    /// <param name="localZone">The zone the local times of the events are in (null = this PC's).</param>
    public static string Write(IEnumerable<CalendarEvent> events, string productId = "-//GGCalendar//IT", TimeZoneInfo? localZone = null)
    {
        var zone = localZone ?? TimeZoneInfo.Local;
        var tzid = IanaId(zone);
        var sb = new StringBuilder();
        Line(sb, "BEGIN:VCALENDAR");
        Line(sb, "VERSION:2.0");
        Line(sb, $"PRODID:{productId}");
        Line(sb, "CALSCALE:GREGORIAN");

        var all = events.ToList();
        var uids = all.Where(x => x.SeriesMasterId is null).ToDictionary(x => x.Id, x => x.IcsUid ?? $"{x.Id}@ggcalendar");
        var now = DateTime.UtcNow;
        foreach (var e in all.Where(x => !x.IsCancelled))
        {
            var isException = e.SeriesMasterId is not null;
            var inZone = !e.IsAllDay && tzid is not null && (isException || !string.IsNullOrEmpty(e.Recurrence));
            Line(sb, "BEGIN:VEVENT");
            Line(sb, "UID:" + Clean(isException ? uids.GetValueOrDefault(e.SeriesMasterId!, $"{e.SeriesMasterId}@ggcalendar") : uids[e.Id]));
            Line(sb, "DTSTAMP:" + now.ToString(UtcStamp, CultureInfo.InvariantCulture));
            if (isException && e.OriginalStart is { } original)
                Line(sb, e.IsAllDay ? "RECURRENCE-ID;VALUE=DATE:" + original.ToString(DateStamp, CultureInfo.InvariantCulture) : Timed("RECURRENCE-ID", original, zone, inZone ? tzid : null));
            if (e.IsAllDay)
            {
                Line(sb, "DTSTART;VALUE=DATE:" + e.Start.ToString(DateStamp, CultureInfo.InvariantCulture));
                Line(sb, "DTEND;VALUE=DATE:" + e.End.ToString(DateStamp, CultureInfo.InvariantCulture));
            }
            else
            {
                Line(sb, Timed("DTSTART", e.Start, zone, inZone ? tzid : null));
                Line(sb, Timed("DTEND", e.End, zone, inZone ? tzid : null));
            }

            Line(sb, "SUMMARY:" + Escape(e.Title));
            if (e.Location.Length > 0) Line(sb, "LOCATION:" + Escape(e.Location));
            var description = Describe(e);
            if (description.Length > 0) Line(sb, "DESCRIPTION:" + Escape(description));
            if (e.MeetLink is not null) Line(sb, "URL:" + Clean(e.MeetLink));
            if (e.Recurrence is { Length: > 0 })
            {
                Line(sb, "RRULE:" + e.Recurrence);
                var excluded = e.ExcludedStarts
                    .Concat(all.Where(x => x.IsCancelled && x.SeriesMasterId == e.Id && x.OriginalStart is not null).Select(x => x.OriginalStart!.Value))
                    .Distinct().OrderBy(d => d).ToList();
                foreach (var d in excluded)
                    Line(sb, e.IsAllDay ? "EXDATE;VALUE=DATE:" + d.ToString(DateStamp, CultureInfo.InvariantCulture) : Timed("EXDATE", d, zone, inZone ? tzid : null));
            }

            Line(sb, "TRANSP:" + (e.Availability == EventAvailability.Free ? "TRANSPARENT" : "OPAQUE"));
            if (e.Visibility != EventVisibility.Default)
                Line(sb, "CLASS:" + (e.Visibility == EventVisibility.Private ? "PRIVATE" : "PUBLIC"));
            foreach (var a in e.Attendees)
                Line(sb, $"ATTENDEE;ROLE={(a.IsOptional ? "OPT-PARTICIPANT" : "REQ-PARTICIPANT")}:mailto:{Clean(a.Email)}");
            // Email reminders are sent by Google, not by the calendar that opens the file: they have no VALARM.
            foreach (var r in e.Reminders.Where(r => r.Method != ReminderMethod.Email))
            {
                Line(sb, "BEGIN:VALARM");
                Line(sb, "ACTION:" + (r.Method == ReminderMethod.Alarm ? "AUDIO" : "DISPLAY"));
                Line(sb, "TRIGGER:" + Trigger(r.Minutes));
                if (r.Method != ReminderMethod.Alarm) Line(sb, "DESCRIPTION:" + Escape(string.IsNullOrWhiteSpace(e.Title) ? "Promemoria" : e.Title));
                Line(sb, "END:VALARM");
            }

            Line(sb, "END:VEVENT");
        }

        Line(sb, "END:VCALENDAR");
        return sb.ToString();
    }

    /// <summary>A local time as a property: with the zone name for a series, in UTC otherwise.</summary>
    private static string Timed(string name, DateTime local, TimeZoneInfo zone, string? tzid)
    {
        if (tzid is not null) return $"{name};TZID={tzid}:" + local.ToString(LocalStamp, CultureInfo.InvariantCulture);
        var utc = DateTime.SpecifyKind(local - zone.GetUtcOffset(DateTime.SpecifyKind(local, DateTimeKind.Unspecified)), DateTimeKind.Utc);
        return $"{name}:" + utc.ToString(UtcStamp, CultureInfo.InvariantCulture);
    }

    /// <summary>The IANA name of the zone (Europe/Rome), which every program understands; null when it has none.</summary>
    private static string? IanaId(TimeZoneInfo zone)
    {
        if (zone.Id.Contains('/')) return zone.Id;
        return TimeZoneInfo.TryConvertWindowsIdToIanaId(zone.Id, out var iana) ? iana : null;
    }

    private static string Describe(CalendarEvent e)
    {
        if (e.MeetLink is null || e.Description.Contains(e.MeetLink, StringComparison.Ordinal)) return e.Description;
        return $"{e.Description}\nGoogle Meet: {e.MeetLink}".Trim();
    }

    /// <summary>"N minutes before the start" as an ISO 8601 duration; whole days are written as days.</summary>
    internal static string Trigger(int minutes) => minutes switch
    {
        <= 0 => "PT0S",
        _ when minutes % (24 * 60) == 0 => $"-P{minutes / (24 * 60)}D",
        _ => $"-PT{minutes}M",
    };

    /// <summary>Text values: backslash, semicolon, comma and line breaks are escaped.</summary>
    private static string Escape(string s) =>
        Clean(s).Replace("\\", "\\\\").Replace(";", "\\;").Replace(",", "\\,").Replace("\r\n", "\\n").Replace("\n", "\\n").Replace("\r", "\\n");

    /// <summary>Removes control characters other than line breaks (those are handled by <see cref="Escape"/> and <see cref="Line"/>).</summary>
    private static string Clean(string s) => new(s.Where(ch => ch is '\n' or '\r' || ch >= ' ').ToArray());

    /// <summary>Writes a line folded at 75 bytes; continuation lines start with a space. A character is never cut.</summary>
    private static void Line(StringBuilder sb, string text)
    {
        var single = text.Replace('\r', ' ').Replace('\n', ' ');
        var used = 0;
        foreach (var rune in single.EnumerateRunes())
        {
            var size = rune.Utf8SequenceLength;
            if (used + size > 75)
            {
                sb.Append("\r\n ");
                used = 1;
            }

            sb.Append(rune.ToString());
            used += size;
        }

        sb.Append("\r\n");
    }
}
