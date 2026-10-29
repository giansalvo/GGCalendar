using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using GGCalendar.App.ViewModels;
using GGCalendar.Core.Ics;
using GGCalendar.Core.Models;
using GGCalendar.Data;

namespace GGCalendar.Core.Tests;

/// <summary>
/// T60-T86: what the .ics engine does with files that are damaged, extreme or unusual, and how it writes them. The files are in
/// Fixtures/ics (data only); the expected values come from the description of each file. Files are read from their bytes, as the
/// Import menu does.
/// </summary>
public class IcsEngineTests
{
    private static string PathOf(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", "ics", name + ".ics");

    private static byte[] Bytes(string name) => File.ReadAllBytes(PathOf(name));

    private static IcsImport Analyze(string name, IcsImportOptions? options = null) => IcsParser.AnalyzeFile(Bytes(name), "dest", options);

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

    private static List<CalendarEvent> Shown(MainViewModel vm, string title, DateTime from, DateTime to) =>
        vm.ExpandedBetween(from, to, visibleOnly: false).Where(e => e.Title == title).OrderBy(e => e.Start).ToList();

    private static readonly DateTime Nov = new(2026, 11, 1);
    private static readonly DateTime Jan = new(2027, 1, 31);

    [Fact]
    [Trait("TestCase", "T60")]
    public void T60_FoldedLinesWithASpaceOrATabAndLfOnlyAreJoined()
    {
        var only = Assert.Single(Analyze("ggcal_fold_lf").Events);

        Assert.Equal(("[TEST GGCAL] " + string.Concat(Enumerable.Repeat("Titolo piegato con spazio ", 6))).Trim(), only.Title.Trim());
        Assert.Equal(string.Concat(Enumerable.Repeat("Luogo piegato con tabulazione ", 6)).Trim(), only.Location.Trim());
    }

    [Fact]
    [Trait("TestCase", "T61")]
    public void T61_EscapedCharactersInTextsBecomeTheRealCharacters()
    {
        var only = Assert.Single(Analyze("ggcal_escapes").Events);

        Assert.Equal("[TEST GGCAL] Virgola, punto e virgola; barra \\ fine", only.Title);
        Assert.Equal("Sala A, piano 2; ala nord", only.Location);
        Assert.Equal("Riga uno\nRiga due, con virgola\nOrario 10:30 e barra \\ finale", only.Description);
    }

    [Fact]
    [Trait("TestCase", "T62")]
    public void T62_QuotedParametersWithColonAndSemicolonDoNotBreakTheLine()
    {
        var only = Assert.Single(Analyze("ggcal_quoted_params").Events);

        Assert.Equal(["anna.verdi@example.com", "luca.rossi@example.com"], only.Attendees.Select(a => a.Email).Order().ToArray());
    }

    [Theory]
    [Trait("TestCase", "T63")]
    [InlineData("ggcal_not_calendar_text")]
    [InlineData("ggcal_not_calendar_html")]
    [InlineData("ggcal_not_calendar_binary")]
    public void T63_AFileThatIsNotACalendarGivesNoEventsAndSaysSo(string file)
    {
        var feed = Analyze(file);

        Assert.Empty(feed.Events);
        Assert.Equal(IcsIssueCode.NotACalendar, feed.Report.Fatal);
        Assert.NotEmpty(feed.Warnings);
    }

    [Fact]
    [Trait("TestCase", "T64")]
    public void T64_AnInnerBlockThatIsNeverClosedIsReportedAndTheEventsAreKept()
    {
        var feed = Analyze("ggcal_unclosed_inner");

        Assert.Equal(2, feed.Events.Count);
        Assert.True(feed.Report.Has(IcsIssueCode.MismatchedEnd));
        Assert.NotEmpty(feed.Warnings);
    }

    [Fact]
    [Trait("TestCase", "T65")]
    public void T65_ALineOver64KbIsDroppedAndReportedAndTheOtherEventsAreKept()
    {
        var feed = Analyze("ggcal_limits_line");

        Assert.Equal(2, feed.Events.Count);
        Assert.True(feed.Report.Has(IcsIssueCode.LineTooLong));
    }

    [Fact]
    [Trait("TestCase", "T65")]
    public void T65_BlocksNestedDeeperThanSixLevelsAreSkippedAndReported()
    {
        var feed = Analyze("ggcal_limits_deep");

        Assert.Equal(2, feed.Events.Count);
        Assert.True(feed.Report.Has(IcsIssueCode.TooDeep));
    }

    [Fact]
    [Trait("TestCase", "T65")]
    public void T65_MoreThanOneHundredThousandComponentsStopTheReadingWithAMessage()
    {
        var text = new StringBuilder("BEGIN:VCALENDAR\r\nVERSION:2.0\r\n");
        for (var i = 0; i < 100_001; i++) text.Append("BEGIN:VEVENT\r\nEND:VEVENT\r\n");
        text.Append("END:VCALENDAR\r\n");

        var clock = Stopwatch.StartNew();
        var feed = IcsParser.AnalyzeFile(Encoding.UTF8.GetBytes(text.ToString()), "dest");

        Assert.True(feed.Report.Has(IcsIssueCode.TooManyComponents));
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(30), $"Troppo lento: {clock.Elapsed.TotalSeconds:F1} s.");
    }

