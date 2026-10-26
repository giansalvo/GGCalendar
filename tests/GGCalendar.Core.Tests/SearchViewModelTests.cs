using GGCalendar.App.ViewModels;
using GGCalendar.Core.Localization;
using GGCalendar.Core.Models;
using GGCalendar.Core.Search;
using GGCalendar.Data;

namespace GGCalendar.Core.Tests;

/// <summary>
/// The view model of the search (issue #8) without a window: the calendars to look in (also the hidden ones), the limit of lines, the
/// messages and what is kept in the settings. They change the language of the process, so they run one at a time with the tests of the app.
/// </summary>
[Collection(Support.AppDriver.CollectionName)]
public class SearchViewModelTests
{
    private static readonly DateTime Day = DateTime.Today.AddDays(3);

    private static (MainViewModel Vm, CalendarStore Store) Rig(params (string Id, string Name)[] calendars)
    {
        SearchViewModel.TypingDelay = TimeSpan.Zero;
        var store = new CalendarStore(":memory:");
        foreach (var (id, name) in calendars) store.UpsertCalendar(new CalendarInfo(id, MainViewModel.LocalAccountName, name, "#1A73E8"));
        return (new MainViewModel(store), store);
    }

    private static void Add(CalendarStore store, string id, string calendar, string title, DateTime? start = null, string description = "") =>
        store.UpsertEvent(new CalendarEvent(id, calendar, title, start ?? Day.AddHours(10), (start ?? Day.AddHours(10)).AddHours(1), Description: description));

    private static string[] Titles(MainViewModel vm) => vm.Search.Result.Hits.Select(h => h.Event.Title).Distinct().Order().ToArray();

    [Fact] // T168
    public void ScopeDefaultsToTheCalendarsShown_AndHiddenOnesAreSelectable()
    {
        var (vm, store) = Rig(("a", "Uno"), ("b", "Due"));
        Add(store, "e1", "a", "Alfa nel primo");
        Add(store, "e2", "b", "Alfa nel secondo");
        vm.Reload();
        vm.Search.IsOpen = true;
        vm.Search.Text = "alfa";
        Assert.Equal(0, vm.Search.Scope);
        Assert.Equal(["Alfa nel primo", "Alfa nel secondo"], Titles(vm));

        vm.Calendars.First(c => c.Id == "b").IsVisible = false; // hidden in the list on the left
        vm.Search.Run();
        Assert.Equal(["Alfa nel primo"], Titles(vm)); // "active calendars" does not look in it

        vm.Search.ChooseScope(1);
        Assert.Equal(["Alfa nel primo", "Alfa nel secondo"], Titles(vm)); // all of them

        vm.Search.ChooseScope(2);
        foreach (var o in vm.Search.Calendars) o.IsSelected = o.Id == "b"; // ticking the hidden one only
        Assert.Equal(2, vm.Search.Scope);
        Assert.Equal(["Alfa nel secondo"], Titles(vm));
    }

    [Fact] // T168
    public void ScopeLabelFollowsTheChoice()
    {
        var (vm, _) = Rig(("a", "Uno"), ("b", "Due"), ("c", "Tre"));
        Assert.Equal(L.T("search.scope_active"), vm.Search.ScopeLabel);
        vm.Search.ChooseScope(1);
        Assert.Equal(L.T("search.scope_all"), vm.Search.ScopeLabel);
        foreach (var o in vm.Search.Calendars) o.IsSelected = false;
        Assert.Equal(L.T("search.scope_none"), vm.Search.ScopeLabel);
        vm.Search.Calendars.First(o => o.Id == "b").IsSelected = true;
        Assert.Equal("Due", vm.Search.ScopeLabel);
        vm.Search.Calendars.First(o => o.Id == "c").IsSelected = true;
        Assert.Equal(L.T("search.scope_many", 2), vm.Search.ScopeLabel);
    }

