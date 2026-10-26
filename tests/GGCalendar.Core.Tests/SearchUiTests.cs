using System.Net;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GGCalendar.App.Services;
using GGCalendar.App.ViewModels;
using GGCalendar.App.Views;
using GGCalendar.Core.Localization;
using GGCalendar.Core.Models;
using GGCalendar.Core.Subscriptions;
using GGCalendar.Core.Tests.Support;
using GGCalendar.Data;
using static GGCalendar.Core.Tests.Support.AppDriver;

namespace GGCalendar.Core.Tests;

/// <summary>The search of events on the real app (issue #8): no Google account and no network; the events are in local calendars made for the test.</summary>
[Collection(CollectionName)]
public class SearchUiTests
{
    private static readonly string Word = "ricerca" + Guid.NewGuid().ToString("N")[..6];

    private static Task InSearchAppAsync(Func<MainWindow, Task> test) => HeadlessApp.RunAsync(async () =>
    {
        var settingsFile = new SettingsStore().FilePath;
        var original = File.Exists(settingsFile) ? File.ReadAllBytes(settingsFile) : null;
        var typing = SearchViewModel.TypingDelay;
        SearchViewModel.TypingDelay = TimeSpan.Zero;
        var window = OpenApp();
        var language = L.Language;
        try
        {
            Clean(window);
            await test(window);
        }
        finally
        {
            var last = CurrentWindow ?? window;
            Clean(last);
            last.Close();
            SearchViewModel.TypingDelay = typing;
            L.Apply(language);
            if (original is not null) File.WriteAllBytes(settingsFile, original);
        }
    });

    private static void Clean(MainWindow window)
    {
        var vm = Vm(window);
        foreach (var s in new CalendarStore().GetSubscriptions()) window.Subscriptions.Remove(s.CalendarId);
        vm.Reload();
        foreach (var ev in vm.AllEvents.Where(e => e.Title.Contains(Word, StringComparison.Ordinal) && e.SeriesMasterId is null).ToList()) vm.DeleteEvent(ev);
        foreach (var c in vm.Calendars.Where(c => c.IsLocal && c.Name.StartsWith(TestPrefix, StringComparison.Ordinal)).ToList()) vm.DeleteLocalCalendar(c.Id);
    }

    private static string LocalCalendar(MainWindow window) => Vm(window).Calendars.First(c => c.IsLocal && !c.Name.StartsWith(TestPrefix, StringComparison.Ordinal)).Id;

    private static CalendarEvent Add(MainWindow window, string title, DateTime start, double hours = 1, string? calendar = null, string description = "", string location = "",
        string? recurrence = null, params string[] guests)
    {
        var ev = new CalendarEvent(Guid.NewGuid().ToString("N"), calendar ?? LocalCalendar(window), Word + " " + title, start, start.AddHours(hours), Location: location, Description: description)
        {
            Recurrence = recurrence,
            Attendees = guests.Select(g => new EventAttendee(g)).ToList(),
        };
        Vm(window).AddEvent(ev);
        return ev;
    }

    private static void Search(MainWindow window, string text)
    {
        var vm = Vm(window);
        vm.Search.IsOpen = true;
        Dispatcher.UIThread.RunJobs();
        ((TextBox)FindById(window, "Search.Box")).Text = text;
        Dispatcher.UIThread.RunJobs();
    }

    private static List<Border> Rows(MainWindow window)
    {
        Dispatcher.UIThread.RunJobs();
        return window.GetVisualDescendants().OfType<Border>().Where(b => Avalonia.Automation.AutomationProperties.GetAutomationId(b) == "Search.Row" && b.IsEffectivelyVisible).ToList();
    }

    private static DateTime Soon => DateTime.Today.AddDays(2).AddHours(10);

    // ---- T165: open and close ----