    [Fact]
    [Trait("TestCase", "T66")]
    public void T66_TwoCalendarsInOneFileGiveTheEventsOfBoth()
    {
        var feed = Analyze("ggcal_multi_vcalendar");

        Assert.Equal(["[TEST GGCAL] Nel primo calendario", "[TEST GGCAL] Nel secondo calendario"], feed.Events.Select(e => e.Title).Order().ToArray());
    }

    [Fact]
    [Trait("TestCase", "T67")]
    public void T67_TheNameOfTheCalendarInTheFileIsAvailableToProposeItForTheNewCalendar()
    {
        var expected = Regex.Match(File.ReadAllText(PathOf("ggcal_basic")), @"^X-WR-CALNAME:(.+?)\r?$", RegexOptions.Multiline).Groups[1].Value;

        Assert.Equal(expected, Analyze("ggcal_basic").Report.CalendarName);
    }

    [Fact]
    [Trait("TestCase", "T68")]
    public void T68_AFileWithMethodCancelImportsNothingAndSaysSo()
    {
        var feed = Analyze("ggcal_method_cancel");

        Assert.Empty(feed.Events);
        Assert.Equal("CANCEL", feed.Report.Method);
        Assert.NotEmpty(feed.Warnings);
    }

    [Fact]
    [Trait("TestCase", "T68")]
    public void T68_AFileWithMethodRequestImportsTheEventWithoutSendingInvitations()
    {
        var feed = Analyze("ggcal_method_request");

        var only = Assert.Single(feed.Events);
        Assert.False(only.SendInvites);
        Assert.Equal("REQUEST", feed.Report.Method);
    }

    [Fact]
    [Trait("TestCase", "T69")]
    public void T69_EventsWithoutStartOrWithAnImpossibleDateAreSkippedAndAnEndBeforeTheStartIsCorrected()
    {
        var feed = Analyze("ggcal_bad_dates");

        Assert.Equal(["[TEST GGCAL] Evento valido", "[TEST GGCAL] Fine prima dell'inizio"], feed.Events.Select(e => e.Title).Order().ToArray());
        var fixedEnd = Event(feed, "[TEST GGCAL] Fine prima dell'inizio");
        Assert.True(fixedEnd.End > fixedEnd.Start);
        Assert.True(feed.Report.Has(IcsIssueCode.MissingStart));
        Assert.True(feed.Report.Has(IcsIssueCode.InvalidDate));
        Assert.True(feed.Report.Has(IcsIssueCode.EndBeforeStart));
    }

    [Fact]
    [Trait("TestCase", "T70")]
    public void T70_EventsWithoutUidAreReportedAndNotDuplicatedWhenTheFileIsImportedAgain()
    {
        var vm = Open();
        var feed = Analyze("ggcal_no_uid");

        Assert.True(feed.Report.Has(IcsIssueCode.MissingUid));
        Assert.Equal((2, 0), vm.ImportEvents("dest", feed.Events, ImportDuplicates.Skip));
        Assert.Equal((0, 2), vm.ImportEvents("dest", Analyze("ggcal_no_uid").Events, ImportDuplicates.Skip));
        Assert.All(feed.Events, e => Assert.False(string.IsNullOrEmpty(e.IcsUid)));
        Assert.Equal(feed.Events.Select(e => e.IcsUid).Order(), Analyze("ggcal_no_uid").Events.Select(e => e.IcsUid).Order());
    }

    [Fact]
    [Trait("TestCase", "T71")]
    public void T71_AUidOfSixHundredCharactersIsCutTo512AndReported()
    {
        var feed = Analyze("ggcal_long_uid");

        Assert.Equal(512, Assert.Single(feed.Events).IcsUid!.Length);
        Assert.True(feed.Report.Has(IcsIssueCode.FieldTruncated));
    }

