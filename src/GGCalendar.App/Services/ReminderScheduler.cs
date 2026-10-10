using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Threading;
using GGCalendar.Core.Models;
using GGCalendar.Core.Reminders;

namespace GGCalendar.App.Services;

/// <summary>Polls the event list and raises <see cref="Fired"/> when a popup reminder becomes due.</summary>
public sealed class ReminderScheduler : IDisposable
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(15) };
    private readonly HashSet<string> _fired = [];
    private readonly List<(DateTime At, DueReminder Reminder)> _snoozed = [];
    private readonly Func<IEnumerable<CalendarEvent>> _events;
    private DateTime _lastCheck = DateTime.Now;

    public ReminderScheduler(Func<IEnumerable<CalendarEvent>> events)
    {
        _events = events;
        _timer.Tick += (_, _) => Check();
    }

    public bool Enabled { get; set; } = true;

    public event Action<DueReminder>? Fired;

    public void Start() => _timer.Start();

    public void Snooze(DueReminder reminder, TimeSpan delay) => _snoozed.Add((DateTime.Now + delay, reminder));

    private void Check()
    {
        var now = DateTime.Now;
        var after = _lastCheck;
        _lastCheck = now;
        if (!Enabled) return;

        foreach (var due in ReminderCalculator.Due(_events(), after, now))
        {
            if (_fired.Add(due.Key)) Fired?.Invoke(due);
        }

        foreach (var s in _snoozed.Where(s => s.At <= now).ToList())
        {
            _snoozed.Remove(s);
            Fired?.Invoke(s.Reminder);
        }
    }

    public void Dispose() => _timer.Stop();
}
