using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using GGCalendar.Core.Models;

namespace GGCalendar.Providers.Google;

/// <summary>Conversion between the Google Calendar v3 event resource and <see cref="CalendarEvent"/>.</summary>
public static class GoogleEventMapper
{
    private const string DateFormat = "yyyy-MM-dd";

    /// <summary>Returns null for cancelled events or events without usable dates.</summary>
    public static CalendarEvent? FromGoogle(JsonElement e, string localId, string calendarId, IReadOnlyList<EventReminder> calendarDefaults)
    {
        if (Str(e, "status") == "cancelled") return null;
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

        var meet = Str(e, "hangoutLink");
        string? rrule = null;
        if (e.TryGetProperty("recurrence", out var rec))
            rrule = rec.EnumerateArray().Select(x => x.GetString()).FirstOrDefault(x => x?.StartsWith("RRULE:", StringComparison.OrdinalIgnoreCase) == true)?[6..];
        return new CalendarEvent(localId, calendarId, Str(e, "summary") ?? "", start, end, allDay, Str(e, "location") ?? "", Str(e, "description") ?? "")
        {
            Recurrence = rrule,
            Attendees = attendees,
            Reminders = reminders,
            Availability = Str(e, "transparency") == "transparent" ? EventAvailability.Free : EventAvailability.Busy,
            Visibility = Str(e, "visibility") switch { "public" => EventVisibility.Public, "private" => EventVisibility.Private, _ => EventVisibility.Default },
            MeetLink = meet,
            AddMeet = meet is not null,
            GuestsCanModify = Bool(e, "guestsCanModify", false),
            GuestsCanInvite = Bool(e, "guestsCanInviteOthers", true),
            GuestsCanSeeList = Bool(e, "guestsCanSeeOtherGuests", true),
        };
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
        JsonObject Time(DateTime t)
        {
            if (ev.IsAllDay) return new JsonObject { ["date"] = t.ToString(DateFormat, CultureInfo.InvariantCulture) };
            var o = new JsonObject
            {
                ["dateTime"] = new DateTimeOffset(DateTime.SpecifyKind(t, DateTimeKind.Unspecified), TimeZoneInfo.Local.GetUtcOffset(t))
                    .ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture),
            };
            if (zone is not null) o["timeZone"] = zone;
            return o;
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

        if (!string.IsNullOrEmpty(ev.Recurrence)) body["recurrence"] = new JsonArray("RRULE:" + ev.Recurrence);
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
