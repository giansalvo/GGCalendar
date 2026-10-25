using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GGCalendar.Core.Models;
using GGCalendar.Core.Recurrence;
using GGCalendar.Data;
using GGCalendar.Core.Localization;

namespace GGCalendar.App.ViewModels;

public sealed partial class CalendarItemViewModel(CalendarInfo info, Action changed) : ObservableObject
{
    public string Id { get; } = info.Id;
    /// <summary>The account's own calendar (on Google its id is the account email): shown first, as "Il mio calendario" like the Android app.</summary>
    public bool IsPrimary { get; } = info.Id.EndsWith("|" + info.AccountName, StringComparison.OrdinalIgnoreCase);
    public string Name => IsPrimary ? L.T("main.my_calendar") : info.Name;

    /// <summary>The language changed: the name of the account's own calendar is shown again.</summary>
    public void LanguageChanged() => OnPropertyChanged(nameof(Name));
    public string ColorHex { get; } = info.ColorHex;
    public string AccountName { get; } = info.AccountName;

    /// <summary>The account as shown to the user: "Local" (in the language shown) for the calendars of this PC.</summary>
    public string DisplayAccountName => IsLocal ? L.T("main.local_account") : info.AccountName;

    /// <summary>Calendars that live only on this PC; Google calendars are removed by disconnecting their account.</summary>
    public bool IsLocal { get; } = !Services.GoogleSyncService.IsGoogleCalendar(info.Id);

    public bool IsGoogle => !IsLocal;

    [ObservableProperty]
    public partial bool IsVisible { get; set; } = info.IsVisible;

    partial void OnIsVisibleChanged(bool value) => changed();

    [ObservableProperty]
    public partial bool IsDropTarget { get; set; }
}

public sealed partial class AccountGroup(string accountName, IReadOnlyList<CalendarItemViewModel> calendars) : ObservableObject
{
    public string AccountName { get; } = accountName;
    public IReadOnlyList<CalendarItemViewModel> Calendars { get; } = calendars;

    /// <summary>What is shown as the name of the account: its address, or "Local" for the calendars of this PC.</summary>
    public string DisplayName => Calendars.All(c => c.IsLocal) ? L.T("main.local_account") : AccountName;

    /// <summary>First letter of the account, drawn in the round avatar.</summary>
    public string Initial => DisplayName.Length > 0 ? char.ToUpperInvariant(DisplayName[0]).ToString() : "?";

    public string Kind => Calendars.Any(c => !c.IsLocal) ? L.T("main.kind_google") : L.T("main.kind_local");

    /// <summary>The language changed: the kind of account is shown again.</summary>
    public void LanguageChanged()
    {
        OnPropertyChanged(nameof(Kind));
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(Initial));
    }

    /// <summary>False when the user folded the calendars of this account away (the arrow beside its name).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Chevron))]
    public partial bool IsExpanded { get; set; } = true;

    /// <summary>Segoe Fluent Icons: chevron up when open, down when folded.</summary>
    public string Chevron => IsExpanded ? "" : "";
}

public sealed record AgendaItem(CalendarEvent Event, string Time, string Title, string ColorHex);

public sealed record AgendaDay(string Label, bool IsToday, IReadOnlyList<AgendaItem> Items);

public sealed record DayHeader(string Label, int Number, bool IsToday);

public enum SeriesScope { This, ThisAndFollowing, All }

public enum ImportDuplicates { Skip, Replace, Copy }

public sealed partial class MainViewModel : ViewModelBase
{
    private static CultureInfo Culture => L.Culture;

