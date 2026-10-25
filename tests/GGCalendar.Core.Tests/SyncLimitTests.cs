using System.Diagnostics;
using System.Net;
using GGCalendar.App.Services;
using GGCalendar.Core.Models;
using GGCalendar.Core.Tests.Support;
using GGCalendar.Data;
using Avalonia.VisualTree;
using GGCalendar.Providers.Google;

namespace GGCalendar.Core.Tests;

/// <summary>
/// T114-T119: many changes waiting for Google (an import of thousands of events). Google refuses a burst of writes with 403
/// rateLimitExceeded; that is "not now", not "no": nothing may be dropped, and nothing may be created twice.
/// </summary>
public class SyncLimitTests
{
    private static CalendarEvent NewEvent(string title) => SyncRig.NewEvent(Guid.NewGuid().ToString("N"), title);

    [Fact]
    [Trait("TestCase", "T114")]
    public async Task T114_WhenGoogleSlowsDownTheRestStaysInTheQueueAndIsSentLater()
    {
        using var rig = new SyncRig();
        rig.Service.PushPace = TimeSpan.Zero;
        var now = DateTimeOffset.UtcNow;
        rig.Service.Clock = () => now;
        for (var i = 0; i < 120; i++) rig.AddLocal(NewEvent("Evento " + i));
        rig.Google.LimitAfterWrites = 50;

        var first = await rig.SyncAsync();

        Assert.Empty(first.Errors);
        Assert.Equal(50, rig.Google.Ids.Count);
        Assert.Equal(70, first.Pending);
        Assert.NotNull(first.RetryAt);
        Assert.Equal(120, rig.Store.GetAllEvents().Count); // nothing is lost on the PC

        // Before the time Google asked for, nothing more is sent.
        var writes = rig.Google.Writes;
        await rig.SyncAsync();
        Assert.Equal(writes, rig.Google.Writes);

        now = first.RetryAt!.Value.AddSeconds(1);
        rig.Google.LimitAfterWrites = null;
        var last = await rig.SyncOkAsync();

        Assert.Equal(0, last.Pending);
        Assert.Null(last.RetryAt);
        Assert.Equal(120, rig.Google.Ids.Count);
        Assert.Equal(120, rig.Store.GetAllEvents().Count);
        Assert.Equal(120, rig.Store.GetAllEvents().Select(e => e.Title).Distinct().Count());
    }

    [Fact]
    [Trait("TestCase", "T115")]
    public async Task T115_AnEventWhoseCreationAnswerWasLostIsNotCreatedTwice()
    {
        using var rig = new SyncRig();
        var ev = rig.AddLocal(NewEvent("L'evento che si perde"));
        rig.Google.LoseNextInsertAnswer = true;

        var first = await rig.SyncAsync();

        Assert.NotEmpty(first.Errors);
        Assert.Single(rig.Google.Ids); // Google did create it

        await rig.SyncOkAsync();

        Assert.Equal([ev.Id], rig.Google.Ids); // the same event, the id chosen by the app
        Assert.Equal(ev.Id, rig.Store.GetRemoteInfo(ev.Id)?.RemoteId);
        Assert.Single(rig.Store.GetAllEvents());
    }

    [Theory]
    [Trait("TestCase", "T116")]
    [InlineData(429, "rateLimitExceeded", true)]
    [InlineData(403, "rateLimitExceeded", true)]
    [InlineData(403, "userRateLimitExceeded", true)]
    [InlineData(403, "quotaExceeded", true)]
    [InlineData(500, "backendError", true)]
    [InlineData(503, "backendError", true)]
    [InlineData(403, "forbiddenForNonOrganizer", false)]
    [InlineData(400, "invalid", false)]
    [InlineData(404, "notFound", false)]
    public async Task T116_OnlyTheRefusalsThatPassAreKeptForLater(int status, string reason, bool temporary)
    {
        using var rig = new SyncRig();
        rig.AddLocal(NewEvent("Un evento"));
        rig.Google.NextWriteFails = ((HttpStatusCode)status, reason);

        var result = await rig.SyncAsync();

        if (temporary)
        {
            Assert.Empty(result.Errors);
            Assert.Equal(1, result.Pending);
            Assert.NotNull(result.RetryAt);
        }
        else
        {
            Assert.Single(result.Errors); // a refusal for good: the user is told, and the queue goes on
            Assert.Equal(0, result.Pending);
        }
    }

