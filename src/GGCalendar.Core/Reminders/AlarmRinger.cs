namespace GGCalendar.Core.Reminders;

public enum RingState
{
    /// <summary>It is the one making the sound.</summary>
    Ringing,

    /// <summary>It waits its turn: alarms that arrive together ring one after the other.</summary>
    Queued,

    /// <summary>The sound has ended by itself (time limit) and the alarm has not been turned off.</summary>
    Silenced,
}

public sealed class RingingAlarm(DueReminder reminder, string? sound)
{
    public DueReminder Reminder { get; } = reminder;

    /// <summary>The sound file to play (null = no sound).</summary>
    public string? Sound { get; } = sound;

    public RingState State { get; internal set; } = RingState.Queued;

    public string Key => Reminder.Key;
}

/// <summary>
/// The alarms that are ringing now (issue #18). Only one makes the sound at a time; the others wait in line, in the order they came.
/// The ringer decides who rings; the sound itself and the time limit are the caller's: it plays when <see cref="SoundRequested"/> is
/// raised, stops when <see cref="SoundStopRequested"/> is, and tells <see cref="Silence"/> when the time limit has passed.
/// </summary>
public sealed class AlarmRinger
{
    private readonly List<RingingAlarm> _items = [];

    public IReadOnlyList<RingingAlarm> Items => _items;

    public RingingAlarm? Current => _items.FirstOrDefault(a => a.State == RingState.Ringing);

    public event Action? Changed;

    /// <summary>The alarm must start making sound.</summary>
    public event Action<RingingAlarm>? SoundRequested;

    /// <summary>The sound must stop (the alarm that was ringing is over or silenced).</summary>
    public event Action? SoundStopRequested;

    /// <summary>Adds an alarm that has just fired. The same alarm twice counts once.</summary>
    public void Add(DueReminder reminder, string? sound)
    {
        if (_items.Any(a => a.Key == reminder.Key)) return;
        var alarm = new RingingAlarm(reminder, sound);
        _items.Add(alarm);
        if (Current is null) Start(alarm);
        Changed?.Invoke();
    }

    /// <summary>The user turned the alarm off (or put it off): it leaves, and the next one in line starts.</summary>
    public void Remove(string key)
    {
        var alarm = _items.FirstOrDefault(a => a.Key == key);
        if (alarm is null) return;
        var wasRinging = alarm.State == RingState.Ringing;
        _items.Remove(alarm);
        if (wasRinging) StopAndStartNext();
        Changed?.Invoke();
    }

    public void RemoveAll()
    {
        if (_items.Count == 0) return;
        var wasRinging = Current is not null;
        _items.Clear();
        if (wasRinging) SoundStopRequested?.Invoke();
        Changed?.Invoke();
    }

    /// <summary>The time limit of the sound has passed: the alarm stays (it was not turned off) but is quiet, and the next one starts.</summary>
    public void Silence(string key)
    {
        var alarm = _items.FirstOrDefault(a => a.Key == key && a.State == RingState.Ringing);
        if (alarm is null) return;
        alarm.State = RingState.Silenced;
        StopAndStartNext();
        Changed?.Invoke();
    }

    private void StopAndStartNext()
    {
        SoundStopRequested?.Invoke();
        if (_items.FirstOrDefault(a => a.State == RingState.Queued) is { } next) Start(next);
    }

    private void Start(RingingAlarm alarm)
    {
        alarm.State = RingState.Ringing;
        SoundRequested?.Invoke(alarm);
    }
}
