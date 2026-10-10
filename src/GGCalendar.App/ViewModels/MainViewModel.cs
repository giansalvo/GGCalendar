using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GGCalendar.Core.Models;

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

public sealed record DayHeader(string Label, int Number, bool IsToday);

public sealed partial class MainViewModel : ViewModelBase
{
    private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("it-IT");
    private readonly List<CalendarEvent> _allEvents;
    private readonly List<CalendarItemViewModel> _calendars = [];

    public MainViewModel()
    {
        foreach (var g in SampleData.Calendars().GroupBy(c => c.AccountName))
        {
            var items = g.Select(c => new CalendarItemViewModel(c, Refresh)).ToList();
            _calendars.AddRange(items);
            Accounts.Add(new AccountGroup(g.Key, items));
        }

        _allEvents = SampleData.Events(StartOfWeek(DateTime.Today));
        SelectedDate = DateTime.Today;
        Settings = new AppSettings();
    }

    public ObservableCollection<AccountGroup> Accounts { get; } = [];
    public IReadOnlyList<CalendarItemViewModel> Calendars => _calendars;
    public IReadOnlyList<CalendarEvent> AllEvents => _allEvents;

    [ObservableProperty]
    public partial AppSettings Settings { get; set; } = new();

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
        Refresh();
    }

    public void UpdateEvent(CalendarEvent ev)
    {
        var i = _allEvents.FindIndex(e => e.Id == ev.Id);
        if (i < 0) return;
        _allEvents[i] = ev;
        Refresh();
    }

    public CalendarEvent NewEvent(DateTime start, DateTime end, bool allDay = false)
    {
        var calendar = _calendars.FirstOrDefault(c => c.IsVisible) ?? _calendars.First();
        return new CalendarEvent(Guid.NewGuid().ToString("N"), calendar.Id, "", start, end) { IsAllDay = allDay };
    }

    [RelayCommand] private void Today() => SelectedDate = DateTime.Today;
    [RelayCommand] private void Previous() => SelectedDate = (SelectedDate ?? DateTime.Today).AddDays(-7);
    [RelayCommand] private void Next() => SelectedDate = (SelectedDate ?? DateTime.Today).AddDays(7);

    public void MoveEvent(CalendarEvent ev, DateTime start, DateTime end)
    {
        var i = _allEvents.FindIndex(e => e.Id == ev.Id);
        if (i < 0) return;
        _allEvents[i] = ev with { Start = start, End = end };
        Refresh();
    }

    public void DeleteEvent(CalendarEvent ev)
    {
        _allEvents.RemoveAll(e => e.Id == ev.Id);
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
        WeekStart = StartOfWeek(date);
        Title = Culture.TextInfo.ToTitleCase(date.ToString("MMMM yyyy", Culture));
        WeekNumber = ISOWeek.GetWeekOfYear(WeekStart.AddDays(3));
        Days = Enumerable.Range(0, 7).Select(i =>
        {
            var d = WeekStart.AddDays(i);
            return new DayHeader(Culture.DateTimeFormat.GetAbbreviatedDayName(d.DayOfWeek).ToUpperInvariant(), d.Day, d.Date == DateTime.Today);
        }).ToList();

        var visible = _calendars.Where(c => c.IsVisible).Select(c => c.Id).ToHashSet();
        VisibleEvents = _allEvents.Where(e => visible.Contains(e.CalendarId)).ToList();
        CalendarColors = _calendars.ToDictionary(c => c.Id, c => c.ColorHex);
    }
}