    [Fact]
    [Trait("TestCase", "T72")]
    public void T72_ThisAndFollowingIsImportedAsAChangeOfOneOccurrenceAndReported()
    {
        var feed = Analyze("ggcal_thisandfuture");

        var master = Assert.Single(feed.Events, e => e.SeriesMasterId is null);
        var changed = Assert.Single(feed.Events, e => e.SeriesMasterId is not null);
        Assert.Equal(master.Id, changed.SeriesMasterId);
        Assert.True(feed.Report.Has(IcsIssueCode.ThisAndFuture));
    }

    [Fact]
    [Trait("TestCase", "T73")]
    public void T73_RulesThatAreWrongOrNotSupportedAreReportedAndEachEventShowsOnce()
    {
        var feed = Analyze("ggcal_bad_rrule");
        var vm = Open();
        vm.ImportEvents("dest", feed.Events, ImportDuplicates.Skip);

        Assert.Equal(5, feed.Events.Count);
        Assert.Equal(5, feed.Report.CountOf(IcsIssueCode.InvalidRule));
        foreach (var title in feed.Events.Select(e => e.Title))
            Assert.Single(Shown(vm, title, Nov, Jan));
    }

    [Fact]
    [Trait("TestCase", "T74")]
    public void T74_UntilAsADateAsUtcAndAsLocalTimeEndTheSeriesOnTheSameDay()
    {
        var feed = Analyze("ggcal_until");
        var vm = Open();
        vm.ImportEvents("dest", feed.Events, ImportDuplicates.Skip);

        foreach (var title in new[] { "[TEST GGCAL] UNTIL data", "[TEST GGCAL] UNTIL UTC", "[TEST GGCAL] UNTIL locale" })
        {
            var shown = Shown(vm, title, Nov, Jan);
            Assert.Equal(InRome("20261105T090000"), shown[^1].Start);
            Assert.Equal(4, shown.Count);
        }
    }

    [Fact]
    [Trait("TestCase", "T75")]
    public void T75_ExcludedDatesInAListInSeparateLinesAndAsDatesOnlyAreAllLeftOut()
    {
        var feed = Analyze("ggcal_exdates");
        var vm = Open();
        vm.ImportEvents("dest", feed.Events, ImportDuplicates.Skip);

        var shown = Shown(vm, "[TEST GGCAL] Serie con date escluse", Nov, Jan);

        Assert.Equal(6, shown.Count);
        foreach (var day in new[] { 3, 4, 5, 7 })
            Assert.DoesNotContain(shown, e => e.Start.Date == new DateTime(2026, 11, day));
    }

    [Fact]
    [Trait("TestCase", "T76")]
    public void T76_RemindersAfterTheStartOrTooEarlyAreDroppedAtMostFiveAreKeptAndEqualOnesAreMerged()
    {
        var feed = Analyze("ggcal_alarm_limits");

        Assert.Empty(Event(feed, "[TEST GGCAL] Promemoria dopo l'inizio").Reminders);
        Assert.Empty(Event(feed, "[TEST GGCAL] Promemoria a 30 giorni").Reminders);
        Assert.Equal(5, Event(feed, "[TEST GGCAL] Otto promemoria").Reminders.Count);
        Assert.Single(Event(feed, "[TEST GGCAL] Due promemoria uguali").Reminders);
        Assert.True(feed.Report.Has(IcsIssueCode.AlarmAfterStart));
        Assert.True(feed.Report.Has(IcsIssueCode.AlarmTooEarly));
        Assert.True(feed.Report.Has(IcsIssueCode.TooManyAlarms));
    }

    [Fact]
    [Trait("TestCase", "T76")]
    public void T76_WithTheOptionOffNoReminderIsImported()
    {
        var feed = Analyze("ggcal_alarm_limits", new IcsImportOptions { ImportAlarms = false });

        Assert.All(feed.Events, e => Assert.Empty(e.Reminders));
    }

    [Fact]
    [Trait("TestCase", "T77")]
    public void T77_AnHttpsAttachmentIsReadAndCategoriesAreIgnoredAndReported()
    {
        var feed = Analyze("ggcal_attach");

        // The link of the attachment (https) is kept; the categories are not supported and the person is told.
        Assert.Equal(["https://example.com/documento.pdf"], Assert.Single(feed.Events).Attachments!.Select(a => a.Url));
        Assert.False(feed.Report.Has(IcsIssueCode.AttachIgnored));
        Assert.True(feed.Report.Has(IcsIssueCode.CategoriesIgnored));
    }

