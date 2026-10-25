using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using GGCalendar.Core.Models;
using GGCalendar.Core.Recurrence;

namespace GGCalendar.Core.Ics;

/// <summary>
/// Turns the blocks read by <see cref="IcsReader"/> into events. The text is untrusted: fields are cut to a maximum length, control
/// characters are dropped, a wrong event is skipped and counted in the report, and nothing is ever sent to anybody. Times become
/// the local time of the PC (<see cref="IcsImportOptions.LocalZone"/>).
/// </summary>
public static class IcsMapper
{
    public const int MaxTitle = 1024;
    public const int MaxLocation = 1024;
    public const int MaxDescription = 8192;
    public const int MaxUid = 512;
    public const int MaxAlarms = 5;
    private const long MaxMinutesBefore = 40_320;

    private static readonly string[] DateFormats = ["yyyyMMdd"];
    private static readonly string[] DateTimeFormats = ["yyyyMMdd'T'HHmmss"];

    public static IcsImport Map(IcsDocument doc, string calendarId, IcsImportOptions? options = null)
    {
        options ??= new IcsImportOptions();
        var local = options.LocalZone ?? TimeZoneInfo.Local;
        var issues = new IcsIssueList();
        foreach (var issue in doc.Issues) issues.Merge(issue);
        var zones = new IcsZoneResolver(doc, issues);
        var method = doc.Properties.FirstOrDefault(p => p.Name == "METHOD")?.Value.Trim().ToUpperInvariant();
        var name = doc.Properties.FirstOrDefault(p => p.Name == "X-WR-CALNAME") is { } nameProperty
            ? Text(nameProperty.Value, MaxTitle, issues, singleLine: true)
            : null;
        if (string.IsNullOrWhiteSpace(name)) name = null;

        var ignored = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var drafts = new List<Draft>();
        foreach (var c in doc.Components)
        {
            switch (c.Name)
            {
                case "VEVENT":
                    if (Build(c, calendarId, local, zones, issues, options, cancelAll: method == "CANCEL") is { } draft) drafts.Add(draft);
                    break;
                case "VTIMEZONE":
                    break;
                default:
                    ignored[c.Name] = ignored.GetValueOrDefault(c.Name) + 1;
                    break;
            }
        }

        var cancelledEvents = 0;
        var cancelledOccurrences = 0;
        var orphans = 0;

        var masters = new Dictionary<string, Draft>();
        foreach (var d in drafts)
        {
            if (d.RecurrenceKey is not null) continue;
            if (d.Cancelled) cancelledEvents++;
            else
            {
                if (masters.ContainsKey(d.Uid)) issues.Add(IcsIssueCode.DuplicateUid, d.Uid);
                masters[d.Uid] = d;
            }
        }

        // A cancelled occurrence of a series becomes an excluded date of it; one without its series is a cancelled event.
        foreach (var d in drafts)
        {
            if (d.RecurrenceKey is null || !d.Cancelled) continue;
            if (masters.TryGetValue(d.Uid, out var master))
            {
                if (master.Recurrence is not null && d.OriginalStart is { } original && !master.Excluded.Contains(original)) master.Excluded.Add(original);
                cancelledOccurrences++;
            }
            else cancelledEvents++;
        }

        var events = new List<CalendarEvent>();
        var seen = new HashSet<string>();
        foreach (var d in drafts)
        {
            if (d.Cancelled) continue;
            if (d.RecurrenceKey is null)
            {
                if (masters[d.Uid] == d) events.Add(d.ToEvent(null, d.Uid));
                continue;
            }

            var id = d.Uid + "|" + d.RecurrenceKey;
            if (!seen.Add(id)) issues.Add(IcsIssueCode.DuplicateUid, d.Uid);
            if (masters.TryGetValue(d.Uid, out var master))
            {
                events.Add(d.ToEvent(masters[d.Uid], d.Uid));
            }
            else
            {
                orphans++;
                events.Add(d.ToEvent(null, id));
            }
        }

        // Modified occurrences point to the local id of their series: the series are created first, then their occurrences.
        var ready = Link(events);
        var report = new IcsReport(method, name, cancelledEvents, cancelledOccurrences, orphans, ignored, issues.List(), doc.Fatal);
        return new IcsImport(ready, IcsMessages.Describe(report)) { Report = report };
    }

