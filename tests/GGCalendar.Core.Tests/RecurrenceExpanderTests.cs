using GGCalendar.Core.Models;
using GGCalendar.Core.Recurrence;
using Xunit;

namespace GGCalendar.Core.Tests;

public class RecurrenceExpanderTests
{
    private static CalendarEvent Make(string? rrule, DateTime start, TimeSpan? len = null) =>
        new("e1", "c", "t", start, start + (len ?? TimeSpan.FromHours(1))) { Recurrence = rrule };

    private static List<DateTime> Starts(CalendarEvent e, DateTime from, DateTime to) =>
        RecurrenceExpander.Expand(e, from, to).Select(o => o.Start).ToList();

    [Fact]
    public void NonRepeatingReturnsMasterOnlyWhenInRange()
    {
        var e = Make(null, new DateTime(2026, 10, 5, 9, 0, 0));
        Assert.Single(Starts(e, new DateTime(2026, 10, 1), new DateTime(2026, 10, 8)));
        Assert.Empty(Starts(e, new DateTime(2026, 10, 6), new DateTime(2026, 10, 8)));
    }

    [Fact]
    public void DailyWithCountStopsAfterCount()
    {
        var e = Make("FREQ=DAILY;COUNT=3", new DateTime(2026, 10, 5, 9, 0, 0));
        var s = Starts(e, new DateTime(2026, 10, 1), new DateTime(2026, 11, 1));
        Assert.Equal([new DateTime(2026, 10, 5, 9, 0, 0), new DateTime(2026, 10, 6, 9, 0, 0), new DateTime(2026, 10, 7, 9, 0, 0)], s);
    }

    [Fact]
    public void WeeklyByDayEveryTwoWeeks()
    {
        // Monday 5 Oct 2026, every 2 weeks on Mon and Thu
        var e = Make("FREQ=WEEKLY;INTERVAL=2;BYDAY=MO,TH", new DateTime(2026, 10, 5, 9, 0, 0));
        var s = Starts(e, new DateTime(2026, 10, 1), new DateTime(2026, 11, 1));
        Assert.Equal(
        [
            new DateTime(2026, 10, 5, 9, 0, 0), new DateTime(2026, 10, 8, 9, 0, 0),
            new DateTime(2026, 10, 19, 9, 0, 0), new DateTime(2026, 10, 22, 9, 0, 0),
        ], s);
    }

    [Fact]
    public void WeeklyWithoutByDayUsesMasterWeekday()
    {
        var e = Make("FREQ=WEEKLY", new DateTime(2026, 10, 7, 9, 0, 0));
        var s = Starts(e, new DateTime(2026, 10, 14), new DateTime(2026, 10, 29));
        Assert.Equal([new DateTime(2026, 10, 14, 9, 0, 0), new DateTime(2026, 10, 21, 9, 0, 0), new DateTime(2026, 10, 28, 9, 0, 0)], s);
    }

    [Fact]
    public void MonthlySkipsMonthsWithoutTheDay()
    {
        var e = Make("FREQ=MONTHLY", new DateTime(2026, 1, 31, 9, 0, 0));
        var s = Starts(e, new DateTime(2026, 1, 1), new DateTime(2026, 5, 1));
        Assert.Equal([new DateTime(2026, 1, 31, 9, 0, 0), new DateTime(2026, 3, 31, 9, 0, 0)], s);
    }

    [Fact]
    public void YearlyWithUntilIsInclusive()
    {
        var e = Make("FREQ=YEARLY;UNTIL=20280310", new DateTime(2026, 3, 10, 9, 0, 0));
        var s = Starts(e, new DateTime(2026, 1, 1), new DateTime(2035, 1, 1));
        Assert.Equal(3, s.Count);
    }

    [Fact]
    public void OccurrencesKeepIdAndDuration()
    {
        var e = Make("FREQ=DAILY", new DateTime(2026, 10, 5, 9, 0, 0), TimeSpan.FromMinutes(90));
        var o = RecurrenceExpander.Expand(e, new DateTime(2026, 10, 8), new DateTime(2026, 10, 9)).Single();
        Assert.Equal("e1", o.Id);
        Assert.Equal(TimeSpan.FromMinutes(90), o.End - o.Start);
        Assert.Equal(3, o.OccurrenceIndex);
    }

    [Fact]
    public void UnsupportedRuleFallsBackToMaster()
    {
        var e = Make("FREQ=MONTHLY;BYSETPOS=1;BYDAY=TU,WE", new DateTime(2026, 10, 13, 9, 0, 0));
        Assert.Single(Starts(e, new DateTime(2026, 10, 1), new DateTime(2026, 12, 1)));
    }

    [Fact]
    public void GoogleStyleWkstIsAccepted()
    {
        var e = Make("FREQ=WEEKLY;WKST=TU;UNTIL=20261101T215959Z", new DateTime(2026, 10, 7, 20, 0, 0));
        Assert.Equal(4, Starts(e, new DateTime(2026, 10, 1), new DateTime(2026, 12, 1)).Count);
    }

    [Fact]
    public void MonthlySecondTuesdayAndLastFriday()
    {
        var second = Make("FREQ=MONTHLY;BYDAY=2TU", new DateTime(2026, 10, 13, 9, 0, 0));
        Assert.Equal([new DateTime(2026, 10, 13, 9, 0, 0), new DateTime(2026, 11, 10, 9, 0, 0)],
            Starts(second, new DateTime(2026, 10, 1), new DateTime(2026, 12, 1)));
        var last = Make("FREQ=MONTHLY;BYDAY=-1FR", new DateTime(2026, 10, 30, 9, 0, 0));
        Assert.Equal([new DateTime(2026, 10, 30, 9, 0, 0), new DateTime(2026, 11, 27, 9, 0, 0)],
            Starts(last, new DateTime(2026, 10, 1), new DateTime(2026, 12, 1)));
    }

    [Fact]
    public void ExcludedStartsAreSkippedButStillCount()
    {
        var e = Make("FREQ=DAILY;COUNT=3", new DateTime(2026, 10, 5, 9, 0, 0)) with
        {
            ExcludedStarts = [new DateTime(2026, 10, 6, 9, 0, 0)],
        };
        Assert.Equal([new DateTime(2026, 10, 5, 9, 0, 0), new DateTime(2026, 10, 7, 9, 0, 0)],
            Starts(e, new DateTime(2026, 10, 1), new DateTime(2026, 11, 1)));
    }

    [Fact]
    public void WithUntilReplacesCountAndKeepsOtherParts()
    {
        var r = RecurrenceRule.WithUntil("FREQ=WEEKLY;WKST=MO;COUNT=10;BYDAY=WE", new DateTime(2026, 10, 14), allDay: true);
        Assert.Equal("FREQ=WEEKLY;WKST=MO;BYDAY=WE;UNTIL=20261014", r);
    }
}
