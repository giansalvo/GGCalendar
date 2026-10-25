using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GGCalendar.App.Controls;
using GGCalendar.App.ViewModels;
using GGCalendar.App.Views;
using GGCalendar.Core.Models;
using GGCalendar.Core.Tests.Support;
using GGCalendar.Core.Zones;
using GGCalendar.Data;
using static GGCalendar.Core.Tests.Support.AppDriver;

namespace GGCalendar.Core.Tests;

/// <summary>
/// T137-T148: the second time zone on the real app (no Google account needed): the choice in the Settings, the column of hours in
/// the grids, the hours in the Schedule, in the card of an event and in the editor. The second zone is chosen relative to the zone of
/// this PC, and the hours expected are computed with TimeZoneInfo, so the tests give the same answer on any PC.
/// </summary>
[Collection(CollectionName)]
public class SecondZoneUiTests
{
    private static string OtherZoneId => TimeZoneInfo.Local.Id == "Tokyo Standard Time" ? "Pacific Standard Time" : "Tokyo Standard Time";

    private static TimeZoneInfo OtherZone => TimeZoneInfo.FindSystemTimeZoneById(OtherZoneId);

    private static async Task<SettingsWindow> OpenSettingsAsync(MainWindow window)
    {
        Click(FindButton(window, "Main.Settings"));
        return await WaitForAsync(() => window.OwnedWindows.OfType<SettingsWindow>().FirstOrDefault(), "la finestra delle impostazioni");
    }

