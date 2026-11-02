using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GGCalendar.App.ViewModels;
using GGCalendar.App.Views;
using GGCalendar.Core.Localization;
using GGCalendar.Core.Models;
using GGCalendar.Core.Tests.Support;
using GGCalendar.Data;
using static GGCalendar.Core.Tests.Support.AppDriver;

namespace GGCalendar.Core.Tests;

/// <summary>
/// T355-T358: how long "Snooze" puts a reminder off (issue #48): the default of the settings, the lengths an alarm offers in its window and the guard against
/// clicks that were meant for something else. T359-T360 (the alarm that comes back) are in AlarmsUiTests.
/// </summary>
[Collection(CollectionName)]
public class SnoozeTests
{
    private static readonly int[] Lengths = [5, 10, 15, 30, 60];

    // ---- T355: what is stored ----

    [Fact]
    [Trait("TestCase", "T355")]
    public void TheDefaultSnoozeIsTenMinutesAndAnOldFileOrAWrongValueGetsIt()
    {
        Assert.Equal(10, new AppSettings().SnoozeMinutes);
        Assert.Equal(Lengths, AppSettings.SnoozeChoices);

        Assert.Equal(10, JsonSerializer.Deserialize<AppSettings>("{\"Theme\":1}")!.SnoozeLength); // a file written before the option existed
        foreach (var wrong in new[] { 0, -5, 7, 61, 1000 })
            Assert.Equal(10, new AppSettings { SnoozeMinutes = wrong }.SnoozeLength);
        foreach (var length in Lengths)
            Assert.Equal(length, new AppSettings { SnoozeMinutes = length }.SnoozeLength);
    }

    [Fact]
    [Trait("TestCase", "T355")]
    public void TheSnoozeIsKeptInTheFileOfTheSettings()
    {
        var settingsFile = new SettingsStore().FilePath;
        var original = File.Exists(settingsFile) ? File.ReadAllBytes(settingsFile) : null;
        try
        {
            var store = new SettingsStore();
            var settings = store.Load();
            settings.SnoozeMinutes = 30;
            store.Save(settings);
            Assert.Equal(30, new SettingsStore().Load().SnoozeLength);
        }
        finally
        {
            if (original is not null) File.WriteAllBytes(settingsFile, original);
        }
    }

    [Fact]
    [Trait("TestCase", "T355")]
    public void TheTextsOfTheSnoozeAreInEveryLanguageWithTheLengthInThem()
    {
        // Read from the tables, not through the language of the app: other tests change it while this one runs.
        foreach (var code in new[] { "it", "en", "de", "es", "fr" })
        {
            var texts = L.Texts(code);
            Assert.Contains("{0}", texts["reminder.snooze"]); // no length written in the text any more
            Assert.DoesNotContain("5", texts["reminder.snooze"]);
            Assert.Contains("{0}", texts["snooze.choice"]);
            Assert.Contains("{0}", texts["reminder.snoozed_status"]);
            Assert.Contains("{1}", texts["reminder.snoozed_status"]);
            foreach (var key in new[] { "reminder.snooze_for", "settings.snooze_default" })
                Assert.False(string.IsNullOrWhiteSpace(texts[key]), code + " " + key);
        }
    }

    // ---- T356: the settings window ----

    private static async Task<SettingsWindow> OpenSettingsAsync(MainWindow window)
    {
        Click(FindButton(window, "Main.Settings"));
        return await WaitForAsync(() => window.OwnedWindows.OfType<SettingsWindow>().FirstOrDefault(), "la finestra delle impostazioni");
    }

