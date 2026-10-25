using GGCalendar.Core.Ics;
using GGCalendar.Core.Models;

namespace GGCalendar.Core.Tests;

/// <summary>All-day events: iCalendar and Google both use an exclusive end date, which must not shift by a day.</summary>
public class IcsAllDayTests
{
    private static CalendarEvent AllDay(string id, DateTime from, DateTime exclusiveEnd) =>
        new(id, "c", "Giorno " + id, from, exclusiveEnd, true) { Reminders = [] };

    [Fact]
    [Trait("TestCase", "T21")]
    public void T21_AOneDayAndAMultiDayEventSurviveTheRoundTrip()
    {
        var one = AllDay("1", new DateTime(2026, 10, 10), new DateTime(2026, 10, 11));
        var many = AllDay("2", new DateTime(2026, 10, 10), new DateTime(2026, 10, 13));

        var parsed = IcsParser.Parse(IcsWriter.Write([one, many]), "dest");

        Assert.Equal(2, parsed.Count);
        foreach (var (before, after) in new[] { (one, parsed[0]), (many, parsed[1]) })
        {
            Assert.True(after.IsAllDay);
            Assert.Equal(before.Start, after.Start);
            Assert.Equal(before.End, after.End);
        }
    }

    [Fact]
    [Trait("TestCase", "T21")]
    public void T21_TheFileHoldsDatesWithTheExclusiveEnd()
    {
        var ics = IcsWriter.Write([AllDay("1", new DateTime(2026, 10, 10), new DateTime(2026, 10, 13))]);

        Assert.Contains("DTSTART;VALUE=DATE:20261010", ics);
        Assert.Contains("DTEND;VALUE=DATE:20261013", ics);
    }

    [Fact]
    [Trait("TestCase", "T21")]
    public void T21_AForeignMultiDayEventKeepsItsEndDate()
    {
        const string ics = "BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nUID:m\r\nDTSTART;VALUE=DATE:20261010\r\nDTEND;VALUE=DATE:20261013\r\nSUMMARY:Gita\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n";

        var ev = Assert.Single(IcsParser.Parse(ics, "c"));

        Assert.True(ev.IsAllDay);
        Assert.Equal(new DateTime(2026, 10, 10), ev.Start);
        Assert.Equal(new DateTime(2026, 10, 13), ev.End);
    }
}
