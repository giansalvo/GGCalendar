using System.Net;
using System.Text;
using GGCalendar.App.Services;
using GGCalendar.Core.Models;
using GGCalendar.Core.Subscriptions;
using GGCalendar.Core.Tests.Support;
using GGCalendar.Data;
using Xunit;

namespace GGCalendar.Core.Tests;

/// <summary>Subscriptions to public calendars by address (issue #4): the address, the download, the store and the refresh, on a fake site.</summary>
public class SubscriptionTests
{
    private const string Secret = "SECRETKEY123";
    private const string Host = "https://calendar.example.com";

    private static string Ics(params (string Uid, string Title, string Start)[] events) => Ics(null, events);

    private static string Ics(string? name, params (string Uid, string Title, string Start)[] events)
    {
        var sb = new StringBuilder("BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:-//test//EN\r\n");
        if (name is not null) sb.Append("X-WR-CALNAME:").Append(name).Append("\r\n");
        foreach (var (uid, title, start) in events)
        {
            sb.Append("BEGIN:VEVENT\r\nUID:").Append(uid).Append("\r\nSUMMARY:").Append(title)
              .Append("\r\nDTSTART:").Append(start).Append("\r\nDTEND:").Append(Plus(start)).Append("\r\nEND:VEVENT\r\n");
        }

        return sb.Append("END:VCALENDAR\r\n").ToString();
    }

    private static string Plus(string start) => start.Substring(0, 9) + "10" + start.Substring(11);

    private static (SubscriptionService Service, CalendarStore Store, FakeSite Site, Clock Clock) Rig(FakeSite? site = null, TimeSpan? timeout = null)
    {
        site ??= new FakeSite();
        var store = new CalendarStore(":memory:");
        var clock = new Clock();
        var fetcher = new IcsFetcher(site) { Timeout = timeout ?? TimeSpan.FromSeconds(10) };
        var service = new SubscriptionService(store, fetcher, () => clock.Now, s => "enc:" + s, s => s["enc:".Length..]);
        return (service, store, site, clock);
    }

    private sealed class Clock
    {
        public DateTime Now { get; set; } = new(2026, 10, 25, 12, 0, 0, DateTimeKind.Utc);
    }

    private static async Task<CalendarInfo> AddAsync(SubscriptionService service, string url, string name = "Pubblico", SubscriptionRange? range = null)
    {
        var preview = await service.PreviewAsync(url);
        Assert.True(preview.IsOk, preview.Problem?.ToString());
        var (calendar, problem) = service.Add(preview.Preview!, name, "#123456", range ?? SubscriptionRange.All);
        Assert.Null(problem);
        return calendar!;
    }

    // ---- the address ----

    [Fact] // T87
    public async Task WebcalAddressIsReadAsHttps()
    {
        Assert.Equal("https://calendar.example.com/a.ics", SubscriptionUrls.Check("webcal://calendar.example.com/a.ics").Uri!.AbsoluteUri);
        var (service, _, site, _) = Rig(new FakeSite().Serve("/a.ics", Ics(("u1", "Uno", "20260310T090000"))));
        var preview = await service.PreviewAsync("webcal://calendar.example.com/a.ics");
        Assert.True(preview.IsOk);
        Assert.All(site.Requests, r => Assert.Equal("https", r.Scheme));
    }

    [Fact] // T88
    public async Task HttpAddressIsRefusedWithoutAnyRequest()
    {
        var (service, _, site, _) = Rig();
        var result = await service.PreviewAsync("http://calendar.example.com/a.ics");
        Assert.Equal(SubscriptionProblem.NotHttps, result.Problem);
        Assert.Empty(site.Requests);
    }

    [Fact] // T89
    public async Task AddressWithCredentialsIsRefusedAndNeverShown()
    {
        var (service, _, site, _) = Rig();
        var result = await service.PreviewAsync("https://utente:" + Secret + "@calendar.example.com/a.ics");
        Assert.Equal(SubscriptionProblem.HasCredentials, result.Problem);
        Assert.DoesNotContain(Secret, result.Problem.ToString());
        Assert.Empty(site.Requests);
    }

