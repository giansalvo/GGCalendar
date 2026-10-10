using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using GGCalendar.Providers.Google;
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
    private readonly GoogleSyncService _google;
    private readonly SemaphoreSlim _syncLock = new(1, 1);
    private DispatcherTimer? _syncTimer;
    private DispatcherTimer? _debounce;
    private ReminderScheduler? _scheduler;

    public MainWindow()
    {
        InitializeComponent();
        _google = new GoogleSyncService(new CalendarStore(), new GoogleAccountStore(), new HttpClient());
        Closed += (_, _) => { _scheduler?.Dispose(); _sound.Dispose(); _syncTimer?.Stop(); };
        Grid.EventChanged += (ev, s, e) => Vm?.MoveEvent(ev, s, e);
        Grid.EventDeleted += async ev => await DeleteAsync(ev);
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
                ApplyTheme(vm.Settings);
                vm.ViewModeIndex = Math.Clamp(vm.Settings.DefaultView, 0, 5);
                vm.PropertyChanged += (_, a) =>
                {
                    if (a.PropertyName is not (nameof(MainViewModel.ViewModeIndex) or nameof(MainViewModel.ShowWeekends))) return;
                    vm.Settings.DefaultView = vm.ViewModeIndex;
                    _store.Save(vm.Settings);
                };
                _scheduler = new ReminderScheduler(() => vm.ExpandedBetween(DateTime.Now.AddDays(-1), DateTime.Now.AddDays(30), visibleOnly: false)) { Enabled = vm.Settings.NotificationsEnabled };
                _scheduler.Fired += OnReminderFired;
                _scheduler.Start();
                StartSync();
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

    private void ConfigureGoogle()
    {
        if (Vm is not { } vm) return;
        _google.Configure(GoogleClientConfig.Resolve(vm.Settings.GoogleClientId, vm.Settings.GoogleClientSecret));
    }

    private void StartSync()
    {
        if (Vm is not { } vm) return;
        ConfigureGoogle();
        vm.ChangeQueued += () =>
        {
            if (vm.Settings.SyncIntervalMinutes <= 0) return;
            _debounce ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _debounce.Stop();
            _debounce.Tick -= OnDebounce;
            _debounce.Tick += OnDebounce;
            _debounce.Start();
        };
        _syncTimer = new DispatcherTimer();
        _syncTimer.Tick += async (_, _) => await SyncAsync();
        ApplySyncInterval();
        if (vm.Settings.SyncIntervalMinutes > 0) _ = SyncAsync();
    }

    private void ApplySyncInterval()
    {
        if (_syncTimer is null || Vm is not { } vm) return;
        _syncTimer.Stop();
        var minutes = vm.Settings.SyncIntervalMinutes;
        if (minutes <= 0) return;
        _syncTimer.Interval = TimeSpan.FromMinutes(minutes);
        _syncTimer.Start();
    }

    private async void OnDebounce(object? sender, EventArgs e)
    {
        _debounce?.Stop();
        await SyncAsync();
    }

    private async void OnSyncNow(object? sender, RoutedEventArgs e) => await SyncAsync();

    private async Task SyncAsync()
    {
        if (Vm is not { } vm || _google.AccountEmails.Count == 0) return;
        if (!await _syncLock.WaitAsync(0)) return;
        vm.IsSyncing = true;
        vm.SyncStatus = "Sincronizzazione...";
        try
        {
            var result = await Task.Run(() => _google.SyncAllAsync(CancellationToken.None));
            if (result.Changed) vm.Reload();
            vm.SyncStatus = result.Errors.Count > 0
                ? "Errore di sincronizzazione: " + result.Errors[0]
                : $"Sincronizzato alle {DateTime.Now:HH:mm}";
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.IO.IOException or Microsoft.Data.Sqlite.SqliteException)
        {
            vm.SyncStatus = "Errore di sincronizzazione: " + ex.Message;
        }
        finally
        {
            vm.IsSyncing = false;
            _syncLock.Release();
        }
    }

    private static void ApplyTheme(AppSettings s)
    {
        if (Avalonia.Application.Current is { } app)
            app.RequestedThemeVariant = s.Theme switch
            {
                1 => Avalonia.Styling.ThemeVariant.Light,
                2 => Avalonia.Styling.ThemeVariant.Dark,
                _ => Avalonia.Styling.ThemeVariant.Default,
            };
    }

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

    private async Task DeleteAsync(CalendarEvent ev)
    {
        if (Vm is not { } vm) return;
        if (!vm.IsRecurring(ev)) { vm.DeleteEvent(ev); return; }
        var scope = await new DeleteRecurringWindow().ShowDialog<DeleteScope?>(this);
        if (scope is { } s) vm.DeleteOccurrence(ev, s);
    }

    private async Task EditAsync(CalendarEvent ev, bool isNew)
    {
        if (Vm is not { } vm) return;
        // Editing a repeating event always edits the whole series (its first occurrence).
        var occurrence = ev;
        if (!isNew) ev = vm.ResolveMaster(ev);
        var dialog = new EventEditWindow { DataContext = new EventEditViewModel(ev, vm.Calendars, isNew, vm.Settings, _sound) };
        var result = await dialog.ShowDialog<EventEditResult?>(this);
        if (result is null) return;
        if (result.Delete) await DeleteAsync(occurrence);
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
        var dialog = new SettingsWindow { DataContext = new SettingsViewModel(vm.Settings, _sound, _google) };
        var result = await dialog.ShowDialog<AppSettings?>(this);
        var accountsChanged = (dialog.DataContext as SettingsViewModel)?.AccountsChanged == true;
        if (result is null)
        {
            if (accountsChanged) { vm.Reload(); await SyncAsync(); }
            return;
        }
        result.ShowWeekends = vm.Settings.ShowWeekends;
        result.DefaultView = vm.Settings.DefaultView;
        _store.Save(result);
        vm.Settings = result;
        ApplyTheme(result);
        ConfigureGoogle();
        ApplySyncInterval();
        if (accountsChanged) vm.Reload();
        if (result.SyncIntervalMinutes > 0 || accountsChanged) _ = SyncAsync();
        if (_scheduler is not null) _scheduler.Enabled = result.NotificationsEnabled;
        ScrollToDayStart();
    }
}
