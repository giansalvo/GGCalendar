using GGCalendar.Core.Ics;
using GGCalendar.Core.Models;

namespace GGCalendar.Core.Tests;

public class IcsParserTests
{
    [Fact]
    [Trait("TestCase", "T21")]
    public void Roundtrips_timed_event()
    {
        var ev = new CalendarEvent("1", "c", "Riunione, test; ok", new(2026, 10, 8, 10, 30, 0), new(2026, 10, 8, 11, 30, 0), Location: "Sala A", Description: "riga1\nriga2")
        {
            Attendees = [new EventAttendee("a@b.it"), new EventAttendee("c@d.it", true)],
            Reminders = [new EventReminder(ReminderMethod.Popup, 15)],
            Availability = EventAvailability.Free,
        };

        var parsed = Assert.Single(IcsParser.Parse(IcsWriter.Write([ev]), "dest"));

        Assert.Equal("dest", parsed.CalendarId);
        Assert.Equal(ev.Title, parsed.Title);
        Assert.Equal(ev.Start, parsed.Start);
        Assert.Equal(ev.End, parsed.End);
        Assert.Equal("Sala A", parsed.Location);
        Assert.Equal("riga1\nriga2", parsed.Description);
        Assert.Equal(2, parsed.Attendees.Count);
        Assert.True(parsed.Attendees[1].IsOptional);
        Assert.Equal(15, Assert.Single(parsed.Reminders).Minutes);
        Assert.Equal(EventAvailability.Free, parsed.Availability);
    }

    [Fact]
    [Trait("TestCase", "T21")]
    public void Roundtrips_series_with_exception_and_exdate()
    {
        var master = new CalendarEvent("m", "c", "Serie", new(2026, 10, 5, 9, 0, 0), new(2026, 10, 5, 10, 0, 0))
        {
            Recurrence = "FREQ=DAILY;COUNT=5",
            ExcludedStarts = [new(2026, 10, 7, 9, 0, 0)],
        };
        var exception = new CalendarEvent("x", "c", "Spostata", new(2026, 10, 6, 14, 0, 0), new(2026, 10, 6, 15, 0, 0))
        {
            SeriesMasterId = "m",
            OriginalStart = new(2026, 10, 6, 9, 0, 0),
        };
        var cancelled = new CalendarEvent("y", "c", "Serie", new(2026, 10, 8, 9, 0, 0), new(2026, 10, 8, 10, 0, 0))
        {
            SeriesMasterId = "m",
            OriginalStart = new(2026, 10, 8, 9, 0, 0),
            IsCancelled = true,
        };

        var parsed = IcsParser.Parse(IcsWriter.Write([master, exception, cancelled]), "c");

        var pm = Assert.Single(parsed, e => e.SeriesMasterId is null);
        var px = Assert.Single(parsed, e => e.SeriesMasterId is not null);
        Assert.Equal("FREQ=DAILY;COUNT=5", pm.Recurrence);
        Assert.Equal(2, pm.ExcludedStarts.Count);
        Assert.Equal(pm.Id, px.SeriesMasterId);
        Assert.Equal(new DateTime(2026, 10, 6, 9, 0, 0), px.OriginalStart);
        Assert.Equal(new DateTime(2026, 10, 6, 14, 0, 0), px.Start);
    }

    [Fact]
    public void Keeps_uid_and_reports_unsupported_parts()
    {
        const string ics = "BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nUID:abc-123\r\nDTSTART:20261008T080000\r\nDTEND:20261008T090000\r\n" +
                           "SUMMARY:A\r\nRDATE:20261015T080000\r\nATTACH:http://x/y.pdf\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n";

        var result = IcsParser.Analyze(ics, "c");

        Assert.Equal("abc-123", Assert.Single(result.Events).IcsUid);
        Assert.Contains(result.Warnings, w => w.Contains("RDATE"));
        Assert.Contains(result.Warnings, w => w.Contains("allegati"));
        Assert.Contains("UID:abc-123", IcsWriter.Write(result.Events));
    }

    [Fact]
    public void Parses_foreign_file_with_utc_folding_and_duration()
    {
        const string ics = "BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nUID:z\r\nDTSTART:20261008T080000Z\r\nDURATION:PT90M\r\n" +
                           "SUMMARY:Lungo\r\n titolo\r\nBEGIN:VALARM\r\nACTION:DISPLAY\r\nTRIGGER:-P1D\r\nEND:VALARM\r\nEND:VEVENT\r\n" +
                           "BEGIN:VEVENT\r\nUID:d\r\nDTSTART;VALUE=DATE:20261010\r\nSUMMARY:Giorno\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n";

        var parsed = IcsParser.Parse(ics, "c");

        var timed = parsed[0];
        Assert.Equal("Lungotitolo", timed.Title);
        Assert.Equal(new DateTime(2026, 10, 8, 8, 0, 0, DateTimeKind.Utc).ToLocalTime(), timed.Start);
        Assert.Equal(TimeSpan.FromMinutes(90), timed.End - timed.Start);
        Assert.Equal(1440, Assert.Single(timed.Reminders).Minutes);
        var day = parsed[1];
        Assert.True(day.IsAllDay);
        Assert.Equal(new DateTime(2026, 10, 11), day.End);
    }
}
