using GGCalendar.App.Services;
using GGCalendar.Core.Subscriptions;
using GGCalendar.Core.Tests.Support;
using GGCalendar.Data;
using static GGCalendar.Core.Tests.Support.AppDriver;

namespace GGCalendar.Core.Tests;

/// <summary>A subscribed calendar and a Google account together (the test account of the app): Google never sees the events of a public calendar.</summary>
[Collection(CollectionName)]
[Trait("Category", "RealGoogle")]
public class SubscriptionGoogleTests
{
    [Fact]
    [Trait("TestCase", "T106")]
    public Task T106_NoEventOfASubscribedCalendarIsSentToGoogle() => InAppAsync(async window =>
    {
        var title = NewTitle() + " abbonato";
        var day = DateTime.Today.AddDays(2);
        var body = "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nBEGIN:VEVENT\r\nUID:g1\r\nSUMMARY:" + title + "\r\n" +
                   $"DTSTART:{day:yyyyMMdd}T100000\r\nDTEND:{day:yyyyMMdd}T110000\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n";
        var site = new FakeSite().Serve("/g.ics", body);
        var previous = SubscriptionService.FetcherFactory;
        SubscriptionService.FetcherFactory = () => new IcsFetcher(site);
        try
        {
            var service = new SubscriptionService(new CalendarStore(), new IcsFetcher(site));
            var preview = await service.PreviewAsync("https://calendar.example.com/g.ics");
            var (calendar, problem) = service.Add(preview.Preview!, NewTitle() + " cal", "#123456", SubscriptionRange.All);
            Assert.Null(problem);
            await Vm(window).ReloadAsync();
            Assert.Contains(Vm(window).AllEvents, e => e.Title == title);

            await SyncAsync(window);

            Assert.DoesNotContain(new CalendarStore().GetPending(), p => p.CalendarId == calendar!.Id);
            using var google = await TestGoogle.ConnectAsync();
            Assert.Empty(await google.FindAsync(title));
            Assert.Contains(Vm(window).AllEvents, e => e.Title == title); // and the sync did not remove it

            service.Remove(calendar!.Id);
            await Vm(window).ReloadAsync();
        }
        finally
        {
            SubscriptionService.FetcherFactory = previous;
        }
    });
}