    [Fact]
    [Trait("TestCase", "T356")]
    public Task TheSettingsOfferTheLengthsAndKeepTheOneChosenOnlyWhenSaved() => HeadlessApp.RunAsync(async () =>
    {
        var settingsFile = new SettingsStore().FilePath;
        var original = File.Exists(settingsFile) ? File.ReadAllBytes(settingsFile) : null;
        var window = OpenApp();
        try
        {
            Vm(window).Settings.SnoozeMinutes = 10;

            // Cancel: nothing changes.
            var dialog = await OpenSettingsAsync(window);
            var box = (ComboBox)FindById(dialog, "Settings.Snooze");
            var options = ((IEnumerable<ChoiceOption<int>>)box.ItemsSource!).ToList();
            Assert.Equal(Lengths, options.Select(o => o.Value));
            Assert.Equal(Lengths.Select(m => L.T("snooze.choice", m)), options.Select(o => o.Label));
            Assert.Equal(10, ((ChoiceOption<int>)box.SelectedItem!).Value);
            box.SelectedItem = options.First(o => o.Value == 60);
            Click(FindButton(dialog, "Settings.Cancel"));
            await WaitForAsync(() => window.OwnedWindows.Count == 0 ? window : null, "la chiusura delle impostazioni");
            Assert.Equal(10, new SettingsStore().Load().SnoozeLength);
            Assert.Equal(10, Vm(window).Settings.SnoozeLength);

            // Save: kept, and the app uses it at once.
            dialog = await OpenSettingsAsync(window);
            box = (ComboBox)FindById(dialog, "Settings.Snooze");
            box.SelectedItem = ((IEnumerable<ChoiceOption<int>>)box.ItemsSource!).First(o => o.Value == 30);
            Dispatcher.UIThread.RunJobs();
            Click(FindButton(dialog, "Settings.Save"));
            await WaitForAsync(() => window.OwnedWindows.Count == 0 ? window : null, "la chiusura delle impostazioni");
            Assert.Equal(30, new SettingsStore().Load().SnoozeLength);
            Assert.Equal(30, Vm(window).Settings.SnoozeLength);
        }
        finally
        {
            window.Close();
            if (original is not null) File.WriteAllBytes(settingsFile, original);
        }
    });

    // ---- T357: the window of an alarm and of a popup ----

    private static CalendarEvent Ev() =>
        new("e1", "c1", "Prova posticipo", new DateTime(2026, 11, 2, 10, 0, 0), new DateTime(2026, 11, 2, 11, 0, 0)) { Reminders = [] };

    private static ReminderNotificationWindow Show(bool alarm, int snoozeMinutes)
    {
        var toast = new ReminderNotificationWindow(Ev(), true, alarm, snoozeMinutes);
        toast.Show();
        Dispatcher.UIThread.RunJobs();
        return toast;
    }

    private static List<Button> Chips(Visual toast) =>
        toast.GetVisualDescendants().OfType<Button>().Where(b => Avalonia.Automation.AutomationProperties.GetAutomationId(b)?.StartsWith("Reminder.SnoozeChoice.") == true).ToList();

    private static bool IsOpen(ReminderNotificationWindow toast) => Toasts().Contains(toast);

    [Fact]
    [Trait("TestCase", "T357")]
    public Task AnAlarmOffersTheDefaultAndEveryLengthAndEachChipPutsItOffAtOnce() => HeadlessApp.RunAsync(() =>
    {
        foreach (var length in Lengths)
        {
            var toast = Show(alarm: true, snoozeMinutes: 10);
            try
            {
                var big = FindButton(toast, "Reminder.Snooze");
                Assert.Equal(L.T("reminder.snooze", 10), big.Content);
                Assert.Equal(Lengths.Select(m => "Reminder.SnoozeChoice." + m), Chips(toast).Select(c => Avalonia.Automation.AutomationProperties.GetAutomationId(c)));
                Assert.Equal(Lengths.Select(m => (object)L.T("snooze.choice", m)), Chips(toast).Select(c => c.Content));

                Click(FindButton(toast, "Reminder.SnoozeChoice." + length));

                Assert.False(IsOpen(toast), "la notifica doveva chiudersi");
                Assert.Equal((ReminderAction.Snooze, length), (toast.Result, toast.SnoozeMinutes));
            }
            finally
            {
                toast.Close();
            }
        }

        return Task.CompletedTask;
    });

