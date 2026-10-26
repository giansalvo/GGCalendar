using System;
using System.Collections.Generic;
using System.Linq;
using GGCalendar.App.Services;
using GGCalendar.App.Views;
using GGCalendar.Core.Localization;
using GGCalendar.Core.Models;
using GGCalendar.Core.Reminders;

namespace GGCalendar.App.ViewModels;

public enum AlarmRowKind { Ringing, Queued, Silenced, Planned, Snoozed }

/// <summary>One line of the list of the alarms (issue #18).</summary>
public sealed record AlarmRow(
    AlarmRowKind Kind,
    string Key,
    CalendarEvent Event,
    string Title,
    string CalendarName,
    string ColorHex,
    DateTime FireAt,
    string Detail,
    PendingAlarm? Pending)
{
    public bool IsRinging => Kind is AlarmRowKind.Ringing or AlarmRowKind.Queued or AlarmRowKind.Silenced;
}

/// <summary>
/// The list of the alarms that ring now and of those waiting to ring: what the button of the alarms and its window show.
/// It joins the scheduler (pending alarms), the ringer (alarms that ring) and the calendars (name, colour, shown or hidden).
/// </summary>
public sealed class AlarmsViewModel : IDisposable
{
    private readonly ReminderScheduler _scheduler;
    private readonly AlarmRinger _ringer;
    private readonly MainViewModel _main;
    private readonly Action<string> _stopRinging;
    private readonly Action _stopAllRinging;

    public AlarmsViewModel(ReminderScheduler scheduler, AlarmRinger ringer, MainViewModel main, Action<string> stopRinging, Action stopAllRinging)
    {
        _scheduler = scheduler;
        _ringer = ringer;
        _main = main;
        _stopRinging = stopRinging;
        _stopAllRinging = stopAllRinging;
        scheduler.PendingChanged += Raise;
        ringer.Changed += Raise;
        L.Changed += Raise;
    }

    public void Dispose()
    {
        _scheduler.PendingChanged -= Raise;
        _ringer.Changed -= Raise;
        L.Changed -= Raise;
    }

    /// <summary>The list may be different (an alarm rang, was cancelled, the calendars shown changed...).</summary>
    public event Action? Changed;

    private void Raise() => Changed?.Invoke();

    /// <summary>The time now, by the clock of the reminders.</summary>
    public DateTime Now => _scheduler.Now;

    /// <summary>False when the notifications are off in the settings: no alarm rings then.</summary>
    public bool NotificationsEnabled => _scheduler.Enabled;

    /// <summary>The alarms that ring now (the one making the sound first), then the ones waiting to ring, soonest first.</summary>
    public IReadOnlyList<AlarmRow> Rows()
    {
        var rows = new List<AlarmRow>();
        foreach (var r in _ringer.Items)
        {
            var kind = r.State switch { RingState.Ringing => AlarmRowKind.Ringing, RingState.Silenced => AlarmRowKind.Silenced, _ => AlarmRowKind.Queued };
            rows.Add(Make(kind, r.Key, r.Reminder, r.Reminder.FireAt, null, hidden: false));
        }

        rows = rows.OrderBy(r => r.Kind == AlarmRowKind.Ringing ? 0 : r.Kind == AlarmRowKind.Queued ? 1 : 2).ThenBy(r => r.FireAt).ToList();
        // An alarm that is ringing is no longer pending, but the snoozed one that came back is the same line: it is not counted twice.
        foreach (var p in _scheduler.Pending())
            rows.Add(Make(p.Kind == PendingKind.Snoozed ? AlarmRowKind.Snoozed : AlarmRowKind.Planned, p.Key, p.Reminder, p.FireAt, p, p.CalendarHidden));
        return rows;
    }

    /// <summary>How many alarms there are in all (ringing and waiting): the number on the button.</summary>
    public int Count => _ringer.Items.Count + _scheduler.Pending().Count;

    public bool IsRinging => _ringer.Items.Count > 0;

    /// <summary>The alarm is cancelled on this computer; the event stays.</summary>
    public void Cancel(AlarmRow row)
    {
        if (row.Pending is not { } pending) return;
        _scheduler.Skip(pending);
        _main.SetStatus("alarms.cancelled", row.Title);
    }

    public void StopRinging(AlarmRow row) => _stopRinging(row.Key);

    public void StopAllRinging() => _stopAllRinging();

    private AlarmRow Make(AlarmRowKind kind, string key, DueReminder due, DateTime fireAt, PendingAlarm? pending, bool hidden)
    {
        var ev = due.Event;
        var calendar = _main.Calendars.FirstOrDefault(c => c.Id == ev.CalendarId);
        var title = string.IsNullOrWhiteSpace(ev.Title) ? L.T("common.untitled") : ev.Title;
        return new AlarmRow(kind, key, ev, title, calendar?.Name ?? "", ev.ColorHex ?? calendar?.ColorHex ?? "#1A73E8", fireAt, Detail(kind, due, hidden), pending);
    }

    private string Detail(AlarmRowKind kind, DueReminder due, bool hidden)
    {
        var ev = due.Event;
        switch (kind)
        {
            case AlarmRowKind.Queued: return L.T("alarms.detail_queued");
            case AlarmRowKind.Silenced: return L.T("alarms.detail_silenced");
            case AlarmRowKind.Snoozed: return hidden ? L.T("alarms.snoozed_hidden") : L.T("alarms.snoozed");
        }

        var before = EventDetailsCard.BeforeStart(due.Reminder.Minutes);
        if (ev.IsAllDay) return due.Reminder.Minutes > 0 ? $"{L.T("alarms.all_day")} · {before}" : L.T("alarms.all_day");
        var start = ev.Start.ToString(_main.Settings.Use24HourClock ? "HH:mm" : "h:mm tt", L.Culture);
        return due.Reminder.Minutes > 0 ? L.T("alarms.before_start", before, start) : L.T("alarms.at_start", start);
    }

    /// <summary>"2 g 3 h", "1 h 15 min", "7 min", "&lt; 1 min": how long until an alarm rings.</summary>
    public static string Remaining(TimeSpan span)
    {
        if (span.TotalSeconds < 60) return L.T("alarms.remaining_less");
        var minutes = (int)span.TotalMinutes;
        if (minutes >= 24 * 60) return L.T("alarms.remaining_days", minutes / (24 * 60), minutes / 60 % 24);
        if (minutes >= 60) return L.T("alarms.remaining_hours", minutes / 60, minutes % 60);
        return L.T("alarms.remaining_minutes", minutes);
    }

    /// <summary>The first line of a row: when it rings, or that it is ringing.</summary>
    public string WhenText(AlarmRow row) => row.Kind switch
    {
        AlarmRowKind.Ringing => L.T("alarms.state_ringing"),
        AlarmRowKind.Queued => L.T("alarms.state_queued"),
        AlarmRowKind.Silenced => L.T("alarms.state_silenced"),
        _ => row.FireAt.ToString("ddd d MMM, " + (_main.Settings.Use24HourClock ? "HH:mm" : "h:mm tt"), L.Culture),
    };
}
