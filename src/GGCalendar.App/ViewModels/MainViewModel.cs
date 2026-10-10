using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GGCalendar.Core.Models;
using GGCalendar.Core.Recurrence;
using GGCalendar.Data;

namespace GGCalendar.App.ViewModels;

public sealed partial class CalendarItemViewModel(CalendarInfo info, Action changed) : ObservableObject
{
    public string Id { get; } = info.Id;
    public string Name { get; } = info.Name;
    public string ColorHex { get; } = info.ColorHex;
    public string AccountName { get; } = info.AccountName;

    [ObservableProperty]
    public partial bool IsVisible { get; set; } = info.IsVisible;

    partial void OnIsVisibleChanged(bool value) => changed();
}

public sealed record AccountGroup(string AccountName, IReadOnlyList<CalendarItemViewModel> Calendars);

public sealed record AgendaItem(CalendarEvent Event, string Time, string Title, string ColorHex);

public sealed record AgendaDay(string Label, bool IsToday, IReadOnlyList<AgendaItem> Items);

public sealed record DayHeader(string Label, int Number, bool IsToday);

public enum SeriesScope { This, ThisAndFollowing, All }

public sealed partial class MainViewModel : ViewModelBase
{
    private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("it-IT");
    private readonly List<CalendarEvent> _allEvents;
    private readonly List<CalendarItemViewModel> _calendars = [];

    private readonly CalendarStore _store;

    public MainViewModel(CalendarStore store)
    {
        _store = store;
        _allEvents = [];
        Settings = new AppSettings();
        Reload();
        SelectedDate = DateTime.Today;
    }

    /// <summary>Re-reads calendars and events from the store (after a sync or when accounts change).</summary>
    public void Reload()
    {
        var hidden = _calendars.Where(c => !c.IsVisible).Select(c => c.Id).ToHashSet();
        var infos = _store.GetCalendars();
        if (infos.Count == 0)
        {
            // First run: a local calendar so the app is usable before any account is connected.
            var local = new CalendarInfo("local-personale", "Locale", "Personale", "#1A73E8");
            _store.UpsertCalendar(local);
            infos = [local];
        }

        _calendars.Clear();
        Accounts.Clear();
        foreach (var g in infos.GroupBy(c => c.AccountName))
        {
            var items = g.Select(info =>
            {
                CalendarItemViewModel? item = null;
                item = new CalendarItemViewModel(info, () =>
                {
                    _store.SetCalendarVisible(info.Id, item!.IsVisible);
                    Refresh();
                });
                return item;
            }).ToList();
            _calendars.AddRange(items);
            Accounts.Add(new AccountGroup(g.Key, items));
        }

        _allEvents.Clear();
        _allEvents.AddRange(_store.GetAllEvents());
        if (SelectedDate is not null) Refresh();
    }

    public ObservableCollection<AccountGroup> Accounts { get; } = [];
    public IReadOnlyList<CalendarItemViewModel> Calendars => _calendars;
    public IReadOnlyList<CalendarEvent> AllEvents => _allEvents;

    [ObservableProperty]
    public partial AppSettings Settings { get; set; } = new();

    /// <summary>0 = giorno, 1 = settimana, 2 = mese, 3 = anno, 4 = programma, 5 = 6 giorni.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTimeGrid), nameof(IsMonth), nameof(IsYear), nameof(IsAgenda), nameof(ViewModeName))]
    public partial int ViewModeIndex { get; set; } = 1;

    public static IReadOnlyList<string> ViewModeNames { get; } = ["Giorno", "Settimana", "Mese", "Anno", "Programma", "6 giorni"];

    public string ViewModeName => ViewModeNames[ViewModeIndex];
    public bool IsTimeGrid => ViewModeIndex is 0 or 1 or 5;
    public bool IsMonth => ViewModeIndex == 2;
    public bool IsYear => ViewModeIndex == 3;
    public bool IsAgenda => ViewModeIndex == 4;

    public bool ShowWeekends
    {
        get => Settings.ShowWeekends;
        set
        {
            if (Settings.ShowWeekends == value) return;
            Settings.ShowWeekends = value;
            OnPropertyChanged();
            Refresh();
        }
    }

