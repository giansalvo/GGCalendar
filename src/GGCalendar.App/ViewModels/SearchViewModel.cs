using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using GGCalendar.Core.Localization;
using GGCalendar.Core.Models;
using GGCalendar.Core.Search;

namespace GGCalendar.App.ViewModels;

/// <summary>A calendar in the list of the calendars to look in.</summary>
public sealed partial class SearchCalendarOption(CalendarItemViewModel calendar, Action changed) : ObservableObject
{
    public string Id { get; } = calendar.Id;
    public string Name => calendar.Name;
    public string ColorHex { get; } = calendar.ColorHex;

    /// <summary>The calendar is shown now (ticked in the list on the left).</summary>
    public bool IsActive => calendar.IsVisible;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    partial void OnIsSelectedChanged(bool value) => changed();

    public void LanguageChanged() => OnPropertyChanged(nameof(Name));
}

/// <summary>One line of the results.</summary>
public sealed record SearchRow(CalendarEvent Event, string Time, string Title, string ColorHex, string CalendarName, string Tags,
    string Snippet, string Note, string? SecondTime, bool IsPast)
{
    public bool HasSnippet => Snippet.Length > 0;
    public bool HasNote => Note.Length > 0;
    public bool HasSecondTime => SecondTime is not null;
    public double RowOpacity => IsPast ? 0.55 : 1;
}

/// <summary>The lines of one day.</summary>
public sealed record SearchGroup(DateTime Day, string DayNumber, string MonthWeekday, bool IsToday, IReadOnlyList<SearchRow> Rows);

/// <summary>
/// The search of events (issue #8): the box and the detailed panel, the calendars to look in (the choice is kept in the settings),
/// and the lines found, day by day. The search itself is <see cref="EventSearch"/>.
/// </summary>
public sealed partial class SearchViewModel : ObservableObject
{
    /// <summary>How long after the last key the simple box searches (a search of many events must not run on every key).</summary>
    public static TimeSpan TypingDelay { get; set; } = TimeSpan.FromMilliseconds(150);

    private readonly MainViewModel _main;
    private bool _updating;
    private int _typing;

    public SearchViewModel(MainViewModel main) => _main = main;

    public ObservableCollection<SearchCalendarOption> Calendars { get; } = [];

