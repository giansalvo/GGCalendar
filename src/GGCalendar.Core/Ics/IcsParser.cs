using System.Globalization;
using GGCalendar.Core.Models;

namespace GGCalendar.Core.Ics;

/// <summary>Reads VEVENTs from an RFC 5545 .ics text. Times are converted to the local time zone.</summary>
public static class IcsParser
{
    private sealed record Prop(string Name, Dictionary<string, string> Params, string Value);

    private sealed class Raw
    {
        public List<Prop> Props { get; } = [];
        public List<List<Prop>> Alarms { get; } = [];
        public Prop? Get(string name) => Props.FirstOrDefault(p => p.Name == name);
    }

    public static IReadOnlyList<CalendarEvent> Parse(string text, string calendarId)
    {
        var raws = ReadEvents(text);
        var events = new List<CalendarEvent>();
        var masters = new Dictionary<string, CalendarEvent>();

        foreach (var raw in raws.Where(r => r.Get("RECURRENCE-ID") is null))
        {
            if (raw.Get("STATUS")?.Value.Equals("CANCELLED", StringComparison.OrdinalIgnoreCase) == true) continue;
            if (Build(raw, calendarId, null, null) is not { } ev) continue;
            events.Add(ev);
            if (raw.Get("UID") is { } uid) masters[uid.Value] = ev;
        }

        foreach (var raw in raws.Where(r => r.Get("RECURRENCE-ID") is not null))
        {
            var uid = raw.Get("UID")?.Value;
            var original = ParseTime(raw.Get("RECURRENCE-ID")!, out _);
            if (uid is not null && original is not null && masters.TryGetValue(uid, out var master))
            {
                var cancelled = raw.Get("STATUS")?.Value.Equals("CANCELLED", StringComparison.OrdinalIgnoreCase) == true;
                var ex = Build(raw, calendarId, master.Id, original);
                if (ex is not null) events.Add(cancelled ? ex with { IsCancelled = true } : ex);
            }
            else if (raw.Get("STATUS")?.Value.Equals("CANCELLED", StringComparison.OrdinalIgnoreCase) != true
                     && Build(raw, calendarId, null, null) is { } standalone)
            {
                events.Add(standalone);
            }
        }

        return events;
    }

    private static CalendarEvent? Build(Raw raw, string calendarId, string? masterId, DateTime? originalStart)
    {
        if (raw.Get("DTSTART") is not { } dtstart) return null;
        var start = ParseTime(dtstart, out var allDay);
        if (start is null) return null;

        DateTime end;
        if (raw.Get("DTEND") is { } dtend && ParseTime(dtend, out _) is { } e) end = e;
        else if (raw.Get("DURATION") is { } dur && ParseDuration(dur.Value) is { } span) end = start.Value + span;
        else end = allDay ? start.Value.AddDays(1) : start.Value.AddHours(1);
        if (end <= start) end = allDay ? start.Value.AddDays(1) : start.Value.AddMinutes(30);

        var attendees = raw.Props.Where(p => p.Name == "ATTENDEE")
            .Select(p => new EventAttendee(
                p.Value.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) ? p.Value[7..] : p.Value,
                p.Params.TryGetValue("ROLE", out var role) && role.Equals("OPT-PARTICIPANT", StringComparison.OrdinalIgnoreCase)))
            .Where(a => a.Email.Contains('@'))
            .ToList();

        var reminders = new List<EventReminder>();
        foreach (var alarm in raw.Alarms)
        {
            var trigger = alarm.FirstOrDefault(p => p.Name == "TRIGGER");
            if (trigger is null || trigger.Params.ContainsKey("VALUE") && trigger.Params["VALUE"] == "DATE-TIME") continue;
            if (ParseDuration(trigger.Value) is not { } span || span > TimeSpan.Zero) continue;
            var action = alarm.FirstOrDefault(p => p.Name == "ACTION")?.Value.ToUpperInvariant();
            var method = action == "EMAIL" ? ReminderMethod.Email : action == "AUDIO" ? ReminderMethod.Alarm : ReminderMethod.Popup;
            reminders.Add(new EventReminder(method, (int)(-span).TotalMinutes));
        }

        var exdates = new List<DateTime>();
        foreach (var p in raw.Props.Where(p => p.Name == "EXDATE"))
            foreach (var v in p.Value.Split(',', StringSplitOptions.RemoveEmptyEntries))
                if (ParseTime(p with { Value = v.Trim() }, out _) is { } d) exdates.Add(d);