    [Fact] // T168: with "all", a calendar that is added is searched too; with a choice of calendars it is not
    public void ANewCalendarIsInAllButNotInAChoice()
    {
        var (vm, store) = Rig(("a", "Uno"));
        vm.Search.ChooseScope(1);
        store.UpsertCalendar(new CalendarInfo("n", MainViewModel.LocalAccountName, "Nuovo", "#123456"));
        Add(store, "e", "n", "Alfa nuovo");
        vm.Reload();
        vm.Search.IsOpen = true;
        vm.Search.Text = "alfa";
        Assert.Equal(["Alfa nuovo"], Titles(vm));

        vm.Search.ChooseScope(2);
        foreach (var o in vm.Search.Calendars) o.IsSelected = o.Id == "a";
        store.UpsertCalendar(new CalendarInfo("m", MainViewModel.LocalAccountName, "Ancora", "#654321"));
        Add(store, "e2", "m", "Alfa ancora");
        vm.Reload();
        Assert.Equal(["a"], vm.Search.Calendars.Where(o => o.IsSelected).Select(o => o.Id));
        Assert.Empty(Titles(vm));
    }

    [Fact] // T173
    public void MessagesFollowTheState()
    {
        var (vm, store) = Rig(("a", "Uno"));
        Add(store, "e", "a", "Alfa");
        vm.Reload();
        vm.Search.IsOpen = true;
        vm.Search.Run();
        Assert.Equal(L.T("search.hint"), vm.Search.Message);
        Assert.True(vm.Search.ShowMessage);

        vm.Search.Text = "a"; // too short
        Assert.Equal(L.T("search.hint"), vm.Search.Message);

        vm.Search.Text = "zzzz";
        Assert.Equal(L.T("search.empty"), vm.Search.Message);
        Assert.Equal("", vm.Search.Summary);

        vm.Search.Text = "alfa";
        Assert.False(vm.Search.ShowMessage);
        Assert.Equal(L.N("search.count", 1), vm.Search.Summary);

        vm.Search.Text = "";
        Assert.Equal(L.T("search.hint"), vm.Search.Message);
        Assert.Empty(vm.Search.Groups);
    }

    [Fact] // T177, T181: "Search" in the panel writes its boxes into the line; plain words stay
    public void SubmitWritesThePanelIntoTheLine()
    {
        var (vm, store) = Rig(("a", "Uno"));
        Add(store, "e1", "a", "Gaza notizie");
        Add(store, "e2", "a", "Prova uno");
        store.UpsertEvent(new CalendarEvent("e3", "a", "Altro", Day.AddHours(12), Day.AddHours(13), Location: "Prova Roma", Description: "nota"));
        vm.Reload();
        vm.Search.IsOpen = true;
        vm.Search.Text = "gaza";
        Assert.Equal(["Gaza notizie"], Titles(vm));

        vm.Search.IsAdvanced = true;
        vm.Search.WhatText = "prova";
        vm.Search.Submit();
        Assert.Equal("gaza what:prova", vm.Search.Text); // the line says what is searched: two different things, both visible
        Assert.Empty(Titles(vm)); // "gaza" AND what:prova
        Assert.False(vm.Search.IsAdvanced);

        vm.Search.Text = "what:prova"; // the person takes the plain word out
        Assert.Equal(["Prova uno"], Titles(vm)); // "what" looks in title and description only: not in the location of e3
    }

    [Fact] // T181: the boxes of the panel show what the line says
    public void ThePanelShowsTheLine()
    {
        var (vm, _) = Rig(("a", "Uno"));
        vm.Search.IsOpen = true;
        vm.Search.Text = "alfa what:prova email:mario location:\"sala uno\" not:annullata from:2026-11-02 to:2026-11-30";
        Assert.Equal(("prova", "mario", "sala uno", "annullata"), (vm.Search.WhatText, vm.Search.WhoText, vm.Search.WhereText, vm.Search.NotText));
        Assert.Equal((new DateTime(2026, 11, 2), new DateTime(2026, 11, 30)), (vm.Search.FromDate, vm.Search.ToDate));

        vm.Search.Text = "alfa";
        Assert.Equal(("", "", "", ""), (vm.Search.WhatText, vm.Search.WhoText, vm.Search.WhereText, vm.Search.NotText));
        Assert.Equal(vm.FirstShownDay(), vm.Search.FromDate);
        Assert.Null(vm.Search.ToDate);
    }

