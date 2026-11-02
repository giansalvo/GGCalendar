using System.Globalization;
using System.Text.Json;
using GGCalendar.App.ViewModels;
using GGCalendar.App.Views;
using GGCalendar.Core.Models;
using GGCalendar.Core.Tests.Support;
using static GGCalendar.Core.Tests.Support.AppDriver;

namespace GGCalendar.Core.Tests;

/// <summary>Moving an event between a calendar of this PC and Google (T28 and T29 with one account, T30 and T31 between the two test accounts, issue #65), on the real app and the test Google accounts. The rules are on the fake Google in <see cref="EventMoveTests"/>.</summary>
[Collection(CollectionName)]
[Trait("Category", "RealGoogle")]
public class EventMoveRealTests
{
    private static List<CalendarEvent> WithTitle(MainWindow window, string title) => Vm(window).AllEvents.Where(e => e.Title == title).ToList();

    [Fact]
    [Trait("TestCase", "T28")]
    public Task T28_AnEventMovedFromALocalCalendarToGoogleExistsThereOnce() => InAppAsync(async window =>
    {
        var local = await CreateLocalCalendarAsync(window, NewTitle());
        var title = NewTitle();
        var created = await CreateEventAsync(window, new EventSpec(title) { CalendarId = local });
        var day = created.Start.Date;

        var (dialog, _) = await OpenEditorAsync(window, title, day);
        await MoveFromEditorAsync(window, dialog, TestCalendarId);
        await SyncAsync(window);
        await SyncAsync(window);

        async Task AssertMovedAsync(MainWindow w)
        {
            var only = Assert.Single(WithTitle(w, title));
            Assert.Equal(TestCalendarId, only.CalendarId);
            using var google = await TestGoogle.ConnectAsync();
            var found = Assert.Single(await google.FindAsync(title));
            Assert.Equal(title, found.GetProperty("summary").GetString());
            var reminder = Assert.Single(found.GetProperty("reminders").GetProperty("overrides").EnumerateArray());
            Assert.Equal(("popup", 30), (reminder.GetProperty("method").GetString(), reminder.GetProperty("minutes").GetInt32()));
        }

        await AssertMovedAsync(window);
        window = await ReopenAsync(window);
        await SyncAsync(window);
        await AssertMovedAsync(window);
    });

    [Fact]
    [Trait("TestCase", "T29")]
    public Task T29_AnEventMovedFromGoogleToALocalCalendarIsGoneFromGoogleAndStaysHere() => InAppAsync(async window =>
    {
        var local = await CreateLocalCalendarAsync(window, NewTitle());
        var title = NewTitle();
        var created = await CreateEventAsync(window, new EventSpec(title));
        await SyncAsync(window);
        var remoteId = TestGoogle.RemoteIdOf(created.Id) ?? throw new Xunit.Sdk.XunitException("L'evento non è stato inviato a Google.");
        var day = created.Start.Date;

        var (dialog, _) = await OpenEditorAsync(window, title, day);
        await MoveFromEditorAsync(window, dialog, local);
        await SyncAsync(window);
        await SyncAsync(window);

        async Task AssertMovedAsync(MainWindow w)
        {
            var only = Assert.Single(WithTitle(w, title));
            Assert.Equal(local, only.CalendarId);
            using var google = await TestGoogle.ConnectAsync();
            Assert.True(await google.IsGoneAsync(remoteId), "L'evento spostato esiste ancora su Google.");
            Assert.Empty(await google.FindAsync(title));
            Assert.Null(TestGoogle.RemoteIdOf(only.Id)); // nothing is sent to Google for it any more
        }

        await AssertMovedAsync(window);
        window = await ReopenAsync(window);
        await SyncAsync(window);
        await AssertMovedAsync(window);
    });

    private static DateTimeOffset At(JsonElement item, string which) => DateTimeOffset.Parse(item.GetProperty(which).GetProperty("dateTime").GetString()!, CultureInfo.InvariantCulture);

    private static (string Method, int Minutes) ReminderOf(JsonElement item)
    {
        var first = Assert.Single(item.GetProperty("reminders").GetProperty("overrides").EnumerateArray());
        return (first.GetProperty("method").GetString()!, first.GetProperty("minutes").GetInt32());
    }

    private static bool IsOccurrence(JsonElement item) => item.TryGetProperty("recurringEventId", out _);