    private static List<CalendarEvent> Link(List<CalendarEvent> events)
    {
        // d.ToEvent(master, ...) already carries the master's Id; nothing else to do, kept as a single place to order the result.
        return events.OrderBy(e => e.SeriesMasterId is null ? 0 : 1).ToList();
    }

    // ---- one VEVENT ----

    private sealed class Draft(
        string uid, string? recurrenceKey, DateTime? originalStart, bool cancelled, string calendarId, string title, string location, string description,
        DateTime start, DateTime end, bool allDay, string? recurrence, List<DateTime> excluded, bool busy, EventVisibility visibility,
        IReadOnlyList<EventAttendee> attendees, IReadOnlyList<EventReminder> reminders)
    {
        public string Uid { get; } = uid;
        public string? RecurrenceKey { get; } = recurrenceKey;
        public DateTime? OriginalStart { get; } = originalStart;
        public bool Cancelled { get; } = cancelled;
        public string? Recurrence { get; } = recurrence;
        public List<DateTime> Excluded { get; } = excluded;
        public string Id { get; } = Guid.NewGuid().ToString("N");

        public CalendarEvent ToEvent(Draft? master, string icsUid) => new(Id, calendarId, title, start, end, allDay, location, description)
        {
            Attendees = attendees,
            IcsUid = icsUid,
            Reminders = reminders,
            Recurrence = master is null ? recurrence : null,
            ExcludedStarts = master is null ? Excluded : [],
            SeriesMasterId = master?.Id,
            OriginalStart = master is null ? null : originalStart,
            Availability = busy ? EventAvailability.Busy : EventAvailability.Free,
            Visibility = visibility,
            SendInvites = false,
        };
    }

    /// <summary>A date or a date-time as written in the file: where it is (UTC, a zone, or floating) and what it says.</summary>
    private sealed record Temporal(DateTime? Date, DateTime? Wall, bool Utc, IcsZone? Zone)
    {
        public bool IsDate => Date is not null;
    }