    [Fact]
    [Trait("TestCase", "T165")]
    public Task T165_TheLensOpensTheSearchNextToSyncAndEscOrXCloseItLeavingTheViewAsItWas() => InSearchAppAsync(async window =>
    {
        var vm = Vm(window);
        var lens = FindButton(window, "Main.Search");
        var sync = FindButton(window, "Main.SyncNow");
        Assert.True(lens.TranslatePoint(default, window)!.Value.X < sync.TranslatePoint(default, window)!.Value.X); // on the left of Sync
        Assert.False(vm.Search.IsOpen);

        var view = vm.ViewModeIndex;
        var date = vm.SelectedDate;
        Click(lens);
        Assert.True(vm.Search.IsOpen);
        Assert.True(FindById(window, "Search.Box").IsVisible);
        await PauseAsync(TimeSpan.FromMilliseconds(100));
        Assert.True(((TextBox)FindById(window, "Search.Box")).IsFocused);

        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        Assert.False(vm.Search.IsOpen);

        window.KeyPressQwerty(PhysicalKey.F, RawInputModifiers.Control); // Ctrl+F
        Dispatcher.UIThread.RunJobs();
        Assert.True(vm.Search.IsOpen);
        Click(FindButton(window, "Search.Close"));
        Assert.False(vm.Search.IsOpen);

        Click(lens);
        Click(lens); // the lens again closes it
        Assert.False(vm.Search.IsOpen);
        Assert.Equal((view, date), (vm.ViewModeIndex, vm.SelectedDate));
        await Task.CompletedTask;
    });

    [Fact]
    [Trait("TestCase", "T165")]
    public Task T165_ChoosingAViewDuringTheSearchClosesItAndShowsThatView() => InSearchAppAsync(async window =>
    {
        var vm = Vm(window);
        Add(window, "Vista", Soon);
        Search(window, Word);
        vm.Search.IsAdvanced = true;
        Assert.True(vm.Search.ShowResults);
        Assert.Single(Rows(window));

        var menu = (MenuFlyout)FindButton(window, "Main.ViewMode").Flyout!;
        var month = menu.Items.OfType<MenuItem>().First(i => (string?)i.Tag == "2");
        month.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(2, vm.ViewModeIndex); // the view chosen
        Assert.False(vm.Search.IsOpen); // the search is closed
        Assert.False(vm.Search.IsAdvanced);
        Assert.False(vm.Search.ShowResults);
        Assert.Equal("", vm.Search.Text); // and the box is empty
        Assert.Equal("", ((TextBox)FindById(window, "Search.Box")).Text);
        Assert.Empty(Rows(window));
        await Task.CompletedTask;
    });

    [Fact]
    [Trait("TestCase", "T165")]
    public Task T165_AClickOnTheSmallCalendarEndsTheSearchToo() => InSearchAppAsync(async window =>
    {
        var vm = Vm(window);
        Add(window, "Giorno", Soon);
        Search(window, Word);
        Assert.True(vm.Search.ShowResults);
        var mini = window.FindControl<Calendar>("MiniCalendar")!;
        var other = (vm.SelectedDate ?? DateTime.Today).AddDays(10);
        mini.SelectedDate = other; // what a click on a day of the small calendar does
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(other.Date, vm.SelectedDate!.Value.Date);
        Assert.False(vm.Search.IsOpen);
        Assert.False(vm.Search.ShowResults);
        Assert.Equal("", ((TextBox)FindById(window, "Search.Box")).Text);
        await Task.CompletedTask;
    });

    [Fact]
    [Trait("TestCase", "T165")]
    public Task T165_TodayAndTheArrowsCloseTheSearchEmptyTheBoxAndShowThePeriod() => InSearchAppAsync(async window =>
    {
        var vm = Vm(window);
        vm.ViewModeIndex = 2;
        vm.SelectedDate = new DateTime(2026, 10, 15);
        Add(window, "Giro", Soon);

        void Searching()
        {
            Search(window, Word);
            vm.Search.IsAdvanced = true;
            Assert.True(vm.Search.ShowResults);
        }

        void AssertEnded()
        {
            Dispatcher.UIThread.RunJobs();
            Assert.False(vm.Search.IsOpen);
            Assert.False(vm.Search.IsAdvanced);
            Assert.False(vm.Search.ShowResults);
            Assert.Equal("", vm.Search.Text);
            Assert.Equal("", ((TextBox)FindById(window, "Search.Box")).Text);
            Assert.Equal(2, vm.ViewModeIndex); // the view is the one it was
        }

        Searching();
        Click(FindButton(window, "Main.Today")); // Click raises the event, the command is run by hand
        FindButton(window, "Main.Today").Command!.Execute(null);
        AssertEnded();
        Assert.Equal(DateTime.Today, vm.SelectedDate);

        Searching();
        vm.NextCommand.Execute(null);
        AssertEnded();
        Assert.Equal(DateTime.Today.AddMonths(1).Month, vm.SelectedDate!.Value.Month); // the month after

        Searching();
        vm.PreviousCommand.Execute(null);
        AssertEnded();
        Assert.Equal(DateTime.Today.Month, vm.SelectedDate!.Value.Month);
        await Task.CompletedTask;
    });