    [Fact] // T181: Cancel drops what was typed in the panel, the line stays
    public void CancelKeepsTheLine()
    {
        var (vm, store) = Rig(("a", "Uno"));
        Add(store, "e2", "a", "Prova uno");
        vm.Reload();
        vm.Search.IsOpen = true;
        vm.Search.Text = "prova";
        vm.Search.IsAdvanced = true;
        vm.Search.WhereText = "roma";
        vm.Search.ToDate = Day;
        vm.Search.CancelAdvanced();
        Assert.False(vm.Search.IsAdvanced);
        Assert.Equal("prova", vm.Search.Text);
        Assert.Equal("", vm.Search.WhereText);
        Assert.Null(vm.Search.ToDate);
        Assert.Equal(["Prova uno"], Titles(vm));
        Assert.True(vm.Search.ShowResults);
    }

    [Fact] // T181: the first day goes into the line only when it is not the one of the view
    public void TheFirstDayIsWrittenOnlyWhenItIsNotTheDefault()
    {
        var (vm, _) = Rig(("a", "Uno"));
        vm.Search.IsOpen = true;
        vm.Search.IsAdvanced = true;
        vm.Search.WhatText = "alfa";
        vm.Search.Submit();
        Assert.Equal("what:alfa", vm.Search.Text);

        vm.Search.IsAdvanced = true;
        vm.Search.FromDate = new DateTime(2026, 11, 2);
        vm.Search.ToDate = new DateTime(2026, 11, 30);
        vm.Search.Submit();
        Assert.Equal("what:alfa from:2026-11-02 to:2026-11-30", vm.Search.Text);
    }

    [Fact] // T181: the keywords on the line work as the boxes of the panel did
    public void KeywordsAreSearched()
    {
        var (vm, store) = Rig(("a", "Uno"));
        store.UpsertEvent(new CalendarEvent("e1", "a", "Riunione alfa", Day.AddHours(9), Day.AddHours(10), Location: "Roma", Description: "budget")
        { Attendees = [new EventAttendee("mario@example.com")] });
        store.UpsertEvent(new CalendarEvent("e2", "a", "Pranzo", Day.AddHours(12), Day.AddHours(13), Location: "Milano", Description: "alfa")
        { Attendees = [new EventAttendee("anna@example.com")] });
        vm.Reload();
        vm.Search.IsOpen = true;

        vm.Search.Text = "alfa";
        Assert.Equal(["Pranzo", "Riunione alfa"], Titles(vm));
        vm.Search.Text = "title:alfa";
        Assert.Equal(["Riunione alfa"], Titles(vm));
        vm.Search.Text = "description:alfa";
        Assert.Equal(["Pranzo"], Titles(vm));
        vm.Search.Text = "email:mario location:roma";
        Assert.Equal(["Riunione alfa"], Titles(vm));
        vm.Search.Text = "location:roma email:mario"; // any order
        Assert.Equal(["Riunione alfa"], Titles(vm));
        vm.Search.Text = "alfa not:milano";
        Assert.Equal(["Riunione alfa"], Titles(vm));
        vm.Search.Text = "alfa -roma";
        Assert.Equal(["Pranzo"], Titles(vm));
        vm.Search.Text = "ALFA TITLE:Riunione";
        Assert.Equal(["Riunione alfa"], Titles(vm));
        vm.Search.Text = "from:" + Day.AddDays(1).ToString("yyyy-MM-dd");
        Assert.Empty(Titles(vm)); // after both
        vm.Search.Text = "from:" + Day.ToString("yyyy-MM-dd") + " to:" + Day.ToString("yyyy-MM-dd");
        Assert.Equal(["Pranzo", "Riunione alfa"], Titles(vm)); // a day alone is a search
        Assert.Equal(L.N("search.count", 2), vm.Search.Summary);
    }

    [Fact] // T181: a keyword without a value, or an unknown one, does not break anything
    public void IncompleteKeywordsAreHarmless()
    {
        var (vm, store) = Rig(("a", "Uno"));
        Add(store, "e", "a", "Alfa");
        vm.Reload();
        vm.Search.IsOpen = true;
        vm.Search.Text = "title:";
        Assert.Equal(L.T("search.hint"), vm.Search.Message); // nothing to look for yet
        vm.Search.Text = "title:alfa foo:bar";
        Assert.Empty(Titles(vm)); // "foo:bar" is a plain word that nothing has
        vm.Search.Text = "title:alfa from:not-a-date";
        Assert.Equal(["Alfa"], Titles(vm)); // the bad day is left out
    }