        return new CalendarEvent(Guid.NewGuid().ToString("N"), calendarId, Unescape(raw.Get("SUMMARY")?.Value ?? ""), start.Value, end, allDay,
            Unescape(raw.Get("LOCATION")?.Value ?? ""), Unescape(raw.Get("DESCRIPTION")?.Value ?? ""))
        {
            Attendees = attendees,
            Reminders = reminders,
            Recurrence = masterId is null ? NormalizeRule(raw.Get("RRULE")?.Value) : null,
            ExcludedStarts = exdates,
            SeriesMasterId = masterId,
            OriginalStart = originalStart,
            Availability = raw.Get("TRANSP")?.Value.Equals("TRANSPARENT", StringComparison.OrdinalIgnoreCase) == true ? EventAvailability.Free : EventAvailability.Busy,
            Visibility = raw.Get("CLASS")?.Value.ToUpperInvariant() switch
            {
                "PRIVATE" or "CONFIDENTIAL" => EventVisibility.Private,
                "PUBLIC" => EventVisibility.Public,
                _ => EventVisibility.Default,
            },
            SendInvites = false,
        };
    }

    private static string? NormalizeRule(string? rule) => string.IsNullOrWhiteSpace(rule) ? null : rule.Trim();

    private static List<Raw> ReadEvents(string text)
    {
        var result = new List<Raw>();
        Raw? current = null;
        List<Prop>? alarm = null;
        foreach (var line in Unfold(text))
        {
            if (line.Equals("BEGIN:VEVENT", StringComparison.OrdinalIgnoreCase)) { current = new Raw(); continue; }
            if (line.Equals("END:VEVENT", StringComparison.OrdinalIgnoreCase))
            {
                if (current is not null) result.Add(current);
                current = null;
                alarm = null;
                continue;
            }

            if (current is null) continue;
            if (line.Equals("BEGIN:VALARM", StringComparison.OrdinalIgnoreCase)) { alarm = []; current.Alarms.Add(alarm); continue; }
            if (line.Equals("END:VALARM", StringComparison.OrdinalIgnoreCase)) { alarm = null; continue; }

            if (ParseLine(line) is not { } prop) continue;
            (alarm ?? current.Props).Add(prop);
        }

        return result;
    }

    private static IEnumerable<string> Unfold(string text)
    {
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var sb = new System.Text.StringBuilder();
        foreach (var line in lines)
        {
            if (line.Length > 0 && (line[0] == ' ' || line[0] == '\t')) { sb.Append(line, 1, line.Length - 1); continue; }
            if (sb.Length > 0) yield return sb.ToString();
            sb.Clear().Append(line);
        }

        if (sb.Length > 0) yield return sb.ToString();
    }

    private static Prop? ParseLine(string line)
    {
        var inQuotes = false;
        var colon = -1;
        for (var i = 0; i < line.Length; i++)
        {
            if (line[i] == '"') inQuotes = !inQuotes;
            else if (line[i] == ':' && !inQuotes) { colon = i; break; }
        }

        if (colon <= 0) return null;
        var head = line[..colon].Split(';');
        var props = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in head.Skip(1))
        {
            var kv = p.Split('=', 2);
            if (kv.Length == 2) props[kv[0]] = kv[1].Trim('"');
        }

        return new Prop(head[0].ToUpperInvariant(), props, line[(colon + 1)..]);
    }

    private static DateTime? ParseTime(Prop p, out bool allDay)
    {
        allDay = false;
        var v = p.Value.Trim();
        if (v.Length == 8 && DateTime.TryParseExact(v, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            allDay = true;
            return date;
        }

        if (v.EndsWith('Z'))
            return DateTime.TryParseExact(v, "yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var utc)
                ? utc.ToLocalTime()
                : null;

        if (!DateTime.TryParseExact(v, "yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var local)) return null;
        if (p.Params.TryGetValue("TZID", out var tzid) && TimeZoneInfo.TryFindSystemTimeZoneById(tzid, out var zone))
        {
            try { return TimeZoneInfo.ConvertTime(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), zone, TimeZoneInfo.Local); }
            catch (ArgumentException) { return local; }
        }

        return local;
    }

    /// <summary>Parses an RFC 5545 duration such as -PT15M, P1D or P1DT2H30M.</summary>
    private static TimeSpan? ParseDuration(string value)
    {
        var s = value.Trim();
        var sign = 1;
        if (s.StartsWith('-')) { sign = -1; s = s[1..]; }
        else if (s.StartsWith('+')) s = s[1..];
        if (!s.StartsWith('P')) return null;

        var total = TimeSpan.Zero;
        var number = "";
        var inTime = false;
        foreach (var ch in s[1..])
        {
            if (ch == 'T') { inTime = true; continue; }
            if (char.IsDigit(ch)) { number += ch; continue; }
            if (!int.TryParse(number, out var n)) return null;
            number = "";
            total += ch switch
            {
                'W' => TimeSpan.FromDays(7 * n),
                'D' => TimeSpan.FromDays(n),
                'H' when inTime => TimeSpan.FromHours(n),
                'M' when inTime => TimeSpan.FromMinutes(n),
                'S' when inTime => TimeSpan.FromSeconds(n),
                _ => TimeSpan.Zero,
            };
        }

        return sign * total;
    }

    private static string Unescape(string s)
    {
        if (!s.Contains('\\')) return s;
        var sb = new System.Text.StringBuilder();
        for (var i = 0; i < s.Length; i++)
        {
            if (s[i] == '\\' && i + 1 < s.Length)
            {
                i++;
                sb.Append(s[i] is 'n' or 'N' ? '\n' : s[i]);
            }
            else sb.Append(s[i]);
        }

        return sb.ToString();
    }
}
