using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GGCalendar.App.ViewModels;
using GGCalendar.App.Views;
using GGCalendar.Core.Tests.Support;
using GGCalendar.Data;
using static GGCalendar.Core.Tests.Support.AppDriver;

namespace GGCalendar.Core.Tests;

/// <summary>What needs no Google account: a local calendar with several events (T08) and the Settings window (T09).</summary>
[Collection(CollectionName)]
[Trait("Category", "RealGoogle")]
public class LocalUiTests
{
    [Fact]
    [Trait("TestCase", "T284")]
    public Task T284_ALocalCalendarWithSeveralEventsKeepsThemAndSendsNothingToGoogle() => InAppAsync(async window =>
    {
        var name = NewTitle();
        var calendar = await CreateLocalCalendarAsync(window, name);
        Assert.Contains(Vm(window).Calendars, c => c.Id == calendar && c.IsLocal && c.Name == name);

        var today = DateTime.Today;
        var timed = NewTitle() + " ora";
        var allDay = NewTitle() + " giorno";
        var weekly = NewTitle() + " settimana";
        await CreateEventAsync(window, new EventSpec(timed) { CalendarId = calendar, Start = today.AddHours(14) });
        await CreateEventAsync(window, new EventSpec(allDay) { CalendarId = calendar, AllDay = true, Start = today, LastDay = today });
        await CreateEventAsync(window, new EventSpec(weekly) { CalendarId = calendar, Start = today.AddHours(16), Recurrence = "FREQ=WEEKLY" });

        void AssertShown(MainWindow w, int visible)
        {
            Assert.Equal(visible, AgendaDayCount(w, today, timed));
            Assert.Equal(visible, AgendaDayCount(w, today, allDay));
            Assert.Equal(visible * 5, AgendaDayCount(w, today, weekly)); // every 7 days in the 30 shown
        }

        AssertShown(window, 1);
        Assert.DoesNotContain(new CalendarStore().GetPending(), p => p.CalendarId == calendar); // nothing is queued for Google

        window = await ReopenAsync(window);
        AssertShown(window, 1);

        // Hiding the calendar hides its events; showing it brings them back.
        var toggle = window.GetVisualDescendants().OfType<Button>().First(b =>
            Avalonia.Automation.AutomationProperties.GetAutomationId(b) == "Calendar.Toggle" && b.DataContext is CalendarItemViewModel { } item && item.Id == calendar);
        Click(toggle);
        AssertShown(window, 0);
        Click(toggle);
        AssertShown(window, 1);
    });

    // Settings

    private static async Task<SettingsWindow> OpenSettingsAsync(MainWindow window)
    {
        Click(FindButton(window, "Main.Settings"));
        return await WaitForAsync(() => window.OwnedWindows.OfType<SettingsWindow>().FirstOrDefault(), "la finestra delle impostazioni");
    }

    private static void Check(Visual dialog, string automationId, bool value) =>
        ((CheckBox)FindById(dialog, automationId)).IsChecked = value;

    /// <summary>Changes four options the way a user would: 24-hour clock, week numbers, start of the day and theme.</summary>
    private static void ChangeOptions(Visual dialog, bool use24Hour, bool weekNumbers, int dayStart, int theme)
    {
        Check(dialog, "Settings.Use24HourClock", use24Hour);
        Check(dialog, "Settings.ShowWeekNumbers", weekNumbers);
        ((ComboBox)FindById(dialog, "Settings.DayStartHour")).SelectedItem = dayStart;
        ((ComboBox)FindById(dialog, "Settings.ThemeIndex")).SelectedIndex = theme;
        Dispatcher.UIThread.RunJobs();
    }

    [Fact]
    [Trait("TestCase", "T09")]
    public Task T09_TheSettingsWindowAppliesWhatIsSavedAndIgnoresWhatIsCancelled() => InAppAsync(async window =>
    {
        // An event at 15:00 shows how the clock setting changes what is read.
        var calendar = await CreateLocalCalendarAsync(window, NewTitle());
        var title = NewTitle();
        var today = DateTime.Today;
        await CreateEventAsync(window, new EventSpec(title) { CalendarId = calendar, Start = today.AddHours(15) });
        string TimeShown(MainWindow w)
        {
            var vm = Vm(w);
            vm.ViewModeIndex = 4;
            vm.SelectedDate = today;
            Dispatcher.UIThread.RunJobs();
            return vm.AgendaDays.SelectMany(d => d.Items).First(i => i.Title == title).Time;
        }

        var original = Vm(window).Settings;
        var (h24, weeks, start, theme) = (original.Use24HourClock, original.ShowWeekNumbers, original.DayStartHour, original.Theme);
        var newTheme = theme == 2 ? 1 : 2;
        var timeBefore = TimeShown(window);
        var fileBefore = File.ReadAllBytes(new SettingsStore().FilePath);

        // Changed and cancelled: nothing changes, not even the file.
        var dialog = await OpenSettingsAsync(window);
        ChangeOptions(dialog, !h24, !weeks, start + 1, newTheme);
        Click(FindButton(dialog, "Settings.Cancel"));
        await WaitForAsync(() => window.OwnedWindows.Count == 0 ? window : null, "la chiusura delle impostazioni");
        var vm = Vm(window);
        Assert.Equal((h24, weeks, start, theme), (vm.Settings.Use24HourClock, vm.Settings.ShowWeekNumbers, vm.Settings.DayStartHour, vm.Settings.Theme));
        Assert.Equal(timeBefore, TimeShown(window));
        Assert.Equal(fileBefore, File.ReadAllBytes(new SettingsStore().FilePath));

        // Changed and saved: it applies at once and survives closing and opening the app.
        dialog = await OpenSettingsAsync(window);
        ChangeOptions(dialog, !h24, !weeks, start + 1, newTheme);
        Click(FindButton(dialog, "Settings.Save"));
        await WaitForAsync(() => window.OwnedWindows.Count == 0 ? window : null, "la chiusura delle impostazioni");
        vm = Vm(window);
        Assert.Equal((!h24, !weeks, start + 1, newTheme), (vm.Settings.Use24HourClock, vm.Settings.ShowWeekNumbers, vm.Settings.DayStartHour, vm.Settings.Theme));
        Assert.Equal(newTheme == 2 ? ThemeVariant.Dark : ThemeVariant.Light, Application.Current!.RequestedThemeVariant);
        var timeAfter = TimeShown(window);
        Assert.NotEqual(timeBefore, timeAfter); // "15:00" and "3:00 PM" are different ways to write the same hour
        Assert.NotEqual(fileBefore, File.ReadAllBytes(new SettingsStore().FilePath));

        window = await ReopenAsync(window);
        vm = Vm(window);
        Assert.Equal((!h24, !weeks, start + 1, newTheme), (vm.Settings.Use24HourClock, vm.Settings.ShowWeekNumbers, vm.Settings.DayStartHour, vm.Settings.Theme));
        Assert.Equal(timeAfter, TimeShown(window));
        // InAppAsync puts the original settings file back when the test ends.
    });
}