    [Theory] // T178: where the search starts depends on the view
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void TheSearchStartsWhereTheViewStarts(int view)
    {
        var (vm, _) = Rig(("a", "Uno"));
        vm.SelectedDate = new DateTime(2026, 10, 15); // a Thursday
        vm.ViewModeIndex = view;
        var expected = view switch
        {
            0 or 4 or 5 => new DateTime(2026, 10, 15),
            1 => new DateTime(2026, 10, 12), // the Monday
            2 => new DateTime(2026, 9, 28),  // the first day of the grid of October
            _ => new DateTime(2026, 1, 1),
        };
        Assert.Equal(expected, vm.FirstShownDay());
        vm.Search.IsOpen = true;
        Assert.Equal(expected, vm.Search.FromDate);
    }

    [Fact] // T177: "Search" in the detailed panel shows the results: the panel folds
    public void SubmitSearchesAndFoldsThePanel()
    {
        var (vm, store) = Rig(("a", "Uno"));
        Add(store, "e", "a", "Alfa", description: "nota");
        vm.Reload();
        vm.Search.IsOpen = true;
        vm.Search.IsAdvanced = true;
        vm.Search.WhatText = "alfa";
        Assert.Empty(vm.Search.Groups); // the boxes of the panel search when asked ("Search")
        vm.Search.Submit();
        Assert.False(vm.Search.IsAdvanced);
        Assert.True(vm.Search.IsOpen);
        Assert.Equal(["Alfa"], Titles(vm));
    }

    [Fact] // T178
    public void EndDateBeforeStartDateIsTold()
    {
        var (vm, store) = Rig(("a", "Uno"));
        Add(store, "e", "a", "Alfa");
        vm.Reload();
        vm.Search.IsOpen = true;
        vm.Search.Text = "alfa from:" + Day.AddDays(5).ToString("yyyy-MM-dd") + " to:" + Day.AddDays(1).ToString("yyyy-MM-dd");
        Assert.Equal(L.T("search.range_invalid"), vm.Search.Message);
        Assert.Empty(vm.Search.Groups);
    }

    [Fact] // T173: more lines than the limit
    public void LinesAreCutAtTheLimit_AndTheNoteIsShown()
    {
        var (vm, store) = Rig(("a", "Uno"));
        for (var i = 0; i < 1100; i++) Add(store, "e" + i, "a", "Alfa " + i, Day.AddMinutes(i));
        vm.Reload();
        vm.Search.IsOpen = true;
        vm.Search.Text = "alfa";
        Assert.Equal(1000, vm.Search.Groups.Sum(g => g.Rows.Count));
        Assert.True(vm.Search.IsTruncated);
        Assert.Equal(L.T("search.truncated", 1000), vm.Search.TruncatedNote);

        vm.Search.Text = "alfa 1099";
        Assert.False(vm.Search.IsTruncated);
        Assert.Equal("", vm.Search.TruncatedNote);
    }

    [Fact] // T170: the texts of the lines
    public void LinesTellTheTimeAndTheDayOfAnEventOfSeveralDays()
    {
        var (vm, store) = Rig(("a", "Uno"));
        var start = new DateTime(2030, 3, 4, 18, 30, 0);
        store.UpsertEvent(new CalendarEvent("m", "a", "Congresso", start, start.AddHours(50)));
        store.UpsertEvent(new CalendarEvent("s", "a", "Pranzo", start.AddDays(10), start.AddDays(10).AddHours(1)));
        store.UpsertEvent(new CalendarEvent("d", "a", "Ferie", start.Date.AddDays(20), start.Date.AddDays(21), IsAllDay: true));
        vm.Reload();
        vm.Search.IsOpen = true;

        vm.Search.Text = "congresso";
        var rows = vm.Search.Groups.SelectMany(g => g.Rows).ToList();
        Assert.Equal(3, rows.Count); // the 4th, the 5th and the 6th
        Assert.Equal(L.T("search.from_time", "18:30"), rows[0].Time);
        Assert.Equal(L.T("common.all_day"), rows[1].Time);
        Assert.Equal(L.T("search.until_time", "20:30"), rows[2].Time);
        Assert.Equal([L.T("search.day_of", 1, 3), L.T("search.day_of", 2, 3), L.T("search.day_of", 3, 3)], rows.Select(r => r.Note).ToArray());
        Assert.Equal(L.N("search.count", 1), vm.Search.Summary); // one event on three lines

        vm.Search.Text = "pranzo";
        Assert.Equal("18:30 – 19:30", vm.Search.Groups.Single().Rows.Single().Time);
        Assert.False(vm.Search.Groups.Single().Rows.Single().HasNote);

        vm.Search.Text = "ferie";
        Assert.Equal(L.T("common.all_day"), vm.Search.Groups.Single().Rows.Single().Time);
    }

