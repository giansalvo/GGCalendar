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
}

/// <summary>T121: the window shown while many changes are sent has a button to stop.</summary>
[Collection(AppDriver.CollectionName)]
public class SyncProgressWindowTests
{
    [Fact]
    [Trait("TestCase", "T121")]
    public Task T121_TheProgressWindowShowsHowFarItIsAndHasAButtonToStop() => HeadlessApp.RunAsync(async () =>
    {
        var stopped = 0;
        var window = new GGCalendar.App.Views.SyncProgressWindow(() => stopped++);
        window.Show();
        await Task.Yield();
        window.Update(1250, 20000);

        var texts = window.GetVisualDescendants().OfType<Avalonia.Controls.TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains(texts, t => t == "Invio a Google: 1250 di 20000 eventi.");
        var button = window.GetVisualDescendants().OfType<Avalonia.Controls.Button>().Single(b => (b.Content as string) == "Interrompi");
        Assert.Equal(6.25, window.GetVisualDescendants().OfType<Avalonia.Controls.ProgressBar>().Single().Value * 100);

        button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));

        Assert.Equal(1, stopped);
        Assert.False(button.IsEnabled);
        Assert.True(window.StopRequested);
        window.Close();
    });
}
