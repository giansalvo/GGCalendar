using GGCalendar.App.Services;
using GGCalendar.Core.Models;
using GGCalendar.Core.Tests.Support;
using GGCalendar.Data;
using GGCalendar.Providers.Google;
using Xunit.Abstractions;

namespace GGCalendar.Core.Tests;

/// <summary>T119: the real Google, which does refuse a burst of writes. Only on the developer's PC (category RealGoogle).</summary>
[Collection(AppDriver.CollectionName)]
[Trait("Category", "RealGoogle")]
public class SyncLimitRealGoogleTests(ITestOutputHelper output)
{
    private const int Count = 300;

    [Fact]
    [Trait("TestCase", "T119")]
    public async Task T119_ManyEventsReachGoogleEvenIfGoogleSlowsDownOnTheWay()
    {
        var settings = new SettingsStore().Load();
        var config = GoogleClientConfig.Resolve(settings.GoogleClientId, settings.GoogleClientSecret);
        var store = new CalendarStore(":memory:");
        var calendar = GoogleSyncService.LocalCalendarId(AppDriver.TestAccount, AppDriver.TestAccount);
        store.UpsertCalendar(new CalendarInfo(calendar, AppDriver.TestAccount, "Prova", "#1A73E8"));
        var service = new GoogleSyncService(store, new GoogleAccountStore(), new HttpClient()) { PushPace = TimeSpan.Zero }; // as fast as possible: if Google slows us down the sending must wait and go on
        service.Configure(config);
        var day = DateTime.Today.AddDays(40);
        var titles = Enumerable.Range(1, Count).Select(AppDriver.NewTitle).ToList();
        for (var i = 0; i < Count; i++)
        {
            var ev = new CalendarEvent(Guid.NewGuid().ToString("N"), calendar, titles[i], day.AddHours(8 + i % 10), day.AddHours(9 + i % 10)) { Reminders = [] };
            store.UpsertEvent(ev);
            store.Enqueue(ev.Id, ev.CalendarId, OutboxOperation.Create, ev);
        }

        using var direct = await TestGoogle.ConnectAsync();
        try
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var slowedDown = 0;
            SyncResult result;
            var pass = 0;
            do
            {
                result = await service.SyncAllAsync(CancellationToken.None);
                output.WriteLine($"giro {++pass}: inviati {result.Pushed}, ancora in coda {result.Pending}, riprovare alle {result.RetryAt?.ToLocalTime():HH:mm:ss}, errori {result.Errors.Count}");
                Assert.Empty(result.Errors);
                if (result.RetryAt is { } at)
                {
                    slowedDown++;
                    await Task.Delay(at - DateTimeOffset.UtcNow + TimeSpan.FromSeconds(2));
                }
            }
            while (result.Pending > 0 && pass < 10);

            Assert.Equal(0, result.Pending);
            var onGoogle = (await direct.FindAsync(AppDriver.TestPrefix)).Select(e => e.GetProperty("summary").GetString()).ToList();
            output.WriteLine($"su Google: {onGoogle.Count(t => titles.Contains(t))} dei {Count} eventi; Google ha rallentato {slowedDown} volte; invio in {clock.Elapsed.TotalSeconds:F0} s");
            Assert.All(titles, t => Assert.Single(onGoogle, g => g == t)); // every event is there, none twice
        }
        finally
        {
            await direct.DeleteLeftoversAsync();
        }
    }
}
