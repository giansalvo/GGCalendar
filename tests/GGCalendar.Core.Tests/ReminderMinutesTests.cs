using System.Globalization;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GGCalendar.App.Services;
using GGCalendar.App.ViewModels;
using GGCalendar.App.Views;
using GGCalendar.Core.Localization;
using GGCalendar.Core.Models;
using GGCalendar.Core.Reminders;
using GGCalendar.Core.Tests.Support;

namespace GGCalendar.Core.Tests;

/// <summary>Issue #33: the time of an event typed to the minute, and the reminders that say "before the event" and when they go off.</summary>
public class ReminderMinutesTests
{
    [Theory]
    [Trait("TestCase", "T380")]
    [InlineData("9:03", true, 9, 3)]
    [InlineData("903", true, 9, 3)]
    [InlineData("0903", true, 9, 3)]
    [InlineData("9.03", true, 9, 3)]
    [InlineData("9 03", true, 9, 3)]
    [InlineData("9h03", true, 9, 3)]
    [InlineData("9", true, 9, 0)]
    [InlineData("23:59", true, 23, 59)]
    [InlineData("  14:07 ", true, 14, 7)]
    [InlineData("9:03 pm", false, 21, 3)]
    [InlineData("9:03PM", false, 21, 3)]
    [InlineData("12:30 am", false, 0, 30)]
    [InlineData("12:30 pm", false, 12, 30)]
    [InlineData("9p", false, 21, 0)]
    [InlineData("13:00", false, 13, 0)] // on a 12-hour clock a 24-hour time is still understood
    public void T380_TheTimeTypedByHandIsUnderstood(string text, bool use24Hour, int hours, int minutes) =>
        Assert.Equal(new TimeSpan(hours, minutes, 0), TimeEntry.Parse(text, use24Hour));

    [Theory]
    [Trait("TestCase", "T380")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("24:00")]
    [InlineData("9:60")]
    [InlineData("12345")]
    [InlineData("9:03:15")]
    [InlineData("pm")]
    public void T380_ATextThatIsNotATimeIsRefused(string text) => Assert.Null(TimeEntry.Parse(text, true));

    [Fact]
    [Trait("TestCase", "T380")]
    public void T380_AmAndPmNeedAnHourFromOneToTwelveAndAre24HourRefused()
    {
        Assert.Null(TimeEntry.Parse("13:00 pm", false));
        Assert.Null(TimeEntry.Parse("0:30 am", false));
        Assert.Null(TimeEntry.Parse("9:03 pm", true));
    }

    [Theory]
    [Trait("TestCase", "T381")]
    [InlineData(9, 3, 1, 9, 5)]   // 9:03 goes to the next multiple of 5
    [InlineData(9, 3, -1, 9, 0)]  // and back to the previous one
    [InlineData(9, 0, 1, 9, 5)]
    [InlineData(9, 0, -1, 8, 55)]
    [InlineData(9, 55, 1, 10, 0)]
    [InlineData(9, 57, 1, 10, 0)]
    [InlineData(0, 3, -1, 0, 0)]  // does not go to the day before
    [InlineData(0, 0, -1, 0, 0)]
    [InlineData(23, 55, 1, 23, 55)] // nor to the day after
    [InlineData(23, 59, 1, 23, 59)]
    [InlineData(23, 58, -1, 23, 55)]
    public void T381_TheArrowsMoveByFiveMinutesAndStayInTheDay(int h, int m, int direction, int expectedH, int expectedM) =>
        Assert.Equal(new TimeSpan(expectedH, expectedM, 0), TimeEntry.Step(new TimeSpan(h, m, 0), direction));

    [Theory]
    [Trait("TestCase", "T381")]
    [InlineData(0, 0, true, "00:00")]
    [InlineData(9, 3, true, "09:03")]
    [InlineData(0, 5, false, "12:05 AM")]
    [InlineData(9, 3, false, "9:03 AM")]
    [InlineData(12, 0, false, "12:00 PM")]
    [InlineData(21, 30, false, "9:30 PM")]
    public void T381_TheTimeIsShownOnTheClockOfTheSettings(int h, int m, bool use24Hour, string expected) =>
        Assert.Equal(expected, TimeEntry.Format(new TimeSpan(h, m, 0), use24Hour));