    [Fact]
    [Trait("TestCase", "T30")]
    public Task T30_AnEventMovedBetweenTwoAccountsIsGoneFromTheFirstAndExistsOnceInTheSecond() => InAppAsync(async window =>
    {
        await EnsureAccountReadyAsync(window, SecondTestAccount);
        var second = TestCalendarIdOf(SecondTestAccount);
        var title = NewTitle();
        var created = await CreateEventAsync(window, new EventSpec(title)); // the standard reminder: a notification 30 minutes before
        await SyncAsync(window);
        var remoteId = TestGoogle.RemoteIdOf(created.Id) ?? throw new Xunit.Sdk.XunitException("L'evento non è stato inviato a Google.");
        JsonElement before;
        using (var google = await TestGoogle.ConnectAsync()) before = await google.GetAsync(remoteId);

        var (dialog, _) = await OpenEditorAsync(window, title, created.Start.Date);
        await MoveFromEditorAsync(window, dialog, second);
        await SyncAsync(window);
        await SyncAsync(window);

        async Task AssertMovedAsync(MainWindow w)
        {
            var only = Assert.Single(WithTitle(w, title));
            Assert.Equal(second, only.CalendarId); // in one calendar of the app, not back in the first one
            using var first = await TestGoogle.ConnectAsync();
            Assert.True(await first.IsGoneAsync(remoteId), "L'evento spostato esiste ancora sul primo account.");
            Assert.Empty(await first.FindAsync(title));
            using var other = await TestGoogle.ConnectAsync(SecondTestAccount);
            var arrived = Assert.Single(await other.FindAsync(title));
            Assert.Equal((title, At(before, "start"), At(before, "end"), ReminderOf(before)),
                (arrived.GetProperty("summary").GetString(), At(arrived, "start"), At(arrived, "end"), ReminderOf(arrived)));
        }

        await AssertMovedAsync(window);
        window = await ReopenAsync(window);
        await SyncAsync(window);
        await AssertMovedAsync(window);
    });

    [Fact]
    [Trait("TestCase", "T31")]
    public Task T31_ASeriesWithAModifiedOccurrenceMovesWholeToTheSecondAccount() => InAppAsync(async window =>
    {
        await EnsureAccountReadyAsync(window, SecondTestAccount);
        var second = TestCalendarIdOf(SecondTestAccount);
        var title = NewTitle();
        var changedTitle = title + " modificato";
        var created = await CreateEventAsync(window, new EventSpec(title) { Recurrence = "FREQ=DAILY" });
        var firstDay = created.Start.Date;
        var changedDay = firstDay.AddDays(1);

        var (editOne, _) = await OpenEditorAsync(window, title, changedDay, SeriesScope.This);
        await RetitleAsync(window, editOne, changedTitle);
        await SyncAsync(window);

        var (dialog, _) = await OpenEditorAsync(window, title, firstDay, SeriesScope.All);
        await MoveFromEditorAsync(window, dialog, second);
        await SyncAsync(window);
        await SyncAsync(window);

        async Task AssertMovedAsync(MainWindow w)
        {
            // The days and the titles are the ones before the move, with no duplicates.
            var days = AgendaOf(w, firstDay, title);
            Assert.Equal(30, days.Count);
            Assert.Equal(30, days.Select(d => d.Day).Distinct().Count());
            foreach (var (day, shown, _) in days) Assert.Equal(day == changedDay ? changedTitle : title, shown);
            Assert.All(Vm(w).AllEvents.Where(e => e.Title == title || e.Title == changedTitle), e => Assert.Equal(second, e.CalendarId));

            using var first = await TestGoogle.ConnectAsync();
            Assert.Empty(await first.FindAsync(title)); // neither the series nor its modified occurrence stay
            using var other = await TestGoogle.ConnectAsync(SecondTestAccount);
            var items = await other.FindAsync(title);
            var series = Assert.Single(items, i => !IsOccurrence(i));
            Assert.Contains("RRULE:FREQ=DAILY", string.Join(" ", series.GetProperty("recurrence").EnumerateArray().Select(r => r.GetString())));
            var occurrence = Assert.Single(items, IsOccurrence);
            Assert.Equal(changedTitle, occurrence.GetProperty("summary").GetString());
            Assert.Equal(changedDay, DateTimeOffset.Parse(occurrence.GetProperty("originalStartTime").GetProperty("dateTime").GetString()!, CultureInfo.InvariantCulture).LocalDateTime.Date);
        }

        await AssertMovedAsync(window);
        window = await ReopenAsync(window);
        await SyncAsync(window);
        await AssertMovedAsync(window);
    });
}