    [Fact]
    [Trait("TestCase", "T117")]
    public async Task T117_ManyChangesAreSentAtASteadyPaceNotInABurst()
    {
        using var rig = new SyncRig();
        rig.Service.PushPace = TimeSpan.FromMilliseconds(30);
        rig.Service.BatchSize = 1; // one request each, to see the pace between requests
        for (var i = 0; i < 40; i++) rig.AddLocal(NewEvent("Evento " + i));

        var clock = Stopwatch.StartNew();
        await rig.SyncOkAsync();

        Assert.Equal(40, rig.Google.Ids.Count);
        Assert.True(clock.Elapsed > TimeSpan.FromMilliseconds(900), $"Gli invii sono stati troppo ravvicinati: {clock.ElapsedMilliseconds} ms.");
    }

    [Fact]
    [Trait("TestCase", "T118")]
    public async Task T118_TheSyncReportsHowManyChangesWereSentOfHowMany()
    {
        using var rig = new SyncRig();
        rig.Service.PushPace = TimeSpan.Zero;
        for (var i = 0; i < 120; i++) rig.AddLocal(NewEvent("Evento " + i));
        var seen = new List<(int Done, int Total)>();

        await rig.Service.SyncAllAsync(CancellationToken.None, (done, total) => seen.Add((done, total)));

        Assert.Equal([(0, 120), (50, 120), (100, 120)], seen); // reported before each request of 50
    }

    [Fact]
    [Trait("TestCase", "T122")]
    public async Task T122_ManyCreationsGoToGoogleFiftyAtATimeNotOneByOne()
    {
        using var rig = new SyncRig();
        rig.Service.PushPace = TimeSpan.Zero;
        for (var i = 0; i < 120; i++) rig.AddLocal(NewEvent("Evento " + i));

        var result = await rig.SyncOkAsync();

        Assert.Equal(3, rig.Google.Batches); // 50 + 50 + 20
        Assert.Equal(120, rig.Google.Ids.Count);
        Assert.Equal(120, result.Pushed);
        Assert.Equal(0, result.Pending);
        Assert.Equal(120, rig.Store.GetAllEvents().Count(e => rig.Store.GetRemoteInfo(e.Id)?.RemoteId is not null));
        Assert.Equal(120, rig.Store.GetAllEvents().Select(e => e.Id).Intersect(rig.Google.Ids).Count()); // Google holds them under the ids of the app
    }

    [Fact]
    [Trait("TestCase", "T123")]
    public async Task T123_InABatchOnlyTheCallsGoogleRefusedForNowStayInTheQueue()
    {
        using var rig = new SyncRig();
        rig.Service.PushPace = TimeSpan.Zero;
        for (var i = 0; i < 60; i++) rig.AddLocal(NewEvent("Evento " + i));
        rig.Google.LimitAfterWrites = 30; // the 31st call of the first batch and the following ones are refused

        var first = await rig.SyncAsync();

        Assert.Empty(first.Errors);
        Assert.Equal(30, rig.Google.Ids.Count);
        Assert.Equal(30, first.Pending);
        Assert.NotNull(first.RetryAt);
        Assert.Equal(60, rig.Store.GetAllEvents().Count);

        rig.Service.Clock = () => first.RetryAt!.Value.AddSeconds(1);
        rig.Google.LimitAfterWrites = null;
        await rig.SyncOkAsync();
        Assert.Equal(60, rig.Google.Ids.Count);
        Assert.Equal(60, rig.Store.GetAllEvents().Count);
    }

