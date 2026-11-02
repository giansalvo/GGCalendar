using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using GGCalendar.App.ViewModels;
using GGCalendar.App.Views;
using GGCalendar.Core.Localization;
using GGCalendar.Core.Tests.Support;
using GGCalendar.Data;
using static GGCalendar.Core.Tests.Support.AppDriver;

namespace GGCalendar.Core.Tests;

/// <summary>The small calendar on the left follows the day chosen with Today, the arrows and the views (issue #52): no Google account and no network.</summary>
[Collection(CollectionName)]
public class MiniCalendarUiTests
{
    private static Task InMiniAppAsync(Func<MainWindow, Calendar, Task> test) => HeadlessApp.RunAsync(async () =>
    {
        var settingsFile = new SettingsStore().FilePath;
        var original = File.Exists(settingsFile) ? File.ReadAllBytes(settingsFile) : null;
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

    private static void AssertShows(Calendar mini, DateTime day)
    {
        Assert.Equal(CalendarMode.Month, mini.DisplayMode);
        Assert.Equal(day.Year, mini.DisplayDate.Year);
        Assert.Equal(day.Month, mini.DisplayDate.Month);
    }

    [Fact]
    [Trait("TestCase", "T332")]
    public Task Today_after_browsing_the_small_calendar_shows_the_current_month() => InMiniAppAsync((window, mini) =>
    {
        var vm = Vm(window);
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
    [InlineData(5)]
    public Task Today_after_the_arrows_of_each_view_shows_the_current_month(int view) => InMiniAppAsync((window, mini) =>
    {
        var vm = Vm(window);
        vm.ViewModeIndex = view;
        Press(vm, "today");
        for (var i = 0; i < 10; i++) Press(vm, "next");
        Press(vm, "today");
        AssertShows(mini, DateTime.Today);
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
        AssertShows(mini, DateTime.Today.AddMonths(1));
        Press(vm, "next");
        AssertShows(mini, DateTime.Today.AddMonths(2));
        Press(vm, "previous");
        AssertShows(mini, DateTime.Today.AddMonths(1));

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
        Press(vm, "today");
        mini.DisplayMode = CalendarMode.Year;
        Pump();

        Press(vm, "today");
        AssertShows(mini, DateTime.Today);
        mini.DisplayMode = CalendarMode.Decade;
        Pump();
        Press(vm, "today");
        AssertShows(mini, DateTime.Today);
        return Task.CompletedTask;
    });

    [Fact]
    [Trait("TestCase", "T335")]
    public Task Today_with_the_search_open_closes_it_and_shows_the_current_month() => InMiniAppAsync((window, mini) =>
    {
        var vm = Vm(window);
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
        AssertShows(mini, shown);
        return Task.CompletedTask;
    });
}
