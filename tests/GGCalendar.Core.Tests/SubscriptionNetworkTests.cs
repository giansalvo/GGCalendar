using GGCalendar.App.Services;
using GGCalendar.Core.Subscriptions;
using GGCalendar.Core.Tests.Support;
using GGCalendar.Data;
using Xunit.Abstractions;

namespace GGCalendar.Core.Tests;

/// <summary>
/// The subscriptions on the real network: the test feeds of the author's site (they answer with a redirect, a status, a delay, a big file,
/// a date of last change) and the public calendar of Google, whose address is kept outside the repository. They are left out of the
/// run on GitHub (category RealGoogle: they need the network and take a few minutes).
/// </summary>
[Trait("Category", "RealGoogle")]
public class SubscriptionNetworkTests(ITestOutputHelper output)
{
    private const string Feeds = "https://profgusinu.altervista.org/GGCalendarAndroid/feeds/";

    /// <summary>The address of the public calendar of Google, written by hand in a file of the test folder (never in the code).</summary>
    private static string? GoogleUrl()
    {
        var file = Path.Combine(TestData.Folder, "public-calendar-url.txt");
        return File.Exists(file) ? File.ReadAllText(file).Trim() : null;
    }

    private static (SubscriptionService Service, CalendarStore Store) Rig(TimeSpan? timeout = null)
    {
        var store = new CalendarStore(":memory:");
        return (new SubscriptionService(store, new IcsFetcher { Timeout = timeout ?? TimeSpan.FromSeconds(60) }), store);
    }

    [Fact]
    [Trait("TestCase", "T92")]
    public async Task T92_ARedirectToHttpsIsFollowed()
    {
        var (service, _) = Rig();
        var preview = await service.PreviewAsync(Feeds + "redirect.php");
        Assert.True(preview.IsOk, preview.Problem?.ToString());
        Assert.NotEmpty(preview.Preview!.Snapshot.Events);
    }

    [Fact]
    [Trait("TestCase", "T94")]
    public async Task T94_AFileOf11MbSentInPiecesIsRefusedWhileReading()
    {
        var (service, store) = Rig();
        Assert.Equal(SubscriptionProblem.TooLarge, (await service.PreviewAsync(Feeds + "big.php")).Problem);
        Assert.Empty(store.GetCalendars());
    }

    [Fact]
    [Trait("TestCase", "T95")]
    public async Task T95_ASlowSiteEndsWithATimeout()
    {
        var (service, _) = Rig(TimeSpan.FromSeconds(3));
        Assert.Equal(SubscriptionProblem.Timeout, (await service.PreviewAsync(Feeds + "slow.php?delay=20")).Problem);
    }

    [Theory]
    [Trait("TestCase", "T97")]
    [InlineData(404, SubscriptionProblem.NotFound)]
    [InlineData(403, SubscriptionProblem.AccessDenied)]
    [InlineData(429, SubscriptionProblem.RateLimited)]
    [InlineData(500, SubscriptionProblem.ServerError)]
    public async Task T97_TheStatusesOfASiteHaveTheirOwnProblem(int code, SubscriptionProblem expected)
    {
        var (service, _) = Rig();
        Assert.Equal(expected, (await service.PreviewAsync(Feeds + "status.php?code=" + code)).Problem);
    }

    [Fact]
    [Trait("TestCase", "T99")]
    public async Task T99_ASiteThatKeepsTheDateOfLastChangeAnswers304()
    {
        var (service, store) = Rig();
        var preview = await service.PreviewAsync(Feeds + "etag.php");
        Assert.True(preview.IsOk, preview.Problem?.ToString());
        Assert.NotNull(preview.Preview!.LastModified);
        var (calendar, _) = service.Add(preview.Preview, "Prova", "#123456", SubscriptionRange.All);
        var result = await service.RefreshAsync(calendar!.Id);
        Assert.Equal(RefreshOutcome.NotModified, result.Outcome);
        Assert.NotEmpty(store.GetAllEvents());
    }

    [Fact]
    [Trait("TestCase", "T156")]
    public async Task T156_ThePublicCalendarOfGoogleIsReadWithItsOrphanOccurrences()
    {
        if (GoogleUrl() is not { } url) { output.WriteLine("Test saltato: manca public-calendar-url.txt nella cartella dei test."); return; }
        var (service, store) = Rig();
        var preview = await service.PreviewAsync(url);
        Assert.True(preview.IsOk, preview.Problem?.ToString());
        var snap = preview.Preview!.Snapshot;
        output.WriteLine($"eventi {snap.Events.Count}, dal {snap.FirstStart:d} al {snap.LastEnd:d}, occorrenze orfane {snap.Report.OrphanOccurrences}");
        Assert.NotEmpty(snap.Events);
        Assert.False(string.IsNullOrWhiteSpace(preview.Preview.SuggestedName));
        Assert.Null(preview.Preview.ETag); // Google sends no ETag for these feeds

        var (calendar, problem) = service.Add(preview.Preview, preview.Preview.SuggestedName, "#123456", SubscriptionRange.All);
        Assert.Null(problem);
        Assert.Equal(snap.Events.Count, store.GetAllEvents().Count(e => e.CalendarId == calendar!.Id));
    }

    [Fact]
    [Trait("TestCase", "T157")]
    public async Task T157_GoogleSendsNoETagSoTheSameFileIsRecognisedByItsContent()
    {
        if (GoogleUrl() is not { } url) { output.WriteLine("Test saltato: manca public-calendar-url.txt nella cartella dei test."); return; }
        var (service, store) = Rig();
        var preview = await service.PreviewAsync(url);
        Assert.True(preview.IsOk, preview.Problem?.ToString());
        var (calendar, _) = service.Add(preview.Preview!, "Google", "#123456", SubscriptionRange.All);
        var first = store.GetSubscription(calendar!.Id)!;

        await Task.Delay(1100);
        var result = await service.RefreshAsync(calendar.Id);
        var second = store.GetSubscription(calendar.Id)!;
        Assert.Equal(RefreshOutcome.NotModified, result.Outcome);
        Assert.Equal(first.Hash, second.Hash);
        Assert.True(second.LastFetchedUtc > first.LastFetchedUtc); // the time of the last check moves
    }

    [Fact]
    [Trait("TestCase", "T157")]
    public async Task T157_TwoReadsOneAfterTheOtherWithTheSameFetcherBothWork()
    {
        if (GoogleUrl() is not { } url) return;
        using var fetcher = new IcsFetcher();
        var uri = SubscriptionUrls.Check(url).Uri!;
        var first = await fetcher.FetchAsync(uri);
        await Task.Delay(1100);
        var second = await fetcher.FetchAsync(uri);
        output.WriteLine($"first {first.Status} {first.Error} {first.Detail}; second {second.Status} {second.Error} {second.Detail}");
        Assert.Equal(FetchStatus.Ok, first.Status);
        Assert.Equal(FetchStatus.Ok, second.Status);
    }
}