    // ---- T166, T167: what is found, as the user sees it ----

    [Fact]
    [Trait("TestCase", "T166")]
    public Task T166_TheLinesShowTheEventTheFieldsFoundAndTheCalendar() => InSearchAppAsync(async window =>
    {
        Add(window, "Riunione", Soon, description: "Parlare del budget trimestrale", location: "Sala Giove", guests: "mario@example.com");
        Add(window, "Altro", Soon.AddHours(3));
        Search(window, "budget");
        var rows = Rows(window);
        var row = Assert.Single(rows);
        var texts = row.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains(Word + " Riunione", texts);
        Assert.Contains(L.T("search.field_description"), texts);
        Assert.Contains(texts, t => t?.Contains("budget") == true);
        Assert.Equal(L.N("search.count", 1), ((TextBlock)FindById(window, "Search.Summary")).Text);

        Search(window, "giove");
        Assert.Contains(L.T("search.field_location"), Rows(window).Single().GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));
        Search(window, "mario@");
        Assert.Contains(L.T("search.field_guests"), Rows(window).Single().GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));
        Search(window, Word + " riunione budget"); // all the words, in different fields
        Assert.Single(Rows(window));
        await Task.CompletedTask;
    });

    // ---- T169, T170 ----

    [Fact]
    [Trait("TestCase", "T169")]
    public Task T169_EveryOccurrenceOfASeriesIsALineFromTodayToAYearLater() => InSearchAppAsync(async window =>
    {
        Add(window, "Palestra", DateTime.Today.AddDays(-730).AddHours(18), recurrence: "FREQ=WEEKLY");
        Search(window, Word + " palestra");
        var days = Vm(window).Search.Result.Hits.Select(h => h.Day).ToList();
        Assert.InRange(days.Count, 52, 54);
        var first = Vm(window).FirstShownDay();
        Assert.True(days.Min() >= first && days.Max() <= first.AddYears(1)); // nothing before the first day of the view
        Assert.Equal(days.Count, days.Distinct().Count());
        Assert.Equal(L.N("search.count", days.Count), ((TextBlock)FindById(window, "Search.Summary")).Text);
        await Task.CompletedTask;
    });

    [Fact]
    [Trait("TestCase", "T170")]
    public Task T170_AnEventOfSeveralDaysIsOnEachDayAndTheLinesAreInOrder() => InSearchAppAsync(async window =>
    {
        var start = DateTime.Today.AddDays(5).AddHours(18);
        Add(window, "Congresso", start, hours: 50);
        Add(window, "Cena", DateTime.Today.AddDays(5).AddHours(20));
        Search(window, Word);
        var hits = Vm(window).Search.Result.Hits;
        Assert.Equal(3 + 1, hits.Count);
        Assert.Equal(hits.OrderBy(h => h.Day).ThenBy(h => h.Event.Start).Select(h => h.Event.Title), hits.Select(h => h.Event.Title)); // chronological
        Assert.Equal(L.N("search.count", 2), ((TextBlock)FindById(window, "Search.Summary")).Text);
        Assert.Equal(4, Rows(window).Count);
        await Task.CompletedTask;
    });

    // ---- T171, T172: the lines are clickable ----

    private static void Tap(MainWindow window, Border row)
    {
        var center = row.TranslatePoint(new Point(row.Bounds.Width / 2, row.Bounds.Height / 2), window)!.Value;
        window.MouseDown(center, MouseButton.Left);
        window.MouseUp(center, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Clicks the line until the card of the event is shown: a click can be lost while the list is still being drawn.</summary>
    private static async Task<EventDetailsCard> ShowCardAsync(MainWindow window, Border row)
    {
        for (var attempt = 0; attempt < 6; attempt++)
        {
            Tap(window, Rows(window).FirstOrDefault(r => ReferenceEquals(r.DataContext, row.DataContext)) ?? row);
            await PauseAsync(TimeSpan.FromMilliseconds(200));
            if (window.FindControl<Popup>("DetailsPopup")?.Child is EventDetailsCard card) return card;
        }

        return await WaitForAsync(() => window.FindControl<Popup>("DetailsPopup")?.Child as EventDetailsCard, "la scheda dell'evento");
    }

    [Fact]
    [Trait("TestCase", "T172")]
    public Task T172_AClickShowsTheCardOfTheEventADoubleClickOpensTheEditorAndAChangeUpdatesTheLines() => InSearchAppAsync(async window =>
    {
        var ev = Add(window, "Riunione", Soon, description: "importante");
        Search(window, "importante");
        var row = Assert.Single(Rows(window));

        var card = await ShowCardAsync(window, row);
        Assert.Contains(card.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == ev.Title);
        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        Assert.True(Vm(window).Search.IsOpen); // Esc closed the card; the search is still open (a second Esc closes it)

        DoubleClick(window, Rows(window).Single());
        var editor = await WaitForAsync(() => window.OwnedWindows.OfType<EventEditWindow>().FirstOrDefault(), "la finestra dell'evento");
        Assert.Equal(ev.Title, ((EventEditViewModel)editor.DataContext!).Title);
        await CancelEditorAsync(window, editor);

        Vm(window).UpdateEvent(Vm(window).AllEvents.Single(e => e.Id == ev.Id) with { Description = "cambiata" });
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(Rows(window)); // "importante" is not there any more
        Assert.Equal(L.T("search.empty"), ((TextBlock)FindById(window, "Search.Message")).Text);
    });

    [Fact]
    [Trait("TestCase", "T171")]
    public Task T171_APublicCalendarIsSearchedAndItsEventsCannotBeChanged() => InSearchAppAsync(async window =>
    {
        var day = DateTime.Today.AddDays(3);
        var body = "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nBEGIN:VEVENT\r\nUID:p1\r\nSUMMARY:" + Word + " pubblico\r\n" +
                   $"DTSTART:{day:yyyyMMdd}T100000\r\nDTEND:{day:yyyyMMdd}T110000\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n";
        var site = new FakeSite().Serve("/p.ics", body);
        var service = new SubscriptionService(new CalendarStore(), new IcsFetcher(site));
        var preview = await service.PreviewAsync("https://calendar.example.com/p.ics");
        var (calendar, problem) = service.Add(preview.Preview!, "Pubblico", "#123456", GGCalendar.Core.Subscriptions.SubscriptionRange.All);
        Assert.Null(problem);
        await Vm(window).ReloadAsync();

        Search(window, Word + " pubblico");
        var row = Assert.Single(Rows(window));
        var card = await ShowCardAsync(window, row);
        Assert.Single(card.GetVisualDescendants().OfType<Button>()); // only Close: no Edit, no Delete
        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        DoubleClick(window, Rows(window).Single());
        await PauseAsync(TimeSpan.FromMilliseconds(300));
        Assert.Empty(window.OwnedWindows.OfType<EventEditWindow>());
    });

    // ---- T174: second time zone ----

    [Fact]
    [Trait("TestCase", "T174")]
    public Task T174_WithTheSecondTimeZoneTheLinesShowTheHourThereToo() => InSearchAppAsync(async window =>
    {
        var vm = Vm(window);
        var other = TimeZoneInfo.Local.Id == "Tokyo Standard Time" ? "Pacific Standard Time" : "Tokyo Standard Time";
        Add(window, "Fuso", Soon);
        Search(window, Word + " fuso");
        Assert.False(vm.Search.Groups.Single().Rows.Single().HasSecondTime);

        vm.Settings = new AppSettings { SecondaryTimeZoneId = other, SecondaryTimeZoneLabel = "Altro" };
        vm.Search.Run();
        Assert.True(vm.Search.Groups.Single().Rows.Single().HasSecondTime);
        Assert.Contains("Altro", vm.Search.Groups.Single().Rows.Single().SecondTime);
        await Task.CompletedTask;
    });

    // ---- T175: languages ----

    [Fact]
    [Trait("TestCase", "T175")]
    public Task T175_TextsAreInTheLanguageShownAndChangeWhileTheSearchIsOpen() => InSearchAppAsync(async window =>
    {
        var vm = Vm(window);
        Add(window, "Lingua", Soon);
        L.Apply("it");
        Search(window, Word + " lingua");
        Assert.Equal(L.N("search.count", 1), vm.Search.Summary);
        var italian = vm.Search.Summary;

        L.Apply("en");
        Dispatcher.UIThread.RunJobs();
        Assert.NotEqual(italian, vm.Search.Summary);
        Assert.Equal("1 event found", vm.Search.Summary);

        ((TextBox)FindById(window, "Search.Box")).Text = "zzzzzz";
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(L.T("search.empty"), ((TextBlock)FindById(window, "Search.Message")).Text);
        L.Apply("de");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(L.T("search.empty"), ((TextBlock)FindById(window, "Search.Message")).Text);
        Assert.Equal(L.T("search.scope_active"), vm.Search.ScopeLabel);
        await Task.CompletedTask;
    });

    // ---- T177: the detailed panel ----

    [Fact]
    [Trait("TestCase", "T177")]
    public Task T177_TheDetailedPanelOpensOverTheWindowWithoutMovingAnythingAndItsBoxesAreCombined() => InSearchAppAsync(async window =>
    {
        var vm = Vm(window);
        Add(window, "Alfa", Soon, location: "Roma", guests: "mario@example.com");
        Add(window, "Alfa", Soon.AddHours(2), location: "Milano", guests: "mario@example.com");
        Add(window, "Beta", Soon.AddHours(4), location: "Roma", guests: "anna@example.com");
        Search(window, Word);
        Assert.Equal(3, Rows(window).Count);

        var mini = window.FindControl<Calendar>("MiniCalendar")!;
        var grid = window.FindControl<Control>("Grid")!;
        var before = (mini.Bounds, mini.TranslatePoint(default, window), grid.Bounds, grid.TranslatePoint(default, window), window.FindControl<Control>("SearchBar")!.Bounds);
        Click(FindButton(window, "Search.Advanced"));
        await PauseAsync(TimeSpan.FromMilliseconds(100));
        Assert.True(vm.Search.IsAdvanced);
        Assert.True(window.FindControl<Popup>("SearchPanel")!.IsOpen);
        var after = (mini.Bounds, mini.TranslatePoint(default, window), grid.Bounds, grid.TranslatePoint(default, window), window.FindControl<Control>("SearchBar")!.Bounds);
        Assert.Equal(before, after); // the panel is over the rest: nothing moved

        vm.Search.Close();
        await Task.CompletedTask;
    });

    [Fact]
    [Trait("TestCase", "T177")]
    public Task T177_SearchInThePanelWritesTheLineInTheBoxAndCancelKeepsIt() => InSearchAppAsync(async window =>
    {
        var vm = Vm(window);
        Add(window, "Alfa", Soon, location: "Roma", guests: "mario@example.com");
        Add(window, "Alfa", Soon.AddHours(2), location: "Milano", guests: "mario@example.com");
        Add(window, "Beta", Soon.AddHours(4), location: "Roma", guests: "anna@example.com");
        Search(window, Word + " gaza"); // an old search that finds nothing
        Assert.Empty(Rows(window));

        vm.Search.IsAdvanced = true;
        vm.Search.WhatText = Word + " alfa";
        vm.Search.WhereText = "roma";
        vm.Search.WhoText = "mario";
        vm.Search.Submit();
        var box = ((TextBox)FindById(window, "Search.Box")).Text;
        Assert.Equal(Word + " gaza what:\"" + Word + " alfa\" email:mario location:roma", box); // the box says what is searched
        Assert.Empty(Rows(window)); // "gaza" is still there: the person takes it out
        ((TextBox)FindById(window, "Search.Box")).Text = box!.Replace(Word + " gaza ", "");
        Dispatcher.UIThread.RunJobs();
        Assert.Single(Rows(window));

        vm.Search.IsAdvanced = true;
        vm.Search.NotText = "roma"; // typed in the panel, then Cancel: the line is as it was
        vm.Search.CancelAdvanced();
        Assert.Equal("", vm.Search.NotText);
        Assert.False(vm.Search.IsAdvanced);
        Assert.Single(Rows(window));
        await Task.CompletedTask;
    });

    // ---- T179: kept between sessions ----

    [Fact]
    [Trait("TestCase", "T179")]
    public Task T179_TheChoiceOfCalendarsIsKeptWhenTheAppIsReopened() => InSearchAppAsync(async window =>
    {
        var second = Vm(window).CreateLocalCalendar(TestPrefix + " ricerca");
        var vm = Vm(window);
        vm.Search.ChooseScope(2);
        foreach (var o in vm.Search.Calendars) o.IsSelected = o.Id == second;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(2, new SettingsStore().Load().SearchScope);
        Assert.Equal([second], new SettingsStore().Load().SearchCalendarIds);

        window = await ReopenAsync(window);
        vm = Vm(window);
        await PauseAsync(TimeSpan.FromMilliseconds(200));
        Assert.Equal(2, vm.Search.Scope);
        Assert.Equal([second], vm.Search.Calendars.Where(o => o.IsSelected).Select(o => o.Id));

        vm.Search.ChooseScope(1);
        window = await ReopenAsync(window);
        Assert.Equal(1, Vm(window).Search.Scope);
    });
}
