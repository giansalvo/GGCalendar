using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Threading;
using GGCalendar.Core.Models;
using GGCalendar.Core.Reminders;
using GGCalendar.Data;

namespace GGCalendar.App.Services;

/// <summary>
/// Polls the event list and raises <see cref="Fired"/> when a reminder becomes due. Shown and snoozed reminders
/// are persisted, so a restart neither repeats them nor loses them, and reminders missed while the app was closed are shown.
/// A reminder of a hidden calendar does not ring (a snoozed one does, as on Android). It also tells the alarms waiting to ring
/// (<see cref="Pending"/>) and cancels one of them (<see cref="Skip"/>).
/// </summary>
public sealed class ReminderScheduler : IDisposable
{
    // How far back reminders missed while the app was closed are still shown (the event list starts a day back too).
    private static readonly TimeSpan CatchUp = TimeSpan.FromDays(1);
    private static readonly TimeSpan SaveEvery = TimeSpan.FromMinutes(5);
    // Expanding every series is costly: the list is rebuilt when the events change, or as the window moves on.
    private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(10);

    /// <summary>How far ahead the events of the list are needed: the pending alarms are listed up to <see cref="PendingAlarms.Horizon"/>.</summary>
    public static TimeSpan Window => PendingAlarms.Horizon;

    private DispatcherTimer? _timer;
    private readonly Func<IEnumerable<CalendarEvent>> _events;
    private readonly Func<string, bool> _isVisible;
    private readonly Func<DateTime> _now;
    private readonly ReminderStateStore _store;
    private readonly ReminderState _state;
    private IReadOnlyList<CalendarEvent>? _cache;
    private DateTime _cachedAt;
    private DateTime _savedAt;
    private bool _enabled = true;

    /// <param name="events">All the events (also of hidden calendars) from a day ago to <see cref="Window"/> ahead.</param>
    /// <param name="isVisible">Whether the calendar with the given id is shown (null = all are).</param>
    /// <param name="now">The clock (the tests move it).</param>
    public ReminderScheduler(Func<IEnumerable<CalendarEvent>> events, ReminderStateStore? store = null, Func<string, bool>? isVisible = null, Func<DateTime>? now = null)
    {
        _events = events;
        _isVisible = isVisible ?? (_ => true);
        _now = now ?? (() => DateTime.Now);
        _store = store ?? new ReminderStateStore();
        _state = _store.Load();
        var current = _now();
        _state.LastCheck = _state.LastCheck == default || _state.LastCheck > current ? current
            : _state.LastCheck < current - CatchUp ? current - CatchUp
            : _state.LastCheck;
    }

    /// <summary>The time now, by the clock of the scheduler.</summary>
    public DateTime Now => _now();

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value) return;
            _enabled = value;
            PendingChanged?.Invoke();
        }
    }

    public event Action<DueReminder>? Fired;

    /// <summary>The list of the pending alarms may be different: an alarm rang, was cancelled or snoozed, or the events or the calendars shown changed.</summary>
    public event Action? PendingChanged;

    public void Start()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        _timer.Tick += (_, _) => Check();
        _timer.Start();
        Check();
    }

    /// <summary>Call when events change, so the next check sees them.</summary>
    public void Invalidate()
    {
        _cache = null;
        PendingChanged?.Invoke();
    }

    /// <summary>Call when a calendar is shown or hidden: the list changes, the events do not.</summary>
    public void VisibilityChanged() => PendingChanged?.Invoke();

    public void Snooze(DueReminder reminder, TimeSpan delay)
    {
        _state.Snoozed.Add(new SnoozedReminder(_now() + delay, reminder));
        Save(_now());
        PendingChanged?.Invoke();
    }

    /// <summary>The alarms waiting to ring: the planned ones of the shown calendars (next <see cref="PendingAlarms.Horizon"/>, no more than <see cref="PendingAlarms.MaxListed"/>) and the snoozed ones, soonest first.</summary>
    public IReadOnlyList<PendingAlarm> Pending()
    {
        var now = _now();
        return PendingAlarms.Build(Candidates(now), _state.Fired, _state.Snoozed.Select(s => (s.At, s.Reminder)), now, _isVisible);
    }

    /// <summary>
    /// Cancels an alarm before it rings; the event stays. A planned alarm is marked as shown, so it is neither rung nor caught up;
    /// a snoozed one is forgotten. Only on this computer.
    /// </summary>
    public void Skip(PendingAlarm alarm)
    {
        if (alarm.Kind == PendingKind.Planned) _state.Fired[alarm.Key] = alarm.FireAt;
        else _state.Snoozed.RemoveAll(s => PendingAlarms.SnoozeKey(s.At, s.Reminder.Key) == alarm.Key);
        Save(_now());
        PendingChanged?.Invoke();
    }

    /// <summary>Looks whether something is due now (the timer calls it every 15 seconds).</summary>
    public void Check()
    {
        var now = _now();
        var after = _state.LastCheck;
        _state.LastCheck = now;
        var dirty = false;

        if (Enabled)
        {
            foreach (var due in ReminderCalculator.Due(Candidates(now), after, now))
            {
                // A reminder missed while the app was closed is only worth showing while its event is not over.
                if (due.Event.End <= now) continue;
                // A hidden calendar does not ring; the reminder is not marked as shown, so it is still there if the calendar is shown again in time.
                if (!_isVisible(due.Event.CalendarId)) continue;
                if (!_state.Fired.TryAdd(due.Key, due.FireAt)) continue;
                dirty = true;
                Fired?.Invoke(due);
            }

            // A snoozed reminder rings even if its calendar was hidden meanwhile (as on Android).
            var back = _state.Snoozed.Where(s => s.At <= now).ToList();
            var existing = back.Count > 0 ? PendingAlarms.Occurrences(Candidates(now)) : [];
            foreach (var s in back)
            {
                _state.Snoozed.Remove(s);
                dirty = true;
                // The event may be gone (deleted or moved) while the reminder was put off.
                if (existing.Contains((s.Reminder.Event.Id, s.Reminder.Event.Start))) Fired?.Invoke(s.Reminder);
            }
        }

        // Older keys can never be due again: checks never look further back than CatchUp.
        foreach (var key in _state.Fired.Where(f => f.Value < now - CatchUp - CatchUp).Select(f => f.Key).ToList())
        {
            _state.Fired.Remove(key);
            dirty = true;
        }

        if (dirty || now - _savedAt > SaveEvery) Save(now);
        if (dirty) PendingChanged?.Invoke();
    }

    private IReadOnlyList<CalendarEvent> Candidates(DateTime now)
    {
        if (_cache is null || now - _cachedAt > CacheFor)
        {
            _cache = _events().ToList();
            _cachedAt = now;
        }

        return _cache;
    }

    private void Save(DateTime now)
    {
        try
        {
            _store.Save(_state);
            _savedAt = now;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort: at worst a missed reminder is not caught up after a restart.
        }
    }

    public void Dispose()
    {
        _timer?.Stop();
        Save(_now());
    }
}