    [Fact]
    [Trait("TestCase", "T382")]
    public void T382_WhenAReminderGoesOffSaysTheDayOnlyIfItIsNotTheDayOfTheEvent()
    {
        var it = CultureInfo.GetCultureInfo("it-IT");
        var start = new DateTime(2026, 11, 24, 9, 3, 0);
        Assert.Equal((null, "08:56"), TimeEntry.WhenParts(start, 7, true, it));
        Assert.Equal((null, "09:03"), TimeEntry.WhenParts(start, 0, true, it));
        Assert.Equal((null, "7:03 AM"), TimeEntry.WhenParts(start, 120, false, CultureInfo.InvariantCulture));
        Assert.Equal(("lun 23 nov", "09:03"), TimeEntry.WhenParts(start, 1440, true, it));
        Assert.Equal(("lun 23 nov", "00:00"), TimeEntry.WhenParts(new DateTime(2026, 11, 24), 1440, true, it));
        Assert.Equal((null, "00:00"), TimeEntry.WhenParts(new DateTime(2026, 11, 24), 0, true, it));
        Assert.Equal(("lun 23 nov", "22:00"), TimeEntry.WhenParts(new DateTime(2026, 11, 24), 120, true, it)); // all day: from midnight
    }

    private static Task<EventEditResult?>? _lastClosed;

    private static EventEditWindow Editor(Window owner, CalendarEvent ev, AppSettings? settings = null, Func<DateTime>? clock = null)
    {
        var calendar = new CalendarItemViewModel(new CalendarInfo("c1", "Locale", "Personale", "#1A73E8"), () => { });
        var dialog = new EventEditWindow
        {
            DataContext = new EventEditViewModel(ev, [calendar], true, settings ?? new AppSettings(), new SoundService(), clock),
        };
        _lastClosed = dialog.ShowDialog<EventEditResult?>(owner);
        Dispatcher.UIThread.RunJobs();
        return dialog;
    }