    [ObservableProperty]
    public partial int YearNumber { get; set; }

    [ObservableProperty]
    public partial int DayCount { get; set; } = 7;

    [ObservableProperty]
    public partial DateTime MonthGridStart { get; set; }

    [ObservableProperty]
    public partial int MonthNumber { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<AgendaDay> AgendaDays { get; set; } = [];

    [ObservableProperty]
    public partial int FirstHour { get; set; }

    [ObservableProperty]
    public partial int LastHour { get; set; } = 24;

    [ObservableProperty]
    public partial string TimeZoneLabel { get; set; } = "";

    [ObservableProperty]
    public partial DateTime? SelectedDate { get; set; }

    [ObservableProperty]
    public partial DateTime WeekStart { get; set; }

    [ObservableProperty]
    public partial string Title { get; set; } = "";

    [ObservableProperty]
    public partial int WeekNumber { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<DayHeader> Days { get; set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<CalendarEvent> VisibleEvents { get; set; } = [];

    [ObservableProperty]
    public partial IReadOnlyDictionary<string, string> CalendarColors { get; set; } = new Dictionary<string, string>();

    partial void OnSelectedDateChanged(DateTime? value) => Refresh();

    partial void OnSettingsChanged(AppSettings value)
    {
        OnPropertyChanged(nameof(ShowWeekends));
        FirstHour = value.HideOutsideHours ? value.DayStartHour : 0;
        LastHour = value.HideOutsideHours ? value.DayEndHour : 24;
        var zone = TimeZoneInfo.TryFindSystemTimeZoneById(value.PrimaryTimeZoneId ?? "", out var z) ? z : TimeZoneInfo.Local;
        var offset = zone.GetUtcOffset(DateTime.UtcNow);
        TimeZoneLabel = offset.Minutes == 0
            ? $"GMT{(offset < TimeSpan.Zero ? "-" : "+")}{Math.Abs(offset.Hours):00}"
            : $"GMT{(offset < TimeSpan.Zero ? "-" : "+")}{Math.Abs(offset.Hours):00}:{Math.Abs(offset.Minutes):00}";
        Refresh();
    }

    private static bool IsRemote(string calendarId) => Services.GoogleSyncService.IsGoogleCalendar(calendarId);

    /// <summary>Raised after a local change was queued for Google, so the host can sync soon.</summary>
    public event Action? ChangeQueued;

    [ObservableProperty]
    public partial string SyncStatus { get; set; } = "";

    [ObservableProperty]
    public partial bool IsSyncing { get; set; }

    public void AddEvent(CalendarEvent ev)
    {
        _allEvents.Add(ev);
        _store.UpsertEvent(ev);
        if (IsRemote(ev.CalendarId)) Queue(() => _store.Enqueue(ev.Id, ev.CalendarId, OutboxOperation.Create, ev));
        Refresh();
    }

    public void UpdateEvent(CalendarEvent ev)
    {
        var i = _allEvents.FindIndex(e => e.Id == ev.Id);
        if (i < 0) return;
        var old = _allEvents[i];
        var oldRemote = _store.GetRemoteInfo(ev.Id)?.RemoteId;
        _allEvents[i] = ev;
        _store.UpsertEvent(ev);

        if (old.CalendarId != ev.CalendarId)
        {
            // Cross-calendar move: delete at the origin, create at the destination.
            if (IsRemote(old.CalendarId) && oldRemote is not null)
                Queue(() => _store.EnqueueDelete(ev.Id, old.CalendarId, oldRemote));
            _store.ClearRemote(ev.Id);
            if (IsRemote(ev.CalendarId)) Queue(() => _store.Enqueue(ev.Id, ev.CalendarId, OutboxOperation.Create, ev));

            // Modified occurrences follow their series; they are recreated after it (Google dropped them with the old series).
            foreach (var child in _allEvents.Where(e => e.SeriesMasterId == ev.Id).ToList())
            {
                var moved = child with { CalendarId = ev.CalendarId };
                _allEvents[_allEvents.FindIndex(e => e.Id == child.Id)] = moved;
                _store.UpsertEvent(moved);
                _store.ClearRemote(child.Id);
                if (IsRemote(ev.CalendarId)) Queue(() => _store.Enqueue(moved.Id, moved.CalendarId, OutboxOperation.Create, moved));
            }
        }
        else if (IsRemote(ev.CalendarId))
        {
            Queue(() => _store.Enqueue(ev.Id, ev.CalendarId, OutboxOperation.Update, ev));
        }

        Refresh();
    }

    private void Queue(Action enqueue)
    {
        enqueue();
        ChangeQueued?.Invoke();
    }

    public CalendarEvent NewEvent(DateTime start, DateTime end, bool allDay = false)
    {
        var calendar = _calendars.FirstOrDefault(c => c.IsVisible) ?? _calendars.First();
        return new CalendarEvent(Guid.NewGuid().ToString("N"), calendar.Id, "", start, end)
        {
            IsAllDay = allDay,
            GuestsCanModify = Settings.DefaultGuestsCanModify,
            GuestsCanInvite = Settings.DefaultGuestsCanInvite,
            GuestsCanSeeList = Settings.DefaultGuestsCanSeeList,
        };
    }

    [RelayCommand] private void Today() => SelectedDate = DateTime.Today;
    [RelayCommand] private void Previous() => Step(-1);
    [RelayCommand] private void Next() => Step(1);

    private void Step(int direction)
    {
        var d = SelectedDate ?? DateTime.Today;
        SelectedDate = ViewModeIndex switch
        {
            0 => d.AddDays(direction),
            2 or 4 => d.AddMonths(direction),
            3 => d.AddYears(direction),
            5 => d.AddDays(6 * direction),
            _ => d.AddDays(7 * direction),
        };
    }

    /// <summary>Switches to the day view for the given date.</summary>
    public void ShowDay(DateTime day)
    {
        ViewModeIndex = 0;
        SelectedDate = day.Date;
    }

    partial void OnViewModeIndexChanged(int value) => Refresh();

    public void MoveEvent(CalendarEvent ev, DateTime start, DateTime end)
    {
        var i = _allEvents.FindIndex(e => e.Id == ev.Id);
        if (i < 0) return;
        var master = _allEvents[i];
        if (ev.OccurrenceIndex is null)
        {
            UpdateEvent(master with { Start = start, End = end });
            return;
        }

        // Dragging one occurrence of a series moves only that occurrence.
        SaveOccurrence(ev, ev with { Start = start, End = end }, SeriesScope.This);
    }

    /// <summary>Moves an event (the whole series for recurring ones) to another calendar, possibly of another account.</summary>
    public void ChangeCalendar(CalendarEvent ev, string calendarId)
    {
        var master = ev.SeriesMasterId is { } m ? _allEvents.FirstOrDefault(e => e.Id == m) ?? ResolveMaster(ev) : ResolveMaster(ev);
        if (master.CalendarId == calendarId) return;
        UpdateEvent(master with { CalendarId = calendarId, MeetLink = null, OccurrenceIndex = null });
    }

    public CalendarEvent ResolveMaster(CalendarEvent ev) => _allEvents.FirstOrDefault(e => e.Id == ev.Id) ?? ev;

    /// <summary>True for an occurrence of a repeating series (not for the single modified occurrences, which are plain events).</summary>
    public bool IsRecurring(CalendarEvent ev) =>
        ev.SeriesMasterId is null && !string.IsNullOrEmpty(ResolveMaster(ev).Recurrence);

    /// <summary>
    /// Saves an edit of <paramref name="occurrence"/>: a single modified occurrence, the occurrence and all following ones
    /// (the series is split in two), or the whole series (<paramref name="edited"/> was made from the master).
    /// </summary>
    public void SaveOccurrence(CalendarEvent occurrence, CalendarEvent edited, SeriesScope scope)
    {
        var master = ResolveMaster(occurrence);
        var isFirst = occurrence.Start == master.Start;
        if (scope == SeriesScope.All || (scope == SeriesScope.ThisAndFollowing && isFirst))
        {
            UpdateEvent(edited with { Id = master.Id, OccurrenceIndex = null });
            return;
        }

        if (scope == SeriesScope.This)
        {
            AddEvent(edited with
            {
                Id = Guid.NewGuid().ToString("N"),
                CalendarId = master.CalendarId,
                Recurrence = null,
                ExcludedStarts = [],
                SeriesMasterId = master.Id,
                OriginalStart = occurrence.Start,
                IsCancelled = false,
                OccurrenceIndex = null,
            });
            return;
        }

        var newRule = edited.Recurrence;
        if (newRule is not null && newRule == master.Recurrence && occurrence.OccurrenceIndex is { } index
            && RecurrenceRule.Parse(master.Recurrence)?.Count is { } total)
            newRule = RecurrenceRule.WithCount(newRule, Math.Max(1, total - index));

        var last = master.IsAllDay ? occurrence.Start.AddDays(-1) : occurrence.Start.AddSeconds(-1);
        var tail = master.ExcludedStarts.Where(d => d >= occurrence.Start).ToList();
        UpdateEvent(master with
        {
            Recurrence = RecurrenceRule.WithUntil(master.Recurrence!, last, master.IsAllDay),
            ExcludedStarts = master.ExcludedStarts.Where(d => d < occurrence.Start).ToList(),
        });
        AddEvent(edited with
        {
            Id = Guid.NewGuid().ToString("N"),
            Recurrence = newRule,
            ExcludedStarts = tail,
            SeriesMasterId = null,
            OriginalStart = null,
            MeetLink = null,
            OccurrenceIndex = null,
        });
    }

    /// <summary>Deletes one occurrence, the occurrence and all following ones, or the whole series.</summary>
    public void DeleteOccurrence(CalendarEvent occurrence, SeriesScope scope)
    {
        var master = ResolveMaster(occurrence);
        var isFirst = occurrence.OccurrenceIndex is null || occurrence.Start == master.Start;
        if (scope == SeriesScope.All || (scope == SeriesScope.ThisAndFollowing && isFirst))
        {
            DeleteEvent(master);
            return;
        }

        if (scope == SeriesScope.This)
        {
            UpdateEvent(master with { ExcludedStarts = [.. master.ExcludedStarts, occurrence.Start] });
            return;
        }

        var last = master.IsAllDay ? occurrence.Start.AddDays(-1) : occurrence.Start.AddSeconds(-1);
        UpdateEvent(master with
        {
            Recurrence = RecurrenceRule.WithUntil(master.Recurrence!, last, master.IsAllDay),
            ExcludedStarts = master.ExcludedStarts.Where(d => d < occurrence.Start).ToList(),
        });
    }

    public IEnumerable<CalendarEvent> ExpandedBetween(DateTime from, DateTime to, bool visibleOnly = true)
    {
        var visible = _calendars.Where(c => c.IsVisible).Select(c => c.Id).ToHashSet();
        // Modified or cancelled occurrences replace the matching occurrence of their series.
        var replaced = _allEvents.Where(e => e.SeriesMasterId is not null && e.OriginalStart is not null)
            .GroupBy(e => e.SeriesMasterId!).ToDictionary(g => g.Key, g => g.Select(e => e.OriginalStart!.Value).ToList());
        return _allEvents.Where(e => !e.IsCancelled && (!visibleOnly || visible.Contains(e.CalendarId)))
            .Select(e => replaced.TryGetValue(e.Id, out var starts) ? e with { ExcludedStarts = [.. e.ExcludedStarts, .. starts] } : e)
            .SelectMany(e => RecurrenceExpander.Expand(e, from, to)).ToList();
    }

    public void DeleteEvent(CalendarEvent ev)
    {
        var remote = _store.GetRemoteInfo(ev.Id)?.RemoteId;
        _allEvents.RemoveAll(e => e.Id == ev.Id);
        _store.DeleteEvent(ev.Id);
        if (IsRemote(ev.CalendarId) && remote is not null) Queue(() => _store.EnqueueDelete(ev.Id, ev.CalendarId, remote));

        // Removing a series removes its modified occurrences too (Google does the same remotely).
        foreach (var child in _allEvents.Where(e => e.SeriesMasterId == ev.Id).ToList())
        {
            _allEvents.Remove(child);
            _store.DeleteEvent(child.Id);
        }

        // Removing a modified occurrence cancels it in the series instead of bringing the original back.
        if (ev.SeriesMasterId is { } masterId && ev.OriginalStart is { } original
            && _allEvents.FirstOrDefault(e => e.Id == masterId) is { } owner)
            UpdateEvent(owner with { ExcludedStarts = [.. owner.ExcludedStarts, original] });
        Refresh();
    }

    private DateTime StartOfWeek(DateTime date)
    {
        var diff = (7 + (int)date.DayOfWeek - (int)Settings.FirstDayOfWeek) % 7;
        return date.Date.AddDays(-diff);
    }

    private void Refresh()
    {
        var date = SelectedDate ?? DateTime.Today;
        var weekStart = StartOfWeek(date);
        var first = weekStart;
        var count = 7;
        switch (ViewModeIndex)
        {
            case 0: first = date.Date; count = 1; break;
            case 5: first = date.Date; count = 6; break;
            default:
                if (!Settings.ShowWeekends)
                {
                    while (first.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) first = first.AddDays(1);
                    count = 5;
                }

                break;
        }

        WeekStart = first;
        DayCount = count;
        MonthGridStart = StartOfWeek(new DateTime(date.Year, date.Month, 1));
        MonthNumber = date.Month;
        YearNumber = date.Year;
        Title = Culture.TextInfo.ToTitleCase(date.ToString(ViewModeIndex switch { 0 => "d MMMM yyyy", 3 => "yyyy", _ => "MMMM yyyy" }, Culture));
        WeekNumber = ISOWeek.GetWeekOfYear(weekStart.AddDays(3));
        Days = Enumerable.Range(0, DayCount).Select(i =>
        {
            var d = WeekStart.AddDays(i);
            return new DayHeader(Culture.DateTimeFormat.GetAbbreviatedDayName(d.DayOfWeek).ToUpperInvariant(), d.Day, d.Date == DateTime.Today);
        }).ToList();

        var visible = _calendars.Where(c => c.IsVisible).Select(c => c.Id).ToHashSet();
        var rangeFrom = ViewModeIndex == 3 ? new DateTime(date.Year, 1, 1) : (MonthGridStart < WeekStart ? MonthGridStart : WeekStart);
        var rangeTo = ViewModeIndex == 3 ? rangeFrom.AddYears(1) : MonthGridStart.AddDays(42) > date.AddDays(31) ? MonthGridStart.AddDays(42) : date.AddDays(31);
        VisibleEvents = ExpandedBetween(rangeFrom, rangeTo).ToList();
        CalendarColors = _calendars.ToDictionary(c => c.Id, c => c.ColorHex);
        if (ViewModeIndex == 4) BuildAgenda(date.Date);
    }

    private void BuildAgenda(DateTime from)
    {
        var fmt = Settings.Use24HourClock ? "HH:mm" : "h:mm tt";
        var to = from.AddDays(30);
        var groups = new List<AgendaDay>();
        for (var d = from; d < to; d = d.AddDays(1))
        {
            var items = VisibleEvents.Where(e => e.Start < d.AddDays(1) && e.End > d)
                .OrderBy(e => !e.IsAllDay).ThenBy(e => e.Start)
                .Select(e => new AgendaItem(e,
                    e.IsAllDay ? "Tutto il giorno" : $"{e.Start.ToString(fmt, Culture)} – {e.End.ToString(fmt, Culture)}",
                    string.IsNullOrWhiteSpace(e.Title) ? "(Senza titolo)" : e.Title,
                    e.ColorHex ?? (CalendarColors.TryGetValue(e.CalendarId, out var c) ? c : "#1A73E8")))
                .ToList();
            if (items.Count > 0)
                groups.Add(new AgendaDay(Culture.TextInfo.ToTitleCase(d.ToString("dddd d MMMM", Culture)), d.Date == DateTime.Today, items));
        }

        AgendaDays = groups;
    }
}

