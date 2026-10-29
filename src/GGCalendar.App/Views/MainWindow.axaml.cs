using System;
using System.Collections.Generic;
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
using GGCalendar.Core.Subscriptions;
using GGCalendar.Core.Reminders;
using GGCalendar.Data;
using GGCalendar.Core.Localization;
using GGCalendar.Core.Recurrence;
using GGCalendar.Core.Sharing;

namespace GGCalendar.App.Views;

public partial class MainWindow : Window
{
    private readonly SettingsStore _store = new();
    private readonly SoundService _sound = new();
    private readonly GoogleSyncService _google;

    /// <summary>The public calendars followed by address.</summary>
    public SubscriptionService Subscriptions { get; }
    private DispatcherTimer? _syncTimer;
    private DispatcherTimer? _retryTimer;
    private CancellationTokenSource? _syncCts;
    private DispatcherTimer? _debounce;
    private ReminderScheduler? _scheduler;

    /// <summary>How the Google service is made when the window opens: the tests put a service on a fake Google here.</summary>
    public static Func<GoogleSyncService>? GoogleFactory { get; set; }

    public MainWindow()
    {
        InitializeComponent();
        _google = GoogleFactory?.Invoke() ?? new GoogleSyncService(new CalendarStore(), new GoogleAccountStore(), new HttpClient());
        Subscriptions = new SubscriptionService(new CalendarStore());
        Subscriptions.Changed += () => { if (Vm is { } vm) _ = vm.ReloadAsync(); };
        Grid.IsReadOnly = MainViewModel.IsReadOnly;
        L.Changed += OnLanguageChanged;
        Closed += (_, _) => { L.Changed -= OnLanguageChanged; _scheduler?.Dispose(); _sound.Dispose(); StopAlarmSound(); _alarmsTimer?.Stop(); _alarms?.Dispose(); _queueTimer?.Stop(); _syncTimer?.Stop(); _retryTimer?.Stop(); _subscriptionTimer?.Stop(); };
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
        SetUpSyncQueue();
        _ringer.SoundRequested += StartAlarmSound;
        _ringer.SoundStopRequested += StopAlarmSound;
        Opened += (_, _) =>
        {
            if (Vm is { } vm)
            {
                vm.Settings = _store.Load();
                ApplyTheme(vm.Settings);
                vm.ViewModeIndex = Math.Clamp(vm.Settings.DefaultView, 0, 5);
                vm.Search.ApplySettings(vm.Settings);
                vm.Search.ScopeChanged += () =>
                {
                    vm.Search.WriteSettings(vm.Settings);
                    _store.Save(vm.Settings);
                };
                vm.PropertyChanged += (_, a) =>
                {
                    if (a.PropertyName is not (nameof(MainViewModel.ViewModeIndex) or nameof(MainViewModel.ShowWeekends))) return;
                    vm.Settings.DefaultView = vm.ViewModeIndex;
                    _store.Save(vm.Settings);
                };
                _scheduler = new ReminderScheduler(() => vm.ExpandedBetween(DateTime.Now.AddDays(-1), DateTime.Now + ReminderScheduler.Window, visibleOnly: false), isVisible: vm.IsCalendarVisible, now: ReminderClock) { Enabled = vm.Settings.NotificationsEnabled };
                _scheduler.Fired += OnReminderFired;
                vm.EventsChanged += _scheduler.Invalidate;
                vm.CalendarVisibilityChanged += _scheduler.VisibilityChanged;
                SetUpAlarms(vm);
                _scheduler.Start();
                StartSync();
                StartSubscriptionRefresh();
            }
            Avalonia.Threading.Dispatcher.UIThread.Post(ScrollToDayStart, Avalonia.Threading.DispatcherPriority.Loaded);
        };
    }

    // ---------- Alarms: the ones ringing and the ones waiting (issue #18) ----------

    private readonly AlarmRinger _ringer = new();

    /// <summary>The clock of the reminders (null = the real one): the tests move it to make alarms ring without waiting.</summary>
    public static Func<DateTime>? ReminderClock { get; set; }

    /// <summary>The alarms that ring now (the tests look at them).</summary>
    public AlarmRinger Ringer => _ringer;

    /// <summary>The scheduler of the reminders, once the window is open.</summary>
    public ReminderScheduler? Scheduler => _scheduler;

    /// <summary>The list of the alarms, once the window is open.</summary>
    public AlarmsViewModel? Alarms => _alarms;

