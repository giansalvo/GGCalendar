using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using GGCalendar.Providers.Google;
using GGCalendar.App.Services;
using GGCalendar.App.ViewModels;
using GGCalendar.Core.Ics;
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
        Grid.EventChanged += async (ev, s, e) => await MoveAsync(ev, s, e);
        Grid.EventDeleted += async ev => await DeleteAsync(ev);
        Grid.EventDroppedOutside += async (ev, point) => await DropOnCalendarAsync(ev, point);
        Grid.EventDragging += HighlightDropTarget;
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
                ? "Errore di sincronizzazione: " + string.Join(" | ", result.Errors)
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

    private async Task MoveAsync(CalendarEvent ev, DateTime start, DateTime end)
    {
        if (Vm is not { } vm) return;
        if (!vm.IsRecurring(ev)) { vm.MoveEvent(ev, start, end); return; }
        var scope = await new SeriesScopeWindow("Sposta evento ricorrente").ShowDialog<SeriesScope?>(this);
        if (scope is { } s) vm.MoveOccurrence(ev, start, end, s);
    }

    private CalendarItemViewModel? CalendarAt(Avalonia.Point point) =>
        (Avalonia.VisualTree.VisualExtensions.GetVisualAt(this, point) is { } hit
            ? Avalonia.VisualTree.VisualExtensions.GetSelfAndVisualAncestors(hit) : [])
        .OfType<Avalonia.StyledElement>().Select(x => x.DataContext).OfType<CalendarItemViewModel>().FirstOrDefault();

    private void HighlightDropTarget(Avalonia.Point? point)
    {
        if (Vm is not { } vm) return;
        var target = point is { } p ? CalendarAt(p) : null;
        foreach (var c in vm.Accounts.SelectMany(a => a.Calendars)) c.IsDropTarget = ReferenceEquals(c, target);
    }

    private async Task DropOnCalendarAsync(CalendarEvent ev, Avalonia.Point point)
    {
        if (Vm is not { } vm) return;
        var target = CalendarAt(point);
        if (target is null) return;

        var master = vm.ResolveMaster(ev);
        if (master.CalendarId == target.Id) return;
        if (!string.IsNullOrEmpty(master.Recurrence) || master.SeriesMasterId is not null || master.Attendees.Count > 0)
        {
            var message = $"L'evento verrà spostato nel calendario \"{target.Name}\".\n\n" +
                (string.IsNullOrEmpty(master.Recurrence) ? "" : "• Verrà spostata l'intera serie.\n") +
                (master.Attendees.Count > 0 ? "• Se il calendario è di un altro account, l'evento viene ricreato: gli invitati riceveranno un nuovo invito e le risposte già date si perdono.\n" : "") +
                "• Un eventuale link Meet verrà rimosso.";
            if (!await new ConfirmWindow("Spostare in un altro calendario?", message, "Sposta").ShowDialog<bool>(this)) return;
        }

        vm.ChangeCalendar(ev, target.Id);
    }

    private async Task DeleteAsync(CalendarEvent ev)
    {
        if (Vm is not { } vm) return;
        if (!vm.IsRecurring(ev)) { vm.DeleteEvent(ev); return; }
        var scope = await new SeriesScopeWindow("Elimina evento ricorrente").ShowDialog<SeriesScope?>(this);
        if (scope is { } s) vm.DeleteOccurrence(ev, s);
    }

    private async Task EditAsync(CalendarEvent ev, bool isNew)
    {
        if (Vm is not { } vm) return;
        var occurrence = ev;
        SeriesScope? scope = null;
        if (!isNew && vm.IsRecurring(ev))
        {
            scope = await new SeriesScopeWindow("Modifica evento ricorrente").ShowDialog<SeriesScope?>(this);
            if (scope is null) return;
        }

        // Editing the whole series (or a plain event) works on the master; otherwise the dialog opens on the occurrence.
        if (!isNew && scope is null or SeriesScope.All) ev = vm.ResolveMaster(ev);
        var dialog = new EventEditWindow { DataContext = new EventEditViewModel(ev, vm.Calendars, isNew, vm.Settings, _sound) };
        var result = await dialog.ShowDialog<EventEditResult?>(this);
        if (result is null) return;
        if (result.Delete) await DeleteAsync(occurrence);
        else if (isNew) vm.AddEvent(result.Event!);
        else if (scope is { } s) vm.SaveOccurrence(occurrence, result.Event!, s);
        else vm.UpdateEvent(result.Event!);
    }

    private static CalendarItemViewModel? CalendarOf(object? sender) =>
        sender is Control { DataContext: CalendarItemViewModel c }
            ? c
            : (sender as Avalonia.StyledElement)?.Parent is ContextMenu { PlacementTarget.DataContext: CalendarItemViewModel p } ? p : null;

    private async Task<string?> PickIcsAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
        {
            Title = "Importa file .ics",
            AllowMultiple = false,
            FileTypeFilter = [new Avalonia.Platform.Storage.FilePickerFileType("iCalendar") { Patterns = ["*.ics", "*.ical"] }],
        });
        if (files.Count == 0) return null;
        await using var stream = await files[0].OpenReadAsync();
        using var reader = new System.IO.StreamReader(stream, System.Text.Encoding.UTF8, true);
        return await reader.ReadToEndAsync();
    }

    private async Task ImportIntoAsync(string calendarId, string calendarName, string text)
    {
        if (Vm is not { } vm) return;
        var parsed = IcsParser.Analyze(text, calendarId);
        var total = parsed.Events.Count(e => e.SeriesMasterId is null);
        if (total == 0)
        {
            vm.SyncStatus = "Nessun evento trovato nel file .ics.";
            return;
        }

        var existing = vm.FindExisting(calendarId, parsed.Events).Count;
        var mode = ImportDuplicates.Skip;
        if (existing > 0 || parsed.Warnings.Count > 0)
        {
            if (await new ImportSummaryWindow(calendarName, total, existing, parsed.Warnings).ShowDialog<ImportDuplicates?>(this) is not { } chosen) return;
            mode = chosen;
        }

        var (imported, skipped) = vm.ImportEvents(calendarId, parsed.Events, mode);
        vm.SyncStatus = $"Importati {imported} eventi nel calendario \"{calendarName}\"" +
            (skipped > 0 ? $", {skipped} già presenti saltati" : "") +
            (parsed.Warnings.Count > 0 ? ". Avvisi: " + string.Join(" ", parsed.Warnings) : ".");
    }

    private async void OnImportIntoCalendar(object? sender, RoutedEventArgs e)
    {
        if (CalendarOf(sender) is not { } cal) return;
        try
        {
            if (await PickIcsAsync() is { } text) await ImportIntoAsync(cal.Id, cal.Name, text);
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            if (Vm is { } vm) vm.SyncStatus = "Errore di importazione: " + ex.Message;
        }
    }

    private async void OnImportAsNewCalendar(object? sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm) return;
        try
        {
            if (await PickIcsAsync() is not { } text) return;
            var name = await new PromptWindow("Importa .ics", "Nome del nuovo calendario locale:", "Importato").ShowDialog<string?>(this);
            if (name is null) return;
            await ImportIntoAsync(vm.CreateLocalCalendar(name), name, text);
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            vm.SyncStatus = "Errore di importazione: " + ex.Message;
        }
    }

    private async void OnNewLocalCalendar(object? sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm) return;
        var name = await new PromptWindow("Nuovo calendario", "Nome del nuovo calendario locale:").ShowDialog<string?>(this);
        if (name is not null) vm.CreateLocalCalendar(name);
    }

    private static string SafeFileName(string name) =>
        string.Concat(name.Select(c => System.IO.Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));

    private async void OnExportCalendar(object? sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm || CalendarOf(sender) is not { } cal) return;
        var file = await StorageProvider.SaveFilePickerAsync(new Avalonia.Platform.Storage.FilePickerSaveOptions
        {
            Title = "Esporta calendario",
            SuggestedFileName = SafeFileName(cal.Name) + ".ics",
            DefaultExtension = "ics",
            FileTypeChoices = [new Avalonia.Platform.Storage.FilePickerFileType("iCalendar") { Patterns = ["*.ics"] }],
        });
        if (file is null) return;
        try
        {
            var events = vm.EventsOf(cal.Id);
            await using var stream = await file.OpenWriteAsync();
            stream.SetLength(0);
            await using var writer = new System.IO.StreamWriter(stream, new System.Text.UTF8Encoding(false));
            await writer.WriteAsync(IcsWriter.Write(events));
            vm.SyncStatus = $"Esportati {events.Count(x => x.SeriesMasterId is null)} eventi del calendario \"{cal.Name}\".";
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            vm.SyncStatus = "Errore di esportazione: " + ex.Message;
        }
    }

    private async void OnDeleteCalendar(object? sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm || CalendarOf(sender) is not { } cal) return;
        if (Services.GoogleSyncService.IsGoogleCalendar(cal.Id))
        {
            vm.SyncStatus = "I calendari Google si rimuovono scollegando l'account dalle impostazioni.";
            return;
        }

        if (await new ConfirmWindow("Eliminare il calendario?", $"Il calendario \"{cal.Name}\" e tutti i suoi eventi verranno eliminati da questo PC. L'operazione non è annullabile.", "Elimina").ShowDialog<bool>(this))
            vm.DeleteLocalCalendar(cal.Id);
    }

    private async void OnNewEvent(object? sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm) return;
        var start = DateTime.Today.AddHours(Math.Max(DateTime.Now.Hour + 1, vm.Settings.DayStartHour));
        if (start.Date != DateTime.Today) start = DateTime.Today.AddHours(vm.Settings.DayStartHour);
        await EditAsync(vm.NewEvent(start, start.AddMinutes(vm.Settings.DefaultEventMinutes)), isNew: true);
    }

    private async void OnOpenAbout(object? sender, RoutedEventArgs e) => await new AboutWindow().ShowDialog(this);

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
