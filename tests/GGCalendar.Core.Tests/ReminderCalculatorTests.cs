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
}