    /// <summary>The window of the alarms that the button opens, once the window is open.</summary>
    public AlarmsPanel? AlarmsList => _alarmsPanel;
    private readonly Dictionary<string, ReminderNotificationWindow> _alarmToasts = [];
    private SoundService? _alarmPlayer;
    private DispatcherTimer? _alarmLimit;
    private DispatcherTimer? _alarmsTimer;
    private AlarmsViewModel? _alarms;
    private AlarmsPanel? _alarmsPanel;

    private void SetUpAlarms(MainViewModel vm)
    {
        if (_scheduler is not { } scheduler) return;
        _alarms = new AlarmsViewModel(scheduler, _ringer, vm, StopAlarm, StopAllAlarms);
        _alarmsPanel = new AlarmsPanel(_alarms);
        _alarmsPanel.EventRequested += ev =>
        {
            AlarmsButton.Flyout?.Hide();
            ShowDetails(ev, AlarmsButton, null);
        };
        ((Flyout)AlarmsButton.Flyout!).Content = _alarmsPanel;
        _alarms.Changed += UpdateAlarmsButton;
        // The alarms that come into the 60 days ahead do not raise anything by themselves: the button follows the clock.
        _alarmsTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _alarmsTimer.Tick += (_, _) => UpdateAlarmsButton();
        _alarmsTimer.Start();
        UpdateAlarmsButton();
    }

    /// <summary>The button of the alarms: grey and not pressable when there are none (or the notifications are off), red while one rings.</summary>
    private void UpdateAlarmsButton()
    {
        if (_alarms is not { } alarms) return;
        var count = alarms.Count;
        var ringing = alarms.IsRinging;
        var active = alarms.NotificationsEnabled && count > 0;
        AlarmsButton.IsEnabled = active;
        AlarmsGlyph.Opacity = active ? 1 : 0.4;
        // Not ringing: the colour of the text again (a null brush set here would make the glyph invisible).
        if (ringing) AlarmsGlyph.Foreground = Avalonia.Media.Brush.Parse("#D93025");
        else AlarmsGlyph.ClearValue(TextBlock.ForegroundProperty);
        AlarmsBadge.IsVisible = active;
        AlarmsBadge.Background = Avalonia.Media.Brush.Parse(ringing ? "#D93025" : "#1A73E8");
        AlarmsBadgeText.Text = count.ToString();
        ToolTip.SetTip(AlarmsButton, L.T(!alarms.NotificationsEnabled ? "alarms.tip_off" : count == 0 ? "alarms.tip_none" : ringing ? "alarms.tip_ringing" : "alarms.tip"));
    }

    private void StartAlarmSound(RingingAlarm alarm)
    {
        StopAlarmSound();
        _alarmPlayer = new SoundService();
        var volume = Vm?.Settings.ReminderVolumePercent ?? 80;
        // A file that does not play (damaged, in use) is replaced by the sound of Windows: an alarm must not be silent.
        if (!_alarmPlayer.Play(alarm.Sound, volume, loop: true)) _alarmPlayer.Play(ReminderSounds.Playable(ReminderSounds.SystemAlarmSound, isAlarm: true), volume, loop: true);
        // An alarm rings until it is turned off, but at most the time set in the settings (1 to 10 minutes); the notification stays on screen, and the next alarm in line starts.
        var limit = new DispatcherTimer { Interval = (Vm?.Settings ?? new AppSettings()).AlarmDuration };
        limit.Tick += (_, _) =>
        {
            limit.Stop();
            _ringer.Silence(alarm.Key);
        };
        _alarmLimit = limit;
        limit.Start();
    }

    private void StopAlarmSound()
    {
        _alarmLimit?.Stop();
        _alarmLimit = null;
        _alarmPlayer?.Dispose();
        _alarmPlayer = null;
    }

    /// <summary>Turns an alarm off: its notification closes, which is what removes it from the ringer.</summary>
    private void StopAlarm(string key)
    {
        if (_alarmToasts.TryGetValue(key, out var toast)) toast.Close();
        else _ringer.Remove(key);
    }

    private void StopAllAlarms()
    {
        foreach (var toast in _alarmToasts.Values.ToList()) toast.Close();
        _ringer.RemoveAll();
    }

    // ---------- The queue of the changes waiting for Google (issue #19) ----------

    private SyncQueueWindow? _queueWindow;
    private DispatcherTimer? _queueTimer;
    private DateTime _queueRefreshedAt;

    private void SetUpSyncQueue()
    {
        _queueTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
        _queueTimer.Tick += (_, _) => RefreshQueueIndicator();
        _queueTimer.Start();
        RefreshQueueIndicator();
    }

