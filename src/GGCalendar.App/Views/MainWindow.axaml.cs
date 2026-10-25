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
using GGCalendar.Core.Localization;

namespace GGCalendar.App.Views;

public partial class MainWindow : Window
{
    private readonly SettingsStore _store = new();
    private readonly SoundService _sound = new();
    private readonly GoogleSyncService _google;
    private readonly SemaphoreSlim _syncLock = new(1, 1);
    private DispatcherTimer? _syncTimer;
    private DispatcherTimer? _retryTimer;
    private CancellationTokenSource? _syncCts;
    private DispatcherTimer? _debounce;
    private ReminderScheduler? _scheduler;

    public MainWindow()
    {
        InitializeComponent();
        _google = new GoogleSyncService(new CalendarStore(), new GoogleAccountStore(), new HttpClient());
        L.Changed += OnLanguageChanged;
        Closed += (_, _) => { L.Changed -= OnLanguageChanged; _scheduler?.Dispose(); _sound.Dispose(); _syncTimer?.Stop(); _retryTimer?.Stop(); };
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
        Grid.EventClicked += (ev, rect) => ShowDetails(ev, Grid, rect);
        AllDay.EventClicked += (ev, rect) => ShowDetails(ev, AllDay, rect);
        Month.EventClicked += (ev, rect) => ShowDetails(ev, Month, rect);
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
                vm.EventsChanged += _scheduler.Invalidate;
                _scheduler.Start();
                StartSync();
            }
            Avalonia.Threading.Dispatcher.UIThread.Post(ScrollToDayStart, Avalonia.Threading.DispatcherPriority.Loaded);
        };
    }

    private static readonly TimeSpan AlarmMaxDuration = TimeSpan.FromMinutes(2);

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

        // An alarm rings until it is turned off, but at most AlarmMaxDuration; the notification stays on screen.
        DispatcherTimer? alarmLimit = null;
        if (isAlarm)
        {
            alarmLimit = new DispatcherTimer { Interval = AlarmMaxDuration };
            alarmLimit.Tick += (_, _) => { alarmLimit.Stop(); player.Stop(); };
            alarmLimit.Start();
        }

        var toast = new ReminderNotificationWindow(due.Event, vm.Settings.Use24HourClock, isAlarm);
        toast.Closed += (_, _) =>
        {
            alarmLimit?.Stop();
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
        // Alarms now travel on Google: events synced before only come back with them after a full download, once.
        if (vm.Settings.SyncSchema < 1)
        {
            _google.ResetSync();
            vm.Settings.SyncSchema = 1;
            _store.Save(vm.Settings);
        }
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
        vm.SetStatus("status.syncing");
        try
        {
            // With many changes waiting (an import into a Google calendar) the sending lasts long: say how far it is, and let the user stop it.
            using var cts = new CancellationTokenSource();
            _syncCts = cts;
            SyncProgressWindow? progress = null;
            var finished = false;
            var discard = false;
            var result = await Task.Run(() => _google.SyncAllAsync(cts.Token, (done, total) =>
            {
                if (total > 30)
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (finished) return;
                        if (progress is null || !progress.StopRequested) vm.SetStatus("status.sending", done, total);
                        if (progress is null)
                        {
                            progress = new SyncProgressWindow(() => cts.Cancel(), async () =>
                            {
                                var waiting = await Task.Run(vm.CountUnsentCreations);
                                var sure = await new ConfirmWindow(L.T("confirm.discard_title"), L.N("confirm.discard_text", waiting), L.T("confirm.discard_button"))
                                    .ShowDialog<bool>(progress!);
                                if (!sure) return false;
                                discard = true;
                                cts.Cancel();
                                return true;
                            });
                            progress.Show(this);
                        }

                        progress.Update(done, total);
                    });
            }));
            finished = true;
            progress?.Close();
            _syncCts = null;
            if (result.Changed) await vm.ReloadAsync();
            var conflicts = result.Conflicts.Count > 0 ? Msg.Of("status.conflicts", Msg.JoinWith(" | ", result.Conflicts)) : Msg.Literal("");
            var waiting = result.Pending > 0
                ? Msg.Join(Msg.Count("status.pending", result.Pending), result.RetryAt is { } at ? Msg.Of("status.pending_retry", at.ToLocalTime().DateTime) : Msg.Literal("."))
                : Msg.Literal("");
            if (result.Cancelled)
            {
                if (discard)
                {
                    var thrownAway = await vm.DiscardUnsentCreationsAsync();
                    vm.SetStatus(Msg.Count("status.send_stopped_discarded", thrownAway));
                }
                else
                {
                    vm.SetStatus(Msg.Count("status.send_stopped_queued", result.Pending));
                }

                return;
            }

            vm.SetStatus(Msg.Join(
                result.Errors.Count > 0 ? Msg.Of("error.sync", Msg.JoinWith(" | ", result.Errors)) : Msg.Of("status.synced", DateTime.Now),
                conflicts, waiting));
            if (result.RetryAt is { } retryAt) ScheduleRetry(retryAt);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.IO.IOException or Microsoft.Data.Sqlite.SqliteException)
        {
            vm.SetStatus("error.sync", ex.Message);
        }
        finally
        {
            vm.IsSyncing = false;
            _syncLock.Release();
        }
    }

    /// <summary>Google asked to slow down: try again when it said, whatever the sync interval is.</summary>
    private void ScheduleRetry(DateTimeOffset at)
    {
        _retryTimer?.Stop();
        var wait = at - DateTimeOffset.UtcNow + TimeSpan.FromSeconds(2);
        if (wait < TimeSpan.FromSeconds(5)) wait = TimeSpan.FromSeconds(5);
        _retryTimer = new DispatcherTimer { Interval = wait };
        _retryTimer.Tick += async (_, _) =>
        {
            _retryTimer?.Stop();
            await SyncAsync();
        };
        _retryTimer.Start();
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

    private void OnAgendaItemTapped(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        if (sender is Control { DataContext: AgendaItem item } row) ShowDetails(item.Event, row, null);
    }

    // ---------- Event details (single click) ----------

    /// <summary>Shows the details card beside <paramref name="rect"/> of <paramref name="target"/> (the whole control when null).</summary>
    private void ShowDetails(CalendarEvent ev, Control target, Avalonia.Rect? rect)
    {
        if (Vm is not { } vm) return;
        var calendar = vm.Calendars.FirstOrDefault(c => c.Id == ev.CalendarId);
        // A modified occurrence belongs to the series of its master; any other event is (or expands from) itself.
        var series = ev.SeriesMasterId is { } masterId ? vm.AllEvents.FirstOrDefault(e => e.Id == masterId) : vm.ResolveMaster(ev);
        var card = new EventDetailsCard(ev, series?.Recurrence, ev.ColorHex ?? calendar?.ColorHex ?? "#1A73E8",
            calendar?.Name ?? "", calendar?.DisplayAccountName ?? "", vm.Settings.Use24HourClock,
            ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark);
        card.EditRequested += async () => { CloseDetails(); await EditAsync(ev, isNew: false); };
        card.DeleteRequested += async () => { CloseDetails(); await DeleteAsync(ev); };
        card.MailRequested += async () => await MailGuestsAsync(ev);
        card.ExportRequested += async () => { CloseDetails(); await ExportEventAsync(vm.ResolveMaster(ev)); };
        card.JoinMeetRequested += async () => await OpenHttpsAsync(ev.MeetLink);
        card.CloseRequested += CloseDetails;

        DetailsPopup.IsOpen = false;
        DetailsPopup.PlacementTarget = target;
        DetailsPopup.PlacementRect = rect;
        DetailsPopup.Child = card;
        DetailsPopup.IsOpen = true;
        card.Focus();
    }

    /// <summary>The language changed: what is drawn by hand shows the new names of months and days, and the card of an event is closed (it is made again when opened).</summary>
    private void OnLanguageChanged()
    {
        CloseDetails();
        Grid.InvalidateVisual();
        AllDay.InvalidateVisual();
        Month.InvalidateVisual();
        Year.InvalidateVisual();

        // The small calendar writes its month and day names when it is built: showing it as years and back makes it write them again.
        var shown = MiniCalendar.DisplayDate;
        MiniCalendar.DisplayMode = CalendarMode.Year;
        MiniCalendar.DisplayMode = CalendarMode.Month;
        MiniCalendar.DisplayDate = shown;
    }

    private void CloseDetails()
    {
        DetailsPopup.IsOpen = false;
        DetailsPopup.Child = null;
    }

    private async Task MailGuestsAsync(CalendarEvent ev)
    {
        var to = string.Join(",", ev.Attendees.Select(a => Uri.EscapeDataString(a.Email)));
        if (to.Length == 0) return;
        await Launcher.LaunchUriAsync(new Uri($"mailto:{to}?subject={Uri.EscapeDataString(ev.Title)}"));
    }

    private async Task OpenHttpsAsync(string? link)
    {
        // Only web links: the value comes from Google, but it is still data from outside the app.
        if (Uri.TryCreate(link, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps) await Launcher.LaunchUriAsync(uri);
    }

    private async Task ExportEventAsync(CalendarEvent ev)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new Avalonia.Platform.Storage.FilePickerSaveOptions
        {
            Title = L.T("file.export_event_title"),
            SuggestedFileName = SafeFileName(string.IsNullOrWhiteSpace(ev.Title) ? L.T("file.default_event_name") : ev.Title) + ".ics",
            DefaultExtension = "ics",
            FileTypeChoices = [new Avalonia.Platform.Storage.FilePickerFileType(L.T("calendar.ics_type")) { Patterns = ["*.ics"] }],
        });
        if (file is null) return;
        try
        {
            await using var stream = await file.OpenWriteAsync();
            stream.SetLength(0);
            await using var writer = new System.IO.StreamWriter(stream, new System.Text.UTF8Encoding(false));
            await writer.WriteAsync(IcsWriter.Write([ev]));
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            if (Vm is { } vm) vm.SetStatus("error.export", ex.Message);
        }
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
        var scope = await new SeriesScopeWindow(L.T("scope.move_title")).ShowDialog<SeriesScope?>(this);
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
            var message = L.T("move.text", target.Name) + "\n\n" +
                (string.IsNullOrEmpty(master.Recurrence) ? "" : L.T("move.series") + "\n") +
                (master.Attendees.Count > 0 ? L.T("move.guests") + "\n" : "") +
                L.T("move.meet");
            if (!await new ConfirmWindow(L.T("move.title"), message, L.T("move.button")).ShowDialog<bool>(this)) return;
        }

        vm.ChangeCalendar(ev, target.Id);
    }

    private async Task DeleteAsync(CalendarEvent ev)
    {
        if (Vm is not { } vm) return;
        CloseDetails();
        if (!vm.IsRecurring(ev)) { vm.DeleteEvent(ev); return; }
        var scope = await new SeriesScopeWindow(L.T("scope.delete_title")).ShowDialog<SeriesScope?>(this);
        if (scope is { } s) vm.DeleteOccurrence(ev, s);
    }

    // True while an event dialog (or its series-scope question) is open.
    private bool _editing;

    private async Task EditAsync(CalendarEvent ev, bool isNew)
    {
        // A double click can arrive twice (e.g. the click that closes the details card is passed on to the calendar):
        // a second dialog would open under the first and, saved or cancelled later, undo the change made in the first.
        if (Vm is null || _editing) return;
        _editing = true;
        try
        {
            await EditCoreAsync(ev, isNew);
        }
        finally
        {
            _editing = false;
        }
    }

    private async Task EditCoreAsync(CalendarEvent ev, bool isNew)
    {
        if (Vm is not { } vm) return;
        CloseDetails();
        var occurrence = ev;
        SeriesScope? scope = null;
        if (!isNew && vm.IsRecurring(ev))
        {
            scope = await new SeriesScopeWindow(L.T("scope.edit_title")).ShowDialog<SeriesScope?>(this);
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

    /// <summary>The bytes of the file the user picks, or null when none is picked or the file is over the limit (the user is told).</summary>
    private async Task<byte[]?> PickIcsAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
        {
            Title = L.T("import.title"),
            AllowMultiple = false,
            FileTypeFilter = [new Avalonia.Platform.Storage.FilePickerFileType(L.T("calendar.ics_type")) { Patterns = ["*.ics", "*.ical"] }],
        });
        if (files.Count == 0) return null;
        await using var stream = await files[0].OpenReadAsync();
        var bytes = IcsSources.ReadLimited(stream);
        if (bytes is null)
            await new WarningWindow(L.T("import.too_large_title"), L.T("import.too_large", IcsSources.MaxBytes / (1024 * 1024))).ShowDialog(this);
        return bytes;
    }

    /// <summary>Above this many events, an import into a Google calendar warns that sending them will take a while.</summary>
    private const int ManyEventsForGoogle = 200;

    /// <summary>
    /// Runs slow work with the window disabled. A small progress window appears only if the work lasts more than a moment, so short
    /// operations show nothing.
    /// </summary>
    private async Task<T> RunBusyAsync<T>(Msg message, Func<Task<T>> work)
    {
        if (Vm is { } vm) vm.SetStatus(message);
        IsEnabled = false;
        BusyWindow? busy = null;
        try
        {
            var task = work();
            if (await Task.WhenAny(task, Task.Delay(300)) != task)
            {
                busy = new BusyWindow(message.Text);
                busy.Show(this);
            }

            return await task;
        }
        finally
        {
            busy?.Close();
            IsEnabled = true;
        }
    }

    private void OnCalendarToggled(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is CalendarItemViewModel item) item.IsVisible = !item.IsVisible;
    }

    private async Task ImportIntoAsync(string calendarId, string calendarName, byte[] bytes)
    {
        if (Vm is not { } vm) return;
        var parsed = await RunBusyAsync(Msg.Of("busy.reading"), () => Task.Run(() => IcsParser.AnalyzeFile(bytes, calendarId)));
        var total = parsed.Events.Count(e => e.SeriesMasterId is null);
        if (total == 0)
        {
            vm.SetStatus(parsed.Report.Fatal is not null && parsed.Messages.Count > 0 ? parsed.Messages[0] : Msg.Of("status.no_events"));
            return;
        }

        var existing = vm.FindExisting(calendarId, parsed.Events).Count;
        var notice = GoogleSyncService.IsGoogleCalendar(calendarId) && total > ManyEventsForGoogle
            ? L.T("import.google_notice", total, SyncProgressWindow.FormatRemaining(SyncProgressWindow.EstimateRemaining(0, total, TimeSpan.Zero)))
            : null;
        var mode = ImportDuplicates.Skip;
        if (existing > 0 || parsed.Warnings.Count > 0 || notice is not null)
        {
            if (await new ImportSummaryWindow(calendarName, total, existing, parsed.Warnings, notice).ShowDialog<ImportDuplicates?>(this) is not { } chosen) return;
            mode = chosen;
        }

        var (imported, skipped) = await RunBusyAsync(Msg.Count("busy.importing", total), () => vm.ImportEventsAsync(calendarId, parsed.Events, mode));
        vm.SetStatus(Msg.Join(
            Msg.Count("status.imported", imported, calendarName),
            skipped > 0 ? Msg.Count("status.imported_skipped", skipped) : Msg.Literal(""),
            parsed.Messages.Count > 0 ? Msg.Of("status.imported_warnings", Msg.JoinWith(" ", parsed.Messages)) : Msg.Literal(".")));
    }

    private async void OnImportIntoCalendar(object? sender, RoutedEventArgs e)
    {
        if (CalendarOf(sender) is not { } cal) return;
        try
        {
            if (await PickIcsAsync() is { } bytes) await ImportIntoAsync(cal.Id, cal.Name, bytes);
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            if (Vm is { } vm) vm.SetStatus("error.import", ex.Message);
        }
    }

    private async void OnImportAsNewCalendar(object? sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm) return;
        try
        {
            if (await PickIcsAsync() is not { } bytes) return;
            var preview = IcsParser.AnalyzeFile(bytes, "preview");
            if (preview.Report.Fatal is not null)
            {
                await new WarningWindow(L.T("import.title"), preview.Warnings.FirstOrDefault() ?? L.T("import.cannot")).ShowDialog(this);
                return;
            }

            var proposed = preview.Report.CalendarName ?? L.T("import.default_name");
            var name = await new PromptWindow(L.T("import.title"), L.T("calendar.name_prompt"), proposed).ShowDialog<string?>(this);
            if (name is null) return;
            await ImportIntoAsync(vm.CreateLocalCalendar(name), name, bytes);
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            vm.SetStatus("error.import", ex.Message);
        }
    }

    private void OnToggleAccount(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is AccountGroup group) group.IsExpanded = !group.IsExpanded;
    }

    // TODO: create a new calendar in this account (on Google: issue #16).
    private void OnNewCalendarInAccount(object? sender, RoutedEventArgs e)
    {
        if (Vm is { } vm) vm.SetStatus("status.new_calendar_unavailable");
    }

    private async void OnNewLocalCalendar(object? sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm) return;
        var name = await new PromptWindow(L.T("calendar.new_title"), L.T("calendar.name_prompt")).ShowDialog<string?>(this);
        if (name is not null) vm.CreateLocalCalendar(name);
    }

    private static string SafeFileName(string name) =>
        string.Concat(name.Select(c => System.IO.Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));

    private async void OnExportCalendar(object? sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm || CalendarOf(sender) is not { } cal) return;
        var file = await StorageProvider.SaveFilePickerAsync(new Avalonia.Platform.Storage.FilePickerSaveOptions
        {
            Title = L.T("file.export_calendar_title"),
            SuggestedFileName = SafeFileName(cal.Name) + ".ics",
            DefaultExtension = "ics",
            FileTypeChoices = [new Avalonia.Platform.Storage.FilePickerFileType(L.T("calendar.ics_type")) { Patterns = ["*.ics"] }],
        });
        if (file is null) return;
        try
        {
            var events = vm.EventsOf(cal.Id);
            await using var stream = await file.OpenWriteAsync();
            stream.SetLength(0);
            await using var writer = new System.IO.StreamWriter(stream, new System.Text.UTF8Encoding(false));
            await writer.WriteAsync(IcsWriter.Write(events));
            vm.SetStatus(Msg.Count("status.exported", events.Count(x => x.SeriesMasterId is null), cal.Name));
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            vm.SetStatus("error.export", ex.Message);
        }
    }

    private async void OnDeleteCalendar(object? sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm || CalendarOf(sender) is not { } cal) return;
        if (Services.GoogleSyncService.IsGoogleCalendar(cal.Id))
        {
            vm.SetStatus("status.google_calendars_hint");
            return;
        }

        if (await new ConfirmWindow(L.T("calendar.delete_title"), L.T("calendar.delete_text", cal.Name), L.T("calendar.delete_button")).ShowDialog<bool>(this))
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
            if (accountsChanged) { await vm.ReloadAsync(); await SyncAsync(); }
            return;
        }
        result.ShowWeekends = vm.Settings.ShowWeekends;
        result.DefaultView = vm.Settings.DefaultView;
        _store.Save(result);
        vm.Settings = result;
        L.Apply(result.Language);
        ApplyTheme(result);
        ConfigureGoogle();
        ApplySyncInterval();
        if (accountsChanged) await vm.ReloadAsync();
        if (result.SyncIntervalMinutes > 0 || accountsChanged) _ = SyncAsync();
        if (_scheduler is not null) _scheduler.Enabled = result.NotificationsEnabled;
        ScrollToDayStart();
    }
}
