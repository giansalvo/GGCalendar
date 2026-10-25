using GGCalendar.Core.Ics;
using GGCalendar.Core.Models;

namespace GGCalendar.Core.Tests;

public class IcsWriterTests
{
    [Fact]
    public void Writes_timed_event_with_attendee_and_alarm()
    {
        var ev = new CalendarEvent("1", "c", "Riunione, test", new(2026, 10, 8, 10, 30, 0), new(2026, 10, 8, 11, 30, 0))
        {
            Attendees = [new EventAttendee("a@b.it")],
        };

        var ics = IcsWriter.Write([ev]);

        var utc = ev.Start - TimeZoneInfo.Local.GetUtcOffset(ev.Start);
        Assert.Contains("DTSTART:" + utc.ToString("yyyyMMdd'T'HHmmss'Z'"), ics); // a single timed event is written in UTC
        Assert.Contains("SUMMARY:Riunione\\, test", ics);
        Assert.Contains("ATTENDEE;ROLE=REQ-PARTICIPANT:mailto:a@b.it", ics);
        Assert.Contains("TRIGGER:-PT30M", ics);
    }

    [Fact]
    public void Writes_all_day_event_as_date_values()
    {
        var ev = new CalendarEvent("2", "c", "Ferie", new(2026, 10, 8), new(2026, 10, 10), IsAllDay: true);
        var ics = IcsWriter.Write([ev]);
        Assert.Contains("DTSTART;VALUE=DATE:20261008", ics);
        Assert.Contains("DTEND;VALUE=DATE:20261010", ics);
    }
}