    [Fact]
    [Trait("TestCase", "T78")]
    public void T78_ControlCharactersAreDroppedAndAnEmojiOnTheLimitIsNotCutInHalf()
    {
        var feed = Analyze("ggcal_control_chars");

        Assert.Equal("[TEST GGCAL] Con caratteri di controllo", Event(feed, "[TEST GGCAL] Con caratteri di controllo").Title);
        var cut = feed.Events.Single(e => e.Title.StartsWith("[TEST GGCAL] AAAA", StringComparison.Ordinal)).Title;
        Assert.Equal(1023, cut.Length);
        Assert.False(char.IsHighSurrogate(cut[^1]));
        Assert.True(feed.Report.Has(IcsIssueCode.FieldTruncated));
    }

    [Fact]
    [Trait("TestCase", "T79")]
    public void T79_ATimeZoneDefinedInTheFileIsUsedWithItsStandardAndDaylightRules()
    {
        var feed = Analyze("ggcal_vtimezone_dst");

        Assert.Equal(Utc("20260715T090000"), Event(feed, "[TEST GGCAL] 12:00 d'estate (UTC+3 = 09:00 UTC)").Start);
        Assert.Equal(Utc("20260115T100000"), Event(feed, "[TEST GGCAL] 12:00 d'inverno (UTC+2 = 10:00 UTC)").Start);
    }

    [Fact]
    [Trait("TestCase", "T80")]
    public void T80_ATimeZoneKnownToTheSystemWinsOverItsRedefinitionInTheFile()
    {
        var feed = Analyze("ggcal_vtimezone_known");

        Assert.Equal(Utc("20261102T110000"), Assert.Single(feed.Events).Start);
    }

    [Fact]
    [Trait("TestCase", "T81")]
    public void T81_APrefixedZoneNameIsUnderstoodAndAnUnknownOneUsesThePcClockWithAWarning()
    {
        var feed = Analyze("ggcal_tzid_prefix");

        Assert.Equal(InRome("20261102T090000"), Event(feed, "[TEST GGCAL] Fuso con prefisso Mozilla").Start);
        Assert.Equal(new DateTime(2026, 11, 3, 9, 0, 0), Event(feed, "[TEST GGCAL] Fuso sconosciuto senza definizione").Start);
        Assert.True(feed.Report.Has(IcsIssueCode.UnknownTimeZone));
    }

    [Fact]
    [Trait("TestCase", "T82")]
    public void T82_ReimportingEventsWithTheSameUidFollowsTheAnswerAboutDuplicates()
    {
        var events = Analyze("ggcal_google_busy").Events;

        var skip = Open();
        Assert.Equal((109, 0), skip.ImportEvents("dest", events, ImportDuplicates.Skip));
        Assert.Equal((0, 109), skip.ImportEvents("dest", Analyze("ggcal_google_busy").Events, ImportDuplicates.Skip));

        var replace = Open();
        replace.ImportEvents("dest", events, ImportDuplicates.Skip);
        replace.ImportEvents("dest", Analyze("ggcal_google_busy").Events, ImportDuplicates.Replace);
        var kept = replace.EventsOf("dest").Where(e => e.SeriesMasterId is null).ToList();
        Assert.Equal(109, kept.Count);
        Assert.Equal(109, kept.Select(e => e.Start).Distinct().Count());

        var copy = Open();
        copy.ImportEvents("dest", events, ImportDuplicates.Skip);
        copy.ImportEvents("dest", Analyze("ggcal_google_busy").Events, ImportDuplicates.Copy);
        Assert.Equal(218, copy.EventsOf("dest").Count(e => e.SeriesMasterId is null));
    }

