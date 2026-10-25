using GGCalendar.App.ViewModels;
using GGCalendar.Core.Ics;
using GGCalendar.Core.Models;
using GGCalendar.Data;

namespace GGCalendar.Core.Tests;

/// <summary>
/// T21: import and export of .ics files, along the path the menu items take (export the calendar, import into another one,
/// the three answers about duplicates), without the native file picker, which cannot be driven without a screen.
/// </summary>
public class IcsImportExportTests
{
    private static readonly DateTime Day = new(2026, 10, 14);

    private static MainViewModel Open()
    {
        var store = new CalendarStore(":memory:");
        store.UpsertCalendar(new CalendarInfo("a", "Locale", "Origine", "#1A73E8"));
        store.UpsertCalendar(new CalendarInfo("b", "Locale", "Destinazione", "#33B679"));
        var vm = new MainViewModel(store);
        vm.AddEvent(new CalendarEvent("timed", "a", "Riunione, con virgola", Day.AddHours(10), Day.AddHours(11), Location: "Sala A", Description: "riga1\nriga2")
        {
            Attendees = [new EventAttendee("ospite@example.com")],
            Reminders = [new EventReminder(ReminderMethod.Popup, 15)],
        });
        vm.AddEvent(new CalendarEvent("allday", "a", "Giornata", Day, Day.AddDays(2), true) { Reminders = [] });
        vm.AddEvent(new CalendarEvent("series", "a", "Serie", Day.AddHours(9), Day.AddHours(10))
        {
            Recurrence = "FREQ=DAILY;COUNT=4", ExcludedStarts = [Day.AddDays(2).AddHours(9)],
        });
        return vm;
    }

    private static string ExportCalendar(MainViewModel vm, string calendarId) => IcsWriter.Write(vm.EventsOf(calendarId));

    private static (int Imported, int Skipped) Import(MainViewModel vm, string calendarId, string ics, ImportDuplicates onExisting) =>
        vm.ImportEvents(calendarId, IcsParser.Analyze(ics, calendarId).Events, onExisting);

    private static List<string> Titles(MainViewModel vm, string calendarId) =>
        vm.EventsOf(calendarId).Where(e => e.SeriesMasterId is null).Select(e => e.Title).Order().ToList();

    [Fact]
    [Trait("TestCase", "T21")]
    public void T21_ACalendarExportedAndImportedElsewhereHasTheSameEvents()
    {
        var vm = Open();

        var (imported, skipped) = Import(vm, "b", ExportCalendar(vm, "a"), ImportDuplicates.Skip);

        Assert.Equal((3, 0), (imported, skipped));
        Assert.Equal(Titles(vm, "a"), Titles(vm, "b"));
        var timed = vm.EventsOf("b").Single(e => e.Title.StartsWith("Riunione", StringComparison.Ordinal));
        Assert.Equal((Day.AddHours(10), Day.AddHours(11), "Sala A", "riga1\nriga2"), (timed.Start, timed.End, timed.Location, timed.Description));
        Assert.Equal("ospite@example.com", Assert.Single(timed.Attendees).Email);
        Assert.Equal(15, Assert.Single(timed.Reminders).Minutes);
        var allDay = vm.EventsOf("b").Single(e => e.Title == "Giornata");
        Assert.Equal((true, Day, Day.AddDays(2)), (allDay.IsAllDay, allDay.Start, allDay.End));
        var series = vm.EventsOf("b").Single(e => e.Title == "Serie");
        Assert.Equal("FREQ=DAILY;COUNT=4", series.Recurrence);
        Assert.Single(series.ExcludedStarts);
    }

    [Fact]
    [Trait("TestCase", "T21")]
    public void T21_ImportingTheSameFileAgainFollowsTheAnswerAboutDuplicates()
    {
        var vm = Open();
        var file = ExportCalendar(vm, "a");
        Import(vm, "b", file, ImportDuplicates.Skip);

        // Skip: nothing is added.
        Assert.Equal((0, 3), Import(vm, "b", file, ImportDuplicates.Skip));
        Assert.Equal(3, Titles(vm, "b").Count);

        // Replace: the imported version takes the place of the existing one, still three events.
        Assert.Equal((3, 0), Import(vm, "b", file, ImportDuplicates.Replace));
        Assert.Equal(3, Titles(vm, "b").Count);

        // Copy: the events are added again.
        Assert.Equal((3, 0), Import(vm, "b", file, ImportDuplicates.Copy));
        Assert.Equal(6, Titles(vm, "b").Count);
    }

    [Fact]
    [Trait("TestCase", "T21")]
    public void T21_ASingleEventExportedFromTheEditorCanBeImportedBack()
    {
        var vm = Open();
        var one = vm.EventsOf("a").Single(e => e.Title.StartsWith("Riunione", StringComparison.Ordinal));

        var (imported, _) = Import(vm, "b", IcsWriter.Write([one]), ImportDuplicates.Skip);

        Assert.Equal(1, imported);
        Assert.Equal(one.Title, Assert.Single(Titles(vm, "b")));
    }

    [Fact]
    [Trait("TestCase", "T21")]
    public void T21_AFileWithoutEventsOrWithUnsupportedPartsIsReported()
    {
        var empty = IcsParser.Analyze("BEGIN:VCALENDAR\r\nEND:VCALENDAR\r\n", "b");
        Assert.Empty(empty.Events);

        const string foreign = "BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nUID:x\r\nDTSTART:20261014T080000\r\nDTEND:20261014T090000\r\n" +
                               "SUMMARY:Estraneo\r\nATTACH:http://x/y.pdf\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n";
        var parsed = IcsParser.Analyze(foreign, "b");
        Assert.Single(parsed.Events);
        Assert.NotEmpty(parsed.Warnings); // the user is told what could not be imported
    }
}