    [Fact]
    [Trait("TestCase", "T357")]
    public Task TheBigButtonUsesTheDefaultAndTurningOffIsAnotherButton() => HeadlessApp.RunAsync(() =>
    {
        var toast = Show(alarm: true, snoozeMinutes: 15);
        try
        {
            Click(FindButton(toast, "Reminder.Snooze"));
            Assert.Equal((ReminderAction.Snooze, 15), (toast.Result, toast.SnoozeMinutes));
        }
        finally
        {
            toast.Close();
        }

        toast = Show(alarm: true, snoozeMinutes: 15);
        try
        {
            Click(FindButton(toast, "Reminder.Dismiss"));
            Assert.Equal(ReminderAction.Dismiss, toast.Result);
            Assert.False(IsOpen(toast));
        }
        finally
        {
            toast.Close();
        }

        return Task.CompletedTask;
    });

    [Fact]
    [Trait("TestCase", "T357")]
    public Task TheChipsStayWhereTheyAreWhateverTheDefaultIs() => HeadlessApp.RunAsync(() =>
    {
        var positions = new List<double[]>();
        var bold = new List<int?>();
        foreach (var length in Lengths)
        {
            var toast = Show(alarm: true, snoozeMinutes: length);
            try
            {
                var chips = Chips(toast);
                positions.Add(chips.Select(c => c.TranslatePoint(new Avalonia.Point(0, 0), toast)!.Value.X).ToArray());
                bold.Add(chips.Select((c, i) => (c, i)).Where(x => x.c.FontWeight == FontWeight.Bold).Select(x => (int?)Lengths[x.i]).SingleOrDefault());
            }
            finally
            {
                toast.Close();
            }
        }

        Assert.All(positions, p => Assert.Equal(positions[0], p)); // the same places for every default
        Assert.Equal(positions[0].Order(), positions[0]); // in order, from the shortest on the left
        Assert.Equal(Lengths.Select(m => (int?)m), bold); // only the default one is marked
        return Task.CompletedTask;
    });

    [Fact]
    [Trait("TestCase", "T358")]
    public Task APopupHasOnlyTheButtonOfTheDefault() => HeadlessApp.RunAsync(() =>
    {
        var toast = Show(alarm: false, snoozeMinutes: 30);
        try
        {
            Assert.Empty(Chips(toast));
            Assert.Equal(L.T("reminder.snooze", 30), FindButton(toast, "Reminder.Snooze").Content);
            Click(FindButton(toast, "Reminder.Snooze"));
            Assert.Equal((ReminderAction.Snooze, 30), (toast.Result, toast.SnoozeMinutes));
        }
        finally
        {
            toast.Close();
        }

        return Task.CompletedTask;
    });

    // ---- T358: the guard time ----

    [Fact]
    [Trait("TestCase", "T358")]
    public Task AClickInTheFirstMomentsIsIgnoredOnEveryButton() => HeadlessApp.RunAsync(async () =>
    {
        var guard = ReminderNotificationWindow.GuardTime;
        ReminderNotificationWindow.GuardTime = TimeSpan.FromSeconds(1.5);
        var toast = Show(alarm: true, snoozeMinutes: 10);
        try
        {
            foreach (var id in new[] { "Reminder.Snooze", "Reminder.SnoozeChoice.60", "Reminder.Dismiss" })
            {
                Click(FindButton(toast, id));
                Assert.True(IsOpen(toast), id + " ha chiuso la notifica troppo presto");
                Assert.Equal(ReminderAction.Dismiss, toast.Result);
                Assert.Equal(0, toast.SnoozeMinutes);
            }

            await PauseAsync(TimeSpan.FromSeconds(1.6));
            Click(FindButton(toast, "Reminder.SnoozeChoice.60"));
            Assert.False(IsOpen(toast));
            Assert.Equal((ReminderAction.Snooze, 60), (toast.Result, toast.SnoozeMinutes));
        }
        finally
        {
            ReminderNotificationWindow.GuardTime = guard;
            toast.Close();
        }
    });
}
