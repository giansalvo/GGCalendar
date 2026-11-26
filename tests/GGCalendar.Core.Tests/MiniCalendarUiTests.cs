using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GGCalendar.App.ViewModels;
using GGCalendar.App.Views;
using GGCalendar.Core.Localization;
using GGCalendar.Core.Tests.Support;
using GGCalendar.Data;
using static GGCalendar.Core.Tests.Support.AppDriver;

namespace GGCalendar.Core.Tests;

/// <summary>The small calendar on the left follows the day chosen with Today, the arrows and the views (issues #52 and #103): no Google account and no network.</summary>
[Collection(CollectionName)]
public class MiniCalendarUiTests
{
    private static Task InMiniAppAsync(Func<MainWindow, Calendar, Task> test, int? defaultView = null) => HeadlessApp.RunAsync(async () =>
    {
        var settingsFile = new SettingsStore().FilePath;
        var original = File.Exists(settingsFile) ? File.ReadAllBytes(settingsFile) : null;
        if (defaultView is { } view)
        {
            var store = new SettingsStore();
            var settings = store.Load();
            settings.DefaultView = view;
            store.Save(settings);
        }

        var window = OpenApp();
        var language = L.Language;
        try
        {
            Pump();
            await test(window, window.FindControl<Calendar>("MiniCalendar")!);
        }
        finally
        {
            (CurrentWindow ?? window).Close();
            L.Apply(language);
            if (original is not null) File.WriteAllBytes(settingsFile, original);
        }
    });

    // The window moves the small calendar with a posted job: it is run before looking.
    private static void Pump() => Dispatcher.UIThread.RunJobs();

    private static void Press(MainViewModel vm, string command)
    {
        switch (command)
        {
            case "today": vm.TodayCommand.Execute(null); break;
            case "next": vm.NextCommand.Execute(null); break;
            case "previous": vm.PreviousCommand.Execute(null); break;
        }
        Pump();
    }

    private static void AssertShows(Calendar mini, DateTime day, CalendarMode mode = CalendarMode.Month)
    {
        Assert.Equal(mode, mini.DisplayMode);
        Assert.Equal(day.Year, mini.DisplayDate.Year);
        Assert.Equal(day.Month, mini.DisplayDate.Month);
    }

    [Fact]
    [Trait("TestCase", "T332")]
    public Task Today_after_browsing_the_small_calendar_shows_the_current_month() => InMiniAppAsync((window, mini) =>
    {
        var vm = Vm(window);
        vm.ViewModeIndex = 1;
        Press(vm, "today");
        mini.DisplayDate = DateTime.Today.AddMonths(3); // the user browses with the arrows of the small calendar
        Pump();
        Assert.NotEqual(DateTime.Today.Month, mini.DisplayDate.Month);

        Press(vm, "today"); // the selected day is already today: nothing changes in the model
        AssertShows(mini, DateTime.Today);
        Assert.Equal(DateTime.Today, vm.SelectedDate);
        return Task.CompletedTask;
    });

    [Theory]
    [Trait("TestCase", "T332")]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public Task Today_after_the_arrows_of_each_view_shows_the_current_month(int view) => InMiniAppAsync((window, mini) =>
    {
        var vm = Vm(window);
        vm.ViewModeIndex = view;
        Press(vm, "today");
        for (var i = 0; i < 10; i++) Press(vm, "next");
        Press(vm, "today");
        AssertShows(mini, DateTime.Today, MiniCalendarZoom.RestingMode(view));
        Assert.Equal(DateTime.Today, vm.SelectedDate);
        return Task.CompletedTask;
    });

    [Fact]
    [Trait("TestCase", "T333")]
    public Task The_arrows_move_the_small_calendar_to_the_month_of_the_new_day() => InMiniAppAsync((window, mini) =>
    {
        var vm = Vm(window);
        vm.ViewModeIndex = 2; // month view: one month for each press
        Press(vm, "today");

        Press(vm, "next");
        AssertShows(mini, DateTime.Today.AddMonths(1), CalendarMode.Year);
        Press(vm, "next");
        AssertShows(mini, DateTime.Today.AddMonths(2), CalendarMode.Year);
        Press(vm, "previous");
        AssertShows(mini, DateTime.Today.AddMonths(1), CalendarMode.Year);

        vm.ViewModeIndex = 1; // week view: the month changes when the week crosses it
        Press(vm, "today");
        for (var i = 0; i < 5; i++) Press(vm, "next");
        AssertShows(mini, DateTime.Today.AddDays(35));
        return Task.CompletedTask;
    });