    [Fact]
    [Trait("TestCase", "T83")]
    public void T83_ExportedTimesKeepTheirInstantWhateverTheTimeZoneOfThePcThatReadsThem()
    {
        var rome = TimeZoneInfo.FindSystemTimeZoneById("Europe/Rome");
        var newYork = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        var day = new DateTime(2026, 11, 3);
        var single = new CalendarEvent("s", "dest", "Singolo", day.AddHours(10), day.AddHours(11)) { Reminders = [], IcsUid = "s@t" };
        var allDay = new CalendarEvent("a", "dest", "Giorno", day, day.AddDays(2), true) { Reminders = [], IcsUid = "a@t" };
        var series = new CalendarEvent("r", "dest", "Serie", day.AddHours(9), day.AddHours(10)) { Reminders = [], IcsUid = "r@t", Recurrence = "FREQ=DAILY;COUNT=4", ExcludedStarts = [day.AddDays(1).AddHours(9)] };
        var moved = new CalendarEvent("m", "dest", "Spostata", day.AddDays(2).AddHours(15), day.AddDays(2).AddHours(16)) { Reminders = [], SeriesMasterId = "r", OriginalStart = day.AddDays(2).AddHours(9) };

        var text = IcsWriter.Write([single, allDay, series, moved], localZone: rome);

        Assert.Matches(@"DTSTART:20261103T090000Z", text);                  // 10:00 in Rome is 09:00 UTC
        Assert.Contains("DTSTART;TZID=Europe/Rome:20261103T090000", text);   // the series keeps its zone
        Assert.Contains("DTEND;VALUE=DATE:20261105", text);                  // the end of an all-day event is exclusive
        Assert.Equal(2, Regex.Matches(text, "UID:r@t").Count);              // the changed occurrence has the UID of its series

        var read = IcsParser.Analyze(text, "dest", new IcsImportOptions { LocalZone = newYork });
        Assert.Equal(TimeZoneInfo.ConvertTime(day.AddHours(10), rome, newYork), Event(read, "Singolo").Start);
        Assert.Equal(day, Event(read, "Giorno").Start);
    }

    [Fact]
    [Trait("TestCase", "T84")]
    public void T84_NoLineIsLongerThan75BytesNoCharacterIsCutAndSpecialCharactersSurviveTheRoundTrip()
    {
        var day = new DateTime(2026, 11, 3);
        var title = "[TEST GGCAL] " + string.Concat(Enumerable.Repeat("àèìòù€☕日本語😀", 8));
        var location = "Sala A, piano 2; ala nord";
        var description = "riga uno\nriga due, con virgola; e punto e virgola";
        var ev = new CalendarEvent("x", "dest", title, day.AddHours(10), day.AddHours(11), Location: location, Description: description) { Reminders = [], IcsUid = "x@t" };

        var text = IcsWriter.Write([ev]);

        var physical = Encoding.UTF8.GetBytes(text.Replace("\r\n", "\n")).Length;
        Assert.True(text.Split("\r\n").All(l => Encoding.UTF8.GetByteCount(l) <= 75), "C'è una riga di più di 75 byte.");
        Assert.True(physical > 0);
        var read = Assert.Single(IcsParser.Analyze(text, "dest").Events);
        Assert.Equal((title, location, description), (read.Title, read.Location, read.Description));
    }

    [Fact]
    [Trait("TestCase", "T85")]
    public void T85_AttendeesRemindersAvailabilityVisibilityAndTheMeetLinkAreWritten()
    {
        var day = new DateTime(2026, 11, 3);
        var ev = new CalendarEvent("x", "dest", "Riunione", day.AddHours(10), day.AddHours(11)) { IcsUid = "x@t" }
            with
        {
            Attendees = [new EventAttendee("obbligatorio@example.com"), new EventAttendee("facoltativo@example.com", true)],
            Reminders = [new EventReminder(ReminderMethod.Popup, 10), new EventReminder(ReminderMethod.Alarm, 30), new EventReminder(ReminderMethod.Email, 60)],
            Availability = EventAvailability.Free,
            Visibility = EventVisibility.Private,
            MeetLink = "https://meet.google.com/abc-defg-hij",
        };

        var text = IcsWriter.Write([ev]);

        Assert.Contains("ROLE=REQ-PARTICIPANT:mailto:obbligatorio@example.com", text);
        Assert.Contains("ROLE=OPT-PARTICIPANT:mailto:facoltativo@example.com", text);
        Assert.Contains("TRANSP:TRANSPARENT", text);
        Assert.Contains("CLASS:PRIVATE", text);
        Assert.Equal(2, Regex.Matches(text, "BEGIN:VALARM").Count); // the reminder by email is not written
        Assert.Single(Regex.Matches(text, "(?m)^URL:https://meet.google.com/abc-defg-hij"));
        Assert.Single(Regex.Matches(text, "Google Meet: https://meet.google.com/abc-defg-hij"));
    }

    [Fact]
    [Trait("TestCase", "T86")]
    public void T86_AFileOverTenMegabytesIsRefusedWhileItIsReadAndOneWithinTheLimitIsRead()
    {
        using var big = new MemoryStream(new byte[IcsSources.MaxBytes + 1]);
        using var fits = new MemoryStream(new byte[IcsSources.MaxBytes]);

        Assert.Null(IcsSources.ReadLimited(big));
        Assert.Equal(IcsSources.MaxBytes, IcsSources.ReadLimited(fits)!.Length);
    }
}
