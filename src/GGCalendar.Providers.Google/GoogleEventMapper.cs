using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using GGCalendar.Core.Models;

namespace GGCalendar.Providers.Google;

/// <summary>Conversion between the Google Calendar v3 event resource and <see cref="CalendarEvent"/>.</summary>
public static class GoogleEventMapper
{
    private const string DateFormat = "yyyy-MM-dd";

    /// <summary>
    /// Returns null for cancelled events or events without usable dates. A cancelled occurrence of a series
    /// (<paramref name="seriesMasterLocalId"/> set) is returned as a tombstone with <see cref="CalendarEvent.IsCancelled"/>.
    /// </summary>
    public static CalendarEvent? FromGoogle(JsonElement e, string localId, string calendarId, IReadOnlyList<EventReminder> calendarDefaults,
        string? seriesMasterLocalId = null)
    {
        var originalStart = seriesMasterLocalId is null ? null : ParseOriginalStart(e, out _);
        if (Str(e, "status") == "cancelled")
        {
            if (seriesMasterLocalId is null || originalStart is not { } os) return null;
            ParseOriginalStart(e, out var originalAllDay);
            return new CalendarEvent(localId, calendarId, "", os, os, originalAllDay)
            {
                SeriesMasterId = seriesMasterLocalId, OriginalStart = os, IsCancelled = true, Reminders = [],
            };
        }

        if (!e.TryGetProperty("start", out var s) || !e.TryGetProperty("end", out var en)) return null;

        bool allDay;
        DateTime start, end;
        if (Str(s, "date") is { } sd)
        {
            allDay = true;
            start = DateTime.ParseExact(sd, DateFormat, CultureInfo.InvariantCulture);
            end = Str(en, "date") is { } ed ? DateTime.ParseExact(ed, DateFormat, CultureInfo.InvariantCulture) : start.AddDays(1);
        }
        else if (Str(s, "dateTime") is { } sdt && Str(en, "dateTime") is { } edt)
        {
            allDay = false;
            start = DateTimeOffset.Parse(sdt, CultureInfo.InvariantCulture).LocalDateTime;
            end = DateTimeOffset.Parse(edt, CultureInfo.InvariantCulture).LocalDateTime;
        }
        else return null;

        var attendees = new List<EventAttendee>();
        if (e.TryGetProperty("attendees", out var att))
        {
            foreach (var a in att.EnumerateArray())
            {
                if (a.TryGetProperty("resource", out var res) && res.GetBoolean()) continue;
                if (Str(a, "email") is { } mail)
                    attendees.Add(new EventAttendee(mail, a.TryGetProperty("optional", out var opt) && opt.GetBoolean()));
            }
        }

        var reminders = calendarDefaults;
        if (e.TryGetProperty("reminders", out var rem))
        {
            var useDefault = rem.TryGetProperty("useDefault", out var ud) && ud.GetBoolean();
            if (!useDefault)
            {
                reminders = [];
                if (rem.TryGetProperty("overrides", out var ov))
                {
                    reminders = ov.EnumerateArray()
                        .Select(o => new EventReminder(Str(o, "method") == "email" ? ReminderMethod.Email : ReminderMethod.Popup,
                            o.TryGetProperty("minutes", out var m) ? m.GetInt32() : 10))
                        .ToList();
                }
            }
        }

        // Google creates the conference asynchronously: right after the request there is conferenceData but no link yet.
        var meet = Str(e, "hangoutLink") ?? MeetUri(e);
        var hasConference = meet is not null || e.TryGetProperty("conferenceData", out _);
        string? rrule = null;
        var excluded = new List<DateTime>();
        if (e.TryGetProperty("recurrence", out var rec))
        {
            foreach (var line in rec.EnumerateArray().Select(x => x.GetString()).Where(x => x is not null))
            {
                if (line!.StartsWith("RRULE:", StringComparison.OrdinalIgnoreCase)) rrule ??= line[6..];
                else if (line.StartsWith("EXDATE", StringComparison.OrdinalIgnoreCase)) excluded.AddRange(ParseExDates(line));
            }
        }

        return new CalendarEvent(localId, calendarId, Str(e, "summary") ?? "", start, end, allDay, Str(e, "location") ?? "", Str(e, "description") ?? "")
        {
            Recurrence = rrule,
            SeriesMasterId = seriesMasterLocalId,
            OriginalStart = originalStart,
            ExcludedStarts = excluded,
            Attendees = attendees,
            Reminders = reminders,
            Availability = Str(e, "transparency") == "transparent" ? EventAvailability.Free : EventAvailability.Busy,
            Visibility = Str(e, "visibility") switch { "public" => EventVisibility.Public, "private" => EventVisibility.Private, _ => EventVisibility.Default },
            MeetLink = meet,
            AddMeet = hasConference,
            GuestsCanModify = Bool(e, "guestsCanModify", false),
            GuestsCanInvite = Bool(e, "guestsCanInviteOthers", true),
            GuestsCanSeeList = Bool(e, "guestsCanSeeOtherGuests", true),
        };
    }

    private static string? MeetUri(JsonElement e) =>
        e.TryGetProperty("conferenceData", out var cd) && cd.TryGetProperty("entryPoints", out var eps) && eps.ValueKind == JsonValueKind.Array
            ? eps.EnumerateArray().Where(p => Str(p, "entryPointType") == "video").Select(p => Str(p, "uri")).FirstOrDefault(u => u is not null)
            : null;