    private static Draft? Build(IcsComponent c, string calendarId, TimeZoneInfo local, IcsZoneResolver zones, IcsIssueList issues, IcsImportOptions options, bool cancelAll)
    {
        if (c.Property("DTSTART") is not { } startProperty)
        {
            issues.Add(IcsIssueCode.MissingStart);
            return null;
        }

        if (ParseTemporal(startProperty, zones) is not { } start)
        {
            issues.Add(IcsIssueCode.InvalidDate, startProperty.Value);
            return null;
        }

        var title = Text(c.Property("SUMMARY")?.Value ?? "", MaxTitle, issues, singleLine: true);
        var location = Text(c.Property("LOCATION")?.Value ?? "", MaxLocation, issues, singleLine: true);
        var description = Text(c.Property("DESCRIPTION")?.Value ?? "", MaxDescription, issues, singleLine: false);
        var uid = ReadUid(c, startProperty, title, location, issues);
        var status = c.Property("STATUS")?.Value.Trim().ToUpperInvariant();
        var cancelled = cancelAll || status == "CANCELLED";

        // Time.
        var allDay = start.IsDate;
        var endTemporal = c.Property("DTEND") is { } endProperty ? ParseTemporal(endProperty, zones) : null;
        var duration = c.Property("DURATION") is { } durationProperty ? Duration(durationProperty.Value) : null;
        var eventZone = start.Zone ?? (start.Utc ? IcsZone.Utc : IcsZone.Of(local));
        DateTime begin, end;
        if (allDay)
        {
            begin = start.Date!.Value;
            var endDay = endTemporal is null ? (DateTime?)null : endTemporal.Date ?? endTemporal.Wall?.Date;
            if (endDay is null && duration is { } span) endDay = begin.AddDays(Math.Max(1, (long)(span.TotalSeconds / 86_400)));
            endDay ??= begin.AddDays(1);
            if (endDay <= begin) endDay = begin.AddDays(1);
            end = endDay.Value;
        }
        else
        {
            begin = ToLocal(start, local);
            if (endTemporal is not null) end = ToLocal(endTemporal, local, fallbackZone: eventZone);
            else if (duration is { } span) end = begin + span;
            else end = begin.AddHours(1);
            if (end < begin) issues.Add(IcsIssueCode.EndBeforeStart);
            if (end <= begin) end = begin.AddMinutes(30);
        }

        var rule = c.Property("RRULE") is { } ruleProperty ? Rule(ruleProperty.Value, allDay, eventZone, local, issues) : null;

        // Series: excluded dates; RDATE is not supported.
        var excluded = new List<DateTime>();
        var rdate = c.All("RDATE").Count();
        if (rdate > 0) issues.Add(IcsIssueCode.RdateIgnored, count: rdate);
        if (rule is not null)
        {
            foreach (var p in c.All("EXDATE"))
            {
                foreach (var piece in p.Value.Split(',', StringSplitOptions.RemoveEmptyEntries))
                {
                    var value = piece.Trim();
                    if (value.Length == 0) continue;
                    if (ParseTemporal(p with { Value = value }, zones) is not { } t)
                    {
                        issues.Add(IcsIssueCode.InvalidDate, value);
                        continue;
                    }

                    var day = OccurrenceStart(t, begin, allDay, local);
                    if (!excluded.Contains(day)) excluded.Add(day);
                }
            }
        }

        // Modified occurrence.
        string? recurrenceKey = null;
        DateTime? originalStart = null;
        if (c.Property("RECURRENCE-ID") is { } rid)
        {
            if (ParseTemporal(rid, zones) is not { } t)
            {
                issues.Add(IcsIssueCode.InvalidDate, rid.Value);
                return null;
            }

            if (rid.Params.TryGetValue("RANGE", out var range) && range.Equals("THISANDFUTURE", StringComparison.OrdinalIgnoreCase)) issues.Add(IcsIssueCode.ThisAndFuture);
            originalStart = OccurrenceStart(t, begin, allDay, local);
            recurrenceKey = allDay ? originalStart.Value.ToString("yyyyMMdd", CultureInfo.InvariantCulture)
                : (originalStart.Value - local.GetUtcOffset(originalStart.Value)).ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        }

        if (c.All("ATTACH").Any()) issues.Add(IcsIssueCode.AttachIgnored);
        if (c.All("CATEGORIES").Any()) issues.Add(IcsIssueCode.CategoriesIgnored);

        var attendees = c.All("ATTENDEE")
            .Select(p => new EventAttendee(
                p.Value.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) ? p.Value[7..] : p.Value,
                p.Params.TryGetValue("ROLE", out var role) && role.Equals("OPT-PARTICIPANT", StringComparison.OrdinalIgnoreCase)))
            .Where(a => a.Email.Contains('@'))
            .ToList();

