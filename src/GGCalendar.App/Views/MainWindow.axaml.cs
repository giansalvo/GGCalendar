using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using GGCalendar.App.Services;
using GGCalendar.App.ViewModels;
using GGCalendar.Core.Models;
using GGCalendar.Core.Reminders;
using GGCalendar.Data;

namespace GGCalendar.App.Views;

public partial class MainWindow : Window
{
    private readonly SettingsStore _store = new();
    private readonly SoundService _sound = new();
    private ReminderScheduler? _scheduler;

    public MainWindow()
    {
        InitializeComponent();
        Closed += (_, _) => { _scheduler?.Dispose(); _sound.Dispose(); };
        Grid.EventChanged += (ev, s, e) => Vm?.MoveEvent(ev, s, e);
        Grid.EventDeleted += ev => Vm?.DeleteEvent(ev);
        Grid.EventOpened += async ev => await EditAsync(ev, isNew: false);
        Grid.EventCreated += async (s, e) =>
        {
            if (Vm is { } vm) await EditAsync(vm.NewEvent(s, e), isNew: true);
        };
        AllDay.EventOpened += async ev => await EditAsync(ev, isNew: false);
        AllDay.DayDoubleClicked += async day =>
        {
            if (Vm is { } vm) await EditAsync(vm.NewEvent(day, day.AddDays(1), allDay: true), isNew: true);
        };
        Month.EventOpened += async ev => await EditAsync(ev, isNew: false);
        Month.DayDoubleClicked += async day =>
        {
            if (Vm is { } vm) await EditAsync(vm.NewEvent(day.AddHours(vm.Settings.DayStartHour), day.AddHours(vm.Settings.DayStartHour).AddMinutes(vm.Settings.DefaultEventMinutes)), isNew: true);
        };
        Month.DayNumberClicked += day => Vm?.ShowDay(day);
        Year.DayClicked += day => Vm?.ShowDay(day);
        KeyDown += OnShortcut;
        Opened += (_, _) =>
        {
            if (Vm is { } vm)
            {
                vm.Settings = _store.Load();
                vm.ViewModeIndex = Math.Clamp(vm.Settings.DefaultView, 0, 5);
                vm.PropertyChanged += (_, a) =>
                {
                    if (a.PropertyName is not (nameof(MainViewModel.ViewModeIndex) or nameof(MainViewModel.ShowWeekends))) return;
                    vm.Settings.DefaultView = vm.ViewModeIndex;
                    _store.Save(vm.Settings);
                };
                _scheduler = new ReminderScheduler(() => vm.AllEvents) { Enabled = vm.Settings.NotificationsEnabled };
                _scheduler.Fired += OnReminderFired;
                _scheduler.Start();
            }
            Avalonia.Threading.Dispatcher.UIThread.Post(ScrollToDayStart, Avalonia.Threading.DispatcherPriority.Loaded);
        };
    }

    private void OnReminderFired(DueReminder due)
    {
        if (Vm is not { } vm) return;
        var isAlarm = due.Reminder.Method == ReminderMethod.Alarm;
        var sound = due.Reminder.SoundPath switch
        {
            null => vm.Settings.DefaultReminderSound,
            EventReminder.NoSound => isAlarm ? vm.Settings.DefaultReminderSound : null,
            var p => p,
        };
        var player = new SoundService();
        player.Play(sound, vm.Settings.ReminderVolumePercent, loop: isAlarm);

        var toast = new ReminderNotificationWindow(due.Event, vm.Settings.Use24HourClock, isAlarm);
        toast.Closed += (_, _) =>
        {
            player.Dispose();
            if (toast.Result == ReminderAction.Snooze) _scheduler?.Snooze(due, TimeSpan.FromMinutes(5));
        };
        toast.Show();
    }

    private MainViewModel? Vm => DataContext as MainViewModel;

    private void OnViewModeSelected(object? sender, RoutedEventArgs e)
    {
        if (Vm is { } vm && sender is MenuItem { Tag: string t } && int.TryParse(t, out var i)) vm.ViewModeIndex = i;
    }

    private async void OnAgendaItemOpened(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        if (sender is Control { DataContext: AgendaItem item }) await EditAsync(item.Event, isNew: false);
    }

    private void OnShortcut(object? sender, Avalonia.Input.KeyEventArgs e)
    {
        if (Vm is not { } vm || e.KeyModifiers != Avalonia.Input.KeyModifiers.None || e.Source is TextBox) return;
        var mode = e.Key switch
        {
            Avalonia.Input.Key.D => 0, Avalonia.Input.Key.W => 1, Avalonia.Input.Key.M => 2,
            Avalonia.Input.Key.Y => 3, Avalonia.Input.Key.A => 4, Avalonia.Input.Key.X => 5, _ => -1,
        };
        if (mode < 0) return;
        vm.ViewModeIndex = mode;
        e.Handled = true;
    }

    private void ScrollToDayStart()
    {
        if (Vm is not { } vm) return;
        var hour = Math.Max(0, vm.Settings.DayStartHour - vm.FirstHour);
        GridScroll.Offset = new Avalonia.Vector(0, hour * Controls.WeekGrid.HourHeight);
    }

    private async Task EditAsync(CalendarEvent ev, bool isNew)
    {
        if (Vm is not { } vm) return;
        var dialog = new EventEditWindow { DataContext = new EventEditViewModel(ev, vm.Calendars, isNew, vm.Settings, _sound) };
        var result = await dialog.ShowDialog<EventEditResult?>(this);
        if (result is null) return;
        if (result.Delete) vm.DeleteEvent(ev);
        else if (isNew) vm.AddEvent(result.Event!);
        else vm.UpdateEvent(result.Event!);
    }

    private async void OnNewEvent(object? sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm) return;
        var start = DateTime.Today.AddHours(Math.Max(DateTime.Now.Hour + 1, vm.Settings.DayStartHour));
        if (start.Date != DateTime.Today) start = DateTime.Today.AddHours(vm.Settings.DayStartHour);
        await EditAsync(vm.NewEvent(start, start.AddMinutes(vm.Settings.DefaultEventMinutes)), isNew: true);
    }

    private async void OnOpenSettings(object? sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm) return;
        var dialog = new SettingsWindow { DataContext = new SettingsViewModel(vm.Settings, _sound) };
        var result = await dialog.ShowDialog<AppSettings?>(this);
        if (result is null) return;
        result.ShowWeekends = vm.Settings.ShowWeekends;
        result.DefaultView = vm.Settings.DefaultView;
        _store.Save(result);
        vm.Settings = result;
        if (_scheduler is not null) _scheduler.Enabled = result.NotificationsEnabled;
        ScrollToDayStart();
    }
}