    [Fact]
    [Trait("TestCase", "T333")]
    public Task A_day_shown_from_the_year_view_moves_the_small_calendar() => InMiniAppAsync((window, mini) =>
    {
        var vm = Vm(window);
        var day = DateTime.Today.AddMonths(4).Date;
        vm.ShowDay(day);
        Pump();
        AssertShows(mini, day);
        Assert.Equal(day, mini.SelectedDate);
        return Task.CompletedTask;
    });

    [Fact]
    [Trait("TestCase", "T334")]
    public Task A_day_chosen_in_the_small_calendar_keeps_its_month() => InMiniAppAsync((window, mini) =>
    {
        var vm = Vm(window);
        vm.ViewModeIndex = 1;
        Pump();
        var day = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 15).AddMonths(2);
        mini.DisplayDate = day;
        mini.SelectedDate = day; // what a click on a day does
        Pump();
        Assert.Equal(day, vm.SelectedDate);
        AssertShows(mini, day);
        return Task.CompletedTask;
    });

    [Fact]
    [Trait("TestCase", "T334")]
    public Task Today_brings_back_the_small_calendar_zoomed_on_the_years() => InMiniAppAsync((window, mini) =>
    {
        var vm = Vm(window);
        vm.ViewModeIndex = 1;
        Press(vm, "today");
        mini.DisplayMode = CalendarMode.Year;
        Pump();

        Press(vm, "today");
        AssertShows(mini, DateTime.Today);
        mini.DisplayMode = CalendarMode.Decade;
        Pump();
        Assert.Equal(CalendarMode.Decade, mini.DisplayMode); // going up is only browsing
        Press(vm, "today");
        AssertShows(mini, DateTime.Today);
        return Task.CompletedTask;
    });

    [Fact]
    [Trait("TestCase", "T335")]
    public Task Today_with_the_search_open_closes_it_and_shows_the_current_month() => InMiniAppAsync((window, mini) =>
    {
        var vm = Vm(window);
        vm.ViewModeIndex = 1;
        Press(vm, "today");
        Press(vm, "next");
        vm.Search.IsOpen = true;
        Pump();

        Press(vm, "today");
        Assert.False(vm.Search.IsOpen);
        AssertShows(mini, DateTime.Today);
        return Task.CompletedTask;
    });

    [Fact]
    [Trait("TestCase", "T335")]
    public Task A_change_of_language_keeps_the_month_of_the_small_calendar() => InMiniAppAsync((window, mini) =>
    {
        var vm = Vm(window);
        vm.ViewModeIndex = 2;
        Press(vm, "today");
        Press(vm, "next");
        Press(vm, "next");
        var shown = mini.DisplayDate;

        L.Apply(L.Language == "en" ? "it" : "en"); // the window listens to the change of language
        Pump();
        AssertShows(mini, shown, CalendarMode.Year);
        return Task.CompletedTask;
    });

    // ---- Issue #103: months with the month view, years with the year view ----

    private const int Week = 1, MonthView = 2, YearView = 3;

    private static void Settle(MainWindow window)
    {
        Pump();
        window.UpdateLayout();
        Pump();
    }

    private static void Show(MainWindow window, int view, DateTime day)
    {
        var vm = Vm(window);
        vm.ViewModeIndex = view;
        vm.SelectedDate = day;
        Settle(window);
    }

    private static void Click(MainWindow window, Control control)
    {
        var center = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseDown(center, MouseButton.Left);
        window.MouseUp(center, MouseButton.Left);
        Settle(window);
    }

    /// <summary>The 12 months or years shown, in reading order.</summary>
    private static List<CalendarButton> Cells(Calendar mini)
    {
        var cells = mini.GetVisualDescendants().OfType<CalendarButton>().Where(c => c.IsEffectivelyVisible).ToList();
        Assert.Equal(12, cells.Count);
        return cells;
    }

    private static CalendarButton YearCell(Calendar mini, int year) => Cells(mini).Single(c => c.Content?.ToString() == year.ToString());

    private static Button Part(Calendar mini, string name) => mini.GetVisualDescendants().OfType<Button>().Single(b => b.Name == name && b.IsEffectivelyVisible);

    [Theory]
    [Trait("TestCase", "T416")]
    [InlineData(0, CalendarMode.Month)]
    [InlineData(1, CalendarMode.Month)]
    [InlineData(2, CalendarMode.Year)]
    [InlineData(3, CalendarMode.Decade)]
    [InlineData(4, CalendarMode.Month)]
    [InlineData(5, CalendarMode.Month)]
    public Task A_change_of_view_changes_what_the_small_calendar_shows(int view, CalendarMode mode) => InMiniAppAsync((window, mini) =>
    {
        var day = new DateTime(2026, 5, 20);
        Show(window, view == Week ? MonthView : Week, day);
        Vm(window).ViewModeIndex = view;
        Settle(window);
        AssertShows(mini, day, mode);
        return Task.CompletedTask;
    });

    [Theory]
    [Trait("TestCase", "T417")]
    [InlineData(1, CalendarMode.Month)]
    [InlineData(2, CalendarMode.Year)]
    [InlineData(3, CalendarMode.Decade)]
    public Task At_start_the_small_calendar_follows_the_default_view(int view, CalendarMode mode) => InMiniAppAsync((window, mini) =>
    {
        Settle(window);
        Assert.Equal(view, Vm(window).ViewModeIndex);
        AssertShows(mini, DateTime.Today, mode);
        return Task.CompletedTask;
    }, defaultView: view);

    [Fact]
    [Trait("TestCase", "T418")]
    public Task A_click_on_a_month_moves_the_month_view_keeping_the_day() => InMiniAppAsync((window, mini) =>
    {
        var vm = Vm(window);
        Show(window, MonthView, new DateTime(2026, 1, 31));

        Click(window, Cells(mini)[1]); // February
        Assert.Equal(new DateTime(2026, 2, 28), vm.SelectedDate);
        Assert.Equal(2, vm.MonthNumber);
        Assert.Equal(MonthView, vm.ViewModeIndex);
        AssertShows(mini, new DateTime(2026, 2, 28), CalendarMode.Year);

        Click(window, Cells(mini)[10]); // November: a second choice works as the first
        Assert.Equal(new DateTime(2026, 11, 28), vm.SelectedDate);
        AssertShows(mini, new DateTime(2026, 11, 28), CalendarMode.Year);
        return Task.CompletedTask;
    });

    [Fact]
    [Trait("TestCase", "T419")]
    public Task A_click_on_a_year_moves_the_year_view_keeping_month_and_day() => InMiniAppAsync((window, mini) =>
    {
        var vm = Vm(window);
        Show(window, YearView, new DateTime(2024, 2, 29));

        Click(window, YearCell(mini, 2027));
        Assert.Equal(new DateTime(2027, 2, 28), vm.SelectedDate);
        Assert.Equal(2027, vm.YearNumber);
        Assert.Equal(YearView, vm.ViewModeIndex);
        AssertShows(mini, new DateTime(2027, 2, 28), CalendarMode.Decade);

        Click(window, YearCell(mini, 2019)); // the year before the calendar decade
        Assert.Equal(new DateTime(2019, 2, 28), vm.SelectedDate);
        AssertShows(mini, new DateTime(2019, 2, 28), CalendarMode.Decade);
        return Task.CompletedTask;
    });

    [Theory]
    [Trait("TestCase", "T420")]
    [InlineData(MonthView, CalendarMode.Year)]
    [InlineData(YearView, CalendarMode.Decade)]
    public Task A_click_on_the_month_or_year_already_shown_stays_there_and_closes_the_search(int view, CalendarMode mode) => InMiniAppAsync((window, mini) =>
    {
        var vm = Vm(window);
        var day = new DateTime(2026, 5, 20);
        Show(window, view, day);
        vm.Search.IsOpen = true;
        Settle(window);

        Click(window, view == MonthView ? Cells(mini)[4] : YearCell(mini, 2026));
        Assert.Equal(day, vm.SelectedDate);
        Assert.False(vm.Search.IsOpen);
        AssertShows(mini, day, mode);
        return Task.CompletedTask;
    });

    [Fact]
    [Trait("TestCase", "T421")]
    public Task In_the_month_view_arrows_and_header_only_browse() => InMiniAppAsync((window, mini) =>
    {
        var vm = Vm(window);
        var day = new DateTime(2026, 5, 20);
        Show(window, MonthView, day);

        Click(window, Part(mini, "PART_NextButton"));
        Assert.Equal(day, vm.SelectedDate);
        Assert.Equal(CalendarMode.Year, mini.DisplayMode);

        Click(window, Cells(mini)[2]); // March of the year reached with the arrow
        Assert.Equal(new DateTime(2027, 3, 20), vm.SelectedDate);

        Click(window, Part(mini, "PART_HeaderButton")); // up to the years
        Assert.Equal(CalendarMode.Decade, mini.DisplayMode);
        Click(window, YearCell(mini, 2029)); // down to its months: still only browsing
        Assert.Equal(CalendarMode.Year, mini.DisplayMode);
        Assert.Equal(new DateTime(2027, 3, 20), vm.SelectedDate);

        Click(window, Cells(mini)[8]); // September 2029
        Assert.Equal(new DateTime(2029, 9, 20), vm.SelectedDate);
        AssertShows(mini, new DateTime(2029, 9, 20), CalendarMode.Year);
        return Task.CompletedTask;
    });

    [Fact]
    [Trait("TestCase", "T422")]
    public Task In_the_year_view_arrows_only_browse() => InMiniAppAsync((window, mini) =>
    {
        var vm = Vm(window);
        var day = new DateTime(2026, 5, 20);
        Show(window, YearView, day);

        Click(window, Part(mini, "PART_NextButton"));
        Assert.Equal(day, vm.SelectedDate);
        Assert.Equal(CalendarMode.Decade, mini.DisplayMode);

        Click(window, YearCell(mini, 2034));
        Assert.Equal(new DateTime(2034, 5, 20), vm.SelectedDate);
        AssertShows(mini, new DateTime(2034, 5, 20), CalendarMode.Decade);
        return Task.CompletedTask;
    });

    [Fact]
    [Trait("TestCase", "T423")]
    public Task Ctrl_down_chooses_a_month_like_a_click() => InMiniAppAsync((window, mini) =>
    {
        var vm = Vm(window);
        var day = new DateTime(2026, 5, 20);
        Show(window, MonthView, day);
        Cells(mini)[4].Focus();
        Settle(window);

        window.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.None);
        Settle(window);
        Assert.Equal(day, vm.SelectedDate); // moving with the arrows is only browsing

        window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.Control);
        Settle(window);
        Assert.Equal(new DateTime(2026, 6, 20), vm.SelectedDate);
        AssertShows(mini, new DateTime(2026, 6, 20), CalendarMode.Year);
        return Task.CompletedTask;
    });

    [Theory]
    [Trait("TestCase", "T424")]
    [InlineData(MonthView, CalendarMode.Year)]
    [InlineData(YearView, CalendarMode.Decade)]
    public Task A_change_of_language_keeps_months_or_years(int view, CalendarMode mode) => InMiniAppAsync((window, mini) =>
    {
        var vm = Vm(window);
        var day = new DateTime(2026, 5, 20);
        Show(window, view, day);

        L.Apply(L.Language == "en" ? "it" : "en");
        Settle(window);
        AssertShows(mini, day, mode);
        Assert.Equal(day, vm.SelectedDate);
        if (view == MonthView) Assert.StartsWith(L.Culture.DateTimeFormat.AbbreviatedMonthNames[4], Cells(mini)[4].Content?.ToString(), StringComparison.OrdinalIgnoreCase);
        return Task.CompletedTask;
    });
}