    /// <summary>The indicator in the status bar: a green dot when nothing waits for Google, an hourglass with the number of events when something does.</summary>
    private void RefreshQueueIndicator()
    {
        int n;
        try
        {
            n = _google.CountQueuedEvents();
        }
        catch (Exception ex) when (ex is Microsoft.Data.Sqlite.SqliteException or System.IO.IOException)
        {
            return; // the store is busy: the next refresh will tell
        }

        _queueRefreshedAt = DateTime.UtcNow;
        QueueDot.IsVisible = n == 0;
        QueueGlyph.IsVisible = n > 0;
        QueueCount.IsVisible = n > 0;
        QueueCount.Text = n.ToString();
        ToolTip.SetTip(QueueButton, n == 0 ? L.T("queue.tip_ok") : L.N("queue.tip", n));
        _queueWindow?.Refresh();
    }

    /// <summary>The window of the queue is open (or opening): no automatic sync starts, so the list does not change under the person's eyes.</summary>
    private bool _queueOpen;

    private async void OnOpenSyncQueue(object? sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm || _queueOpen) return;
        _queueOpen = true;
        try
        {
            // A sending that is going on is stopped first (what was not sent stays in the queue): the list would change while it is read,
            // and cancelling would have to wait for the end of a sending that may last minutes.
            if (_syncCts is { } sending)
            {
                vm.SetStatus("queue.pausing");
                sending.Cancel();
            }

            await _google.Gate.WaitAsync();
            _google.Gate.Release();
            RefreshQueueIndicator();

            var window = new SyncQueueWindow(_google, vm, CancelQueuedAsync, () => SyncCoreAsync(fromQueue: true));
            _queueWindow = window;
            await window.ShowDialog(this);
        }
        finally
        {
            _queueWindow = null;
            _queueOpen = false;
        }

