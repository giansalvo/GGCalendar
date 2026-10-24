using GGCalendar.Core.Models;
using GGCalendar.Core.Reminders;

namespace GGCalendar.Core.Tests;

public class ReminderCalculatorTests
{
    private static readonly DateTime Start = new(2026, 10, 8, 10, 30, 0);

    private static CalendarEvent Ev(params EventReminder[] r) =>
        new("1", "c", "T", Start, Start.AddHours(1)) { Reminders = r };

    private static EventReminder Popup(int m) => new(ReminderMethod.Popup, m);
    private static EventReminder Email(int m) => new(ReminderMethod.Email, m);

    [Fact]
    public void Returns_popup_reminder_inside_window()
    {
        var due = ReminderCalculator.Due([Ev(Popup(30))], Start.AddMinutes(-31), Start.AddMinutes(-29)).ToList();
        Assert.Single(due);
        Assert.Equal(Start.AddMinutes(-30), due[0].FireAt);
    }

    [Fact]
    public void Ignores_email_and_out_of_window_reminders()
    {
        var evs = new[] { Ev(Email(30), Popup(5)) };
        Assert.Empty(ReminderCalculator.Due(evs, Start.AddMinutes(-31), Start.AddMinutes(-29)));
    }

    [Fact]
    [Trait("TestCase", "T03")]
    public void T03_EveryPopupAndAlarmOfAnEventFiresAtItsOwnTime()
    {
        var ev = Ev(Popup(30), Popup(10), new EventReminder(ReminderMethod.Alarm, 5), Email(60));

        var due = ReminderCalculator.Due([ev], Start.AddMinutes(-61), Start).ToList();

        Assert.Equal([Start.AddMinutes(-30), Start.AddMinutes(-10), Start.AddMinutes(-5)], due.Select(d => d.FireAt).ToArray());
        Assert.Equal([ReminderMethod.Popup, ReminderMethod.Popup, ReminderMethod.Alarm], due.Select(d => d.Reminder.Method).ToArray());
    }

    [Fact]
    [Trait("TestCase", "T03")]
    public void T03_AnAllDayEventFiresRelativeToMidnight()
    {
        var day = new DateTime(2026, 10, 10);
        var ev = new CalendarEvent("1", "c", "T", day, day.AddDays(1), true) { Reminders = [Popup(1440)] };

        var due = Assert.Single(ReminderCalculator.Due([ev], new DateTime(2026, 10, 8, 23, 0, 0), new DateTime(2026, 10, 9, 1, 0, 0)));

        Assert.Equal(new DateTime(2026, 10, 9), due.FireAt);
    }

    [Fact]
    [Trait("TestCase", "T03")]
    public void T03_AnEventAlreadyEndedFiresNothing()
    {
        var ev = Ev(Popup(30));

        Assert.Empty(ReminderCalculator.Due([ev], Start.AddHours(1), Start.AddHours(2)));
    }

    [Fact]
    [Trait("TestCase", "T03")]
    public void T03_EachFiringHasADistinctKey()
    {
        var due = ReminderCalculator.Due([Ev(Popup(30), Popup(10))], Start.AddMinutes(-31), Start).ToList();

        Assert.Equal(2, due.Select(d => d.Key).Distinct().Count());
    }
}