    [Fact]
    [Trait("TestCase", "T383")]
    public Task T383_TheTimeBoxTakesATypedTimeAndTheArrowsMoveItByFiveMinutes() => HeadlessApp.RunAsync(() =>
    {
        var owner = new Window();
        owner.Show();
        var start = new DateTime(2026, 11, 24, 9, 0, 0);
        var dialog = Editor(owner, new CalendarEvent("e1", "c1", "", start, start.AddMinutes(105)));
        var vm = (EventEditViewModel)dialog.DataContext!;
        var box = dialog.GetVisualDescendants().OfType<TimeBox>().First();

        Assert.Equal("09:00", box.Box.Text);

        // typed to the minute, taken when the focus leaves the box; the end follows, keeping the duration
        box.Box.Focus();
        box.Box.Text = "903";
        dialog.FindControl<TextBox>("TitleBox")!.Focus();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new TimeSpan(9, 3, 0), vm.StartTime);
        Assert.Equal("09:03", box.Box.Text);
        Assert.Equal(new TimeSpan(10, 48, 0), vm.EndTime);

        // the arrow key moves to the next multiple of 5
        box.Box.Focus();
        box.Box.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Up });
        Assert.Equal(new TimeSpan(9, 5, 0), vm.StartTime);
        Assert.Equal("09:05", box.Box.Text);

        // a text that is not a time goes back to the last good one
        box.Box.Text = "banana";
        dialog.FindControl<TextBox>("TitleBox")!.Focus();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new TimeSpan(9, 5, 0), vm.StartTime);
        Assert.Equal("09:05", box.Box.Text);

        dialog.Close();
        owner.Close();
        return Task.CompletedTask;
    });

    [Fact]
    [Trait("TestCase", "T384")]
    public Task T384_AReminderSaysBeforeTheEventAndWhenItGoesOffAndTheQuickChoicesFillIt() => HeadlessApp.RunAsync(() =>
    {
        var owner = new Window();
        owner.Show();
        var start = new DateTime(2026, 11, 24, 9, 3, 0);
        var ev = new CalendarEvent("e1", "c1", "", start, start.AddHours(1)) { Reminders = [new EventReminder(ReminderMethod.Alarm, 7)] };
        var dialog = Editor(owner, ev);
        var vm = (EventEditViewModel)dialog.DataContext!;
        var r = vm.Reminders.Single();

        Assert.Equal(L.T("edit.reminder_before"), r.Suffix);
        Assert.Equal(L.T("edit.reminder_alarm_at", L.T("edit.when_time", "08:56")), r.Preview);

        // typing another number updates both
        r.Value = 0;
        Assert.Equal(L.T("edit.reminder_at_start"), r.Suffix);
        Assert.Equal(L.T("edit.reminder_alarm_at", L.T("edit.when_time", "09:03")), r.Preview);

        // a quick choice fills number and unit, then the box is empty again
        r.Quick = ReminderItemViewModel.QuickOptions.First(o => o.Minutes == 1440);
        Assert.Equal(1m, r.Value);
        Assert.Equal(1440, r.Unit.Minutes);
        Assert.Equal(1440, r.ToModel().Minutes);
        Assert.Null(r.Quick);

        r.Quick = ReminderItemViewModel.QuickOptions.First(o => o.Minutes == 5);
        Assert.Equal(5m, r.Value);
        Assert.Equal(1, r.Unit.Minutes);

        // moving the start moves the moment; a reminder added later knows the start too
        vm.StartTime = new TimeSpan(10, 0, 0);
        Assert.Equal(L.T("edit.reminder_alarm_at", L.T("edit.when_time", "09:55")), r.Preview);
        vm.AddReminderCommand.Execute(null);
        Assert.Equal(L.T("edit.reminder_popup_at", L.T("edit.when_time", "09:50")), vm.Reminders[1].Preview);

        // all day: counted from midnight
        vm.IsAllDay = true;
        Assert.Contains("23:50", vm.Reminders[1].Preview);

        dialog.Close();
        owner.Close();
        return Task.CompletedTask;
    });

    [Fact]
    [Trait("TestCase", "T386")]
    public Task T386_EnterInTheTimeBoxConfirmsTheTimeAndDoesNotSaveTheEditor() => HeadlessApp.RunAsync(() =>
    {
        var owner = new Window();
        owner.Show();
        var start = new DateTime(2026, 11, 24, 9, 0, 0);
        var dialog = Editor(owner, new CalendarEvent("e1", "c1", "", start, start.AddHours(1)));
        var vm = (EventEditViewModel)dialog.DataContext!;
        var box = dialog.GetVisualDescendants().OfType<TimeBox>().First();

        box.Box.Focus();
        box.Box.Text = "9:07";
        box.Box.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(new TimeSpan(9, 7, 0), vm.StartTime);
        Assert.False(_lastClosed!.IsCompleted, "Invio nel campo orario non deve salvare né chiudere l'editor.");
        dialog.Close();
        owner.Close();
        return Task.CompletedTask;
    });

    [Fact]
    [Trait("TestCase", "T387")]
    public Task T387_ARemindersInThePastIsWarnedAndLeftOutWhenSaving() => HeadlessApp.RunAsync(() =>
    {
        var owner = new Window();
        owner.Show();
        var now = new DateTime(2026, 11, 24, 8, 0, 0);
        var start = new DateTime(2026, 11, 24, 9, 0, 0);
        var ev = new CalendarEvent("e1", "c1", "Prova", start, start.AddHours(1))
        {
            Reminders = [new EventReminder(ReminderMethod.Popup, 30), new EventReminder(ReminderMethod.Alarm, 90), new EventReminder(ReminderMethod.Email, 60)],
        };
        var dialog = Editor(owner, ev, clock: () => now);
        var vm = (EventEditViewModel)dialog.DataContext!;

        // 08:30 is still to come; 07:30 is gone; 08:00 is the same minute as now: not past
        Assert.False(vm.Reminders[0].IsPast);
        Assert.True(vm.Reminders[1].IsPast);
        Assert.False(vm.Reminders[2].IsPast);
        var warnings = dialog.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Text == L.T("edit.reminder_past") && t.IsEffectivelyVisible).ToList();
        Assert.Single(warnings);

        // the warning follows the number
        vm.Reminders[0].Value = 2;
        vm.Reminders[0].Unit = ReminderItemViewModel.Units.First(u => u.Minutes == 60);
        Assert.True(vm.Reminders[0].IsPast);
        vm.Reminders[0].Value = 0;
        Assert.False(vm.Reminders[0].IsPast);

        // saved without the past one; the others are kept
        vm.Reminders[0].Value = 30;
        vm.Reminders[0].Unit = ReminderItemViewModel.Units.First(u => u.Minutes == 1);
        var built = vm.TryBuild()!;
        Assert.Equal([new EventReminder(ReminderMethod.Popup, 30), new EventReminder(ReminderMethod.Email, 60)],
            built.Reminders.Select(r => r with { SoundPath = null }).ToList());

        // moving the event later makes the past one valid again
        vm.StartTime = new TimeSpan(12, 0, 0);
        Assert.False(vm.Reminders[1].IsPast);

        // an event that repeats keeps every reminder: they count for the next days
        vm.StartTime = new TimeSpan(9, 0, 0);
        Assert.True(vm.Reminders[1].IsPast);
        vm.Recurrence = vm.RecurrenceOptions.First(o => o.Value == "FREQ=DAILY");
        Assert.False(vm.Reminders[1].IsPast);
        Assert.Equal(3, vm.TryBuild()!.Reminders.Count);

        dialog.Close();
        owner.Close();
        return Task.CompletedTask;
    });
}