        // What is still waiting goes on its way again (the sending was held while the window was open).
        if (_google.CountQueuedEvents() > 0) _ = SyncAsync();
    }

    /// <summary>Cancels the changes waiting for the events: they go back as Google has them (a sync that is running ends first).</summary>
    private async Task CancelQueuedAsync(IReadOnlyList<QueuedEvent> events)
    {
        if (Vm is not { } vm) return;
        var result = await _google.CancelQueuedAsync(events.Select(q => q.EventId).ToList(), CancellationToken.None);
        await vm.ReloadAsync();
        vm.SetStatus(result.Cancelled == 0 ? Msg.Of("queue.cancel_none") : Msg.Count("queue.cancelled", result.Cancelled));
        RefreshQueueIndicator();
        // The events that come back from Google (deleted, moved) arrive with the next sync: it starts now.
        if (result.RestoreFromGoogle) _ = SyncCoreAsync(fromQueue: true);
    }

    // ---------- Refresh of the public calendars ----------

    private DispatcherTimer? _subscriptionTimer;
    private bool _refreshingSubscriptions;

    /// <summary>How often the app looks whether a public calendar is due (the interval itself is in the settings).</summary>
    public static TimeSpan SubscriptionPollPeriod { get; set; } = TimeSpan.FromMinutes(1);

    private void StartSubscriptionRefresh()
    {
        _subscriptionTimer = new DispatcherTimer { Interval = SubscriptionPollPeriod };
        _subscriptionTimer.Tick += async (_, _) => await RefreshDueSubscriptionsAsync();
        _subscriptionTimer.Start();
        _ = RefreshDueSubscriptionsAsync(); // at start: the ones whose last check is older than the interval
    }

    private async Task RefreshDueSubscriptionsAsync()
    {
        if (Vm is not { } vm || _refreshingSubscriptions || vm.Settings.SubscriptionIntervalMinutes <= 0) return;
        _refreshingSubscriptions = true;
        try
        {
            var results = await Subscriptions.RefreshDueAsync(TimeSpan.FromMinutes(vm.Settings.SubscriptionIntervalMinutes));
            if (results.Count == 0) return;
            await vm.ReloadAsync();
            // Silent when nothing changed; a failure or a change is told in the status bar.
            foreach (var r in results.Where(r => r.Outcome != RefreshOutcome.NotModified))
            {
                var name = vm.Calendars.FirstOrDefault(c => c.Id == r.CalendarId)?.Name ?? "";
                if (r.Outcome == RefreshOutcome.Failed)
                    vm.SetStatus(Msg.Of("error.sub_refresh", name, SubscriptionMessages.Error(r.Problem ?? SubscriptionProblem.Unreadable)));
                else if (r.Changes is { } c)
                    vm.SetStatus("status.sub_updated", name, c.Added, c.Changed, c.Removed);
            }
        }
        finally
        {
            _refreshingSubscriptions = false;
        }
    }

    /// <summary>How long the alarm that rings now has to ring (the limit set in the settings when it started); null when none rings.</summary>
    public TimeSpan? AlarmLimit => _alarmLimit?.Interval;

    private void OnReminderFired(DueReminder due)
    {
        if (Vm is not { } vm) return;
        var isAlarm = due.Reminder.Method == ReminderMethod.Alarm;
        // The sound of the reminder, else the default of its kind (popup or alarm); a file that is gone is replaced by a sound of Windows.
        var sound = ReminderSounds.Playable(ReminderSounds.Choose(due.Reminder, vm.Settings), isAlarm);

        // A reminder plays its sound once; an alarm rings in a loop, one at a time (see AlarmRinger), until it is turned off.
        SoundService? player = null;
        if (!isAlarm)
        {
            player = new SoundService();
            if (!player.Play(sound, vm.Settings.ReminderVolumePercent, loop: false) && sound is not null)
                player.Play(ReminderSounds.Playable(ReminderSounds.SystemReminderSound, isAlarm: false), vm.Settings.ReminderVolumePercent, loop: false);
        }

        var toast = new ReminderNotificationWindow(due.Event, vm.Settings.Use24HourClock, isAlarm);
        toast.Closed += (_, _) =>
        {
            player?.Dispose();
            if (isAlarm)
            {
                _alarmToasts.Remove(due.Key);
                _ringer.Remove(due.Key);
            }

            if (toast.Result == ReminderAction.Snooze) _scheduler?.Snooze(due, TimeSpan.FromMinutes(5));
        };
        if (isAlarm)
        {
            _alarmToasts[due.Key] = toast;
            _ringer.Add(due, sound);
        }

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
            RefreshQueueIndicator();
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
        // Alarms (1) and attachments (2) travel on Google: events synced before only come back with them after a full download, once.
        if (vm.Settings.SyncSchema < 2)
        {
            _google.ResetSync();
            vm.Settings.SyncSchema = 2;
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

    private Task SyncAsync() => SyncCoreAsync(fromQueue: false);

    /// <param name="fromQueue">The window of the queue asked for it ("Riprova ora", or events that come back from Google): the automatic syncs are held while that window is open, this one is not.</param>
    private async Task SyncCoreAsync(bool fromQueue)
    {
        if (Vm is not { } vm || _google.AccountEmails.Count == 0) return;
        if (_queueOpen && !fromQueue) return;
        if (!await _google.Gate.WaitAsync(0)) return;
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
                // The number in the status bar goes down as the changes are sent.
                if (DateTime.UtcNow - _queueRefreshedAt > TimeSpan.FromSeconds(1)) Dispatcher.UIThread.Post(RefreshQueueIndicator);
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
            _google.Gate.Release();
            RefreshQueueIndicator();
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

    /// <summary>Puts the font chosen in the settings in use; the grids drawn by hand are drawn again with it.</summary>
    private void ApplyFont(AppSettings s)
    {
        AppFonts.Apply(s.FontFamily);
        foreach (var grid in Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(this).OfType<Control>().Where(c => c is Controls.WeekGrid or Controls.MonthGrid or Controls.YearGrid or Controls.AllDayStrip))
        {
            grid.InvalidateMeasure();
            grid.InvalidateVisual();
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
            ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark,
            vm.SecondZoneText(ev), readOnly: MainViewModel.IsReadOnly(ev));
        card.EditRequested += async () => { CloseDetails(); await EditAsync(ev, isNew: false); };
        card.DeleteRequested += async () => { CloseDetails(); await DeleteAsync(ev); };
        string ShareText()
        {
            var rule = RecurrenceRule.Parse(series?.Recurrence)?.Describe();
            return EventShareText.Build(ev, rule is null ? null : char.ToUpper(rule[0], L.Culture) + rule[1..], calendar?.Name ?? "", vm.Settings.Use24HourClock, vm.SecondZoneText(ev));
        }

        card.ShareHint += key => vm.SetStatus(key is null ? null : Msg.Of(key));
        card.ShareRequested += async target =>
        {
            CloseDetails();
            vm.SetStatus(await NewSharer().ShareAsync(vm.ResolveMaster(ev), ShareText(), target));
        };
        card.JoinMeetRequested += async () => await OpenHttpsAsync(ev.MeetLink);
        card.LinkRequested += async uri => await OpenDescriptionLinkAsync(uri);
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
        RefreshQueueIndicator();
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

    /// <summary>A link of a description comes from outside the app (an invitation, a feed): the user sees the real address and decides.</summary>
    private async Task OpenDescriptionLinkAsync(Uri uri)
    {
        if (uri.Scheme is not ("http" or "https" or "mailto")) return;
        var address = uri.Scheme == Uri.UriSchemeMailto ? uri.OriginalString : uri.AbsoluteUri;
        var sure = await new ConfirmWindow(L.T("confirm.open_link_title"), L.T("confirm.open_link_text", address), L.T("confirm.open_link_button")).ShowDialog<bool>(this);
        if (sure) await Launcher.LaunchUriAsync(uri);
    }

    private async Task OpenHttpsAsync(string? link)
    {
        // Only web links: the value comes from Google, but it is still data from outside the app.
        if (Uri.TryCreate(link, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps) await Launcher.LaunchUriAsync(uri);
    }

    /// <summary>For tests: replaces what the share button of the card does on the screen and in the system.</summary>
    public static Func<MainWindow, EventSharer>? SharerFactory { get; set; }

    private EventSharer NewSharer() => SharerFactory?.Invoke(this) ?? new EventSharer
    {
        // The system opens the address with the program that handles it (the apps of WhatsApp and Telegram have their own); false when there is none.
        Launch = uri =>
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri.OriginalString) { UseShellExecute = true })?.Dispose();
                return Task.FromResult(true);
            }
            catch (System.ComponentModel.Win32Exception)
            {
                return Task.FromResult(false);
            }
        },
        OpenMailTo = mailto =>
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(mailto) { UseShellExecute = true })?.Dispose();
            return Task.CompletedTask;
        },
        Copy = async text =>
        {
            if (Clipboard is null) throw new InvalidOperationException("clipboard");
            await Avalonia.Input.Platform.ClipboardExtensions.SetTextAsync(Clipboard, text);
        },
        SaveIcs = ExportEventAsync,
        ShowShareSheet = async (ev, text) =>
        {
            if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 10240)) throw new PlatformNotSupportedException("Windows 10");
            await WindowsShareSheet.ShowAsync(TryGetPlatformHandle()?.Handle ?? IntPtr.Zero, ev, text);
        },
    };

    private async Task<bool> ExportEventAsync(CalendarEvent ev)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new Avalonia.Platform.Storage.FilePickerSaveOptions
        {
            Title = L.T("file.export_event_title"),
            SuggestedFileName = SafeFileName(string.IsNullOrWhiteSpace(ev.Title) ? L.T("file.default_event_name") : ev.Title) + ".ics",
            DefaultExtension = "ics",
            FileTypeChoices = [new Avalonia.Platform.Storage.FilePickerFileType(L.T("calendar.ics_type")) { Patterns = ["*.ics"] }],
        });
        if (file is null) return false;
        try
        {
            await using var stream = await file.OpenWriteAsync();
            stream.SetLength(0);
            await using var writer = new System.IO.StreamWriter(stream, new System.Text.UTF8Encoding(false));
            // Shared with someone else: the addresses of the guests do not go in the file (the text of the event has only how many they are).
            await writer.WriteAsync(IcsWriter.Write([ev], includeAttendees: false));
            return true;
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            if (Vm is { } vm) vm.SetStatus("error.export", ex.Message);
            return false;
        }
    }

    // ---------- Search (issue #8) ----------

    private void OpenSearch()
    {
        if (Vm is not { } vm) return;
        vm.Search.IsOpen = true;
        Avalonia.Threading.Dispatcher.UIThread.Post(() => { SearchBox.Focus(); SearchBox.SelectAll(); }, Avalonia.Threading.DispatcherPriority.Input);
    }

    private void OnToggleSearch(object? sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm) return;
        if (vm.Search.IsOpen) vm.Search.Close();
        else OpenSearch();
    }

    private void OnCloseSearch(object? sender, RoutedEventArgs e) => Vm?.Search.Close();

    private void OnToggleAdvancedSearch(object? sender, RoutedEventArgs e)
    {
        if (Vm is { } vm) vm.Search.IsAdvanced = !vm.Search.IsAdvanced;
    }

    private void OnSearchBoxKeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
    {
        if (e.Key != Avalonia.Input.Key.Enter) return;
        Vm?.Search.Submit();
        e.Handled = true;
    }

    /// <summary>A click on a result shows the card of the event beside the line, like a click on an event in the views.</summary>
    private void OnSearchRowTapped(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        if (sender is Control { DataContext: SearchRow row } line) ShowDetails(row.Event, line, null);
    }

    private async void OnSearchRowDoubleTapped(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        if (sender is Control { DataContext: SearchRow row }) await EditAsync(row.Event, isNew: false);
    }

    private void OnCancelAdvancedSearch(object? sender, RoutedEventArgs e) => Vm?.Search.CancelAdvanced();

    private void OnRunSearch(object? sender, RoutedEventArgs e) => Vm?.Search.Submit();

    private void OnSearchScope(object? sender, RoutedEventArgs e)
    {
        if (Vm is { } vm && sender is Button { Tag: string t } && int.TryParse(t, out var scope)) vm.Search.ChooseScope(scope);
        Vm?.Search.Run();
        SearchScopeButton.Flyout?.Hide(); // a preset is a final choice
    }

    /// <summary>"Fatto" in the list of calendars: closes it keeping the ticks.</summary>
    private void OnSearchScopeDone(object? sender, RoutedEventArgs e)
    {
        Vm?.Search.Run();
        SearchScopeButton.Flyout?.Hide();
    }

    private void OnSearchFieldKeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
    {
        if (e.Key != Avalonia.Input.Key.Enter) return;
        Vm?.Search.Submit();
        e.Handled = true;
    }

    private void OnShortcut(object? sender, Avalonia.Input.KeyEventArgs e)
    {
        if (Vm is { } v && e.Key == Avalonia.Input.Key.F && e.KeyModifiers == Avalonia.Input.KeyModifiers.Control) { OpenSearch(); e.Handled = true; return; }
        if (Vm is { } v2 && e.Key == Avalonia.Input.Key.Escape && v2.Search.IsOpen) { v2.Search.Close(); e.Handled = true; return; }
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
        if (Vm is not { } vm || MainViewModel.IsReadOnly(ev)) return;
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
        if (target is null || target.IsSubscription || MainViewModel.IsReadOnly(ev)) return;

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
        if (MainViewModel.IsReadOnly(ev)) return;
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
        if (Vm is null || _editing || MainViewModel.IsReadOnly(ev)) return;
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
        var dialog = new EventEditWindow { DataContext = new EventEditViewModel(ev, vm.Calendars.Where(c => !c.IsSubscription).ToList(), isNew, vm.Settings, _sound) };
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

    private static AccountGroup? AccountOf(object? sender) =>
        sender is Control { DataContext: AccountGroup g }
            ? g
            : (sender as Avalonia.StyledElement)?.Parent is ContextMenu { PlacementTarget.DataContext: AccountGroup p } ? p : null;

    /// <summary>The menu of an account: a new calendar in it. On a Google account it is created on Google; in "Local" it is a calendar of this PC.</summary>
    private async void OnNewCalendarInAccount(object? sender, RoutedEventArgs e)
    {
        if (AccountOf(sender) is not { } group) return;
        if (group.Calendars.Any(c => c.IsGoogle)) await CreateGoogleCalendarAsync(group.AccountName);
        else OnNewLocalCalendar(sender, e);
    }

    /// <summary>
    /// The menu of a Google account: disconnect it. The person is told what goes away from this PC and confirms; then the access is revoked on
    /// Google (best effort) and the account and its calendars are forgotten here. Same question as in the settings.
    /// </summary>
    private async void OnDisconnectAccount(object? sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm || AccountOf(sender) is not { IsGoogle: true } group) return;
        var email = group.AccountName;
        var info = _google.DescribeAccount(email);
        if (!await new ConfirmWindow(AccountDisconnect.Title, AccountDisconnect.Text(email, info), AccountDisconnect.Button).ShowDialog<bool>(this)) return;

        // A sync that is running ends first: the calendars cannot go away while their changes are being sent.
        var revoked = await RunBusyAsync(Msg.Of("busy.account_disconnecting", email), () => _google.RemoveAccountAsync(email, CancellationToken.None));
        await vm.ReloadAsync();
        vm.SetStatus(revoked ? "settings.disconnected_revoked" : "settings.disconnected_not_revoked", email);
        RefreshQueueIndicator();
    }

    /// <summary>"Create" menu: a calendar on one of the Google accounts (the window asks which one when there are several).</summary>
    private async void OnNewGoogleCalendar(object? sender, RoutedEventArgs e) => await CreateGoogleCalendarAsync(null);

    private async Task CreateGoogleCalendarAsync(string? email)
    {
        if (Vm is not { } vm) return;
        var accounts = _google.AccountEmails;
        if (accounts.Count == 0) return;
        var color = MainViewModel.CalendarPalette[vm.Calendars.Count(c => c.IsGoogle) % MainViewModel.CalendarPalette.Length];
        if (await new GoogleCalendarWindow(accounts, email, color).ShowDialog<GoogleCalendarRequest?>(this) is not { } request) return;
        try
        {
            var created = await RunBusyAsync(Msg.Of("busy.gcal_creating"), () => _google.CreateCalendarAsync(request.Email, request.Name, request.ColorHex, CancellationToken.None));
            await vm.ReloadAsync();
            vm.SetStatus("status.gcal_created", created.Name, request.Email);
        }
        catch (GoogleCalendarException ex)
        {
            vm.SetStatus(Msg.Of("error.gcal_create", ex.Reason));
        }
    }

    /// <summary>
    /// "Edit calendar": the name and the colour. A calendar of this PC is changed here; one of Google goes to Google first (online only), then here.
    /// The name can be changed only on a calendar of this PC or a Google calendar the person owns (not the primary one); the colour on any.
    /// </summary>
    private async void OnEditCalendar(object? sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm || CalendarOf(sender) is not { IsSubscription: false } cal) return;
        var dialog = new CalendarEditWindow(cal.Name, cal.ColorHex, cal.CanRename, cal.RenameHint, cal.RenameNote);
        if (await dialog.ShowDialog<CalendarEditRequest?>(this) is not { } request) return;

        if (cal.IsLocal)
        {
            vm.UpdateLocalCalendar(cal.Id, request.Name, request.ColorHex);
            vm.SetStatus("status.calendar_edited", request.Name);
            return;
        }

        try
        {
            var name = cal.CanRename ? request.Name : null;
            var edit = await RunBusyAsync(Msg.Of("busy.gcal_editing"), () => _google.UpdateCalendarAsync(cal.Id, name, request.ColorHex, CancellationToken.None));
            await vm.ReloadAsync();
            vm.SetStatus(edit.NotCompleted is { } problem
                ? Msg.Of("status.gcal_edited_partial", edit.Calendar.Name, Msg.Of("gcal.problem." + problem))
                : Msg.Of("status.calendar_edited", edit.Calendar.Name));
        }
        catch (GoogleCalendarException ex)
        {
            vm.SetStatus(Msg.Of("error.gcal_edit", ex.Reason));
        }
    }

    /// <summary>Deletes a calendar of Google (the item is grey for the primary one and for those the person does not own).</summary>
    private async void OnDeleteGoogleCalendar(object? sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm || CalendarOf(sender) is not { CanDeleteFromGoogle: true } cal) return;
        var events = vm.EventsOf(cal.Id).Count(x => x.SeriesMasterId is null);
        var pending = vm.PendingChangesFor(cal.Id);
        var text = L.N("gcal.delete_text", events, cal.Name, cal.AccountName) + (pending > 0 ? "\n\n" + L.N("gcal.delete_pending", pending) : "");
        if (!await new ConfirmWindow(L.T("gcal.delete_title"), text, L.T("calendar.delete_button")).ShowDialog<bool>(this)) return;
        try
        {
            await RunBusyAsync(Msg.Of("busy.gcal_deleting"), async () =>
            {
                await _google.DeleteCalendarAsync(cal.Id, CancellationToken.None);
                return true;
            });
            await vm.ReloadAsync();
            vm.SetStatus("status.gcal_deleted", cal.Name);
        }
        catch (GoogleCalendarException ex)
        {
            vm.SetStatus(Msg.Of("error.gcal_delete", ex.Reason));
        }
    }

    // ---------- Public calendars (by address) ----------

    private async void OnNewPublicCalendar(object? sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm) return;
        var color = MainViewModel.CalendarPalette[vm.Calendars.Count(c => c.IsSubscription) % MainViewModel.CalendarPalette.Length];
        var added = await new SubscriptionWindow(Subscriptions, color).ShowDialog<SubscriptionEdit?>(this);
        if (added?.Added is not { } calendar) return;
        await vm.ReloadAsync();
        var count = Subscriptions.EventCount(calendar.Id);
        vm.SetStatus(Msg.Count("status.sub_added", count, calendar.Name));
    }

    private async void OnRefreshSubscription(object? sender, RoutedEventArgs e)
    {
        if (CalendarOf(sender) is not { IsSubscription: true } cal) return;
        await RefreshSubscriptionsAsync([cal.Id], force: true);
    }

    /// <summary>Downloads the calendars again and says in the status bar how it went.</summary>
    private async Task RefreshSubscriptionsAsync(string[] ids, bool force)
    {
        if (Vm is not { } vm) return;
        vm.SetStatus("status.sub_refreshing");
        foreach (var id in ids)
        {
            var name = vm.Calendars.FirstOrDefault(c => c.Id == id)?.Name ?? "";
            var result = await Subscriptions.RefreshAsync(id, force);
            await vm.ReloadAsync();
            if (result.Outcome == RefreshOutcome.Failed)
                vm.SetStatus(Msg.Of("error.sub_refresh", name, SubscriptionMessages.Error(result.Problem ?? SubscriptionProblem.Unreadable)));
            else if (result.Changes is { Any: true } c)
                vm.SetStatus("status.sub_updated", name, c.Added, c.Changed, c.Removed);
            else
                vm.SetStatus("status.sub_unchanged", name);
        }
    }

    private async void OnEditSubscription(object? sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm || CalendarOf(sender) is not { IsSubscription: true } cal) return;
        var info = new CalendarInfo(cal.Id, cal.AccountName, cal.Name, cal.ColorHex, cal.IsVisible);
        if (await new SubscriptionWindow(Subscriptions, cal.ColorHex, info, cal.Subscription).ShowDialog<SubscriptionEdit?>(this) is not { } edit) return;
        var refreshed = await Subscriptions.UpdateAsync(cal.Id, edit.Name, edit.ColorHex, edit.Range);
        await vm.ReloadAsync();
        if (refreshed?.Outcome == RefreshOutcome.Failed)
            vm.SetStatus(Msg.Of("error.sub_refresh", edit.Name, SubscriptionMessages.Error(refreshed.Problem ?? SubscriptionProblem.Unreadable)));
    }

    private async void OnShowSubscriptionUrl(object? sender, RoutedEventArgs e)
    {
        if (CalendarOf(sender) is not { IsSubscription: true } cal || Subscriptions.GetUrl(cal.Id) is not { Length: > 0 } url) return;
        await new SubscriptionUrlWindow(url).ShowDialog(this);
    }

    private async void OnRemoveSubscription(object? sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm || CalendarOf(sender) is not { IsSubscription: true } cal) return;
        if (!await new ConfirmWindow(L.T("sub.remove_title"), L.T("sub.remove_text", cal.Name), L.T("common.remove")).ShowDialog<bool>(this)) return;
        Subscriptions.Remove(cal.Id);
        await vm.ReloadAsync();
        vm.SetStatus("status.sub_removed", cal.Name);
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
        if (Services.GoogleSyncService.IsGoogleCalendar(cal.Id)) return; // the menu item of a Google calendar is another one (OnDeleteGoogleCalendar)

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
        var dialog = new SettingsWindow { DataContext = new SettingsViewModel(vm.Settings, _sound, _google, Subscriptions) };
        var result = await dialog.ShowDialog<AppSettings?>(this);
        var accountsChanged = (dialog.DataContext as SettingsViewModel)?.AccountsChanged == true;
        if ((dialog.DataContext as SettingsViewModel)?.SubscriptionsChanged == true) await vm.ReloadAsync();
        if (result is null)
        {
            if (accountsChanged) { await vm.ReloadAsync(); RefreshQueueIndicator(); await SyncAsync(); }
            return;
        }
        result.ShowWeekends = vm.Settings.ShowWeekends;
        result.DefaultView = vm.Settings.DefaultView;
        result.SearchScope = vm.Settings.SearchScope;
        result.SearchCalendarIds = vm.Settings.SearchCalendarIds;
        // What the settings window does not show stays as it was: the splash already shown and the version of the sync data.
        result.SplashVersion = vm.Settings.SplashVersion;
        result.SyncSchema = vm.Settings.SyncSchema;
        _store.Save(result);
        vm.Settings = result;
        L.Apply(result.Language);
        ApplyTheme(result);
        ApplyFont(result);
        ConfigureGoogle();
        ApplySyncInterval();
        if (accountsChanged) await vm.ReloadAsync();
        RefreshQueueIndicator();
        if (result.SyncIntervalMinutes > 0 || accountsChanged) _ = SyncAsync();
        if (_scheduler is not null) _scheduler.Enabled = result.NotificationsEnabled;
        ScrollToDayStart();
    }
}
