using GGCalendar.Core.Ics;
using GGCalendar.Core.Models;

namespace GGCalendar.Core.Tests;

public class IcsWriterTests
{
    [Fact]
    [Trait("TestCase", "T322")]
    public void The_file_to_share_has_no_guests_but_keeps_the_rest()
    {
        var ev = new CalendarEvent("1", "c", "Riunione", new DateTime(2026, 10, 29, 11, 0, 0), new DateTime(2026, 10, 29, 12, 0, 0), Location: "Aula 12")
        {
            Attendees = [new EventAttendee("a@b.it"), new EventAttendee("c@d.it", true)],
            Reminders = [new EventReminder(ReminderMethod.Popup, 30)],
        };
        var shared = IcsWriter.Write([ev], includeAttendees: false);
        Assert.DoesNotContain("ATTENDEE", shared);
        Assert.DoesNotContain("a@b.it", shared);
        Assert.DoesNotContain("c@d.it", shared);
        Assert.Contains("SUMMARY:Riunione", shared);
        Assert.Contains("LOCATION:Aula 12", shared);
        Assert.Contains("BEGIN:VALARM", shared);

        // The export of a calendar (the default) keeps them.
        var whole = IcsWriter.Write([ev]);
        Assert.Contains("ATTENDEE;ROLE=REQ-PARTICIPANT:mailto:a@b.it", whole);
        Assert.Contains("ATTENDEE;ROLE=OPT-PARTICIPANT:mailto:c@d.it", whole);
    }

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