    private static DateTime? ParseOriginalStart(JsonElement e, out bool allDay)
    {
        allDay = false;
        if (!e.TryGetProperty("originalStartTime", out var o)) return null;
        if (Str(o, "date") is { } d)
        {
            allDay = true;
            return DateTime.ParseExact(d, DateFormat, CultureInfo.InvariantCulture);
        }

        return Str(o, "dateTime") is { } dt ? DateTimeOffset.Parse(dt, CultureInfo.InvariantCulture).LocalDateTime : null;
    }

    /// <summary>Id of a single occurrence of a recurring event: the master id plus the original start (UTC, or the date for all-day events).</summary>
    public static string InstanceId(string masterRemoteId, DateTime originalStart, bool allDay) =>
        masterRemoteId + "_" + (allDay
            ? originalStart.ToString("yyyyMMdd", CultureInfo.InvariantCulture)
            : DateTime.SpecifyKind(originalStart, DateTimeKind.Local).ToUniversalTime().ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture));

    private static IEnumerable<DateTime> ParseExDates(string line)
    {
        var colon = line.IndexOf(':');
        if (colon < 0) yield break;
        var header = line[..colon];
        var tz = header.Split(';').FirstOrDefault(p => p.StartsWith("TZID=", StringComparison.OrdinalIgnoreCase))?[5..];
        foreach (var raw in line[(colon + 1)..].Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var v = raw.Trim();
            if (v.Length == 8 && DateTime.TryParseExact(v, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
            {
                yield return day;
            }
            else if (v.EndsWith('Z') && DateTime.TryParseExact(v, "yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture,
                         DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var utc))
            {
                yield return DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime();
            }
            else if (DateTime.TryParseExact(v, "yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
            {
                if (tz is not null && TimeZoneInfo.TryFindSystemTimeZoneById(tz, out var zone))
                    yield return TimeZoneInfo.ConvertTime(local, zone, TimeZoneInfo.Local);
                else yield return local;
            }
        }
    }

    public static IReadOnlyList<EventReminder> ParseDefaultReminders(JsonElement calendarListEntry)
    {
        if (!calendarListEntry.TryGetProperty("defaultReminders", out var dr)) return [];
        return dr.EnumerateArray()
            .Select(o => new EventReminder(Str(o, "method") == "email" ? ReminderMethod.Email : ReminderMethod.Popup,
                o.TryGetProperty("minutes", out var m) ? m.GetInt32() : 10))
            .ToList();
    }

    public static JsonObject ToGoogle(CalendarEvent ev, bool includeMeetRequest)
    {
        var zone = IanaZone();
        // Updates are PATCHes, which merge "start"/"end" with what Google has: the unused form must be sent as null to be
        // removed, otherwise switching between all-day and timed leaves both "date" and "dateTime" and Google rejects it.
        JsonObject Time(DateTime t)
        {
            if (ev.IsAllDay)
                return new JsonObject { ["date"] = t.ToString(DateFormat, CultureInfo.InvariantCulture), ["dateTime"] = null, ["timeZone"] = null };
            return new JsonObject
            {
                ["date"] = null,
                ["dateTime"] = new DateTimeOffset(DateTime.SpecifyKind(t, DateTimeKind.Unspecified), TimeZoneInfo.Local.GetUtcOffset(t))
                    .ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture),
                ["timeZone"] = zone,
            };
        }

        var body = new JsonObject
        {
            ["summary"] = ev.Title,
            ["location"] = ev.Location,
            ["description"] = ev.Description,
            ["start"] = Time(ev.Start),
            ["end"] = Time(ev.End),
            ["transparency"] = ev.Availability == EventAvailability.Free ? "transparent" : "opaque",
            ["visibility"] = ev.Visibility switch { EventVisibility.Public => "public", EventVisibility.Private => "private", _ => "default" },
            ["guestsCanModify"] = ev.GuestsCanModify,
            ["guestsCanInviteOthers"] = ev.GuestsCanInvite,
            ["guestsCanSeeOtherGuests"] = ev.GuestsCanSeeList,
            ["attendees"] = new JsonArray(ev.Attendees.Select(a => (JsonNode)new JsonObject { ["email"] = a.Email, ["optional"] = a.IsOptional }).ToArray()),
            ["reminders"] = new JsonObject
            {
                ["useDefault"] = false,
                ["overrides"] = new JsonArray(ev.Reminders
                    .Select(r => (JsonNode)new JsonObject { ["method"] = r.Method == ReminderMethod.Email ? "email" : "popup", ["minutes"] = r.Minutes })
                    .ToArray()),
            },
        };

        if (!string.IsNullOrEmpty(ev.Recurrence))
        {
            var lines = new JsonArray("RRULE:" + ev.Recurrence);
            if (ev.ExcludedStarts.Count > 0)
            {
                lines.Add(ev.IsAllDay
                    ? "EXDATE;VALUE=DATE:" + string.Join(',', ev.ExcludedStarts.Select(d => d.ToString("yyyyMMdd", CultureInfo.InvariantCulture)))
                    : "EXDATE:" + string.Join(',', ev.ExcludedStarts.Select(d => d.ToUniversalTime().ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture))));
            }

            body["recurrence"] = lines;
        }
        if (includeMeetRequest)
        {
            body["conferenceData"] = new JsonObject
            {
                ["createRequest"] = new JsonObject
                {
                    ["requestId"] = Guid.NewGuid().ToString("N"),
                    ["conferenceSolutionKey"] = new JsonObject { ["type"] = "hangoutsMeet" },
                },
            };
        }

        return body;
    }

    private static string? IanaZone() =>
        TimeZoneInfo.TryConvertWindowsIdToIanaId(TimeZoneInfo.Local.Id, out var iana) ? iana : null;

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool Bool(JsonElement e, string name, bool fallback) =>
        e.TryGetProperty(name, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : fallback;
}
