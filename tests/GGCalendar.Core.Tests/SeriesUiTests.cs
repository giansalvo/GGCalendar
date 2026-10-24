using System.Globalization;
using System.Text.Json;
using GGCalendar.App.Services;
using GGCalendar.App.ViewModels;
using GGCalendar.App.Views;
using GGCalendar.Core.Tests.Support;
using static GGCalendar.Core.Tests.Support.AppDriver;

namespace GGCalendar.Core.Tests;

/// <summary>Repeating events on the test Google account: creating a series (T04), changing one, the following or all of its events (T05-T07), the clock change (T16).</summary>
[Collection(CollectionName)]
[Trait("Category", "RealGoogle")]
public class SeriesUiTests
{
    private const string Daily = "FREQ=DAILY";

    private static string Recurrence(JsonElement item) =>
        item.TryGetProperty("recurrence", out var r) ? string.Join(" ", r.EnumerateArray().Select(x => x.GetString())) : "";

    private static bool IsOccurrence(JsonElement item) => item.TryGetProperty("recurringEventId", out _);

    private static string Title(JsonElement item) => item.GetProperty("summary").GetString()!;

    /// <summary>A user creates "Ogni giorno" from today and syncs.</summary>
    private static async Task<(string Title, DateTime First)> CreateDailySeriesAsync(MainWindow window)
    {
        var title = NewTitle();
        var created = await CreateEventAsync(window, new EventSpec(title) { Recurrence = Daily });
        await SyncAsync(window);
        return (title, created.Start.Date);
    }

    [Fact]
    [Trait("TestCase", "T04")]
    public Task T04_ADailyEventIsOneSeriesOnGoogleAndShowsOnEveryDay() => InAppAsync(async window =>
    {
        var (title, first) = await CreateDailySeriesAsync(window);

        using var google = await TestGoogle.ConnectAsync();
        var items = await google.FindAsync(title);
        var series = Assert.Single(items);
        Assert.False(IsOccurrence(series));
        Assert.Contains("RRULE:FREQ=DAILY", Recurrence(series));

        void AssertEveryDay(MainWindow w)
        {
            var days = AgendaOf(w, first, title).Select(e => e.Day).ToList();
            Assert.Equal(30, days.Count);
            Assert.Equal(30, days.Distinct().Count()); // no duplicates
        }

        AssertEveryDay(window);
        window = await ReopenAsync(window);
        AssertEveryDay(window);
    });

    [Fact]
    [Trait("TestCase", "T05")]
    public Task T05_ChangingOnlyOneEventOfASeriesLeavesTheOthersAlone() => InAppAsync(async window =>
    {
        var (title, first) = await CreateDailySeriesAsync(window);
        var changed = first.AddDays(1);
        var newTitle = title + " modificato";

        var (dialog, _) = await OpenEditorAsync(window, title, changed, SeriesScope.This);
        await RetitleAsync(window, dialog, newTitle);
        await SyncAsync(window);

        foreach (var (day, shown, _) in AgendaOf(window, first, title))
            Assert.Equal(day == changed ? newTitle : title, shown);

        using var google = await TestGoogle.ConnectAsync();
        var items = await google.FindAsync(title);
        Assert.Equal(title, Title(Assert.Single(items, i => !IsOccurrence(i))));
        var occurrence = Assert.Single(items, IsOccurrence);
        Assert.Equal(newTitle, Title(occurrence));
        Assert.Equal(changed, DateTimeOffset.Parse(occurrence.GetProperty("originalStartTime").GetProperty("dateTime").GetString()!, CultureInfo.InvariantCulture).LocalDateTime.Date);
    });