    [Theory] // T90
    [InlineData("ciao")]
    [InlineData("https://")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ftp://calendar.example.com/a.ics")]
    public async Task NotAnAddressIsRefused(string text)
    {
        var (service, _, site, _) = Rig();
        var result = await service.PreviewAsync(text);
        Assert.NotNull(result.Problem);
        Assert.Empty(site.Requests);
    }

    [Fact] // T98
    public void AddressIsMaskedAndRedacted()
    {
        var uri = new Uri(Host + "/ical/" + Secret + "/basic.ics?key=" + Secret);
        Assert.Equal("calendar.example.com/…", SubscriptionUrls.Mask(uri));
        Assert.DoesNotContain(Secret, SubscriptionUrls.Mask(uri));
        var text = SubscriptionUrls.Redact("error calling " + uri.AbsoluteUri + " and " + uri.PathAndQuery, uri);
        Assert.DoesNotContain(Secret, text);
    }

    // ---- download ----

    [Fact] // T91
    public async Task PreviewCountsEventsAndIgnoredComponents()
    {
        var body = Ics("Pubblico di prova", ("u1", "Uno", "20260310T090000"), ("u2", "Due", "20260311T090000"))
            .Replace("END:VCALENDAR", "BEGIN:VTODO\r\nUID:t1\r\nSUMMARY:fare\r\nEND:VTODO\r\nEND:VCALENDAR");
        var (service, store, _, _) = Rig(new FakeSite().Serve("/a.ics", body));
        var preview = await service.PreviewAsync(Host + "/a.ics");
        Assert.True(preview.IsOk);
        Assert.Equal(2, preview.Preview!.Snapshot.Events.Count);
        Assert.Equal("Pubblico di prova", preview.Preview.SuggestedName);
        Assert.Equal(1, preview.Preview.Snapshot.Report.IgnoredComponents["VTODO"]);
        Assert.Empty(store.GetCalendars()); // nothing is saved by the preview
    }

    [Fact] // T92
    public async Task RedirectsToHttpsAreFollowedAndToHttpRefused()
    {
        var site = new FakeSite()
            .Redirect("/old", Host + "/new.ics")
            .Redirect("/relative", "/new.ics")
            .Redirect("/insecure", "http://calendar.example.com/new.ics")
            .Serve("/new.ics", Ics(("u1", "Uno", "20260310T090000")));
        var (service, _, _, _) = Rig(site);
        Assert.True((await service.PreviewAsync(Host + "/old")).IsOk);
        Assert.True((await service.PreviewAsync(Host + "/relative")).IsOk);
        Assert.Equal(SubscriptionProblem.RedirectNotHttps, (await service.PreviewAsync(Host + "/insecure")).Problem);
    }

    [Fact] // T93
    public async Task MoreThanFiveRedirectsStop()
    {
        var site = new FakeSite().Serve("/end.ics", Ics(("u1", "Uno", "20260310T090000")));
        for (var i = 0; i < 7; i++) site.Redirect("/r" + i, Host + "/r" + (i + 1));
        site.Redirect("/r7", Host + "/end.ics");
        var (service, _, _, _) = Rig(site);
        Assert.Equal(SubscriptionProblem.TooManyRedirects, (await service.PreviewAsync(Host + "/r0")).Problem);
        Assert.Equal(6, site.Requests.Count); // the first request and five redirects
    }

    [Fact] // T94
    public async Task FileOver10MbIsRefusedBeforeAndWhileReading()
    {
        var size = 11L * 1024 * 1024;
        var site = new FakeSite()
            .Route("/declared", (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new DeclaredLengthContent(size) }))
            .Route("/chunked", (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new UnknownLengthContent(size) }));
        var (service, store, _, _) = Rig(site);
        Assert.Equal(SubscriptionProblem.TooLarge, (await service.PreviewAsync(Host + "/declared")).Problem);
        Assert.Equal(SubscriptionProblem.TooLarge, (await service.PreviewAsync(Host + "/chunked")).Problem);
        Assert.Empty(store.GetCalendars());
    }

    [Fact] // T95
    public async Task SlowSiteTimesOut()
    {
        var site = new FakeSite().Route("/slow", async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var (service, _, _, _) = Rig(site, TimeSpan.FromMilliseconds(300));
        Assert.Equal(SubscriptionProblem.Timeout, (await service.PreviewAsync(Host + "/slow")).Problem);
    }

    [Fact] // T155 (cancel)
    public async Task CancellingStopsTheRequest()
    {
        var site = new FakeSite().Route("/slow", async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var (service, _, _, _) = Rig(site);
        using var cts = new CancellationTokenSource(200);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.PreviewAsync(Host + "/slow", cts.Token));
    }

    [Fact] // T96
    public async Task WebPageAndEmptyFileAreNotCalendars()
    {
        var site = new FakeSite()
            .Serve("/page", "<html><body>Ciao</body></html>")
            .Serve("/empty", "  \r\n");
        var (service, _, _, _) = Rig(site);
        Assert.Equal(SubscriptionProblem.NotACalendar, (await service.PreviewAsync(Host + "/page")).Problem);
        Assert.Equal(SubscriptionProblem.Empty, (await service.PreviewAsync(Host + "/empty")).Problem);
    }

    [Theory] // T97
    [InlineData(404, SubscriptionProblem.NotFound)]
    [InlineData(410, SubscriptionProblem.NotFound)]
    [InlineData(401, SubscriptionProblem.AccessDenied)]
    [InlineData(403, SubscriptionProblem.AccessDenied)]
    [InlineData(429, SubscriptionProblem.RateLimited)]
    [InlineData(500, SubscriptionProblem.ServerError)]
    [InlineData(503, SubscriptionProblem.ServerError)]
    [InlineData(418, SubscriptionProblem.HttpError)]
    public async Task HttpErrorsHaveTheirOwnProblem(int status, SubscriptionProblem expected)
    {
        var (service, _, _, _) = Rig(new FakeSite().Status("/a.ics", (HttpStatusCode)status));
        Assert.Equal(expected, (await service.PreviewAsync(Host + "/a.ics")).Problem);
    }

    [Fact] // T97
    public async Task CertificateAndNetworkErrorsAreTold()
    {
        var site = new FakeSite()
            .Route("/tls", (_, _) => throw new HttpRequestException("ssl", new System.Security.Authentication.AuthenticationException("bad certificate")))
            .Route("/net", (_, _) => throw new HttpRequestException("no route to " + Host + "/net?" + Secret, new System.Net.Sockets.SocketException()));
        var (service, _, _, _) = Rig(site);
        Assert.Equal(SubscriptionProblem.Tls, (await service.PreviewAsync(Host + "/tls")).Problem);
        Assert.Equal(SubscriptionProblem.NoNetwork, (await service.PreviewAsync(Host + "/net")).Problem);
    }

    [Fact] // the request: user agent, no credentials, no cookies
    public async Task RequestSaysWhoAsksAndSendsNoCredentials()
    {
        var (service, _, site, _) = Rig(new FakeSite().Serve("/a.ics", Ics(("u1", "Uno", "20260310T090000"))));
        await service.PreviewAsync(Host + "/a.ics");
        var request = Assert.Single(site.Messages);
        Assert.Equal("GGCalendar", request.Headers.UserAgent.ToString());
        Assert.Null(request.Headers.Authorization);
        Assert.False(request.Headers.Contains("Cookie"));
    }

    // ---- limits ----

    [Fact] // T104
    public async Task SameAddressOnlyOnceAndAtMostTenSubscriptions()
    {
        var site = new FakeSite();
        for (var i = 0; i < 12; i++) site.Serve("/c" + i, Ics(("u" + i, "Uno", "20260310T090000")));
        var (service, store, _, _) = Rig(site);
        await AddAsync(service, Host + "/c0");
        Assert.Equal(SubscriptionProblem.Duplicate, (await service.PreviewAsync(Host + "/c0")).Problem);
        Assert.Equal(SubscriptionProblem.Duplicate, (await service.PreviewAsync("webcal://calendar.example.com/c0")).Problem);
        for (var i = 1; i < 10; i++) await AddAsync(service, Host + "/c" + i);
        Assert.Equal(10, store.GetSubscriptions().Count);
        Assert.Equal(SubscriptionProblem.TooManySubscriptions, (await service.PreviewAsync(Host + "/c10")).Problem);
    }

    [Fact] // T105
    public async Task MoreThan5000EventsIsRefusedWithoutSavingAnything()
    {
        var events = Enumerable.Range(0, 5001).Select(i => ("u" + i, "E" + i, "20260310T090000")).ToArray();
        var (service, store, _, _) = Rig(new FakeSite().Serve("/big", Ics(events)));
        var preview = await service.PreviewAsync(Host + "/big");
        Assert.True(preview.IsOk); // the window must be able to ask for a shorter range
        var (calendar, problem) = service.Add(preview.Preview!, "Grande", "#123456", SubscriptionRange.All);
        Assert.Null(calendar);
        Assert.Equal(SubscriptionProblem.TooManyEvents, problem);
        Assert.Empty(store.GetCalendars());
    }

    [Fact] // T105: the limit counts the events after the range
    public async Task LimitCountsEventsInsideTheRange()
    {
        var events = Enumerable.Range(0, 5100).Select(i => ("u" + i, "E" + i, i < 100 ? "20260310T090000" : "20300310T090000")).ToArray();
        var (service, store, _, _) = Rig(new FakeSite().Serve("/big", Ics(events)));
        var preview = await service.PreviewAsync(Host + "/big");
        var (calendar, problem) = service.Add(preview.Preview!, "Grande", "#123456", new SubscriptionRange(new DateTime(2026, 1, 1), new DateTime(2026, 12, 31)));
        Assert.Null(problem);
        Assert.Equal(100, store.GetEvents(new DateTime(2026, 1, 1), new DateTime(2027, 1, 1)).Count(e => e.CalendarId == calendar!.Id));
    }

    // ---- save, refresh ----

    [Fact] // T103
    public async Task CalendarGetsChosenNameColourAndRange()
    {
        var site = new FakeSite().Serve("/a.ics", Ics(("u1", "Uno", "20260310T090000"), ("u2", "Due", "20260610T090000"), ("u3", "Tre", "20261010T090000")));
        var (service, store, _, _) = Rig(site);
        var preview = (await service.PreviewAsync(Host + "/a.ics")).Preview!;
        Assert.Equal(3, preview.Snapshot.Events.Count);
        var range = new SubscriptionRange(new DateTime(2026, 5, 1), new DateTime(2026, 7, 31));
        Assert.Equal(1, preview.Recount(range).Snapshot.Events.Count);

        var (calendar, _) = service.Add(preview, "  Il mio  ", "#AABBCC", range);
        Assert.Equal("Il mio", calendar!.Name);
        Assert.Equal("#AABBCC", calendar.ColorHex);
        Assert.StartsWith("sub:", calendar.Id);
        Assert.Equal("Due", Assert.Single(store.GetAllEvents()).Title);
        Assert.Equal(range, store.GetSubscription(calendar.Id)!.Range);
    }

    [Fact] // T107
    public async Task AddressIsStoredEncryptedAndRemovalDeletesEverything()
    {
        var (service, store, _, _) = Rig(new FakeSite().Serve("/" + Secret + "/a.ics", Ics(("u1", "Uno", "20260310T090000"))));
        var calendar = await AddAsync(service, Host + "/" + Secret + "/a.ics");
        Assert.NotNull(store.GetSubscription(calendar.Id));
        Assert.Equal(Host + "/" + Secret + "/a.ics", service.GetUrl(calendar.Id));

        service.Remove(calendar.Id);
        Assert.Empty(store.GetCalendars());
        Assert.Empty(store.GetAllEvents());
        Assert.Empty(store.GetSubscriptions());
    }

    [Fact] // T107 with the real encryption of Windows
    public async Task RealEncryptionHidesTheAddress()
    {
        var site = new FakeSite().Serve("/" + Secret + "/a.ics", Ics(("u1", "Uno", "20260310T090000")));
        var store = new CalendarStore(":memory:");
        var service = new SubscriptionService(store, new IcsFetcher(site));
        var calendar = await AddAsync(service, Host + "/" + Secret + "/a.ics");
        var stored = store.GetSubscription(calendar.Id)!.EncryptedUrl;
        Assert.DoesNotContain(Secret, stored);
        Assert.DoesNotContain("calendar.example.com", stored);
        Assert.Equal(Host + "/" + Secret + "/a.ics", service.GetUrl(calendar.Id));
    }

    [Fact] // T100
    public async Task RefreshAppliesAddedChangedAndRemovedEvents()
    {
        string body = Ics(("u1", "Uno", "20260310T090000"), ("u2", "Due", "20260311T090000"), ("u3", "Tre", "20260312T090000"));
        var site = new FakeSite().Route("/a.ics", (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) }));
        var (service, store, _, _) = Rig(site);
        var calendar = await AddAsync(service, Host + "/a.ics");
        var idsBefore = store.GetAllEvents().ToDictionary(e => e.IcsUid!, e => e.Id);

        body = Ics(("u1", "Uno", "20260310T090000"), ("u2", "Due cambiato", "20260311T090000"), ("u4", "Quattro", "20260313T090000"));
        var result = await service.RefreshAsync(calendar.Id);
        Assert.Equal(RefreshOutcome.Updated, result.Outcome);
        Assert.Equal(new SubscriptionChanges(1, 1, 1), result.Changes);

        var after = store.GetAllEvents().ToDictionary(e => e.IcsUid!);
        Assert.Equal(["u1", "u2", "u4"], after.Keys.Order());
        Assert.Equal("Due cambiato", after["u2"].Title);
        Assert.Equal(idsBefore["u1"], after["u1"].Id); // the events that did not change keep their ids
        Assert.Equal(idsBefore["u2"], after["u2"].Id);
    }

    [Fact] // T99
    public async Task UnchangedFileAnswers304ByDate()
    {
        var site = new FakeSite().Serve("/a.ics", Ics(("u1", "Uno", "20260310T090000")), lastModified: "Wed, 21 Oct 2026 07:28:00 GMT");
        var (service, store, _, _) = Rig(site);
        var calendar = await AddAsync(service, Host + "/a.ics");
        Assert.Equal("Wed, 21 Oct 2026 07:28:00 GMT", store.GetSubscription(calendar.Id)!.LastModified);

        var result = await service.RefreshAsync(calendar.Id);
        Assert.Equal(RefreshOutcome.NotModified, result.Outcome);
        Assert.True(site.Messages.Last().Headers.IfModifiedSince.HasValue);
        Assert.Single(store.GetAllEvents());
    }

    [Fact] // T108
    public async Task ETagGives304AndADifferentETagWithTheSameContentChangesNothing()
    {
        var body = Ics(("u1", "Uno", "20260310T090000"));
        var etag = "\"v1\"";
        var site = new FakeSite().Route("/a.ics", (req, _) =>
        {
            if (req.Headers.IfNoneMatch.Any(t => t.Tag == etag)) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotModified));
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
            response.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue(etag);
            return Task.FromResult(response);
        });
        var (service, store, _, _) = Rig(site);
        var calendar = await AddAsync(service, Host + "/a.ics");

        Assert.Equal(RefreshOutcome.NotModified, (await service.RefreshAsync(calendar.Id)).Outcome);
        Assert.Equal("Uno", Assert.Single(store.GetAllEvents()).Title);

        etag = "\"v2\""; // another version, the same content
        var again = await service.RefreshAsync(calendar.Id);
        Assert.Equal(RefreshOutcome.NotModified, again.Outcome);
        Assert.Equal("\"v2\"", store.GetSubscription(calendar.Id)!.ETag);
    }

    [Fact] // T157: no ETag, no date: the hash recognises the same file
    public async Task SameContentWithoutValidatorsIsRecognisedByHash()
    {
        var site = new FakeSite().Serve("/a.ics", Ics(("u1", "Uno", "20260310T090000")));
        var (service, store, _, clock) = Rig(site);
        var calendar = await AddAsync(service, Host + "/a.ics");
        var first = store.GetSubscription(calendar.Id)!;
        Assert.NotNull(first.Hash);

        clock.Now = clock.Now.AddMinutes(45);
        var result = await service.RefreshAsync(calendar.Id);
        Assert.Equal(RefreshOutcome.NotModified, result.Outcome);
        var second = store.GetSubscription(calendar.Id)!;
        Assert.Equal(first.Hash, second.Hash);
        Assert.Equal(clock.Now, second.LastFetchedUtc); // the time of the last check moves
    }

    [Fact] // T157: the feeds of Google change DTSTAMP and the order of the events at every download
    public async Task SameCalendarWithAnotherOrderAndTimestampIsTheSameContent()
    {
        string Feed(bool reversed, string stamp)
        {
            var events = new[] { ("u1", "Uno", "20260310T090000"), ("u2", "Due", "20260311T090000"), ("u3", "Tre", "20260312T090000") };
            if (reversed) Array.Reverse(events);
            return Ics(events).Replace("DTSTART:", "DTSTAMP:" + stamp + "\r\nDTSTART:");
        }

        var body = Feed(false, "20261025T180451Z");
        var site = new FakeSite().Route("/a.ics", (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) }));
        var (service, store, _, _) = Rig(site);
        var calendar = await AddAsync(service, Host + "/a.ics");
        var first = store.GetSubscription(calendar.Id)!.Hash;
        var ids = store.GetAllEvents().Select(e => e.Id).Order().ToList();

        body = Feed(true, "20261025T180453Z");
        var result = await service.RefreshAsync(calendar.Id);
        Assert.Equal(RefreshOutcome.NotModified, result.Outcome);
        Assert.Equal(first, store.GetSubscription(calendar.Id)!.Hash);
        Assert.Equal(ids, store.GetAllEvents().Select(e => e.Id).Order());

        body = Feed(true, "20261025T180453Z").Replace("Tre", "Tre cambiato");
        Assert.Equal(RefreshOutcome.Updated, (await service.RefreshAsync(calendar.Id)).Outcome);
    }

    [Fact] // T101
    public async Task FailedRefreshKeepsEventsAndTellsTheReason()
    {
        var failing = false;
        var site = new FakeSite().Route("/a.ics", (_, _) => Task.FromResult(failing
            ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Ics(("u1", "Uno", "20260310T090000"))) }));
        var (service, store, _, _) = Rig(site);
        var calendar = await AddAsync(service, Host + "/a.ics");

        failing = true;
        var result = await service.RefreshAsync(calendar.Id);
        Assert.Equal(RefreshOutcome.Failed, result.Outcome);
        Assert.Equal(SubscriptionProblem.ServerError, result.Problem);
        Assert.Single(store.GetAllEvents());
        Assert.Equal("ServerError", store.GetSubscription(calendar.Id)!.LastError);

        failing = false;
        await service.RefreshAsync(calendar.Id);
        Assert.Null(store.GetSubscription(calendar.Id)!.LastError);
    }

    [Fact] // T101: a refresh that returns a file too large or not a calendar keeps what is there
    public async Task BadFileOnRefreshKeepsEvents()
    {
        var body = Ics(("u1", "Uno", "20260310T090000"));
        var site = new FakeSite().Route("/a.ics", (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) }));
        var (service, store, _, _) = Rig(site);
        var calendar = await AddAsync(service, Host + "/a.ics");
        body = "<html>login</html>";
        var result = await service.RefreshAsync(calendar.Id);
        Assert.Equal(SubscriptionProblem.NotACalendar, result.Problem);
        Assert.Single(store.GetAllEvents());
    }

    [Fact] // T102 and T160
    public async Task DueSubscriptionsAreRefreshedAtTheIntervalWithATolerance()
    {
        var site = new FakeSite().Serve("/a.ics", Ics(("u1", "Uno", "20260310T090000"))).Serve("/b.ics", Ics(("u2", "Due", "20260311T090000")));
        var (service, store, _, clock) = Rig(site);
        await AddAsync(service, Host + "/a.ics");
        clock.Now = clock.Now.AddMinutes(20);
        await AddAsync(service, Host + "/b.ics");
        var interval = TimeSpan.FromMinutes(30);

        site.Requests.Clear();
        Assert.Empty(await service.RefreshDueAsync(interval)); // 20 and 0 minutes: nothing is due

        clock.Now = clock.Now.AddMinutes(8); // a: 28 minutes, within the tolerance of 3 minutes; b: 8
        var due = await service.RefreshDueAsync(interval);
        Assert.Equal(["sub:"], due.Select(r => r.CalendarId[..4]).Distinct());
        Assert.Single(due);
        Assert.Single(site.Requests);
        Assert.Equal("/a.ics", site.Requests[0].AbsolutePath);
        Assert.Equal(2, store.GetSubscriptions().Count);
    }

    [Fact] // T160: tolerance is at most 2 minutes
    public async Task ToleranceIsAtMostTwoMinutes()
    {
        var site = new FakeSite().Serve("/a.ics", Ics(("u1", "Uno", "20260310T090000")));
        var (service, _, _, clock) = Rig(site);
        await AddAsync(service, Host + "/a.ics");
        clock.Now = clock.Now.AddHours(24).AddMinutes(-3);
        Assert.Empty(await service.RefreshDueAsync(TimeSpan.FromHours(24)));
        clock.Now = clock.Now.AddMinutes(1).AddSeconds(1);
        Assert.Single(await service.RefreshDueAsync(TimeSpan.FromHours(24)));
    }

    [Fact] // T160: without a network the other subscriptions are not tried
    public async Task NoNetworkStopsTheRound()
    {
        var site = new FakeSite()
            .Serve("/a.ics", Ics(("u1", "Uno", "20260310T090000")))
            .Serve("/b.ics", Ics(("u2", "Due", "20260311T090000")));
        var (service, _, _, clock) = Rig(site);
        await AddAsync(service, Host + "/a.ics");
        await AddAsync(service, Host + "/b.ics");
        site.Route("/a.ics", (_, _) => throw new HttpRequestException("down", new System.Net.Sockets.SocketException()));
        site.Route("/b.ics", (_, _) => throw new HttpRequestException("down", new System.Net.Sockets.SocketException()));
        clock.Now = clock.Now.AddHours(2);
        site.Requests.Clear();

        var results = await service.RefreshDueAsync(TimeSpan.FromMinutes(30));
        Assert.Single(results);
        Assert.Equal(SubscriptionProblem.NoNetwork, results[0].Problem);
        Assert.Single(site.Requests);
    }

    [Fact] // changing the range downloads the file again
    public async Task ChangingTheRangeDownloadsAgain()
    {
        var site = new FakeSite().Serve("/a.ics", Ics(("u1", "Uno", "20260310T090000"), ("u2", "Due", "20260610T090000")), etag: "\"v1\"");
        var (service, store, _, _) = Rig(site);
        var calendar = await AddAsync(service, Host + "/a.ics", range: new SubscriptionRange(new DateTime(2026, 5, 1), null));
        Assert.Equal("Due", Assert.Single(store.GetAllEvents()).Title);

        var result = await service.UpdateAsync(calendar.Id, "Nuovo nome", "#000000", SubscriptionRange.All);
        Assert.Equal(RefreshOutcome.Updated, result!.Outcome);
        Assert.Equal(2, store.GetAllEvents().Count);
        Assert.Equal("Nuovo nome", store.GetCalendars().Single().Name);
    }

    [Fact] // T162
    public async Task DataSurvivesReopeningTheDatabase()
    {
        var path = Path.Combine(Path.GetTempPath(), "ggcal-sub-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var site = new FakeSite().Serve("/a.ics", Ics(("u1", "Uno", "20260310T090000")));
            var store = new CalendarStore(path);
            var service = new SubscriptionService(store, new IcsFetcher(site), null, s => "enc:" + s, s => s[4..]);
            var calendar = await AddAsync(service, Host + "/a.ics", range: new SubscriptionRange(new DateTime(2026, 1, 1), new DateTime(2026, 12, 31)));
            site.Status("/a.ics", HttpStatusCode.NotFound);
            await service.RefreshAsync(calendar.Id);

            var reopened = new CalendarStore(path).GetSubscription(calendar.Id)!;
            Assert.Equal("NotFound", reopened.LastError);
            Assert.Equal(new DateTime(2026, 1, 1), reopened.RangeFrom);
            Assert.Equal(new DateTime(2026, 12, 31), reopened.RangeTo);
            Assert.NotNull(reopened.Hash);
            Assert.NotNull(reopened.LastSyncedUtc);
            Assert.Single(new CalendarStore(path).GetAllEvents());
        }
        finally
        {
            foreach (var f in Directory.GetFiles(Path.GetTempPath(), Path.GetFileName(path) + "*")) File.Delete(f);
        }
    }

    // ---- range ----

    private static CalendarEvent Ev(string id, DateTime start, int days = 1, string? rule = null) =>
        new(id, "c", id, start, start.AddDays(days), IsAllDay: true) { Recurrence = rule };

    [Fact] // T103
    public void RangeKeepsEventsWithAnyDayInside()
    {
        var events = new[]
        {
            Ev("before", new DateTime(2026, 4, 20)),
            Ev("starts-before", new DateTime(2026, 4, 29), 3), // 29, 30 April, 1 May
            Ev("inside", new DateTime(2026, 5, 15)),
            Ev("ends-after", new DateTime(2026, 5, 30), 5),
            Ev("after", new DateTime(2026, 6, 1)),
            Ev("last-day", new DateTime(2026, 5, 31)),
        };
        var kept = new SubscriptionRange(new DateTime(2026, 5, 1), new DateTime(2026, 5, 31)).Filter(events).Select(e => e.Id).Order();
        Assert.Equal(["ends-after", "inside", "last-day", "starts-before"], kept);
    }

    [Fact] // T103
    public void EmptySideIsUnlimitedAndSeriesAreKeptIfAnyOccurrenceIsInside()
    {
        var weekly = Ev("weekly", new DateTime(2026, 1, 5), 1, "FREQ=WEEKLY;COUNT=10"); // until March
        var forever = Ev("forever", new DateTime(2026, 1, 5), 1, "FREQ=DAILY");
        var single = Ev("single", new DateTime(2026, 8, 1));
        var all = new[] { weekly, forever, single };

        Assert.Equal(["forever", "single"], new SubscriptionRange(new DateTime(2026, 7, 1), null).Filter(all).Select(e => e.Id).Order());
        Assert.Equal(["forever", "weekly"], new SubscriptionRange(null, new DateTime(2026, 2, 1)).Filter(all).Select(e => e.Id).Order());
        Assert.Equal(["forever", "weekly"], new SubscriptionRange(new DateTime(2026, 2, 1), new DateTime(2026, 2, 28)).Filter(all).Select(e => e.Id).Order());
        Assert.Equal(3, SubscriptionRange.All.Filter(all).Count);
    }

    [Fact] // T103: modified occurrences follow their series
    public void ModifiedOccurrencesFollowTheirSeries()
    {
        var series = Ev("s", new DateTime(2026, 1, 5), 1, "FREQ=WEEKLY;COUNT=4");
        var moved = Ev("m", new DateTime(2026, 1, 13)) with { SeriesMasterId = "s", OriginalStart = new DateTime(2026, 1, 12) };
        var other = Ev("o", new DateTime(2025, 1, 5), 1, "FREQ=WEEKLY;COUNT=2");
        var movedOther = Ev("mo", new DateTime(2025, 1, 13)) with { SeriesMasterId = "o", OriginalStart = new DateTime(2025, 1, 12) };
        var kept = new SubscriptionRange(new DateTime(2026, 1, 1), null).Filter([series, moved, other, movedOther]).Select(e => e.Id).Order();
        Assert.Equal(["m", "s"], kept);
    }

    // ---- ids, times ----

    [Fact] // stable ids from the UID
    public void IdsComeFromTheUidAndSurviveAReparse()
    {
        var bytes = Encoding.UTF8.GetBytes(Ics(("u1", "Uno", "20260310T090000"), ("u2", "Due", "20260311T090000")));
        var a = SubscriptionContent.Process(bytes, "sub:x", SubscriptionRange.All).Events;
        var b = SubscriptionContent.Process(bytes, "sub:x", SubscriptionRange.All).Events;
        Assert.Equal(a.Select(e => e.Id), b.Select(e => e.Id));
        Assert.Equal(2, a.Select(e => e.Id).Distinct().Count());
        Assert.NotEqual(a[0].Id, SubscriptionContent.Process(bytes, "sub:y", SubscriptionRange.All).Events[0].Id);
    }

    [Fact] // T91 and T156: the public feeds of Google, with occurrences whose series is not in the file
    public void OrphanOccurrencesBecomeSingleEventsWithStableIds()
    {
        var text = "BEGIN:VCALENDAR\r\nVERSION:2.0\r\n" +
                   "BEGIN:VEVENT\r\nUID:series@google.com\r\nRECURRENCE-ID:20260310T090000Z\r\nSUMMARY:Lezione\r\nDTSTART:20260311T090000Z\r\nDTEND:20260311T100000Z\r\nEND:VEVENT\r\n" +
                   "BEGIN:VEVENT\r\nUID:series@google.com\r\nRECURRENCE-ID:20260317T090000Z\r\nSUMMARY:Lezione\r\nDTSTART:20260318T090000Z\r\nDTEND:20260318T100000Z\r\nEND:VEVENT\r\n" +
                   "END:VCALENDAR\r\n";
        var snap = SubscriptionContent.Process(Encoding.UTF8.GetBytes(text), "sub:x", SubscriptionRange.All);
        Assert.Equal(2, snap.Events.Count);
        Assert.Equal(2, snap.Report.OrphanOccurrences);
        Assert.Equal(2, snap.Events.Select(e => e.Id).Distinct().Count());
        Assert.All(snap.Events, e => Assert.Empty(e.Reminders)); // no reminders for a read-only calendar
    }

    [Fact] // T163
    public void TimesInOtherZonesAreShownInThePcZone()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Tokyo Standard Time");
        var text = "BEGIN:VCALENDAR\r\nVERSION:2.0\r\n" +
                   "BEGIN:VEVENT\r\nUID:a\r\nSUMMARY:Utc\r\nDTSTART:20260310T090000Z\r\nDTEND:20260310T100000Z\r\nEND:VEVENT\r\n" +
                   "BEGIN:VEVENT\r\nUID:b\r\nSUMMARY:Floating\r\nDTSTART:20260310T090000\r\nDTEND:20260310T100000\r\nEND:VEVENT\r\n" +
                   "BEGIN:VEVENT\r\nUID:c\r\nSUMMARY:Day\r\nDTSTART;VALUE=DATE:20260310\r\nDTEND;VALUE=DATE:20260311\r\nEND:VEVENT\r\n" +
                   "END:VCALENDAR\r\n";
        var events = SubscriptionContent.Process(Encoding.UTF8.GetBytes(text), "sub:x", SubscriptionRange.All, new GGCalendar.Core.Ics.IcsImportOptions { LocalZone = zone })
            .Events.ToDictionary(e => e.Title);
        Assert.Equal(new DateTime(2026, 3, 10, 18, 0, 0), events["Utc"].Start);
        Assert.Equal(new DateTime(2026, 3, 10, 9, 0, 0), events["Floating"].Start);
        Assert.True(events["Day"].IsAllDay);
        Assert.Equal(new DateTime(2026, 3, 10), events["Day"].Start);
    }
}
