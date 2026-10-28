using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using GGCalendar.App.ViewModels;
using GGCalendar.App.Views;
using GGCalendar.Core.Models;
using GGCalendar.Core.Tests.Support;
using static GGCalendar.Core.Tests.Support.AppDriver;

namespace GGCalendar.Core.Tests;

/// <summary>Changes that travel between the app and the test Google account: conflicts (T11), edits made on Google (T12), deletions (T13), all-day events (T14).</summary>
[Collection(CollectionName)]
[Trait("Category", "RealGoogle")]
public class SyncUiTests
{
    private const string PickedHex = "#D50000"; // Google's "Pomodoro", color number 11

    private static string TitleOf(JsonElement item) => item.GetProperty("summary").GetString()!;

    /// <summary>A user creates an event and syncs. Returns what the tests need to know about it.</summary>
    private static async Task<(string Title, CalendarEvent Created, string RemoteId)> CreateSyncedAsync(MainWindow window, Func<string, EventSpec>? spec = null)
    {
        var title = NewTitle();
        var created = await CreateEventAsync(window, spec?.Invoke(title) ?? new EventSpec(title));
        await SyncAsync(window);
        var remoteId = TestGoogle.RemoteIdOf(created.Id) ?? throw new Xunit.Sdk.XunitException("L'evento non è stato inviato a Google.");
        return (title, created, remoteId);
    }

    private static List<string> TestTitles(MainWindow window) =>
        Vm(window).AllEvents.Where(e => e.CalendarId == TestCalendarId && e.Title.StartsWith(TestPrefix, StringComparison.Ordinal)).Select(e => e.Title).ToList();

    [Fact]
    [Trait("TestCase", "T286")]
    public Task T286_AnEventChangedOnBothSidesKeepsGoogleAndSavesTheLocalCopy() => InAppAsync(async window =>
    {
        var (title, created, remoteId) = await CreateSyncedAsync(window);
        var day = created.Start.Date;
        var googleTitle = title + " G";
        var localTitle = title + " L";

        // Google changes the event; before the app hears about it, the user changes it too.
        using var google = await TestGoogle.ConnectAsync();
        await google.PatchAsync(remoteId, new JsonObject { ["summary"] = googleTitle });
        var (dialog, _) = await OpenEditorAsync(window, title, day);
        await RetitleAsync(window, dialog, localTitle);
        await SyncAsync(window);

        // Google's version wins, and the user's version stays as a separate "(conflict)" event.
        var titles = TestTitles(window);
        Assert.Contains(googleTitle, titles);
        Assert.Contains(localTitle + GGCalendar.Core.Localization.L.T("sync.conflict_suffix"), titles);
        Assert.DoesNotContain(localTitle, titles);
        var onGoogle = (await google.FindAsync(TestPrefix)).Select(TitleOf).ToList();
        Assert.Contains(googleTitle, onGoogle);
        Assert.Contains(localTitle + GGCalendar.Core.Localization.L.T("sync.conflict_suffix"), onGoogle);
        Assert.DoesNotContain(localTitle, onGoogle);
    });

    [Fact]
    [Trait("TestCase", "T287")]
    public Task T287_ANewTitleSetOnGoogleArrivesInTheApp() => InAppAsync(async window =>
    {
        var (title, created, remoteId) = await CreateSyncedAsync(window);
        var day = created.Start.Date;
        var newTitle = title + " G";

        using var google = await TestGoogle.ConnectAsync();
        await google.PatchAsync(remoteId, new JsonObject { ["summary"] = newTitle });
        await SyncAsync(window);

        Assert.Equal(1, AgendaDayCount(window, day, newTitle));
        Assert.Equal(0, AgendaDayCount(window, day, title));
        window = await ReopenAsync(window);
        Assert.Equal(1, AgendaDayCount(window, day, newTitle));
    });

    [Fact]
    [Trait("TestCase", "T287")]
    public Task T287_ANewColorSetOnGoogleArrivesInTheApp() => InAppAsync(async window =>
    {
        var (title, created, remoteId) = await CreateSyncedAsync(window);
        var day = created.Start.Date;

        using var google = await TestGoogle.ConnectAsync();
        await google.PatchAsync(remoteId, new JsonObject { ["colorId"] = "11" });
        await SyncAsync(window);

        Assert.Equal(PickedHex, ColorSeenByTheUser(window, title, day), ignoreCase: true);
        window = await ReopenAsync(window);
        Assert.Equal(PickedHex, ColorSeenByTheUser(window, title, day), ignoreCase: true);
    });

    [Fact]
    [Trait("TestCase", "T288")]
    public Task T288_AnEventDeletedInTheAppDisappearsFromGoogle() => InAppAsync(async window =>
    {
        var (title, created, remoteId) = await CreateSyncedAsync(window);
        var day = created.Start.Date;

        var (dialog, _) = await OpenEditorAsync(window, title, day);
        await DeleteFromEditorAsync(window, dialog);
        await SyncAsync(window);

        Assert.Equal(0, AgendaDayCount(window, day, title));
        using var google = await TestGoogle.ConnectAsync();
        Assert.True(await google.IsGoneAsync(remoteId), "L'evento eliminato nell'app esiste ancora su Google.");
    });

