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
/// Polls the event list and raises <see cref="Fired"/> when a popup reminder becomes due. Shown and snoozed reminders
/// are persisted, so a restart neither repeats them nor loses them, and reminders missed while the app was closed are shown.
/// </summary>
public sealed class ReminderScheduler : IDisposable
{
    // How far back reminders missed while the app was closed are still shown (the event list starts a day back too).
    private static readonly TimeSpan CatchUp = TimeSpan.FromDays(1);
    private static readonly TimeSpan SaveEvery = TimeSpan.FromMinutes(5);
    // Expanding every series is costly: the list is rebuilt when the events change, or as the window moves on.
    private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(10);

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(15) };
    private readonly Func<IEnumerable<CalendarEvent>> _events;
    private readonly ReminderStateStore _store;
    private readonly ReminderState _state;
    private IReadOnlyList<CalendarEvent>? _cache;
    private DateTime _cachedAt;
    private DateTime _savedAt;

    public ReminderScheduler(Func<IEnumerable<CalendarEvent>> events, ReminderStateStore? store = null)
    {
        _events = events;
        _store = store ?? new ReminderStateStore();
        _state = _store.Load();
        var now = DateTime.Now;
        _state.LastCheck = _state.LastCheck == default || _state.LastCheck > now ? now
            : _state.LastCheck < now - CatchUp ? now - CatchUp
            : _state.LastCheck;
        _timer.Tick += (_, _) => Check();
    }

    public bool Enabled { get; set; } = true;

    public event Action<DueReminder>? Fired;

    public void Start()
    {
        _timer.Start();
        Check();
    }

    /// <summary>Call when events change, so the next check sees them.</summary>
    public void Invalidate() => _cache = null;

    public void Snooze(DueReminder reminder, TimeSpan delay)
    {
        _state.Snoozed.Add(new SnoozedReminder(DateTime.Now + delay, reminder));
        Save(DateTime.Now);
    }

    private void Check()
    {
        var now = DateTime.Now;
        var after = _state.LastCheck;
        _state.LastCheck = now;
        var dirty = false;

        if (Enabled)
        {
            foreach (var due in ReminderCalculator.Due(Candidates(now), after, now))
            {
                // A reminder missed while the app was closed is only worth showing while its event is not over.
                if (due.Event.End <= now) continue;
                if (!_state.Fired.TryAdd(due.Key, due.FireAt)) continue;
                dirty = true;
                Fired?.Invoke(due);
            }

            foreach (var s in _state.Snoozed.Where(s => s.At <= now).ToList())
            {
                _state.Snoozed.Remove(s);
                dirty = true;
                Fired?.Invoke(s.Reminder);
            }
        }

        // Older keys can never be due again: checks never look further back than CatchUp.
        foreach (var key in _state.Fired.Where(f => f.Value < now - CatchUp - CatchUp).Select(f => f.Key).ToList())
        {
            _state.Fired.Remove(key);
            dirty = true;
        }

        if (dirty || now - _savedAt > SaveEvery) Save(now);
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
        _timer.Stop();
        Save(DateTime.Now);
    }
}
