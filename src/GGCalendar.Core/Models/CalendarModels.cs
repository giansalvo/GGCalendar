namespace GGCalendar.Core.Models;

public sealed record CalendarInfo(string Id, string AccountName, string Name, string ColorHex, bool IsVisible = true);

public enum EventAvailability { Busy, Free }

public enum EventVisibility { Default, Public, Private }

/// <summary>Popup = toast; Email = sent by the provider; Alarm = toast with a sound that loops until dismissed (at most 2 minutes).</summary>
public enum ReminderMethod { Popup, Email, Alarm }

public sealed record EventAttendee(string Email, bool IsOptional = false);

/// <summary>
/// Popup reminders show a notification and play <paramref name="SoundPath"/> (null = default sound from settings,
/// <see cref="NoSound"/> = silent, otherwise a .wav/.mp3 path). Email reminders are sent by the calendar provider.
/// </summary>
public sealed record EventReminder(ReminderMethod Method, int Minutes, string? SoundPath = null)
{
    public const string NoSound = "none";

    /// <summary>Private extended property of the Google event that lists, in minutes, the popup reminders that are alarms.</summary>
    public const string AlarmProperty = "ggAlarm";

    /// <summary>
    /// The value of <see cref="AlarmProperty"/>: the minutes of the alarms, ascending and without repeats ("10,30"), or
    /// an empty string when there are none. Never null: Google refuses a null value with a 400.
    /// </summary>
    public static string FormatAlarmMinutes(IEnumerable<EventReminder> reminders) =>
        string.Join(',', reminders.Where(r => r.Method == ReminderMethod.Alarm).Select(r => r.Minutes).Distinct().Order());

    /// <summary>Reads <see cref="AlarmProperty"/>: spaces are allowed and anything that is not a number is ignored.</summary>
    public static HashSet<int> ParseAlarmMinutes(string? value)
    {
        var minutes = new HashSet<int>();
        if (value is null) return minutes;
        foreach (var part in value.Split(','))
            if (int.TryParse(part.Trim(), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var m))
                minutes.Add(m);
        return minutes;
    }

    /// <summary>
    /// Turns the popups of Google whose minutes are in <paramref name="alarmMinutes"/> into alarms. One popup per number
    /// of minutes: a second popup with the same minutes stays a notification. Email reminders are not touched.
    /// </summary>
    public static IReadOnlyList<EventReminder> ApplyAlarmMinutes(IReadOnlyList<EventReminder> remote, IReadOnlySet<int> alarmMinutes)
    {
        var left = new HashSet<int>(alarmMinutes);
        return remote.Select(r => r.Method == ReminderMethod.Popup && left.Remove(r.Minutes) ? r with { Method = ReminderMethod.Alarm } : r).ToList();
    }

    /// <summary>
    /// For an event whose alarms Google has told (<see cref="AlarmProperty"/>): only the custom sounds are local, so each
    /// reminder takes the sound of the local one with the same method and minutes.
    /// </summary>
    public static IReadOnlyList<EventReminder> KeepLocalSounds(IReadOnlyList<EventReminder> remote, IReadOnlyList<EventReminder> local)
    {
        var unused = local.Where(l => l.Method != ReminderMethod.Email).ToList();
        return remote.Select(r =>
        {
            if (r.Method == ReminderMethod.Email) return r;
            var match = unused.FirstOrDefault(l => l.Method == r.Method && l.Minutes == r.Minutes);
            if (match is null) return r;
            unused.Remove(match);
            return r with { SoundPath = match.SoundPath };
        }).ToList();
    }

    /// <summary>
    /// Fallback for events that carry no <see cref="AlarmProperty"/> (created before it, or by a program that does not
    /// write it): Google only knows popup and email reminders, so alarms and custom sounds come back from a
    /// sync as plain popups. Each remote popup takes the method and sound of the local non-email reminder with the same
    /// minutes, so a sync does not turn an alarm back into a notification.
    /// </summary>
    public static IReadOnlyList<EventReminder> KeepLocalOptions(IReadOnlyList<EventReminder> remote, IReadOnlyList<EventReminder> local)
    {
        var unused = local.Where(l => l.Method != ReminderMethod.Email).ToList();
        return remote.Select(r =>
        {
            if (r.Method != ReminderMethod.Popup) return r;
            var match = unused.FirstOrDefault(l => l.Minutes == r.Minutes);
            if (match is null) return r;
            unused.Remove(match);
            return match;
        }).ToList();
    }
}

/// <summary>For all-day events Start is the first day at 00:00 and End is exclusive (00:00 of the day after the last).</summary>
public sealed record CalendarEvent(
    string Id,
    string CalendarId,
    string Title,
    DateTime Start,
    DateTime End,
    bool IsAllDay = false,
    string Location = "",
    string Description = "")
{
    public IReadOnlyList<EventAttendee> Attendees { get; init; } = [];
    public IReadOnlyList<EventReminder> Reminders { get; init; } = [new EventReminder(ReminderMethod.Popup, 30)];

    /// <summary>UID of the .ics file the event was imported from; used to recognise it when the same file is imported again.</summary>
    public string? IcsUid { get; init; }
    /// <summary>RFC 5545 RRULE value (without the "RRULE:" prefix), or null when the event does not repeat.</summary>
    public string? Recurrence { get; init; }

    /// <summary>Start times of single occurrences removed from the series (RFC 5545 EXDATE).</summary>
    public IReadOnlyList<DateTime> ExcludedStarts { get; init; } = [];

    /// <summary>
    /// Set on a single modified (or cancelled) occurrence of a series: the local id of the series' master event
    /// and the start the occurrence had before it was modified.
    /// </summary>
    public string? SeriesMasterId { get; init; }
    public DateTime? OriginalStart { get; init; }

    /// <summary>A removed occurrence kept as a tombstone so the series does not show it again after a sync.</summary>
    public bool IsCancelled { get; init; }

    /// <summary>
    /// True when Google sent <see cref="EventReminder.AlarmProperty"/> for this event, even empty: the alarms in
    /// <see cref="Reminders"/> then come from Google and are not guessed from the local copy. Never persisted.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool AlarmsFromProvider { get; init; }

    /// <summary>Set only on expanded occurrences of a repeating event (0 = the first one); never persisted.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public int? OccurrenceIndex { get; init; }

    /// <summary>Per-event color override; null uses the calendar color.</summary>
    public string? ColorHex { get; init; }

    public EventAvailability Availability { get; init; } = EventAvailability.Busy;
    public EventVisibility Visibility { get; init; } = EventVisibility.Default;

    /// <summary>True when a Google Meet conference must be created for this event on sync.</summary>
    public bool AddMeet { get; init; }
    public string? MeetLink { get; init; }

    /// <summary>Whether invitation emails are sent to the attendees when the event is saved.</summary>
    public bool SendInvites { get; init; } = true;

    public bool GuestsCanModify { get; init; }
    public bool GuestsCanInvite { get; init; }
    public bool GuestsCanSeeList { get; init; } = true;
}
