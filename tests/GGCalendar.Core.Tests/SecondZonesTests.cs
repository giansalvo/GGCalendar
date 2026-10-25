using GGCalendar.Core.Models;
using GGCalendar.Core.Zones;

namespace GGCalendar.Core.Tests;

/// <summary>
/// T137, T139, T141, T144: the hours of the second time zone, line by line (the same cases as GGCalendar Mobile). The zones are named
/// explicitly, so the tests give the same answer on any PC.
/// </summary>
public class SecondZonesTests
{
    private static readonly TimeZoneInfo Rome = TimeZoneInfo.FindSystemTimeZoneById("Europe/Rome");
    private static readonly TimeZoneInfo Tokyo = TimeZoneInfo.FindSystemTimeZoneById("Asia/Tokyo");
    private static readonly TimeZoneInfo NewYork = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

    private static IReadOnlyList<ZoneRow> Rows(string day, TimeZoneInfo second) => SecondZones.Rows(DateTime.Parse(day), Rome, second);

    private static string Hour(ZoneRow row) => row.Time.ToString("HH:mm");

    [Fact]
    [Trait("TestCase", "T139")]
    public void T139_InSummerRomeToTokyoIsSevenHoursAhead()
    {
        var r = Rows("2026-07-15", Tokyo);
        Assert.Equal(24, r.Count);
        Assert.Equal("07:00", Hour(r[0]));
        Assert.Equal("19:00", Hour(r[12]));
        Assert.Equal(new DateOnly(2026, 7, 15), r[0].Date);
        Assert.Equal(new DateOnly(2026, 7, 16), r[17].Date); // 17:00 in Rome is 00:00 of the next day there
    }

