using System.Diagnostics;
using System.Globalization;
using System.Text;
using GGCalendar.App.ViewModels;
using GGCalendar.Core.Ics;
using GGCalendar.Core.Models;
using GGCalendar.Data;
using Xunit.Abstractions;

namespace GGCalendar.Core.Tests;

/// <summary>
/// T45-T59: .ics files that stress the import (and, for series, the export) in ways the first feeds do not. The files are read
/// the way the Import menu reads them (from their bytes), and the expected values come from the description of each file.
/// </summary>
public class IcsFeedFixtureMoreTests(ITestOutputHelper output)
{
    private static IcsImport Analyze(string name) =>
        IcsParser.AnalyzeFile(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "ics", name + ".ics")), "dest");

    private static MainViewModel Open()
    {
        var store = new CalendarStore(":memory:");
        store.UpsertCalendar(new CalendarInfo("dest", "Locale", "Destinazione", "#1A73E8"));
        store.UpsertCalendar(new CalendarInfo("copy", "Locale", "Copia", "#33B679"));
        return new MainViewModel(store);
    }

    private static DateTime Utc(string stamp) =>
        DateTime.ParseExact(stamp, "yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal).ToLocalTime();

    private static DateTime InRome(string stamp) =>
        TimeZoneInfo.ConvertTime(DateTime.ParseExact(stamp, "yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture), TimeZoneInfo.FindSystemTimeZoneById("Europe/Rome"), TimeZoneInfo.Local);

    private static CalendarEvent Event(IcsImport import, string title) => import.Events.Single(e => e.Title == title);

    private static List<CalendarEvent> Shown(MainViewModel vm, DateTime from, DateTime to) =>
        vm.ExpandedBetween(from, to, visibleOnly: false).OrderBy(e => e.Start).ToList();

    /// <summary>Exports what the calendar "dest" holds and imports it into "copy", as the user would do with the menu items.</summary>
    private static void ExportAndReimport(MainViewModel vm) =>
        vm.ImportEvents("copy", IcsParser.Analyze(IcsWriter.Write(vm.EventsOf("dest")), "copy").Events, ImportDuplicates.Skip);

    private static readonly DateTime November = new(2026, 11, 1);
    private static readonly DateTime December = new(2026, 12, 1);

    [Fact]
    [Trait("TestCase", "T45")]
    public void T45_ASeriesWithAMovedOccurrenceShowsTheMoveAndSurvivesExportAndReimport()
    {
        var vm = Open();
        vm.ImportEvents("dest", Analyze("ggcal_series_modified").Events, ImportDuplicates.Skip);
        ExportAndReimport(vm);

        foreach (var calendar in new[] { "dest", "copy" })
        {
            var shown = Shown(vm, November, December).Where(e => e.CalendarId == calendar).ToList();
            Assert.Equal(5, shown.Count);
            Assert.Equal(4, shown.Count(e => e.Title == "[TEST GGCAL] Serie con occorrenza modificata"));
            var moved = Assert.Single(shown, e => e.Title == "[TEST GGCAL] Occorrenza spostata nel pomeriggio");
            Assert.Equal((InRome("20261104T150000"), InRome("20261104T160000")), (moved.Start, moved.End));
        }
    }

    [Fact]
    [Trait("TestCase", "T46")]
    public void T46_AnAllDayWeeklySeriesSkipsTheExcludedDayAlsoAfterExportAndReimport()
    {
        var vm = Open();
        vm.ImportEvents("dest", Analyze("ggcal_series_allday").Events, ImportDuplicates.Skip);
        ExportAndReimport(vm);

        foreach (var calendar in new[] { "dest", "copy" })
        {
            var shown = Shown(vm, November, December.AddMonths(1)).Where(e => e.CalendarId == calendar).ToList();
            Assert.Equal([new DateTime(2026, 11, 3), new DateTime(2026, 11, 17), new DateTime(2026, 11, 24)], shown.Select(e => e.Start).ToArray());
            Assert.All(shown, e => Assert.True(e.IsAllDay));
        }
    }

    [Fact]
    [Trait("TestCase", "T47")]
    public void T47_ACancelledOccurrenceOfASeriesIsNotShown()
    {
        var vm = Open();
        vm.ImportEvents("dest", Analyze("ggcal_cancelled_occurrence").Events, ImportDuplicates.Skip);

        var shown = Shown(vm, November, December);

        Assert.Equal(3, shown.Count);
        Assert.DoesNotContain(shown, e => e.Start == InRome("20261103T100000"));
    }

    [Fact]
    [Trait("TestCase", "T48")]
    public void T48_AnOccurrenceWithoutItsSeriesIsImportedAsASingleEventAndReported()
    {
        var feed = Analyze("ggcal_orphan_occurrence");

        var orphan = Event(feed, "[TEST GGCAL] Occorrenza senza serie madre");
        Assert.Null(orphan.SeriesMasterId);
        Assert.Equal(InRome("20261105T110000"), orphan.Start);
        Assert.Contains(feed.Events, e => e.Title == "[TEST GGCAL] Evento normale accanto");
        Assert.NotEmpty(feed.Warnings);
    }

    [Fact]
    [Trait("TestCase", "T49")]
    public void T49_TasksJournalsAndFreeBusyAreIgnoredAndReported()
    {
        var feed = Analyze("ggcal_other_components");

        Assert.Equal(["[TEST GGCAL] Evento dopo", "[TEST GGCAL] Evento prima"], feed.Events.Select(e => e.Title).Order().ToArray());
        Assert.NotEmpty(feed.Warnings);
    }

    [Fact]
    [Trait("TestCase", "T50")]
    public void T50_ABrokenLineIsSkippedAndReportedWithoutLosingTheOtherEvents()
    {
        var feed = Analyze("ggcal_malformed_line");

        Assert.Equal(3, feed.Events.Count);
        Assert.NotEmpty(feed.Warnings);
    }

    [Fact]
    [Trait("TestCase", "T51")]
    public void T51_VeryLongFieldsDoNotBlockTheImportAreShortenedAndReported()
    {
        var feed = Analyze("ggcal_long_fields");

        var only = Assert.Single(feed.Events);
        Assert.True(only.Description.Length < 200_000, "La descrizione di 200 000 caratteri doveva essere accorciata.");
        Assert.NotEmpty(feed.Warnings);
    }

    [Fact]
    [Trait("TestCase", "T52")]
    public void T52_ATitleFoldedInTheMiddleOfACharacterComesOutWhole()
    {
        var feed = Analyze("ggcal_multibyte_fold");

        Assert.Equal("[TEST GGCAL] " + string.Concat(Enumerable.Repeat("àèìòù€☕日本語😀", 12)), Assert.Single(feed.Events).Title);
    }

    [Fact]
    [Trait("TestCase", "T53")]
    public void T53_AttendeesKeepTheirEmailAndUnknownPropertiesCauseNoError()
    {
        var feed = Analyze("ggcal_attendees");

        var only = Assert.Single(feed.Events);
        Assert.Equal(["anna@example.com", "marco@example.com", "senza-nome@example.com"], only.Attendees.Select(a => a.Email).Order().ToArray());
        Assert.Equal((EventAvailability.Free, EventVisibility.Private), (only.Availability, only.Visibility));
    }

    [Fact]
    [Trait("TestCase", "T54")]
    public void T54_TheEndIsComputedFromADurationOrDefaultedAndExtraDatesAreReported()
    {
        var feed = Analyze("ggcal_duration_rdate");

        var ninety = Event(feed, "[TEST GGCAL] Durata 90 minuti");
        Assert.Equal(TimeSpan.FromMinutes(90), ninety.End - ninety.Start);
        var three = Event(feed, "[TEST GGCAL] Durata 3 giorni");
        Assert.Equal((true, new DateTime(2026, 11, 3), new DateTime(2026, 11, 6)), (three.IsAllDay, three.Start, three.End));
        var open = Event(feed, "[TEST GGCAL] Tutto il giorno senza fine");
        Assert.Equal((true, new DateTime(2026, 11, 5), new DateTime(2026, 11, 6)), (open.IsAllDay, open.Start, open.End));
        Assert.Contains(feed.Warnings, w => w.Contains("RDATE"));
    }

    [Fact]
    [Trait("TestCase", "T55")]
    public void T55_RemindersThatCanBeComputedAreKeptAndTheOthersAreReported()
    {
        var feed = Analyze("ggcal_alarms_advanced");

        Assert.Equal(4, feed.Events.Count);
        Assert.Contains(Event(feed, "[TEST GGCAL] Relativo alla fine").Reminders, r => r.Minutes == 30); // 90 min before an end that is 60 min after the start
        Assert.Contains(Event(feed, "[TEST GGCAL] Con ripetizione").Reminders, r => r.Minutes == 15);
        var two = Event(feed, "[TEST GGCAL] Audio ed email, due promemoria").Reminders;
        Assert.Contains(two, r => r is { Method: ReminderMethod.Alarm, Minutes: 30 });
        Assert.Contains(two, r => r is { Method: ReminderMethod.Email, Minutes: 2880 });
        Assert.NotEmpty(feed.Warnings); // the absolute trigger cannot be kept
    }

    [Fact]
    [Trait("TestCase", "T56")]
    public void T56_ATimeInATimeZoneDefinedInsideTheFileUsesItsRules()
    {
        var feed = Analyze("ggcal_vtimezone_custom");

        Assert.Equal(Utc("20261102T100000"), Assert.Single(feed.Events).Start); // 12:00 at UTC+2
    }

    [Fact]
    [Trait("TestCase", "T57")]
    public void T57_ADailySeriesAcrossTheDaylightSavingChangeStaysAtTenLocalTime()
    {
        var vm = Open();
        vm.ImportEvents("dest", Analyze("ggcal_series_dst").Events, ImportDuplicates.Skip);

        var shown = Shown(vm, new DateTime(2026, 10, 20), November);

        Assert.Equal(6, shown.Count);
        Assert.All(shown, e => Assert.Equal(InRome("20261023T100000").TimeOfDay, e.Start.TimeOfDay));
    }

    [Fact]
    [Trait("TestCase", "T58")]
    public void T58_AWindows1252FileKeepsItsAccents()
    {
        var feed = Analyze("ggcal_windows1252");

        Assert.Equal("[TEST GGCAL] Caffè, perché? àèìòù €", Assert.Single(feed.Events).Title);
    }

    [Fact]
    [Trait("TestCase", "T59")]
    public void T59_AFileWithFiveThousandEventsImportedAgainAsReplaceOrCopyKeepsTheRightNumberInAcceptableTime()
    {
        var feed = Analyze("ggcal_5000");
        var vm = Open();
        var clock = Stopwatch.StartNew();

        Assert.Equal((5000, 0), vm.ImportEvents("dest", feed.Events, ImportDuplicates.Skip));
        Assert.Equal((5000, 0), vm.ImportEvents("dest", Analyze("ggcal_5000").Events, ImportDuplicates.Replace));
        Assert.Equal(5000, vm.EventsOf("dest").Count(e => e.SeriesMasterId is null));
        Assert.Equal((5000, 0), vm.ImportEvents("dest", Analyze("ggcal_5000").Events, ImportDuplicates.Copy));
        Assert.Equal(10_000, vm.EventsOf("dest").Count(e => e.SeriesMasterId is null));

        output.WriteLine($"Tre importazioni di 5000 eventi: {clock.Elapsed.TotalSeconds:F1} s");
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(60), $"Importazione troppo lenta: {clock.Elapsed.TotalSeconds:F1} s.");
    }
}