    [Fact]
    [Trait("TestCase", "T288")]
    public Task T288_AnEventDeletedOnGoogleDisappearsFromTheApp() => InAppAsync(async window =>
    {
        var (title, created, remoteId) = await CreateSyncedAsync(window);
        var day = created.Start.Date;

        using var google = await TestGoogle.ConnectAsync();
        await google.DeleteAsync(remoteId);
        await SyncAsync(window);

        Assert.Equal(0, AgendaDayCount(window, day, title));
        Assert.DoesNotContain(title, TestTitles(window));
    });

    [Fact]
    [Trait("TestCase", "T288")]
    public Task T288_DeletingOneEventOfASeriesKeepsTheOthers() => InAppAsync(async window =>
    {
        var (title, created, remoteId) = await CreateSyncedAsync(window, t => new EventSpec(t) { Recurrence = "FREQ=DAILY" });
        var first = created.Start.Date;
        var deleted = first.AddDays(1);

        var (dialog, _) = await OpenEditorAsync(window, title, deleted, SeriesScope.This);
        await DeleteFromEditorAsync(window, dialog, SeriesScope.This);
        await SyncAsync(window);

        var days = AgendaOf(window, first, title).Select(e => e.Day).ToList();
        Assert.Equal(29, days.Count);
        Assert.DoesNotContain(deleted, days);

        using var google = await TestGoogle.ConnectAsync();
        Assert.False(await google.IsGoneAsync(remoteId), "La serie è sparita da Google.");
        var instances = await google.InstancesAsync(remoteId, first, first.AddDays(30));
        Assert.Equal(29, instances.Count);
        Assert.DoesNotContain(instances, i => DateTimeOffset.Parse(i.GetProperty("start").GetProperty("dateTime").GetString()!, CultureInfo.InvariantCulture).LocalDateTime.Date == deleted);
    });

    [Fact]
    [Trait("TestCase", "T288")]
    public Task T288_DeletingAWholeSeriesRemovesItFromGoogle() => InAppAsync(async window =>
    {
        var (title, created, remoteId) = await CreateSyncedAsync(window, t => new EventSpec(t) { Recurrence = "FREQ=DAILY" });
        var first = created.Start.Date;

        var (dialog, _) = await OpenEditorAsync(window, title, first.AddDays(1), SeriesScope.All);
        await DeleteFromEditorAsync(window, dialog, SeriesScope.All);
        await SyncAsync(window);

        Assert.Empty(AgendaOf(window, first, title));
        using var google = await TestGoogle.ConnectAsync();
        Assert.True(await google.IsGoneAsync(remoteId), "La serie eliminata nell'app esiste ancora su Google.");
    });

    [Fact]
    [Trait("TestCase", "T14")]
    public Task T14_AllDayEventsKeepTheirDaysOnGoogleAndInTheApp() => InAppAsync(async window =>
    {
        var day = DateTime.Today.AddDays(5);
        var oneDay = NewTitle() + " uno";
        var threeDays = NewTitle() + " tre";
        var one = await CreateEventAsync(window, new EventSpec(oneDay) { AllDay = true, Start = day, LastDay = day });
        var three = await CreateEventAsync(window, new EventSpec(threeDays) { AllDay = true, Start = day, LastDay = day.AddDays(2) });
        await SyncAsync(window);

        void AssertInApp(MainWindow w)
        {
            var events = Vm(w).AllEvents;
            var a = events.Single(e => e.Title == oneDay);
            var b = events.Single(e => e.Title == threeDays);
            Assert.Equal((true, day, day.AddDays(1)), (a.IsAllDay, a.Start.Date, a.End.Date)); // the end date is exclusive
            Assert.Equal((true, day, day.AddDays(3)), (b.IsAllDay, b.Start.Date, b.End.Date));
            Assert.Equal(1, AgendaDayCount(w, day.AddDays(-1), oneDay));
            Assert.Equal(3, AgendaDayCount(w, day.AddDays(-1), threeDays));
        }

        AssertInApp(window);

        using var google = await TestGoogle.ConnectAsync();
        var googleOne = await google.GetAsync(TestGoogle.RemoteIdOf(one.Id)!);
        var googleThree = await google.GetAsync(TestGoogle.RemoteIdOf(three.Id)!);
        string Date(JsonElement e, string which) => e.GetProperty(which).GetProperty("date").GetString()!;
        Assert.Equal((day.ToString("yyyy-MM-dd"), day.AddDays(1).ToString("yyyy-MM-dd")), (Date(googleOne, "start"), Date(googleOne, "end")));
        Assert.Equal((day.ToString("yyyy-MM-dd"), day.AddDays(3).ToString("yyyy-MM-dd")), (Date(googleThree, "start"), Date(googleThree, "end")));

        window = await ReopenAsync(window);
        AssertInApp(window);
    });
}