    [Fact]
    [Trait("TestCase", "T139")]
    public void T139_ZonesWithHalfHoursAndQuartersOfAnHour()
    {
        var india = Rows("2026-01-15", TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata"));
        Assert.Equal(("04:30", "05:30", "03:30"), (Hour(india[0]), Hour(india[1]), Hour(india[23])));
        var nepal = Rows("2026-01-15", TimeZoneInfo.FindSystemTimeZoneById("Asia/Kathmandu"));
        Assert.Equal(("04:45", "05:45"), (Hour(nepal[0]), Hour(nepal[1])));
    }

    [Fact]
    [Trait("TestCase", "T141")]
    public void T141_TheDayTheClockOfThePcMovesForwardTheLinesFollowTheRealTime()
    {
        // Rome 2026-03-29: 02:00 does not exist, 03:00 is the next hour. Tokyo does not change.
        var r = Rows("2026-03-29", Tokyo);
        Assert.Equal(["08:00", "09:00", "10:00", "10:00", "11:00"], r.Take(5).Select(Hour));
        Assert.Equal("06:00", Hour(r[23])); // 23:00 CEST is 21:00Z, 06:00 of the next day in Tokyo
    }

    [Fact]
    [Trait("TestCase", "T141")]
    public void T141_TheTwoZonesChangeTheirClocksOnDifferentDays()
    {
        // New York is on summer time from 8 March, Rome from 29 March: in between the gap is 5 hours, not 6.
        Assert.Equal("18:00", Hour(Rows("2026-03-01", NewYork)[0]));
        Assert.Equal("19:00", Hour(Rows("2026-03-10", NewYork)[0]));
        Assert.Equal("18:00", Hour(Rows("2026-04-10", NewYork)[0])); // both on summer time again: 6 hours
    }

    [Fact]
    [Trait("TestCase", "T141")]
    public void T141_TheDayRomeChangesInsideTheWeekTheHoursAfterTheChangeAreThoseOfThatDay()
    {
        // Rome 2026-03-29, New York already on summer time: 00:00 is 19:00 (CET, 5 hours); from 03:00 CEST the gap is 6 hours again.
        var r = Rows("2026-03-29", NewYork);
        Assert.Equal(["19:00", "20:00", "21:00", "21:00", "22:00"], r.Take(5).Select(Hour));
    }

    [Fact]
    [Trait("TestCase", "T141")]
    public void T141_WhenTheSecondZoneMovesItsClockBackAnHourIsRepeated()
    {
        // New York 2026-11-01 02:00 EDT becomes 01:00 EST at 06:00Z; Rome (CET) is at 07:00. The lines of 06:00 and 07:00 both read 01:00.
        var r = Rows("2026-11-01", NewYork);
        Assert.Equal("19:00", Hour(r[0]));
        Assert.Equal(["01:00", "01:00", "02:00"], new[] { r[6], r[7], r[8] }.Select(Hour));
    }

    [Fact]
    [Trait("TestCase", "T141")]
    public void T141_AnHourThatHappensTwiceInThePcIsTheFirstOfTheTwo()
    {
        // Rome 2026-10-25: 03:00 CEST goes back to 02:00 CET, so 02:30 happens twice; the line of 02:00 is the first (CEST, 00:00Z).
        var r = Rows("2026-10-25", Tokyo);
        Assert.Equal("09:00", Hour(r[2])); // 02:00 CEST = 00:00Z = 09:00 in Tokyo
        Assert.Equal("11:00", Hour(r[3])); // 03:00 CET is 02:00Z, 11:00 in Tokyo: two real hours after the line above
    }

    [Fact]
    [Trait("TestCase", "T139")]
    public void T139_ADayIsWrittenOnTheFirstLineAndWhereTheDateChanges()
    {
        var r = Rows("2026-01-15", NewYork); // 6 hours behind
        Assert.Equal(new DateOnly(2026, 1, 14), r[0].Date);
        Assert.Equal([0, 6], r.Select((row, i) => (row, i)).Where(p => p.row.ShowDate).Select(p => p.i));
        Assert.Equal((new DateOnly(2026, 1, 15), new TimeOnly(0, 0)), (r[6].Date, r[6].Time));
    }

    [Fact]
    [Trait("TestCase", "T141")]
    public void T141_TwoDaysHaveTheSameHoursOrNot()
    {
        Assert.True(SecondZones.SameHours(Rows("2026-03-02", NewYork), Rows("2026-03-06", NewYork)));
        Assert.False(SecondZones.SameHours(Rows("2026-03-02", NewYork), Rows("2026-03-10", NewYork)));
        Assert.False(SecondZones.SameHours(Rows("2026-03-27", NewYork), Rows("2026-03-29", NewYork)));
        Assert.False(SecondZones.SameHours(Rows("2026-10-30", NewYork), Rows("2026-11-02", NewYork)));
    }

    [Fact]
    [Trait("TestCase", "T144")]
    public void T144_TheNameOfTheColumnIsCutToSixCharactersAsPeopleSeeThem()
    {
        Assert.Equal("abcdef", SecondZones.ClampLabel("  abcdefghi "));
        Assert.Equal("Tokyo", SecondZones.ClampLabel("Tokyo"));
        Assert.Equal("", SecondZones.ClampLabel("   "));
        Assert.Equal("", SecondZones.ClampLabel(null));
        var emoji = "\U0001F600";
        Assert.Equal(string.Concat(Enumerable.Repeat(emoji, 6)), SecondZones.ClampLabel(string.Concat(Enumerable.Repeat(emoji, 9))));
    }

    [Fact]
    [Trait("TestCase", "T144")]
    public void T144_TheNameOfferedFirst()
    {
        Assert.Equal("Tokyo", SecondZones.DefaultLabel("Asia/Tokyo"));
        Assert.Equal("NY", SecondZones.DefaultLabel("America/New_York"));
        Assert.Equal("LA", SecondZones.DefaultLabel("America/Los_Angeles"));
        Assert.Equal("BA", SecondZones.DefaultLabel("America/Argentina/Buenos_Aires"));
        Assert.Equal("London", SecondZones.DefaultLabel("Europe/London"));
        Assert.Equal("Tokyo", SecondZones.DefaultLabel("Tokyo Standard Time")); // a Windows id is read through its IANA name
        Assert.Equal("NY", SecondZones.DefaultLabel("Eastern Standard Time"));
        Assert.Equal("Tokyo", SecondZones.LabelOf("Asia/Tokyo", "  "));
        Assert.Equal("Tokyo", SecondZones.LabelOf("Asia/Tokyo", null));
        Assert.Equal("Casa", SecondZones.LabelOf("Asia/Tokyo", "Casa"));
        Assert.Equal("abcdef", SecondZones.LabelOf("Asia/Tokyo", "abcdefgh"));
    }

    [Fact]
    [Trait("TestCase", "T137")]
    public void T137_TheZoneToDrawIsNullWhenOffUnknownOrTheSameAsThePc()
    {
        var on = new AppSettings { SecondaryTimeZoneId = "Asia/Tokyo", SecondaryTimeZoneLabel = "Tk" };
        Assert.Equal(new SecondZone("Asia/Tokyo", "Tk"), SecondZones.From(on, Rome));
        Assert.Equal("Tokyo", SecondZones.From(new AppSettings { SecondaryTimeZoneId = "Asia/Tokyo" }, Rome)?.Label);
        Assert.Null(SecondZones.From(new AppSettings(), Rome)); // none chosen
        Assert.Null(SecondZones.From(new AppSettings { SecondaryTimeZoneId = "" }, Rome));
        Assert.Null(SecondZones.From(new AppSettings { SecondaryTimeZoneId = "Nowhere/Land" }, Rome)); // a zone this PC does not know
        Assert.Null(SecondZones.From(new AppSettings { SecondaryTimeZoneId = Rome.Id }, Rome)); // the same as the PC
        Assert.NotNull(SecondZones.From(on, TimeZoneInfo.FindSystemTimeZoneById("Europe/London")));
    }

    [Fact]
    [Trait("TestCase", "T152")]
    public void T152_TheOffsetLabelIsTheSameInEveryLanguage()
    {
        Assert.Equal("GMT+01", SecondZones.OffsetLabel(TimeSpan.FromHours(1)));
        Assert.Equal("GMT-05", SecondZones.OffsetLabel(TimeSpan.FromHours(-5)));
        Assert.Equal("GMT+05:30", SecondZones.OffsetLabel(new TimeSpan(5, 30, 0)));
        Assert.Equal("GMT-09:30", SecondZones.OffsetLabel(new TimeSpan(-9, -30, 0)));
        Assert.Equal("GMT+00", SecondZones.OffsetLabel(TimeSpan.Zero));
        Assert.Equal("GMT+02", SecondZones.OffsetLabel(Rome, new DateTime(2026, 7, 15)));
        Assert.Equal("GMT+01", SecondZones.OffsetLabel(Rome, new DateTime(2026, 1, 15)));
    }

    [Fact]
    [Trait("TestCase", "T142")]
    public void T142_TheHoursOfAnEventInTheSecondZoneWithTheDayWhenItIsNotTheSame()
    {
        var culture = System.Globalization.CultureInfo.InvariantCulture;
        // 15:00-16:00 in Rome in winter: 23:00-00:00 in Tokyo, the 23:00 is the same day and the 00:00 is the next one.
        Assert.Equal("23:00 – 00:00 (+1)", SecondZones.Range(new DateTime(2026, 1, 15, 15, 0, 0), new DateTime(2026, 1, 15, 16, 0, 0), Rome, Tokyo, true, culture));
        // 20:00-21:00 in Rome: 04:00-05:00 the next day in Tokyo, one mark for both.
        Assert.Equal("04:00 – 05:00 (+1)", SecondZones.Range(new DateTime(2026, 1, 15, 20, 0, 0), new DateTime(2026, 1, 15, 21, 0, 0), Rome, Tokyo, true, culture));
        // 02:00-03:00 in Rome in winter, New York: the day before.
        Assert.Equal("20:00 – 21:00 (-1)", SecondZones.Range(new DateTime(2026, 1, 15, 2, 0, 0), new DateTime(2026, 1, 15, 3, 0, 0), Rome, NewYork, true, culture));
        // the same day there: no mark; and in 12-hour clock
        Assert.Equal("08:00 – 09:00", SecondZones.Range(new DateTime(2026, 1, 15, 14, 0, 0), new DateTime(2026, 1, 15, 15, 0, 0), Rome, NewYork, true, culture));
        Assert.Equal("8:00 AM – 9:00 AM", SecondZones.Range(new DateTime(2026, 1, 15, 14, 0, 0), new DateTime(2026, 1, 15, 15, 0, 0), Rome, NewYork, false, culture));
    }
}