    /// <summary>Raised when the choice of the calendars changes, so that the host keeps it in the settings.</summary>
    public event Action? ScopeChanged;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowResults))]
    public partial bool IsOpen { get; set; }

    /// <summary>There is something to look for (a search was made or is being made): only then the results take the place of the view.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowResults))]
    public partial bool HasQuery { get; set; }

    /// <summary>The results are shown in place of the view. Until then (the box is open but empty, the panel is being filled in) the view stays where it was.</summary>
    public bool ShowResults => IsOpen && HasQuery;

    /// <summary>The panel with the detailed boxes (the arrow of the search box).</summary>
    [ObservableProperty]
    public partial bool IsAdvanced { get; set; }

    [ObservableProperty]
    public partial string Text { get; set; } = "";

    [ObservableProperty]
    public partial string WhatText { get; set; } = "";

    [ObservableProperty]
    public partial string WhoText { get; set; } = "";

    [ObservableProperty]
    public partial string WhereText { get; set; } = "";

    [ObservableProperty]
    public partial string NotText { get; set; } = "";

    /// <summary>The first day: the first one the view shows, unless the person chooses another one (it is already in the box).</summary>
    [ObservableProperty]
    public partial DateTime? FromDate { get; set; }

    private DateTime _defaultFrom = DateTime.Today;

    partial void OnIsOpenChanged(bool value)
    {
        if (!value) return;
        _defaultFrom = _main.FirstShownDay();
        Reparse();
    }

    [ObservableProperty]
    public partial DateTime? ToDate { get; set; }

    /// <summary>0 = the calendars that are shown, 1 = all of them, 2 = the ones ticked in the list.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveTick), nameof(AllTick), nameof(ScopeLabel))]
    public partial int Scope { get; set; }

    public bool ActiveTick => Scope == 0;
    public bool AllTick => Scope == 1;

    public string ScopeLabel => Scope switch
    {
        0 => L.T("search.scope_active"),
        1 => L.T("search.scope_all"),
        _ => Calendars.Count(o => o.IsSelected) switch
        {
            0 => L.T("search.scope_none"),
            1 => Calendars.First(o => o.IsSelected).Name,
            var n => L.T("search.scope_many", n),
        },
    };

    [ObservableProperty]
    public partial IReadOnlyList<SearchGroup> Groups { get; set; } = [];

    /// <summary>"4 events found".</summary>
    [ObservableProperty]
    public partial string Summary { get; set; } = "";

    /// <summary>What is said in place of the lines: the hint, "nothing found", the dates that do not fit.</summary>
    [ObservableProperty]
    public partial string Message { get; set; } = "";

    [ObservableProperty]
    public partial bool ShowMessage { get; set; } = true;

    /// <summary>"Only the first 1000 lines are shown".</summary>
    [ObservableProperty]
    public partial string TruncatedNote { get; set; } = "";

    [ObservableProperty]
    public partial bool IsTruncated { get; set; }

    private SearchResult _result = SearchResult.Empty;

    /// <summary>The result of the last search (for the tests).</summary>
    public SearchResult Result => _result;

    // ---- calendars ----

    /// <summary>The calendars changed (loaded, added, removed): the list is made again and the choice kept for the ones that remain.</summary>
    public void SetCalendars(IReadOnlyList<CalendarItemViewModel> calendars)
    {
        var chosen = Calendars.Where(o => o.IsSelected).Select(o => o.Id).ToHashSet();
        _updating = true;
        Calendars.Clear();
        foreach (var c in calendars) Calendars.Add(new SearchCalendarOption(c, OptionTicked) { IsSelected = Scope == 1 || chosen.Contains(c.Id) });
        _updating = false;
        OnPropertyChanged(nameof(ScopeLabel));
        Refresh();
    }

    private void OptionTicked()
    {
        if (_updating) return;
        Scope = 2; // ticking a calendar means "these ones"
        OnPropertyChanged(nameof(ScopeLabel));
        ScopeChanged?.Invoke();
        Refresh();
    }

    /// <summary>"Active calendars" or "All calendars": a choice that is complete.</summary>
    public void ChooseScope(int scope)
    {
        _updating = true;
        Scope = scope;
        foreach (var o in Calendars) o.IsSelected = scope == 1 || (scope == 0 && o.IsActive);
        _updating = false;
        ScopeChanged?.Invoke();
        Refresh();
    }

    /// <summary>Reads the choice kept in the settings; calendars that do not exist any more are dropped.</summary>
    public void ApplySettings(AppSettings settings)
    {
        var known = Calendars.Select(o => o.Id).ToHashSet();
        var ids = (settings.SearchCalendarIds ?? []).Where(known.Contains).ToHashSet();
        var scope = Math.Clamp(settings.SearchScope, 0, 2);
        if (scope == 2 && ids.Count == 0) scope = 0;
        _updating = true;
        Scope = scope;
        foreach (var o in Calendars) o.IsSelected = scope == 1 || (scope == 2 && ids.Contains(o.Id)) || (scope == 0 && o.IsActive);
        _updating = false;
        OnPropertyChanged(nameof(ScopeLabel));
        Refresh();
    }

    public void WriteSettings(AppSettings settings)
    {
        settings.SearchScope = Scope;
        settings.SearchCalendarIds = Scope == 2 ? Calendars.Where(o => o.IsSelected).Select(o => o.Id).ToList() : [];
    }

    // ---- open, close ----

    public void Close()
    {
        IsOpen = false;
        IsAdvanced = false;
    }

    /// <summary>Ends the search and empties the box: the view is shown again (used when the person moves through the days).</summary>
    public void CloseAndClear()
    {
        Close();
        Text = "";
    }

    /// <summary>"Search" (or Enter) in the detailed panel: searches and folds the panel, so that the results are in view.</summary>
    public void Submit()
    {
        // The line says what the panel says: the first day only when it is not the one of the view.
        var from = FromDate?.Date == _defaultFrom.Date ? null : FromDate;
        Text = SearchSyntax.Merge(Text, WhatText, WhoText, WhereText, NotText, from, ToDate);
        Run();
        IsAdvanced = false;
    }

    /// <summary>"Cancel" in the detailed panel: empties its boxes and folds it.</summary>
    public void CancelAdvanced()
    {
        ShowLineInPanel(); // what was typed in the panel is dropped: the boxes show the line again
        IsAdvanced = false;
    }

    /// <summary>
    /// The line of the box is what is searched: plain words and keywords (<see cref="SearchSyntax"/>). The boxes of the detailed panel
    /// show what the line says; "Search" in the panel writes them back into the line.
    /// </summary>
    private SearchQuery _parsed = new();

    partial void OnTextChanged(string value)
    {
        Reparse();
        _ = SearchAfterTypingAsync();
    }

    private void Reparse()
    {
        _parsed = SearchSyntax.Parse(Text);
        ShowLineInPanel();
    }

    /// <summary>The boxes of the panel take what the line says (the first day is the one of the view unless the line has <c>from:</c>).</summary>
    private void ShowLineInPanel()
    {
        WhatText = _parsed.What;
        WhoText = _parsed.Who;
        WhereText = _parsed.Where;
        NotText = _parsed.Not;
        FromDate = _parsed.From ?? _defaultFrom;
        ToDate = _parsed.To;
    }

    private async Task SearchAfterTypingAsync()
    {
        var mine = ++_typing;
        if (TypingDelay > TimeSpan.Zero) await Task.Delay(TypingDelay);
        if (mine == _typing) Run();
    }

    // ---- search ----

    public SearchQuery Query()
    {
        IReadOnlySet<string>? ids = Scope switch
        {
            0 => Calendars.Where(o => o.IsActive).Select(o => o.Id).ToHashSet(),
            1 => null,
            _ => Calendars.Where(o => o.IsSelected).Select(o => o.Id).ToHashSet(),
        };
        return _parsed with { From = _parsed.From ?? _defaultFrom, CalendarIds = ids };
    }

    /// <summary>Searches again when the search is open (events or calendars changed, or the language did).</summary>
    public void Refresh()
    {
        if (IsOpen) Run();
    }

    public void Run()
    {
        _typing++; // a search made now makes a pending one useless
        var q = Query();
        if (q.From is { } from && q.To is { } to && from.Date > to.Date)
        {
            HasQuery = true;
            Show(SearchResult.Empty, L.T("search.range_invalid"));
            return;
        }

        // The first day of the view is not something to look for by itself: only a day written in the line (from:) is.
        if (!EventSearch.HasCriteria(q with { From = _parsed.From }))
        {
            HasQuery = false;
            Show(SearchResult.Empty, L.T("search.hint"));
            return;
        }

        HasQuery = true;
        var result = EventSearch.Search(_main.AllEvents, q, DateTime.Today);
        Show(result, result.Hits.Count == 0 ? L.T("search.empty") : "");
    }

    private void Show(SearchResult result, string message)
    {
        _result = result;
        var culture = L.Culture;
        var today = DateTime.Today;
        Groups = result.Hits.GroupBy(h => h.Day).Select(g => new SearchGroup(
            g.Key, g.Key.Day.ToString(culture),
            g.Key.ToString("MMM yyyy", culture).ToUpper(culture).Replace(".", "") + ", " + g.Key.ToString("ddd", culture).ToUpper(culture).Replace(".", ""),
            g.Key == today, g.Select(Row).ToList())).ToList();
        Message = message;
        ShowMessage = message.Length > 0;
        Summary = result.Hits.Count == 0 ? "" : L.N("search.count", result.EventCount);
        IsTruncated = result.Truncated;
        TruncatedNote = result.Truncated ? L.T("search.truncated", EventSearch.MaxRows) : "";
    }

    private SearchRow Row(SearchHit h)
    {
        var e = h.Event;
        var fmt = _main.Settings.Use24HourClock ? "HH:mm" : "h:mm tt";
        var culture = L.Culture;
        string time;
        if (e.IsAllDay) time = L.T("common.all_day");
        else if (h.DayCount == 1) time = $"{e.Start.ToString(fmt, culture)} – {e.End.ToString(fmt, culture)}";
        else if (h.IsFirstDay) time = L.T("search.from_time", e.Start.ToString(fmt, culture));
        else if (h.IsLastDay) time = L.T("search.until_time", e.End.ToString(fmt, culture));
        else time = L.T("common.all_day");

        var note = h.DayCount > 1 ? L.T("search.day_of", h.DayNumber, h.DayCount) : "";
        if (!string.IsNullOrEmpty(e.Recurrence) && Core.Recurrence.RecurrenceRule.Parse(e.Recurrence) is { } rule)
            note = note.Length > 0 ? note + " · " + rule.Describe() : rule.Describe();

        var calendar = _main.Calendars.FirstOrDefault(c => c.Id == e.CalendarId);
        var tags = string.Join(" · ", h.Fields.Select(f => L.T(f switch
        {
            SearchField.Title => "search.field_title",
            SearchField.Description => "search.field_description",
            SearchField.Location => "search.field_location",
            SearchField.Attachments => "search.field_attachments",
            _ => "search.field_guests",
        })));
        return new SearchRow(e, time, string.IsNullOrWhiteSpace(e.Title) ? L.T("common.untitled") : e.Title, e.ColorHex ?? calendar?.ColorHex ?? "#1A73E8",
            calendar?.Name ?? "", tags, h.Snippet, note, h.DayCount == 1 ? _main.SecondZoneText(e) : null, h.Day < today());
    }

    private static DateTime today() => DateTime.Today;

    /// <summary>The language changed: the texts of the lines, the names of the days and the label of the calendars are made again.</summary>
    public void LanguageChanged()
    {
        foreach (var o in Calendars) o.LanguageChanged();
        OnPropertyChanged(nameof(ScopeLabel));
        Refresh();
    }
}
