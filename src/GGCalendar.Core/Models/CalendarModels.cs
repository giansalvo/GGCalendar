namespace GGCalendar.Core.Models;

public sealed record CalendarInfo(string Id, string AccountName, string Name, string ColorHex, bool IsVisible = true);

public enum EventAvailability { Busy, Free }

public enum EventVisibility { Default, Public, Private }

/// <summary>Popup = toast; Email = sent by the provider; Alarm = toast with a sound that loops until dismissed.</summary>
public enum ReminderMethod { Popup, Email, Alarm }

public sealed record EventAttendee(string Email, bool IsOptional = false);

/// <summary>
/// Popup reminders show a notification and play <paramref name="SoundPath"/> (null = default sound from settings,
/// <see cref="NoSound"/> = silent, otherwise a .wav/.mp3 path). Email reminders are sent by the calendar provider.
/// </summary>
public sealed record EventReminder(ReminderMethod Method, int Minutes, string? SoundPath = null)
{
    public const string NoSound = "none";
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