    /// <summary>Chooses the second zone (null = none) and the name of its column in the Settings, as a user would, and saves.</summary>
    private static async Task ChooseSecondZoneAsync(MainWindow window, string? zoneId, string? label = null, bool save = true)
    {
        var dialog = await OpenSettingsAsync(window);
        var combo = (ComboBox)FindById(dialog, "Settings.SecondaryTimeZone");
        combo.SelectedItem = combo.ItemsSource!.Cast<TimeZoneOption>().First(o => o.Id == (zoneId ?? ""));
        Dispatcher.UIThread.RunJobs();
        if (label is not null) ((TextBox)FindById(dialog, "Settings.SecondaryTimeZoneLabel")).Text = label;
        Click(FindButton(dialog, save ? "Settings.Save" : "Settings.Cancel"));
        await WaitForAsync(() => window.OwnedWindows.Count == 0 ? window : null, "la chiusura delle impostazioni");
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Runs on the app with the settings of the test folder put back when it ends.</summary>
    private static Task InLocalAppAsync(Func<MainWindow, Task> test) => HeadlessApp.RunAsync(async () =>
    {
        var settingsFile = new SettingsStore().FilePath;
        var original = File.Exists(settingsFile) ? File.ReadAllBytes(settingsFile) : null;
        var window = OpenApp();
        try
        {
            await test(window);
        }
        finally
        {
            (CurrentWindow ?? window).Close();
            if (original is not null) File.WriteAllBytes(settingsFile, original);
        }
    });

    private static WeekGrid GridOf(MainWindow w) => w.FindControl<WeekGrid>("Grid")!;

    private static AllDayStrip StripOf(MainWindow w) => w.FindControl<AllDayStrip>("AllDay")!;

    [Fact]
    [Trait("TestCase", "T137")]
    public Task T137_TheSecondZoneAndItsNameAreSavedAndKeptAfterReopening() => InLocalAppAsync(async window =>
    {
        await ChooseSecondZoneAsync(window, OtherZoneId, "Casa");
        var settings = new SettingsStore().Load();
        Assert.Equal((OtherZoneId, "Casa"), (settings.SecondaryTimeZoneId, settings.SecondaryTimeZoneLabel));

        window = await ReopenAsync(window);
        Assert.Equal(new SecondZone(OtherZoneId, "Casa"), Vm(window).SecondZone);

        // The same zone as the PC is not kept, and "none" empties it; the name typed is cut to six characters.
        await ChooseSecondZoneAsync(window, TimeZoneInfo.Local.Id, "x");
        Assert.Null(new SettingsStore().Load().SecondaryTimeZoneId);
        Assert.Null(Vm(window).SecondZone);
        await ChooseSecondZoneAsync(window, OtherZoneId, "abcdefghijk");
        Assert.Equal("abcdef", new SettingsStore().Load().SecondaryTimeZoneLabel);
        await ChooseSecondZoneAsync(window, null);
        Assert.Null(new SettingsStore().Load().SecondaryTimeZoneId);
    });

    [Fact]
    [Trait("TestCase", "T138")]
    public Task T138_WithoutASecondZoneTheGridHasOneColumnOfHours() => InLocalAppAsync(async window =>
    {
        await ChooseSecondZoneAsync(window, null);
        var vm = Vm(window);
        foreach (var mode in new[] { 0, 1, 5 }) // Day, Week, 6 days
        {
            vm.ViewModeIndex = mode;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(WeekGrid.BaseGutter, GridOf(window).Gutter);
            Assert.Equal(WeekGrid.BaseGutter, StripOf(window).Gutter);
            Assert.False(vm.HasSecondZone);
            Assert.False(FindById(window, "Main.SecondZoneLabel").IsVisible);
        }

        Assert.Equal(WeekGrid.BaseGutter, vm.DaysMargin.Left);
        Assert.Equal(SecondZones.OffsetLabel(TimeZoneInfo.Local, DateTime.Today), vm.TimeZoneLabel);
    });

    [Fact]
    [Trait("TestCase", "T139")]
    public Task T139_WithASecondZoneTheGridsOfDay6DaysAndWeekHaveASecondColumn() => InLocalAppAsync(async window =>
    {
        await ChooseSecondZoneAsync(window, OtherZoneId);
        var vm = Vm(window);
        var width = WeekGrid.BaseGutter + WeekGrid.ZoneColumnWidth;
        foreach (var mode in new[] { 0, 5, 1 }) // Day, 6 days, Week
        {
            vm.ViewModeIndex = mode;
            Dispatcher.UIThread.RunJobs();
            Assert.True(vm.IsTimeGrid);
            Assert.Equal(width, GridOf(window).Gutter);
            Assert.Equal(SecondZones.DefaultLabel(OtherZoneId), GridOf(window).SecondZone?.Label);
            Assert.True(FindById(window, "Main.SecondZoneLabel").IsVisible);
            Assert.Equal(SecondZones.DefaultLabel(OtherZoneId), ((TextBlock)FindById(window, "Main.SecondZoneLabel")).Text);
        }

        // Month and Year are not grids of hours: no column there.
        foreach (var mode in new[] { 2, 3, 4 })
        {
            vm.ViewModeIndex = mode;
            Dispatcher.UIThread.RunJobs();
            Assert.False(vm.IsTimeGrid);
        }
    });

    [Fact]
    [Trait("TestCase", "T140")]
    public Task T140_ColumnsHeadersAndTheStripOfAllDayEventsStayAligned() => InLocalAppAsync(async window =>
    {
        await ChooseSecondZoneAsync(window, OtherZoneId);
        var vm = Vm(window);
        vm.ViewModeIndex = 1;
        Dispatcher.UIThread.RunJobs();

        var width = WeekGrid.BaseGutter + WeekGrid.ZoneColumnWidth;
        Assert.Equal(width, vm.TimeAxisWidth);
        Assert.Equal(width, GridOf(window).Gutter); // the day columns start here...
        Assert.Equal(width, StripOf(window).Gutter); // ...and so do the all-day events...
        Assert.Equal(width, vm.DaysMargin.Left);     // ...and the names of the days
        Assert.Equal(12, vm.DaysMargin.Right);

        // Without the zone everything goes back by the same amount.
        await ChooseSecondZoneAsync(window, null);
        Assert.Equal((WeekGrid.BaseGutter, WeekGrid.BaseGutter, WeekGrid.BaseGutter), (GridOf(window).Gutter, StripOf(window).Gutter, vm.DaysMargin.Left));
    });

    [Fact]
    [Trait("TestCase", "T142")]
    public Task T142_TheScheduleWritesTheHoursInTheSecondZoneNextToTheOnesOfThePc() => InLocalAppAsync(async window =>
    {
        await ChooseSecondZoneAsync(window, OtherZoneId, "Tk");
        var vm = Vm(window);

        // Only a calendar made for the test is shown, so that what other tests left in the data is not listed.
        var states = vm.Calendars.ToDictionary(c => c.Id, c => c.IsVisible);
        var calendar = vm.CreateLocalCalendar(NewTitle());
        foreach (var c in vm.Calendars) c.IsVisible = c.Id == calendar;
        try
        {
            var day = DateTime.Today.AddDays(1);
            var timed = new CalendarEvent(Guid.NewGuid().ToString("N"), calendar, NewTitle(), day.AddHours(14), day.AddHours(15));
            var allDay = new CalendarEvent(Guid.NewGuid().ToString("N"), calendar, NewTitle(), day, day.AddDays(1), IsAllDay: true);
            vm.AddEvent(timed);
            vm.AddEvent(allDay);
            vm.ViewModeIndex = 4;
            vm.SelectedDate = DateTime.Today;
            Dispatcher.UIThread.RunJobs();

            var items = vm.AgendaDays.SelectMany(d => d.Items).ToList();
            var timedItem = items.Single(i => i.Event.Id == timed.Id);
            var allDayItem = items.Single(i => i.Event.Id == allDay.Id);
            Assert.True(timedItem.HasSecondTime);
            Assert.StartsWith("Tk", timedItem.SecondTime);
            Assert.EndsWith(SecondZones.Range(timed.Start, timed.End, TimeZoneInfo.Local, OtherZone, vm.Settings.Use24HourClock, GGCalendar.Core.Localization.L.Culture), timedItem.SecondTime);
            Assert.False(allDayItem.HasSecondTime); // an all-day event has no hours

            await ChooseSecondZoneAsync(window, null); // without a second zone the line is as it was
            Assert.All(vm.AgendaDays.SelectMany(d => d.Items), i => Assert.False(i.HasSecondTime));
        }
        finally
        {
            vm.DeleteLocalCalendar(calendar);
            foreach (var c in vm.Calendars.Where(c => states.ContainsKey(c.Id)).ToList()) c.IsVisible = states[c.Id];
        }
    });

    [Fact]
    [Trait("TestCase", "T143")]
    public Task T143_ChangingTheZoneAppliesAtOnceAndCancelChangesNothing() => InLocalAppAsync(async window =>
    {
        var vm = Vm(window);
        await ChooseSecondZoneAsync(window, OtherZoneId, "Tk", save: false); // changed and cancelled
        Assert.Null(vm.SecondZone);
        Assert.Null(new SettingsStore().Load().SecondaryTimeZoneId);

        vm.ViewModeIndex = 1;
        await ChooseSecondZoneAsync(window, OtherZoneId, "Tk"); // saved: the column is there at once
        Assert.Equal(WeekGrid.BaseGutter + WeekGrid.ZoneColumnWidth, GridOf(window).Gutter);
        Assert.Equal("Tk", ((TextBlock)FindById(window, "Main.SecondZoneLabel")).Text);

        await ChooseSecondZoneAsync(window, null); // taken away: it goes at once
        Assert.Equal(WeekGrid.BaseGutter, GridOf(window).Gutter);
        Assert.False(FindById(window, "Main.SecondZoneLabel").IsVisible);
    });

    [Fact]
    [Trait("TestCase", "T145")]
    public Task T145_TheCardOfAnEventShowsItsHoursInTheSecondZone() => InLocalAppAsync(async window =>
    {
        await ChooseSecondZoneAsync(window, OtherZoneId, "Tk");
        var vm = Vm(window);
        var day = DateTime.Today.AddDays(1);
        var timed = new CalendarEvent("x1", "c", "t", day.AddHours(14), day.AddHours(15));
        var allDay = new CalendarEvent("x2", "c", "t", day, day.AddDays(1), IsAllDay: true);

        var text = vm.SecondZoneText(timed)!;
        Assert.StartsWith(GGCalendar.Core.Localization.L.T("time.there", "Tk", ""), text);
        Assert.Contains(SecondZones.Range(timed.Start, timed.End, TimeZoneInfo.Local, OtherZone, vm.Settings.Use24HourClock, GGCalendar.Core.Localization.L.Culture), text);
        Assert.Null(vm.SecondZoneText(allDay));

        var card = new EventDetailsCard(timed, null, "#1A73E8", "cal", "acc", true, false, text);
        Assert.Contains(card.GetVisualDescendants().OfType<TextBlock>(), t => t.Name == "SecondZoneText" && t.Text == text);
        var without = new EventDetailsCard(timed, null, "#1A73E8", "cal", "acc", true, false);
        Assert.DoesNotContain(without.GetVisualDescendants().OfType<TextBlock>(), t => t.Name == "SecondZoneText");

        await ChooseSecondZoneAsync(window, null);
        Assert.Null(vm.SecondZoneText(timed));
        await Task.CompletedTask;
    });

    [Fact]
    [Trait("TestCase", "T146")]
    public Task T146_TheEditorShowsTheHoursInTheSecondZoneAndFollowsWhatIsTyped() => InLocalAppAsync(async window =>
    {
        await ChooseSecondZoneAsync(window, OtherZoneId, "Tk");
        ChooseCreateMenuItem(window, "Menu.CreateEvent");
        var editor = await WaitForAsync(() => window.OwnedWindows.OfType<EventEditWindow>().FirstOrDefault(), "l'editor");
        var edit = (EventEditViewModel)editor.DataContext!;
        var line = FindById(editor, "Edit.SecondZone");

        edit.IsAllDay = false;
        edit.StartDate = DateTime.Today.AddDays(2);
        edit.EndDate = DateTime.Today.AddDays(2);
        edit.StartTime = TimeSpan.FromHours(10);
        edit.EndTime = TimeSpan.FromHours(11);
        Dispatcher.UIThread.RunJobs();
        var expected = SecondZones.Range(edit.StartDate!.Value + edit.StartTime!.Value, edit.EndDate!.Value + edit.EndTime!.Value, TimeZoneInfo.Local, OtherZone, edit.Use24Hour, GGCalendar.Core.Localization.L.Culture);
        Assert.True(line.IsVisible);
        Assert.Contains(expected, edit.SecondZoneText);
        Assert.StartsWith(GGCalendar.Core.Localization.L.T("time.there", "Tk", ""), edit.SecondZoneText);

        edit.StartTime = TimeSpan.FromHours(16); // the end moves with the start (an hour later): the line follows
        edit.EndTime = TimeSpan.FromHours(17);
        Dispatcher.UIThread.RunJobs();
        var moved = SecondZones.Range(edit.StartDate.Value + edit.StartTime.Value, edit.EndDate.Value + edit.EndTime.Value, TimeZoneInfo.Local, OtherZone, edit.Use24Hour, GGCalendar.Core.Localization.L.Culture);
        Assert.Contains(moved, edit.SecondZoneText);

        edit.IsAllDay = true; // an all-day event has no hours
        Dispatcher.UIThread.RunJobs();
        Assert.False(line.IsVisible);

        await CancelEditorAsync(window, editor);
    });

    [Fact]
    [Trait("TestCase", "T148")]
    public Task T148_TheSettingsHaveNoPrimaryZoneAndAnOldSettingsFileStillLoads() => InLocalAppAsync(async window =>
    {
        var dialog = await OpenSettingsAsync(window);
        Assert.NotNull(FindById(dialog, "Settings.SecondaryTimeZone"));
        Assert.DoesNotContain(dialog.GetVisualDescendants().OfType<Control>(), c => Avalonia.Automation.AutomationProperties.GetAutomationId(c) == "Settings.PrimaryTimeZone");
        Click(FindButton(dialog, "Settings.Cancel"));
        await WaitForAsync(() => window.OwnedWindows.Count == 0 ? window : null, "la chiusura delle impostazioni");

        // A file written by an earlier version has a primary zone: it is ignored, the rest is read.
        var file = Path.Combine(Path.GetTempPath(), "ggcal_old_settings_" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(file, """{"PrimaryTimeZoneId":"Tokyo Standard Time","SecondaryTimeZoneId":"Eastern Standard Time","DayStartHour":9}""");
        try
        {
            var loaded = new SettingsStore(file).Load();
            Assert.Equal(("Eastern Standard Time", 9), (loaded.SecondaryTimeZoneId, loaded.DayStartHour));
        }
        finally
        {
            File.Delete(file);
        }
    });
}
