using GGCalendar.Core.Models;

namespace GGCalendar.Core.Reminders;

public sealed record DueReminder(CalendarEvent Event, EventReminder Reminder, DateTime FireAt)
{
    public string Key => $"{Event.Id}|{Event.Start:O}|{Reminder.Method}|{Reminder.Minutes}";
}

public static class ReminderCalculator
{
    /// <summary>Popup reminders whose fire time falls in (<paramref name="after"/>, <paramref name="until"/>] and whose event has not yet ended.</summary>
    public static IEnumerable<DueReminder> Due(IEnumerable<CalendarEvent> events, DateTime after, DateTime until)
    {
        foreach (var ev in events)
        {
            if (ev.End <= after) continue;
            foreach (var r in ev.Reminders)
            {
                if (r.Method == ReminderMethod.Email) continue;
                var fire = ev.Start.AddMinutes(-r.Minutes);
                if (fire > after && fire <= until) yield return new DueReminder(ev, r, fire);
            }
        }
    }
}