    [Fact]
    [Trait("TestCase", "T120")]
    public async Task T120_TheUserCanStopTheSendingAndTheRestIsSentLaterWithoutDuplicates()
    {
        using var rig = new SyncRig();
        rig.Service.PushPace = TimeSpan.Zero;
        rig.Service.BatchSize = 1;
        for (var i = 0; i < 40; i++) rig.AddLocal(NewEvent("Evento " + i));
        using var stop = new CancellationTokenSource();

        var first = await rig.Service.SyncAllAsync(stop.Token, (done, _) =>
        {
            if (done == 10) stop.Cancel(); // the user presses "Interrompi" after ten events
        });

        Assert.True(first.Cancelled);
        Assert.Empty(first.Errors); // stopping is not an error
        Assert.InRange(rig.Google.Ids.Count, 10, 11);
        Assert.Equal(40 - rig.Google.Ids.Count, first.Pending);
        Assert.Equal(40, rig.Store.GetAllEvents().Count);

        var last = await rig.SyncOkAsync();

        Assert.False(last.Cancelled);
        Assert.Equal(0, last.Pending);
        Assert.Equal(40, rig.Google.Ids.Count);
        Assert.Equal(40, rig.Store.GetAllEvents().Count);
    }

    [Fact]
    [Trait("TestCase", "T125")]
    public async Task T125_DiscardingTheQueueRemovesTheEventsNotSentYetAndKeepsTheRest()
    {
        using var rig = new SyncRig();
        var sent = rig.AddLocal(NewEvent("Già inviato"));
        await rig.SyncOkAsync();
        var edited = rig.Edit(sent, sent with { Title = "Già inviato, modificato" }); // an edit waiting for Google
        rig.Store.EnqueueDelete(Guid.NewGuid().ToString("N"), FakeGoogle.LocalCalendarId, "remote-x"); // a deletion waiting for Google
        for (var i = 0; i < 3; i++) rig.AddLocal(NewEvent("Da inviare " + i));
        var series = rig.AddLocal(NewEvent("Serie da inviare") with { Recurrence = "FREQ=DAILY;COUNT=3" });
        rig.AddLocal(NewEvent("Occorrenza della serie") with { SeriesMasterId = series.Id, OriginalStart = series.Start });
        Assert.Equal(5, rig.Store.CountUnsentCreations());

        var thrownAway = rig.Store.DiscardUnsentCreations();

        Assert.Equal(5, thrownAway);
        Assert.Equal([edited.Title], rig.Store.GetAllEvents().Select(e => e.Title)); // this PC and Google agree again
        Assert.Equal([OutboxOperation.Update, OutboxOperation.Delete], rig.Store.GetPending().Select(p => p.Operation).Order());
        Assert.Equal(0, rig.Store.CountUnsentCreations());
    }

    [Fact]
    [Trait("TestCase", "T126")]
    public async Task T126_StoppingAndDiscardingLeavesGoogleAndThisPcWithTheSameEvents()
    {
        using var rig = new SyncRig();
        rig.Service.PushPace = TimeSpan.Zero;
        rig.Service.BatchSize = 1;
        for (var i = 0; i < 40; i++) rig.AddLocal(NewEvent("Evento " + i));
        using var stop = new CancellationTokenSource();
        var first = await rig.Service.SyncAllAsync(stop.Token, (done, _) =>
        {
            if (done == 10) stop.Cancel();
        });
        Assert.True(first.Cancelled);

        var thrownAway = rig.Store.DiscardUnsentCreations();

        Assert.Equal(40 - rig.Google.Ids.Count, thrownAway);
        Assert.Equal(rig.Google.Ids.Count, rig.Store.GetAllEvents().Count);
        Assert.Empty(rig.Store.GetPending());
        var writes = rig.Google.Writes;
        await rig.SyncOkAsync();
        Assert.Equal(writes, rig.Google.Writes); // nothing more is sent
        Assert.Equal(rig.Google.Ids.Count, rig.Store.GetAllEvents().Count);
    }
}


/// <summary>T121, T127-T128: the window shown while many changes are sent: progress, estimate and the two buttons to stop.</summary>
[Collection(AppDriver.CollectionName)]
public class SyncProgressWindowTests
{
    private static List<string?> Texts(Avalonia.Controls.Window window) =>
        window.GetVisualDescendants().OfType<Avalonia.Controls.TextBlock>().Select(t => t.Text).ToList();