    /// <summary>The name the local calendars have as account in the database: it is a key, not a text to show (see <see cref="AccountGroup.DisplayName"/>).</summary>
    public const string LocalAccountName = "Locale";
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
        // Weak: the language is a global, and a view model that nobody uses any more must not stay alive (nor be refreshed) because of it.
        var weak = new WeakReference<MainViewModel>(this);
        Action? handler = null;
        handler = () =>
        {
            if (weak.TryGetTarget(out var viewModel)) viewModel.OnLanguageChanged();
            else L.Changed -= handler;
        };
        L.Changed += handler;
    }

    /// <summary>Shows again, in the new language, everything the view model composes: titles, day names, names of calendars, the status message.</summary>
    private void OnLanguageChanged()
    {
        if (!Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(OnLanguageChanged);
            return;
        }

        foreach (var calendar in _calendars) calendar.LanguageChanged();
        foreach (var account in Accounts) account.LanguageChanged();
        OnPropertyChanged(nameof(ViewModeName));
        OnPropertyChanged(nameof(WeekLabel));
        OnPropertyChanged(nameof(SyncStatus));
        Refresh();
    }

    // Bumped by every local change, so a background reload can tell that what it read is already stale.
    private int _changeCount;

    /// <summary>Raised when events or calendars change (not on navigation), e.g. to refresh cached reminders.</summary>
    public event Action? EventsChanged;

    private void Changed()
    {
        _changeCount++;
        EventsChanged?.Invoke();
    }

    /// <summary>Re-reads calendars and events from the store (after a sync or when accounts change).</summary>
    public void Reload() => Apply(Load());

    /// <summary>Like <see cref="Reload"/>, but reads the store off the UI thread; retried if the user edits meanwhile.</summary>
    public async Task ReloadAsync()
    {
        while (true)
        {
            var before = _changeCount;
            var data = await Task.Run(Load);
            if (before != _changeCount) continue;
            Apply(data);
            return;
        }
    }

    private (IReadOnlyList<CalendarInfo> Calendars, IReadOnlyList<CalendarEvent> Events) Load()
    {
        var infos = _store.GetCalendars();
        if (infos.Count == 0)
        {
            // First run: a local calendar so the app is usable before any account is connected.
            var local = new CalendarInfo("local-personale", LocalAccountName, L.T("main.default_calendar"), "#1A73E8");
            _store.UpsertCalendar(local);
            infos = [local];
        }

        return (infos, _store.GetAllEvents());
    }

    private void Apply((IReadOnlyList<CalendarInfo> Calendars, IReadOnlyList<CalendarEvent> Events) data)
    {
        var (infos, events) = data;
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
                    _changeCount++;
                    Refresh();
                });
                return item;
            }).ToList();
            // The account's own calendar first, then the others by name.
            items = items.OrderByDescending(i => i.IsPrimary).ThenBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            _calendars.AddRange(items);
            Accounts.Add(new AccountGroup(g.Key, items));
        }

        _allEvents.Clear();
        _allEvents.AddRange(events);
        EventsChanged?.Invoke();
        if (SelectedDate is not null) Refresh();
    }

    public ObservableCollection<AccountGroup> Accounts { get; } = [];
    public IReadOnlyList<CalendarItemViewModel> Calendars => _calendars;
    public IReadOnlyList<CalendarEvent> AllEvents => _allEvents;

    [ObservableProperty]
    public partial AppSettings Settings { get; set; } = new();

    /// <summary>0 = giorno, 1 = settimana, 2 = mese, 3 = anno, 4 = programma, 5 = 6 giorni.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTimeGrid), nameof(IsMonth), nameof(IsYear), nameof(IsAgenda), nameof(ViewModeName), nameof(ShowAgendaEmpty))]
    public partial int ViewModeIndex { get; set; } = 1;

    private static readonly string[] ViewModeKeys = ["main.view_day", "main.view_week", "main.view_month", "main.view_year", "main.view_agenda", "main.view_6days"];

    public string ViewModeName => L.T(ViewModeKeys[ViewModeIndex]);
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
    [NotifyPropertyChangedFor(nameof(ShowAgendaEmpty))]
    public partial IReadOnlyList<AgendaDay> AgendaDays { get; set; } = [];

    /// <summary>The "no events in the next 30 days" message of the schedule view: shown only when there is nothing to list.</summary>
    public bool ShowAgendaEmpty => IsAgenda && AgendaDays.Count == 0;

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
    [NotifyPropertyChangedFor(nameof(WeekLabel))]
    public partial int WeekNumber { get; set; }

    /// <summary>"Week 43", in the language shown now.</summary>
    public string WeekLabel => L.T("main.week_number", WeekNumber);

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

    /// <summary>The message in the status bar, kept as key and arguments so that it can be shown again in another language (and checked by tests).</summary>
    public Msg? Status { get; private set; }

    /// <summary>The status message as text in the language shown now.</summary>
    public string SyncStatus => Status?.Text ?? "";

    /// <summary>An error message: its key starts with "error.".</summary>
    public bool SyncIsError => Status is { } m && m.Key.StartsWith("error.", StringComparison.Ordinal);

    public void SetStatus(Msg? message)
    {
        Status = message;
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(SyncStatus));
        OnPropertyChanged(nameof(SyncIsError));
    }

    public void SetStatus(string key, params object?[] args) => SetStatus(Msg.Of(key, args));

    [ObservableProperty]
    public partial bool IsSyncing { get; set; }

    /// <summary>Events of <paramref name="incoming"/> that already exist in the calendar (same file UID, or same title and times).</summary>
    public IReadOnlyList<(CalendarEvent Incoming, CalendarEvent Existing)> FindExisting(string calendarId, IReadOnlyList<CalendarEvent> incoming)
    {
        var current = _allEvents.Where(e => e.CalendarId == calendarId && e.SeriesMasterId is null).ToList();
        var byUid = current.Where(e => e.IcsUid is not null).GroupBy(e => e.IcsUid!).ToDictionary(g => g.Key, g => g.First());
        var bySignature = current.GroupBy(Signature).ToDictionary(g => g.Key, g => g.First());
        var found = new List<(CalendarEvent, CalendarEvent)>();
        foreach (var ev in incoming.Where(e => e.SeriesMasterId is null))
        {
            if (ev.IcsUid is not null && byUid.TryGetValue(ev.IcsUid, out var match)) found.Add((ev, match));
            else if (bySignature.TryGetValue(Signature(ev), out match)) found.Add((ev, match));
        }

        return found;
    }

    private static string Signature(CalendarEvent e) => $"{e.Title.Trim().ToLowerInvariant()}|{e.Start:O}|{e.End:O}";

    /// <summary>
    /// Adds the events to the calendar. Events already present are skipped, replaced by the imported version,
    /// or imported again as copies, depending on <paramref name="onExisting"/>. Returns (imported, skipped).
    /// </summary>
    public (int Imported, int Skipped) ImportEvents(string calendarId, IReadOnlyList<CalendarEvent> events, ImportDuplicates onExisting)
    {
        var plan = PlanImport(calendarId, events, onExisting);
        _store.ApplyImport(plan.Removed, plan.Added, IsRemote(calendarId));
        FinishImport(calendarId, plan);
        return (plan.Imported, plan.Skipped);
    }

    /// <summary>Like <see cref="ImportEvents"/>, but the events are written to the store off the UI thread (one transaction), so the window keeps answering.</summary>
    public async Task<(int Imported, int Skipped)> ImportEventsAsync(string calendarId, IReadOnlyList<CalendarEvent> events, ImportDuplicates onExisting)
    {
        var plan = PlanImport(calendarId, events, onExisting);
        var remote = IsRemote(calendarId);
        _changeCount++; // a background reload that started before the import must not put the old list back
        await Task.Run(() => _store.ApplyImport(plan.Removed, plan.Added, remote));
        FinishImport(calendarId, plan);
        return (plan.Imported, plan.Skipped);
    }

    private sealed record ImportPlan(List<CalendarEvent> Removed, List<CalendarEvent> Added, int Imported, int Skipped);

    /// <summary>Decides what the import adds and what it replaces, without touching anything.</summary>
    private ImportPlan PlanImport(string calendarId, IReadOnlyList<CalendarEvent> events, ImportDuplicates onExisting)
    {
        var existing = FindExisting(calendarId, events).ToDictionary(p => p.Incoming.Id, p => p.Existing);
        var skippedMasters = new HashSet<string>();
        var removed = new List<CalendarEvent>();
        var added = new List<CalendarEvent>();
        var imported = 0;
        var skipped = 0;
        foreach (var ev in events)
        {
            if (ev.SeriesMasterId is { } masterId)
            {
                if (skippedMasters.Contains(masterId)) continue;
            }
            else if (existing.TryGetValue(ev.Id, out var old) && onExisting != ImportDuplicates.Copy)
            {
                if (onExisting == ImportDuplicates.Skip)
                {
                    skippedMasters.Add(ev.Id);
                    skipped++;
                    continue;
                }

                if (!removed.Any(r => r.Id == old.Id)) removed.Add(old);
            }

            added.Add(ev);
            if (ev.SeriesMasterId is null) imported++;
        }

        return new ImportPlan(removed, added, imported, skipped);
    }

    /// <summary>Brings the list in memory in line with what the store now holds, and refreshes the view once.</summary>
    private void FinishImport(string calendarId, ImportPlan plan)
    {
        var gone = plan.Removed.Select(r => r.Id).ToHashSet();
        var children = _allEvents.Where(e => e.SeriesMasterId is { } m && gone.Contains(m)).Select(e => e.Id);
        gone.UnionWith(children);
        gone.UnionWith(plan.Added.Select(a => a.Id)); // an import that finished while a reload ran must not leave a second copy
        _allEvents.RemoveAll(e => gone.Contains(e.Id));
        _allEvents.AddRange(plan.Added);
        Changed();
        if (IsRemote(calendarId) && (plan.Added.Count > 0 || plan.Removed.Count > 0)) ChangeQueued?.Invoke();
        Refresh();
    }

    /// <summary>How many events are waiting in the queue to be created on Google.</summary>
    public int CountUnsentCreations() => _store.CountUnsentCreations();

    /// <summary>Throws away the events not sent to Google yet, from the queue and from this PC; returns how many.</summary>
    public async Task<int> DiscardUnsentCreationsAsync()
    {
        _changeCount++;
        var count = await Task.Run(_store.DiscardUnsentCreations);
        await ReloadAsync();
        return count;
    }

    public IReadOnlyList<CalendarEvent> EventsOf(string calendarId) => _allEvents.Where(e => e.CalendarId == calendarId).ToList();

    private static readonly string[] CalendarPalette = ["#1A73E8", "#0B8043", "#E67C73", "#8E24AA", "#F6BF26", "#039BE5", "#D50000", "#616161"];

    /// <summary>Creates a calendar that lives only on this PC (not synced).</summary>
    public string CreateLocalCalendar(string name)
    {
        var id = "local-" + Guid.NewGuid().ToString("N")[..8];
        var color = CalendarPalette[_calendars.Count % CalendarPalette.Length];
        _store.UpsertCalendar(new CalendarInfo(id, LocalAccountName, name, color));
        Reload();
        return id;
    }

    public void DeleteLocalCalendar(string calendarId)
    {
        if (IsRemote(calendarId)) return;
        _store.DeleteCalendar(calendarId);
        Reload();
    }

    public void AddEvent(CalendarEvent ev)
    {
        _allEvents.Add(ev);
        _store.UpsertEvent(ev);
        Changed();
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
        Changed();

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
            Queue(() => _store.Enqueue(ev.Id, ev.CalendarId, OutboxOperation.Update, ev, old));
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
        // Prefer a Google account's primary calendar (its remote id is the account email), so new events sync by default;
        // the local calendar sorts first ("Locale") but is only the fallback.
        var visible = _calendars.Where(c => c.IsVisible).ToList();
        var calendar = visible.FirstOrDefault(c => IsRemote(c.Id) && c.Id == Services.GoogleSyncService.LocalCalendarId(c.AccountName, c.AccountName))
            ?? visible.FirstOrDefault(c => IsRemote(c.Id))
            ?? visible.FirstOrDefault()
            ?? _calendars.First();
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

        // Callers ask for the scope first (see MoveOccurrence); a single occurrence is the safe default.
        SaveOccurrence(ev, ev with { Start = start, End = end }, SeriesScope.This);
    }

    /// <summary>Drags one occurrence of a series: only it, it and the following ones, or the whole series.</summary>
    public void MoveOccurrence(CalendarEvent occurrence, DateTime start, DateTime end, SeriesScope scope)
    {
        var master = ResolveMaster(occurrence);
        var delta = start - occurrence.Start;
        var dayShift = (start.Date - occurrence.Start.Date).Days;
        var rule = master.Recurrence;
        // Moving a weekly "on these days" series to another weekday shifts the days too.
        if (scope != SeriesScope.This && dayShift != 0
            && RecurrenceRule.Parse(rule) is { Frequency: RecurrenceFrequency.Weekly, Days.Count: > 0 } parsed)
            rule = (parsed with { Days = parsed.Days.Select(d => (DayOfWeek)((((int)d + dayShift) % 7 + 7) % 7)).ToList() }).ToRrule(master.IsAllDay);

        switch (scope)
        {
            case SeriesScope.This:
                SaveOccurrence(occurrence, occurrence with { Start = start, End = end }, scope);
                break;
            case SeriesScope.All:
                var newStart = master.Start + delta;
                SaveOccurrence(occurrence, master with { Start = newStart, End = newStart + (end - start), Recurrence = rule }, scope);
                break;
            default:
                SaveOccurrence(occurrence, occurrence with { Start = start, End = end, Recurrence = rule }, scope);
                break;
        }
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
            // Moving the series moves every slot: exclusions and replaced-occurrence markers must follow,
            // otherwise the original slots of deleted/modified occurrences show up again.
            var shift = edited.Start - master.Start;
            if (shift != TimeSpan.Zero)
            {
                edited = edited with { ExcludedStarts = edited.ExcludedStarts.Select(d => d + shift).ToList() };
                foreach (var child in _allEvents.Where(e => e.SeriesMasterId == master.Id && e.OriginalStart is not null).ToList())
                {
                    var moved = child with { OriginalStart = child.OriginalStart + shift };
                    _allEvents[_allEvents.FindIndex(e => e.Id == child.Id)] = moved;
                    _store.UpsertEvent(moved);
                }
            }

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
        if (newRule is not null && occurrence.OccurrenceIndex is { } index
            && RecurrenceRule.Parse(newRule)?.Count is not null
            && RecurrenceRule.Parse(master.Recurrence)?.Count is { } total)
            newRule = RecurrenceRule.WithCount(newRule, Math.Max(1, total - index));

        var last = master.IsAllDay ? occurrence.Start.AddDays(-1) : occurrence.Start.Date.AddSeconds(-1);
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

        var last = master.IsAllDay ? occurrence.Start.AddDays(-1) : occurrence.Start.Date.AddSeconds(-1);
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
        Changed();
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
                    e.IsAllDay ? L.T("common.all_day") : $"{e.Start.ToString(fmt, Culture)} – {e.End.ToString(fmt, Culture)}",
                    string.IsNullOrWhiteSpace(e.Title) ? L.T("common.untitled") : e.Title,
                    e.ColorHex ?? (CalendarColors.TryGetValue(e.CalendarId, out var c) ? c : "#1A73E8")))
                .ToList();
            if (items.Count > 0)
                groups.Add(new AgendaDay(Culture.TextInfo.ToTitleCase(d.ToString("dddd d MMMM", Culture)), d.Date == DateTime.Today, items));
        }

        AgendaDays = groups;
    }
}

