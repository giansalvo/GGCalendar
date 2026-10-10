using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GGCalendar.Core.Models;
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

public sealed partial class MainViewModel : ViewModelBase
{
    private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("it-IT");
    private readonly List<CalendarEvent> _allEvents;
    private readonly List<CalendarItemViewModel> _calendars = [];

    private readonly CalendarStore _store;

    public MainViewModel(CalendarStore store)
    {
        _store = store;
        var infos = store.GetCalendars();
        if (infos.Count == 0)
        {
            // First run: a local calendar so the app is usable before any account is connected.
            var local = new CalendarInfo("local-personale", "Locale", "Personale", "#1A73E8");
            store.UpsertCalendar(local);
            infos = [local];
        }

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

        _allEvents = store.GetAllEvents().ToList();
        SelectedDate = DateTime.Today;
        Settings = new AppSettings();
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

    public void AddEvent(CalendarEvent ev)
    {
        _allEvents.Add(ev);
        _store.UpsertEvent(ev);
        Refresh();
    }

    public void UpdateEvent(CalendarEvent ev)
    {
        var i = _allEvents.FindIndex(e => e.Id == ev.Id);
        if (i < 0) return;
        _allEvents[i] = ev;
        _store.UpsertEvent(ev);
        Refresh();
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
        _allEvents[i] = ev with { Start = start, End = end };
        _store.UpsertEvent(_allEvents[i]);
        Refresh();
    }

    public void DeleteEvent(CalendarEvent ev)
    {
        _allEvents.RemoveAll(e => e.Id == ev.Id);
        _store.DeleteEvent(ev.Id);
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
        VisibleEvents = _allEvents.Where(e => visible.Contains(e.CalendarId)).ToList();
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

