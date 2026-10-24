using System.Diagnostics;
using System.Globalization;
using GGCalendar.App.ViewModels;
using GGCalendar.Core.Ics;
using GGCalendar.Core.Models;
using GGCalendar.Data;
using Xunit.Abstractions;

namespace GGCalendar.Core.Tests;

/// <summary>
/// T35-T44 and T32-T34: .ics files from other producers (the sample feeds of GGCalendarAndroid, kept in Fixtures/ics as plain data):
/// what the desktop makes of them when the user imports them. Expected values come from the description of each file.
/// </summary>
public class IcsFeedFixtureTests(ITestOutputHelper output)
{
    private static string Read(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "ics", name + ".ics"));

    private static IcsImport Analyze(string name) => IcsParser.Analyze(Read(name), "dest");

    private static MainViewModel Open()
    {
        var store = new CalendarStore(":memory:");
        store.UpsertCalendar(new CalendarInfo("dest", "Locale", "Destinazione", "#1A73E8"));
        return new MainViewModel(store);
    }

    private static DateTime Utc(string stamp) =>
        DateTime.ParseExact(stamp, "yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal).ToLocalTime();

    private static DateTime InZone(string zoneId, string stamp) =>
        TimeZoneInfo.ConvertTime(DateTime.ParseExact(stamp, "yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture), TimeZoneInfo.FindSystemTimeZoneById(zoneId), TimeZoneInfo.Local);

    private static CalendarEvent Event(IcsImport import, string title) => import.Events.Single(e => e.Title == title);

    [Fact]
    [Trait("TestCase", "T35")]
    public void T35_TheBasicFeedKeepsTimesZonesAllDayAndSpecialCharacters()
    {
        var feed = Analyze("ggcal_basic");

        var utc = Event(feed, "[PROVA] Riunione in UTC");
        Assert.Equal((Utc("20261027T090000"), Utc("20261027T100000"), "Sala A, Sassari"), (utc.Start, utc.End, utc.Location));
        var rome = Event(feed, "[PROVA] Pranzo di lavoro (Roma)");
        Assert.Equal((InZone("Europe/Rome", "20261027T130000"), InZone("Europe/Rome", "20261027T143000")), (rome.Start, rome.End));
        var day = Event(feed, "[PROVA] Giornata intera");
        Assert.Equal((true, new DateTime(2026, 10, 28), new DateTime(2026, 10, 29)), (day.IsAllDay, day.Start, day.End));
        var four = Event(feed, "[PROVA] Convegno di 4 giorni");
        Assert.Equal((true, new DateTime(2026, 10, 29), new DateTime(2026, 11, 2)), (four.IsAllDay, four.Start, four.End));
        Assert.Equal(new DateTime(2026, 10, 30, 11, 0, 0), Event(feed, "[PROVA] Ora flottante (senza fuso)").Start); // no zone: the clock of the PC
        Assert.Equal(TimeSpan.FromHours(4), Event(feed, "[PROVA] A cavallo della mezzanotte").End - Event(feed, "[PROVA] A cavallo della mezzanotte").Start);
        var open = Event(feed, "[PROVA] Senza fine (solo inizio)");
        Assert.True(open.End > open.Start);
        Assert.Contains(feed.Events, e => e.Title == "[PROVA] Caffè, perché? Àèìòù ☕ \\ ; ,");
    }

    [Fact]
    [Trait("TestCase", "T32")]
    [Trait("Category", "Pending")]
    public void T32_AnEventMarkedCancelledInTheFileIsNotImported()
    {
        var feed = Analyze("ggcal_basic");

        Assert.DoesNotContain(feed.Events, e => e.Title.Contains("Annullato nel file"));
        Assert.Equal(10, feed.Events.Count);
    }

    [Fact]
    [Trait("TestCase", "T36")]
    public void T36_TimesInUtcBecomeTheLocalTimeOfThePc()
    {
        var feed = Analyze("ggcal_utc");

        Assert.Equal(4, feed.Events.Count);
        Assert.Equal(Utc("20261027T060000"), Event(feed, "[PROVA] UTC 06:00").Start);
        Assert.Equal(Utc("20261027T120000"), Event(feed, "[PROVA] UTC 12:00").Start);
        var late = Event(feed, "[PROVA] UTC 23:30");
        Assert.Equal((Utc("20261027T233000"), Utc("20261028T003000")), (late.Start, late.End));
        Assert.Equal(Utc("20261029T001500"), Event(feed, "[PROVA] UTC 00:15 (dopo la mezzanotte)").Start);
    }

    [Fact]
    [Trait("TestCase", "T37")]
    public void T37_WindowsTimeZoneNamesAreUnderstoodAndAnUnknownOneUsesThePcClock()
    {
        var feed = Analyze("ggcal_windows_tz");

        Assert.Equal(InZone("W. Europe Standard Time", "20261027T100000"), Event(feed, "[PROVA] 10:00 in W. Europe Standard Time").Start);
        Assert.Equal(InZone("Eastern Standard Time", "20261027T100000"), Event(feed, "[PROVA] 10:00 in Eastern Standard Time").Start);
        Assert.Equal(new DateTime(2026, 10, 28, 10, 0, 0), Event(feed, "[PROVA] Fuso sconosciuto (usa quello del telefono)").Start);
    }

    [Fact]
    [Trait("TestCase", "T38")]
    public void T38_AnAppleStyleFileIsReadWithItsTimedAndAllDayEvents()
    {
        var feed = Analyze("ggcal_apple");

        Assert.Equal(2, feed.Events.Count);
        Assert.Equal(InZone("Europe/Rome", "20261027T090000"), Event(feed, "[PROVA] Evento in stile Apple").Start);
        var day = Event(feed, "[PROVA] Tutto il giorno Apple");
        Assert.Equal((true, new DateTime(2026, 10, 28)), (day.IsAllDay, day.Start));
    }

    [Fact]
    [Trait("TestCase", "T39")]
    public void T39_TheRemindersOfTheFileBecomeRemindersOfTheEvents()
    {
        var feed = Analyze("ggcal_alarms");

        Assert.Equal(3, feed.Events.Count);
        Assert.Contains(Event(feed, "[PROVA] Con VALARM -PT5M").Reminders, r => r.Minutes == 5);
        Assert.Contains(Event(feed, "[PROVA] Con VALARM -PT1H").Reminders, r => r.Minutes == 60);
        Assert.Contains(Event(feed, "[PROVA] Con VALARM -P1D").Reminders, r => r.Minutes == 1440);
    }

    [Fact]
    [Trait("TestCase", "T40")]
    public void T40_SeriesFromAnotherProducerRepeatAsExpectedAndWhatIsUnsupportedIsReported()
    {
        var feed = Analyze("ggcal_rrule");
        var vm = Open();
        vm.ImportEvents("dest", feed.Events, ImportDuplicates.Skip);
        int Count(string title, DateTime from, DateTime to) => vm.ExpandedBetween(from, to, visibleOnly: false).Count(e => e.Title == title);
        var from = new DateTime(2026, 10, 1);
        var to = new DateTime(2028, 1, 1);

        Assert.Equal(12, Count("[PROVA] Lun Mer Ven, 12 volte", from, to));
        Assert.Equal(16, Count("[PROVA] Ogni 2 giorni fino a +30", from, to));
        Assert.Equal(6, Count("[PROVA] Il 15 di ogni mese, 6 volte", from, to));
        Assert.Equal(2, Count("[PROVA] Compleanno di prova (ogni anno)", new DateTime(2026, 11, 1), new DateTime(2027, 12, 31)));
        Assert.Equal(1, Count("[PROVA] Secondo lunedì del mese (BYSETPOS: non supportato)", from, to)); // shown once, as declared

        var shown = vm.ExpandedBetween(from, to, visibleOnly: false).Where(e => e.Title.StartsWith("[PROVA] Serie con eccezioni", StringComparison.Ordinal)).ToList();
        Assert.DoesNotContain(shown, e => e.Title == "[PROVA] Serie con eccezioni" && e.Start == Utc("20261029T150000")); // the excluded date
        Assert.Contains(shown, e => e.Title == "[PROVA] Serie con eccezioni (spostata)" && e.Start == Utc("20261031T180000"));

        Assert.Equal(2, feed.Warnings.Count);
        Assert.Contains(feed.Warnings, w => w.Contains("RDATE"));
        Assert.Contains(feed.Warnings, w => w.Contains("non supportata"));
    }

    [Fact]
    [Trait("TestCase", "T41")]
    public void T41_ABomAndUnixLineEndingsAreNoProblem()
    {
        var feed = Analyze("ggcal_bom_lf");

        Assert.Single(feed.Events);
    }

    [Fact]
    [Trait("TestCase", "T42")]
    public void T42_AnEmptyFileHasNoEvents()
    {
        Assert.Empty(Analyze("ggcal_empty").Events);
    }

    [Fact]
    [Trait("TestCase", "T33")]
    [Trait("Category", "Pending")]
    public void T33_ATruncatedFileKeepsTheCompleteEventsAndSaysItWasCut()
    {
        var feed = Analyze("ggcal_malformed");

        Assert.Equal(6, feed.Events.Count);
        Assert.NotEmpty(feed.Warnings); // the user must know the file was cut short
    }

    [Fact]
    [Trait("TestCase", "T34")]
    [Trait("Category", "Pending")]
    public void T34_TwoEventsWithTheSameUidAreReadOnceAndReported()
    {
        var feed = Analyze("ggcal_dup_uid");

        Assert.Equal(2, feed.Events.Count);
        Assert.NotEmpty(feed.Warnings);
    }

    [Fact]
    [Trait("TestCase", "T43")]
    public void T43_ManyEventsWithTheSameUidAndRecurrenceIdAreAllImportedAndNotDuplicatedOnReimport()
    {
        // 109 "Occupato" events as Google's public feed has them.
        var feed = Analyze("ggcal_google_busy");
        var vm = Open();

        Assert.Equal(109, feed.Events.Count);
        Assert.Equal((109, 0), vm.ImportEvents("dest", feed.Events, ImportDuplicates.Skip));
        Assert.Equal((0, 109), vm.ImportEvents("dest", feed.Events, ImportDuplicates.Skip));
    }

    [Fact]
    [Trait("TestCase", "T44")]
    public void T44_AFileWithFiveThousandEventsImportsInAcceptableTime()
    {
        var feed = Analyze("ggcal_5000");
        var vm = Open();

        var clock = Stopwatch.StartNew();
        var (imported, skipped) = vm.ImportEvents("dest", feed.Events, ImportDuplicates.Skip);
        output.WriteLine($"Importazione di {imported} eventi: {clock.Elapsed.TotalSeconds:F1} s");

        Assert.Equal((5000, 0), (imported, skipped));
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(60), $"Importazione troppo lenta: {clock.Elapsed.TotalSeconds:F1} s.");
    }
}
