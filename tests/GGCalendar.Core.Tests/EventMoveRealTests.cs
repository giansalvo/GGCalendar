using GGCalendar.App.Views;
using GGCalendar.Core.Models;
using GGCalendar.Core.Tests.Support;
using static GGCalendar.Core.Tests.Support.AppDriver;

namespace GGCalendar.Core.Tests;

/// <summary>Moving an event between a calendar of this PC and Google (T28, T29, issue #65), on the real app and the test Google account. The rules are on the fake Google in <see cref="EventMoveTests"/>.</summary>
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
}