    private static Avalonia.Controls.Button ButtonOf(Avalonia.Controls.Window window, string text) =>
        window.GetVisualDescendants().OfType<Avalonia.Controls.Button>().Single(b => b.Content as string == text);

    private static void Click(Avalonia.Controls.Button button) => button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));

    [Fact]
    [Trait("TestCase", "T121")]
    public Task T121_TheProgressWindowShowsHowFarItIsAndHasAButtonToStop() => HeadlessApp.RunAsync(async () =>
    {
        var stopped = 0;
        var window = new GGCalendar.App.Views.SyncProgressWindow(() => stopped++, () => Task.FromResult(false));
        window.Show();
        await Task.Yield();
        window.Update(1250, 20000);

        Assert.Contains(Texts(window), t => t == "Invio a Google: 1250 di 20000 eventi.");
        Assert.Equal(6.25, window.GetVisualDescendants().OfType<Avalonia.Controls.ProgressBar>().Single().Value * 100);
        var button = ButtonOf(window, "Interrompi");

        Click(button);

        Assert.Equal(1, stopped);
        Assert.False(button.IsEnabled);
        Assert.True(window.StopRequested);
        window.Close();
    });

    [Fact]
    [Trait("TestCase", "T127")]
    public Task T127_StopAndDiscardAsksFirstAndOnlyThenStops() => HeadlessApp.RunAsync(async () =>
    {
        var stopped = 0;
        var asked = 0;
        var answer = false;
        var window = new GGCalendar.App.Views.SyncProgressWindow(() => stopped++, () =>
        {
            asked++;
            return Task.FromResult(answer);
        });
        window.Show();
        await Task.Yield();
        var discard = ButtonOf(window, "Interrompi e scarta la coda");

        Click(discard); // the user says no in the confirmation
        Assert.Equal((1, false), (asked, window.StopRequested));
        Assert.True(discard.IsEnabled);

        answer = true;
        Click(discard); // and now yes
        Assert.Equal(2, asked);
        Assert.True(window.StopRequested);
        Assert.False(discard.IsEnabled);
        Assert.False(ButtonOf(window, "Interruzione...").IsEnabled);
        Assert.Equal(0, stopped); // the stop itself is done by whoever confirmed (the caller cancels the sending)
        window.Close();
    });

    [Fact]
    [Trait("TestCase", "T128")]
    public Task T128_TheWindowEstimatesTheTimeNeededToSendEverything() => HeadlessApp.RunAsync(async () =>
    {
        var window = new GGCalendar.App.Views.SyncProgressWindow(() => { }, () => Task.FromResult(false));
        window.Show();
        await Task.Yield();

        window.Update(0, 20000); // before any measure: the usual speed (10 events a second)

        Assert.Contains(Texts(window), t => t == "Tempo stimato per inviare tutti gli eventi: circa 33 minuti");
        window.Close();

        var view = GGCalendar.App.Views.SyncProgressWindow.EstimateRemaining(1000, 19000, TimeSpan.FromSeconds(100)); // 10 a second measured
        Assert.Equal(TimeSpan.FromSeconds(1900), view);
        Assert.Equal(TimeSpan.FromSeconds(19000 / 10.0), GGCalendar.App.Views.SyncProgressWindow.EstimateRemaining(5, 19000, TimeSpan.FromSeconds(2))); // too little to measure
        Assert.Equal(TimeSpan.FromSeconds(19000 / 100.0), GGCalendar.App.Views.SyncProgressWindow.EstimateRemaining(10000, 19000, TimeSpan.FromSeconds(100))); // faster than the default

        Assert.Equal("meno di un minuto", GGCalendar.App.Views.SyncProgressWindow.FormatRemaining(TimeSpan.FromSeconds(30)));
        Assert.Equal("circa 5 minuti", GGCalendar.App.Views.SyncProgressWindow.FormatRemaining(TimeSpan.FromMinutes(5)));
        Assert.Equal("circa 1 h 30 min", GGCalendar.App.Views.SyncProgressWindow.FormatRemaining(TimeSpan.FromMinutes(90)));
        Assert.Equal("circa 2 h", GGCalendar.App.Views.SyncProgressWindow.FormatRemaining(TimeSpan.FromMinutes(120)));
    });
}