    [Fact]
    [Trait("TestCase", "T06")]
    public Task T06_ChangingThisAndTheFollowingEventsSplitsTheSeries() => InAppAsync(async window =>
    {
        var (title, first) = await CreateDailySeriesAsync(window);
        var from = first.AddDays(2);
        var newTitle = title + " dal terzo giorno";

        var (dialog, _) = await OpenEditorAsync(window, title, from, SeriesScope.ThisAndFollowing);
        await RetitleAsync(window, dialog, newTitle);
        await SyncAsync(window);

        foreach (var (day, shown, _) in AgendaOf(window, first, title))
            Assert.Equal(day < from ? title : newTitle, shown);

        using var google = await TestGoogle.ConnectAsync();
        var items = await google.FindAsync(TestPrefix);
        Assert.DoesNotContain(items, IsOccurrence);
        var old = Assert.Single(items, i => Title(i) == title);
        Assert.Matches("UNTIL=|COUNT=", Recurrence(old)); // the old series ends before the changed day
        var created = Assert.Single(items, i => Title(i) == newTitle);
        Assert.Contains("RRULE:FREQ=DAILY", Recurrence(created));
    });

    [Fact]
    [Trait("TestCase", "T07")]
    public Task T07_ChangingAllTheEventsOfASeriesChangesThemAll() => InAppAsync(async window =>
    {
        var (title, first) = await CreateDailySeriesAsync(window);
        var newTitle = title + " tutti";

        var (dialog, _) = await OpenEditorAsync(window, title, first.AddDays(1), SeriesScope.All);
        await RetitleAsync(window, dialog, newTitle);
        await SyncAsync(window);

        var shown = AgendaOf(window, first, title);
        Assert.Equal(30, shown.Count);
        Assert.All(shown, e => Assert.Equal(newTitle, e.Title));

        using var google = await TestGoogle.ConnectAsync();
        var items = await google.FindAsync(TestPrefix);
        var series = Assert.Single(items);
        Assert.Equal(newTitle, Title(series));
        Assert.Contains("RRULE:FREQ=DAILY", Recurrence(series));
    });

    [Fact]
    [Trait("TestCase", "T16")]
    public Task T16_ADailySeriesKeepsItsHourAcrossTheClockChange() => InAppAsync(async window =>
    {
        // Clocks go forward in Europe on Sunday 28/03/2027. The series starts on the Friday at 9:00 (PC with the Italian time zone).
        var start = new DateTime(2027, 3, 26, 9, 0, 0);
        var changed = new DateTime(2027, 3, 29);
        var title = NewTitle();
        await CreateEventAsync(window, new EventSpec(title) { Recurrence = Daily, Start = start });
        await SyncAsync(window);

        var (dialog, _) = await OpenEditorAsync(window, title, changed, SeriesScope.This);
        await RetitleAsync(window, dialog, title + " dopo");
        await SyncAsync(window);

        // Every event is still at 9:00 in the app, before and after the change...
        var shown = AgendaOf(window, start, title);
        Assert.Equal(30, shown.Count);
        Assert.All(shown, e => Assert.Equal(TimeSpan.FromHours(9), e.Start.TimeOfDay));

        // ...and on Google, which expands the series itself.
        using var google = await TestGoogle.ConnectAsync();
        var items = await google.FindAsync(TestPrefix);
        var master = Assert.Single(items, i => !IsOccurrence(i));
        var instances = await google.InstancesAsync(master.GetProperty("id").GetString()!, start.Date, start.Date.AddDays(7));
        Assert.Equal(7, instances.Count);
        Assert.All(instances, i => Assert.Equal(TimeSpan.FromHours(9),
            DateTimeOffset.Parse(i.GetProperty("start").GetProperty("dateTime").GetString()!, CultureInfo.InvariantCulture).ToLocalTime().TimeOfDay));

        // The modified event is the one of Monday 29th at 9:00, not an hour off.
        var occurrence = Assert.Single(items, IsOccurrence);
        var expected = new DateTimeOffset(changed.AddHours(9), TimeZoneInfo.Local.GetUtcOffset(changed.AddHours(9)));
        Assert.Equal(expected.UtcDateTime,
            DateTimeOffset.Parse(occurrence.GetProperty("originalStartTime").GetProperty("dateTime").GetString()!, CultureInfo.InvariantCulture).UtcDateTime);
    });
}