    [Fact] // T174: 12-hour clock
    public void TheClockOfTheSettingsIsUsed()
    {
        var (vm, store) = Rig(("a", "Uno"));
        Add(store, "e", "a", "Alfa", new DateTime(2030, 3, 4, 9, 5, 0));
        vm.Reload();
        vm.Settings = new AppSettings { Use24HourClock = false };
        vm.Search.IsOpen = true;
        vm.Search.Text = "alfa";
        Assert.StartsWith("9:05", vm.Search.Groups.Single().Rows.Single().Time);
    }

    [Fact] // T172: a change of the events is shown at once
    public void ResultsFollowTheEvents()
    {
        var (vm, store) = Rig(("a", "Uno"));
        Add(store, "e", "a", "Alfa vecchio");
        vm.Reload();
        vm.Search.IsOpen = true;
        vm.Search.Text = "alfa";
        Assert.Equal(["Alfa vecchio"], Titles(vm));

        var ev = vm.AllEvents.Single();
        vm.UpdateEvent(ev with { Title = "Alfa nuovo" });
        Assert.Equal(["Alfa nuovo"], Titles(vm));
        vm.DeleteEvent(vm.AllEvents.Single());
        Assert.Empty(Titles(vm));
        Assert.Equal(L.T("search.empty"), vm.Search.Message);
    }

    // ---- what is kept (T179) ----

    [Fact] // T179
    public void TheChoiceIsWrittenAndReadBack()
    {
        var (vm, _) = Rig(("a", "Uno"), ("b", "Due"), ("c", "Tre"));
        var settings = new AppSettings();

        vm.Search.ChooseScope(1);
        vm.Search.WriteSettings(settings);
        Assert.Equal((1, 0), (settings.SearchScope, settings.SearchCalendarIds.Count));

        vm.Search.ChooseScope(2);
        foreach (var o in vm.Search.Calendars) o.IsSelected = o.Id is "a" or "c";
        vm.Search.WriteSettings(settings);
        Assert.Equal(2, settings.SearchScope);
        Assert.Equal(["a", "c"], settings.SearchCalendarIds.Order());

        var (other, _) = Rig(("a", "Uno"), ("b", "Due"), ("c", "Tre"));
        other.Search.ApplySettings(settings);
        Assert.Equal(2, other.Search.Scope);
        Assert.Equal(["a", "c"], other.Search.Calendars.Where(o => o.IsSelected).Select(o => o.Id).Order());
    }

    [Fact] // T179
    public void CalendarsThatAreGoneAreDropped_AndAnEmptyChoiceFallsBackToTheActiveOnes()
    {
        var (vm, _) = Rig(("a", "Uno"), ("b", "Due"));
        vm.Search.ApplySettings(new AppSettings { SearchScope = 2, SearchCalendarIds = ["a", "gone"] });
        Assert.Equal(["a"], vm.Search.Calendars.Where(o => o.IsSelected).Select(o => o.Id));

        vm.Search.ApplySettings(new AppSettings { SearchScope = 2, SearchCalendarIds = ["gone"] });
        Assert.Equal(0, vm.Search.Scope);

        vm.Search.ApplySettings(new AppSettings { SearchScope = 9, SearchCalendarIds = null! }); // a damaged file
        Assert.Equal(0, vm.Search.Scope);
    }

    [Fact] // T179: a settings file written before the search existed
    public void AnOldSettingsFileHasNoChoice()
    {
        var path = Path.Combine(Path.GetTempPath(), "ggcal-set-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(path, "{ \"SyncIntervalMinutes\": 10, \"Language\": \"it\" }");
            var loaded = new SettingsStore(path).Load();
            Assert.Equal((0, 0), (loaded.SearchScope, loaded.SearchCalendarIds.Count));

            loaded.SearchScope = 2;
            loaded.SearchCalendarIds = ["a", "b"];
            new SettingsStore(path).Save(loaded);
            var back = new SettingsStore(path).Load();
            Assert.Equal(2, back.SearchScope);
            Assert.Equal(["a", "b"], back.SearchCalendarIds);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