        var alarmBlocks = c.ChildrenNamed("VALARM").ToList();
        var reminders = options.ImportAlarms && alarmBlocks.Count > 0 ? Alarms(alarmBlocks, begin, end, issues) : [];
        var visibility = c.Property("CLASS")?.Value.Trim().ToUpperInvariant() switch
        {
            "PRIVATE" or "CONFIDENTIAL" => EventVisibility.Private,
            "PUBLIC" => EventVisibility.Public,
            _ => EventVisibility.Default,
        };
        var busy = c.Property("TRANSP")?.Value.Trim().ToUpperInvariant() != "TRANSPARENT";
        return new Draft(uid, recurrenceKey, originalStart, cancelled, calendarId, title, location, description, begin, end, allDay, rule, excluded, busy,
            visibility, attendees, reminders);
    }

    private static string ReadUid(IcsComponent c, IcsProperty startProperty, string title, string location, IcsIssueList issues)
    {
        var uid = c.Property("UID")?.Value.Trim();
        if (uid is not null) uid = new string(uid.Where(ch => ch >= ' ').ToArray());
        if (!string.IsNullOrEmpty(uid))
        {
            if (uid.Length <= MaxUid) return uid;
            issues.Add(IcsIssueCode.FieldTruncated, "UID");
            return uid[..MaxUid];
        }

        issues.Add(IcsIssueCode.MissingUid);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(startProperty.Value + "|" + title + "|" + location));
        return "noid-" + Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }

    // ---- dates and zones ----

    private static Temporal? ParseTemporal(IcsProperty p, IcsZoneResolver zones)
    {
        var v = p.Value.Trim();
        if ((p.Params.TryGetValue("VALUE", out var kind) && kind.Equals("DATE", StringComparison.OrdinalIgnoreCase)) || (v.Length == 8 && v.All(char.IsAsciiDigit)))
            return DateTime.TryParseExact(v, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? new Temporal(date, null, false, null) : null;

        var utc = v.EndsWith('Z') || v.EndsWith('z');
        var text = (utc ? v[..^1] : v).ToUpperInvariant();
        if (!DateTime.TryParseExact(text, DateTimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var wall)) return null;
        IcsZone? zone = null;
        if (!utc && p.Params.TryGetValue("TZID", out var tzid)) zone = zones.Find(tzid);
        return new Temporal(null, wall, utc, zone);
    }

    /// <summary>The local time of the PC of a date-time (a date at 00:00).</summary>
    private static DateTime ToLocal(Temporal t, TimeZoneInfo local, IcsZone? fallbackZone = null)
    {
        if (t.Date is { } date) return date;
        var wall = t.Wall!.Value;
        var zone = t.Utc ? IcsZone.Utc : t.Zone ?? fallbackZone;
        if (zone is null) return wall; // floating: the clock of the PC
        var instant = DateTime.SpecifyKind(wall - zone.OffsetAt(wall), DateTimeKind.Utc);
        return DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeFromUtc(instant, local), DateTimeKind.Unspecified);
    }

    /// <summary>The start of the occurrence a RECURRENCE-ID or an EXDATE names: a date for a timed series is the occurrence of that day, at the time of the series.</summary>
    private static DateTime OccurrenceStart(Temporal t, DateTime seriesStart, bool allDay, TimeZoneInfo local)
    {
        if (allDay) return (t.Date ?? t.Wall!.Value.Date);
        if (t.IsDate) return t.Date!.Value.Add(seriesStart.TimeOfDay);
        return ToLocal(t, local);
    }

    // ---- recurrence ----

    private static readonly HashSet<string> SupportedRuleKeys = ["FREQ", "INTERVAL", "COUNT", "UNTIL", "BYDAY", "BYMONTHDAY", "BYMONTH", "BYSETPOS", "WKST"];
    private static readonly HashSet<string> Frequencies = ["DAILY", "WEEKLY", "MONTHLY", "YEARLY"];
    private static readonly Regex DayCode = new("^[+-]?\\d{0,2}(MO|TU|WE|TH|FR|SA|SU)$", RegexOptions.Compiled);

    /// <summary>The rule as the app stores it (UNTIL made unambiguous), or null for a rule that is wrong or that this app cannot show.</summary>
    private static string? Rule(string raw, bool allDay, IcsZone zone, TimeZoneInfo local, IcsIssueList issues)
    {
        var body = raw.Trim();
        if (body.StartsWith("RRULE:", StringComparison.OrdinalIgnoreCase)) body = body[6..];

        string? Bad()
        {
            issues.Add(IcsIssueCode.InvalidRule, body);
            return null;
        }

        var map = new Dictionary<string, string>();
        foreach (var part in body.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            if (string.IsNullOrWhiteSpace(part)) continue;
            var eq = part.IndexOf('=');
            if (eq <= 0) return Bad();
            map[part[..eq].Trim().ToUpperInvariant()] = part[(eq + 1)..].Trim().ToUpperInvariant();
        }

        if (!map.TryGetValue("FREQ", out var freq) || !Frequencies.Contains(freq)) return Bad();
        if (!map.Keys.All(SupportedRuleKeys.Contains)) return Bad();
        if (map.ContainsKey("COUNT") && map.ContainsKey("UNTIL")) return Bad();
        foreach (var key in new[] { "INTERVAL", "COUNT" })
            if (map.TryGetValue(key, out var n) && (!int.TryParse(n, out var parsed) || parsed < 1)) return Bad();
        if (map.TryGetValue("BYDAY", out var byDay) && !byDay.Split(',').All(d => DayCode.IsMatch(d))) return Bad();
        if (map.TryGetValue("BYMONTHDAY", out var byMonthDay) && !byMonthDay.Split(',').All(d => int.TryParse(d, out var n) && n is >= -31 and <= 31 && n != 0)) return Bad();
        if (map.TryGetValue("BYMONTH", out var byMonth) && !byMonth.Split(',').All(d => int.TryParse(d, out var n) && n is >= 1 and <= 12)) return Bad();
        if (map.TryGetValue("BYSETPOS", out var bySetPos) && !bySetPos.Split(',').All(d => int.TryParse(d, out var n) && n != 0)) return Bad();
        if (map.TryGetValue("UNTIL", out var until))
        {
            if (UntilValue(until, allDay, zone, local) is not { } normalized) return Bad();
            map["UNTIL"] = normalized;
        }

        var rule = string.Join(';', map.Select(kv => kv.Key + "=" + kv.Value));
        // What the app can show is decided by the app's own rule model.
        return RecurrenceRule.Parse(rule) is null ? Bad() : rule;
    }

    /// <summary>
    /// UNTIL as the app stores it: the last day of the series in the local time of the PC, written as the end of that day in UTC
    /// (as <see cref="RecurrenceRule.ToRrule"/> does) for a timed series and as a date for an all-day one. A date means the end of that day
    /// in the zone of the event.
    /// </summary>
    private static string? UntilValue(string v, bool allDay, IcsZone zone, TimeZoneInfo local)
    {
        const string stamp = "yyyyMMdd'T'HHmmss";
        DateTime last;
        if (allDay)
        {
            if (v.Length < 8 || !DateTime.TryParseExact(v[..8], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out last)) return null;
            return last.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        }

        DateTime instant;
        if (v.EndsWith('Z'))
        {
            if (!DateTime.TryParseExact(v[..^1], stamp, CultureInfo.InvariantCulture, DateTimeStyles.None, out var utc)) return null;
            instant = utc;
        }
        else if (v.Length == 8)
        {
            if (!DateTime.TryParseExact(v, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) return null;
            var end = date.AddHours(23).AddMinutes(59).AddSeconds(59);
            instant = end - zone.OffsetAt(end);
        }
        else
        {
            if (!DateTime.TryParseExact(v, stamp, CultureInfo.InvariantCulture, DateTimeStyles.None, out var wall)) return null;
            instant = wall - zone.OffsetAt(wall);
        }

        last = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(instant, DateTimeKind.Utc), local).Date;
        return last.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + "T235959Z";
    }

    // ---- reminders ----

    private static List<EventReminder> Alarms(List<IcsComponent> blocks, DateTime start, DateTime end, IcsIssueList issues)
    {
        var lengthSeconds = (long)(end - start).TotalSeconds;
        var result = new List<EventReminder>();
        foreach (var a in blocks)
        {
            if (a.Property("TRIGGER") is not { } trigger)
            {
                issues.Add(IcsIssueCode.InvalidAlarm);
                continue;
            }

            if (trigger.Params.TryGetValue("VALUE", out var kind) && kind.Equals("DATE-TIME", StringComparison.OrdinalIgnoreCase))
            {
                issues.Add(IcsIssueCode.AbsoluteAlarm);
                continue;
            }

            if (DurationSeconds(trigger.Value) is not { } seconds)
            {
                issues.Add(IcsIssueCode.InvalidAlarm, trigger.Value);
                continue;
            }

            var fromStart = trigger.Params.TryGetValue("RELATED", out var related) && related.Equals("END", StringComparison.OrdinalIgnoreCase) ? seconds + lengthSeconds : seconds;
            var before = -fromStart;
            if (before < 0)
            {
                issues.Add(IcsIssueCode.AlarmAfterStart);
                continue;
            }

            var minutes = (before + 59) / 60;
            if (minutes > MaxMinutesBefore)
            {
                issues.Add(IcsIssueCode.AlarmTooEarly);
                continue;
            }

            var method = a.Property("ACTION")?.Value.Trim().ToUpperInvariant() switch
            {
                "AUDIO" => ReminderMethod.Alarm,
                "EMAIL" => ReminderMethod.Email,
                _ => ReminderMethod.Popup,
            };
            var reminder = new EventReminder(method, (int)minutes);
            if (result.Contains(reminder)) continue;
            if (result.Count >= MaxAlarms)
            {
                issues.Add(IcsIssueCode.TooManyAlarms);
                continue;
            }

            result.Add(reminder);
        }

        return result;
    }

    private static readonly Regex DurationPattern = new("^([+-])?P(?:(\\d+)W)?(?:(\\d+)D)?(?:T(?:(\\d+)H)?(?:(\\d+)M)?(?:(\\d+)S)?)?$", RegexOptions.Compiled);

    private static TimeSpan? Duration(string value) => DurationSeconds(value) is { } s ? TimeSpan.FromSeconds(s) : null;

    /// <summary>An RFC 5545 duration in seconds, or null if it is not one.</summary>
    internal static long? DurationSeconds(string value)
    {
        var m = DurationPattern.Match(value.Trim().ToUpperInvariant());
        if (!m.Success) return null;
        var groups = new[] { 2, 3, 4, 5, 6 }.Select(i => m.Groups[i].Value).ToArray();
        if (groups.All(g => g.Length == 0) || groups.Any(g => g.Length > 9)) return null;
        long N(int i) => groups[i].Length == 0 ? 0 : long.Parse(groups[i], CultureInfo.InvariantCulture);
        var total = N(0) * 7 * 86_400 + N(1) * 86_400 + N(2) * 3_600 + N(3) * 60 + N(4);
        return m.Groups[1].Value == "-" ? -total : total;
    }

    // ---- text ----

    private static string Text(string raw, int max, IcsIssueList issues, bool singleLine)
    {
        var sb = new StringBuilder(raw.Length);
        for (var i = 0; i < raw.Length; i++)
        {
            if (raw[i] == '\\' && i + 1 < raw.Length)
            {
                var next = raw[i + 1];
                sb.Append(next is 'n' or 'N' ? '\n' : next);
                i++;
                continue;
            }

            sb.Append(raw[i]);
        }

        var cleaned = new string(sb.ToString().Where(ch => ch == '\n' || (ch >= ' ' && ch != '\u007F')).ToArray());
        var shaped = singleLine ? cleaned.Replace('\n', ' ') : cleaned;
        var trimmed = shaped.Trim();
        if (trimmed.Length <= max) return trimmed;
        issues.Add(IcsIssueCode.FieldTruncated);
        // Do not cut a surrogate pair in half.
        var cut = char.IsHighSurrogate(trimmed[max - 1]) ? max - 1 : max;
        return trimmed[..cut];
    }
}
