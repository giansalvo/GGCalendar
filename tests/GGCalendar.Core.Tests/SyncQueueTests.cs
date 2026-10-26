using System.Text.Json;
using System.Text.Json.Serialization;
using GGCalendar.App.Services;
using GGCalendar.Core.Models;
using GGCalendar.Core.Tests.Support;
using GGCalendar.Data;

namespace GGCalendar.Core.Tests;

/// <summary>The queue of the changes waiting for Google (issue #19): what is in it, how it is counted and what cancelling does.</summary>
public class SyncQueueTests
{
    private static readonly JsonSerializerOptions Json = new() { Converters = { new JsonStringEnumConverter() } };
    private const string Cal = "me@example.com|me@example.com";
    private const string Other = "me@example.com|altro";

    private static CalendarEvent Ev(string id, string title, string calendar = Cal) =>
        new(id, calendar, title, new DateTime(2026, 11, 3, 9, 0, 0), new DateTime(2026, 11, 3, 10, 0, 0)) { Reminders = [] };

    private static string J(CalendarEvent e) => JsonSerializer.Serialize(e, Json);

    private static OutboxItem Row(long seq, OutboxOperation op, CalendarEvent ev, CalendarEvent? before = null, string? calendar = null, DateTime? at = null) =>
        new(seq, ev.Id, calendar ?? ev.CalendarId, op, op == OutboxOperation.Delete ? "remote-" + ev.Id : J(ev), before is null ? (op == OutboxOperation.Delete ? J(ev) : null) : J(before), at);

    // ---- the grouping (pure) ----

    [Fact]
    [Trait("TestCase", "T236")]
    public void T236_TheRowsAreGroupedByEventAndTheKindIsWhatThePersonDid()
    {
        var a = Ev("a", "Nuovo");
        var b = Ev("b", "Modificato");
        var c = Ev("c", "Eliminato");
        var moved = Ev("m", "Spostato", Other);
        var at = new DateTime(2026, 11, 3, 8, 0, 0, DateTimeKind.Utc);
        var rows = new[]
        {
            Row(1, OutboxOperation.Create, a, at: at),
            Row(2, OutboxOperation.Update, b with { Title = "Modificato 1" }, b, at: at.AddMinutes(1)),
            Row(3, OutboxOperation.Update, b with { Title = "Modificato 2" }, b with { Title = "Modificato 1" }, at: at.AddMinutes(2)),
            Row(4, OutboxOperation.Delete, c, at: at.AddMinutes(3)),
            Row(5, OutboxOperation.Delete, moved, calendar: Cal, at: at.AddMinutes(4)),
            Row(6, OutboxOperation.Create, moved, at: at.AddMinutes(4)),
            Row(7, OutboxOperation.Update, a, a, at: at.AddMinutes(5)), // an edit of an event that is not on Google yet: still a new event
        };

        var queue = PendingQueue.Group(rows);

        Assert.Equal(["a", "b", "c", "m"], queue.Select(q => q.EventId).ToArray());
        Assert.Equal([QueueKind.New, QueueKind.Edited, QueueKind.Deleted, QueueKind.Moved], queue.Select(q => q.Kind).ToArray());
        Assert.Equal(["Nuovo", "Modificato 2", "Eliminato", "Spostato"], queue.Select(q => q.Title).ToArray()); // the last version of the event, or the one kept when it was deleted
        Assert.Equal(2, queue[1].Edits);
        Assert.Equal(at, queue[0].Since);
        Assert.Equal((Other, Cal), (queue[3].CalendarId, queue[3].FromCalendarId)); // where it is now, where it was
    }

    [Fact]
    [Trait("TestCase", "T236")]
    public void T236_TheCalendarsOfThisPcHaveNoQueueAndOldDeletionsHaveNoTitle()
    {
        var local = Ev("l", "Locale", "local-1");
        var old = new OutboxItem(2, "d", Cal, OutboxOperation.Delete, "remote-d"); // made before the title was kept

        var queue = PendingQueue.Group([Row(1, OutboxOperation.Create, local), old]);

        var only = Assert.Single(queue);
        Assert.Equal(("d", QueueKind.Deleted, null), (only.EventId, only.Kind, only.Title));
    }

    // ---- the count ----

    [Fact]
    [Trait("TestCase", "T237")]
    public void T237_TheNumberCountsEventsNotRows()
    {
        using var rig = new SyncRig();
        var one = rig.AddLocal(Ev("a", "Uno", FakeGoogle.LocalCalendarId));
        var edited = one with { Title = "Uno bis" };
        rig.Edit(one, edited);
        rig.Edit(edited, edited with { Title = "Uno ter" });
        Assert.Equal(3, rig.Store.GetPending().Count);
        Assert.Equal(1, rig.Service.CountQueuedEvents());

        rig.AddLocal(Ev("b", "Due", FakeGoogle.LocalCalendarId));

        Assert.Equal(2, rig.Service.CountQueuedEvents());
    }

    [Fact]
    [Trait("TestCase", "T237")]
    public void T237_AMovedEventCountsOnceAndAnEmptyQueueIsZero()
    {
        using var rig = new SyncRig();
        Assert.Equal(0, rig.Service.CountQueuedEvents());
        var ev = Ev("m", "Spostato", FakeGoogle.LocalCalendarId);
        rig.Store.EnqueueDelete("m", FakeGoogle.LocalCalendarId, "remote-m", ev);
        rig.Store.Enqueue("m", Other, OutboxOperation.Create, ev with { CalendarId = Other });

        Assert.Equal(1, rig.Service.CountQueuedEvents());
    }

    [Fact]
    [Trait("TestCase", "T237")]
    public async Task T237_TheNumberGoesToZeroWhenEverythingIsSent()
    {
        using var rig = new SyncRig();
        rig.AddLocal(Ev("a", "Uno", FakeGoogle.LocalCalendarId));
        rig.AddLocal(Ev("b", "Due", FakeGoogle.LocalCalendarId));
        Assert.Equal(2, rig.Service.CountQueuedEvents());

        await rig.SyncOkAsync();

        Assert.Equal(0, rig.Service.CountQueuedEvents());
    }

    // ---- cancelling ----

    private static async Task<(SyncRig Rig, CalendarEvent Event)> SyncedEventAsync(string title = "Su Google")
    {
        var rig = new SyncRig();
        rig.Google.Seed(title, new DateTime(2026, 11, 10, 9, 0, 0, DateTimeKind.Utc));
        await rig.SyncOkAsync();
        return (rig, rig.Local(title)!);
    }

    [Fact]
    [Trait("TestCase", "T238")]
    public async Task T238_CancellingANewEventRemovesItFromThisPcAndNothingReachesGoogle()
    {
        using var rig = new SyncRig();
        await rig.SyncOkAsync();
        rig.AddLocal(Ev("n", "Nuovo", FakeGoogle.LocalCalendarId));
        rig.AddLocal(Ev("k", "Da tenere", FakeGoogle.LocalCalendarId));
        var writes = rig.Google.Writes;

        var result = await rig.Service.CancelQueuedAsync(["n"], CancellationToken.None);

        Assert.Equal(new GoogleSyncService.QueueCancelResult(1, false), result);
        Assert.Null(rig.Local("Nuovo"));
        Assert.NotNull(rig.Local("Da tenere"));
        Assert.Equal(["k"], rig.Service.GetQueue().Select(q => q.EventId).ToArray());

        await rig.SyncOkAsync();
        Assert.Null(rig.Google.FindBySummary("Nuovo")); // it never reached Google
        Assert.NotNull(rig.Google.FindBySummary("Da tenere")); // the other one was sent as usual
        Assert.Equal(writes + 1, rig.Google.Writes);
    }

    [Fact]
    [Trait("TestCase", "T238")]
    public async Task T238_CancellingANewSeriesTakesItsModifiedOccurrencesAndTheirChangesWithIt()
    {
        using var rig = new SyncRig();
        await rig.SyncOkAsync();
        var master = Ev("s", "Serie", FakeGoogle.LocalCalendarId) with { Recurrence = "FREQ=WEEKLY" };
        var exception = Ev("x", "Serie (eccezione)", FakeGoogle.LocalCalendarId) with { SeriesMasterId = "s", OriginalStart = master.Start.AddDays(7) };
        rig.AddLocal(master);
        rig.AddLocal(exception);

        await rig.Service.CancelQueuedAsync(["s"], CancellationToken.None);

        Assert.Empty(rig.Store.GetAllEvents().Where(e => e.Id is "s" or "x"));
        Assert.Empty(rig.Store.GetPending());
    }

    [Fact]
    [Trait("TestCase", "T239")]
    public async Task T239_CancellingEditsPutsTheEventBackAsGoogleHasItAndSendsNothing()
    {
        var (rig, original) = await SyncedEventAsync("Riunione");
        using var _ = rig;
        var v1 = rig.Edit(original, original with { Title = "Riunione bis" });
        rig.Edit(v1, v1 with { Title = "Riunione ter" });
        var writes = rig.Google.Writes;

        var result = await rig.Service.CancelQueuedAsync([original.Id], CancellationToken.None);

        Assert.Equal(new GoogleSyncService.QueueCancelResult(1, false), result);
        Assert.Equal("Riunione", rig.Store.GetEvent(original.Id)!.Title);
        Assert.Empty(rig.Store.GetPending());
        Assert.NotNull(rig.Store.GetRemoteInfo(original.Id)?.RemoteId); // still linked to its event on Google

        await rig.SyncOkAsync();
        Assert.Equal(writes, rig.Google.Writes);
        Assert.Equal("Riunione", rig.Local("Riunione")?.Title);
    }

    [Fact]
    [Trait("TestCase", "T239")]
    public async Task T239_WhenTheFirstEditWasSentTheEventGoesBackToWhatGoogleHasNotToTheOriginal()
    {
        var (rig, original) = await SyncedEventAsync("Riunione");
        using var _ = rig;
        var v1 = rig.Edit(original, original with { Title = "Riunione bis" });
        await rig.SyncOkAsync(); // Google has "Riunione bis"
        rig.Edit(v1, v1 with { Title = "Riunione ter" });

        await rig.Service.CancelQueuedAsync([original.Id], CancellationToken.None);

        Assert.Equal("Riunione bis", rig.Store.GetEvent(original.Id)!.Title);
        await rig.SyncOkAsync();
        Assert.Equal("Riunione bis", rig.Local("Riunione bis")?.Title);
        Assert.Equal(1, rig.Google.EventsIn(FakeGoogle.RemoteCalendar));
    }

    [Fact]
    [Trait("TestCase", "T239")]
    public async Task T239_ChangesQueuedByAnOlderVersionHaveNoOriginalSoGoogleSendsItBack()
    {
        var (rig, original) = await SyncedEventAsync("Riunione");
        using var _ = rig;
        rig.Store.UpsertEvent(original with { Title = "Locale" });
        rig.Store.Enqueue(original.Id, original.CalendarId, OutboxOperation.Update, original with { Title = "Locale" }); // no base: made before the base was kept

        var result = await rig.Service.CancelQueuedAsync([original.Id], CancellationToken.None);

        Assert.True(result.RestoreFromGoogle);
        Assert.Empty(rig.Store.GetPending());
        await rig.SyncOkAsync();
        Assert.Equal("Riunione", rig.Store.GetEvent(original.Id)?.Title ?? rig.Local("Riunione")?.Title);
    }

    [Fact]
    [Trait("TestCase", "T240")]
    public async Task T240_CancellingADeletionBringsTheEventBackFromGoogle()
    {
        var (rig, original) = await SyncedEventAsync("Pranzo");
        using var _ = rig;
        var remote = rig.Store.GetRemoteInfo(original.Id)!.Value.RemoteId!;
        rig.Store.DeleteEvent(original.Id);
        rig.Store.EnqueueDelete(original.Id, original.CalendarId, remote, original);
        Assert.Null(rig.Local("Pranzo"));

        var result = await rig.Service.CancelQueuedAsync([original.Id], CancellationToken.None);

        Assert.Equal(new GoogleSyncService.QueueCancelResult(1, true), result);
        Assert.Empty(rig.Store.GetPending());
        await rig.SyncOkAsync();
        Assert.NotNull(rig.Local("Pranzo"));
        Assert.DoesNotContain(rig.Google.Requests, r => r.StartsWith("DELETE", StringComparison.Ordinal)); // Google never heard of the deletion
        Assert.Equal(1, rig.Google.EventsIn(FakeGoogle.RemoteCalendar));
    }

    [Fact]
    [Trait("TestCase", "T241")]
    public async Task T241_CancellingAMoveRemovesTheCopyAtTheDestinationAndBringsTheOriginalBack()
    {
        var (rig, original) = await SyncedEventAsync("Dentista");
        using var _ = rig;
        var otherRemote = rig.Google.AddCalendar("Personale");
        await rig.SyncOkAsync();
        var otherLocal = FakeGoogle.Account + "|" + otherRemote;
        var remote = rig.Store.GetRemoteInfo(original.Id)!.Value.RemoteId!;
        var moved = original with { CalendarId = otherLocal };
        rig.Store.UpsertEvent(moved);
        rig.Store.EnqueueDelete(original.Id, original.CalendarId, remote, original);
        rig.Store.ClearRemote(original.Id);
        rig.Store.Enqueue(original.Id, otherLocal, OutboxOperation.Create, moved);
        Assert.Equal(QueueKind.Moved, Assert.Single(rig.Service.GetQueue()).Kind);

        var result = await rig.Service.CancelQueuedAsync([original.Id], CancellationToken.None);

        Assert.Equal(new GoogleSyncService.QueueCancelResult(1, true), result);
        Assert.Empty(rig.Store.GetPending());
        Assert.DoesNotContain(rig.Store.GetAllEvents(), e => e.CalendarId == otherLocal);
        await rig.SyncOkAsync();
        Assert.Contains(rig.Store.GetAllEvents(), e => e.Title == "Dentista" && e.CalendarId == FakeGoogle.LocalCalendarId);
        Assert.Equal(1, rig.Google.EventsIn(FakeGoogle.RemoteCalendar));
        Assert.Equal(0, rig.Google.EventsIn(otherRemote)); // the destination never got it
    }

    [Fact]
    [Trait("TestCase", "T242")]
    public async Task T242_OnlyTheEventsStillWaitingAreCancelledAndTheOthersKeepTheirPlaceInTheQueue()
    {
        using var rig = new SyncRig();
        await rig.SyncOkAsync();
        rig.AddLocal(Ev("a", "Uno", FakeGoogle.LocalCalendarId));
        rig.AddLocal(Ev("b", "Due", FakeGoogle.LocalCalendarId));
        await rig.SyncOkAsync(); // both were sent meanwhile

        var result = await rig.Service.CancelQueuedAsync(["a", "b", "mai-esistito"], CancellationToken.None);

        Assert.Equal(0, result.Cancelled);
        Assert.NotNull(rig.Local("Uno")); // an event that was already sent is not touched
        Assert.NotNull(rig.Local("Due"));
    }

    [Fact]
    [Trait("TestCase", "T242")]
    public async Task T242_CancellingOneEventLeavesTheOtherInTheQueue()
    {
        using var rig = new SyncRig();
        await rig.SyncOkAsync();
        rig.AddLocal(Ev("a", "Uno", FakeGoogle.LocalCalendarId));
        rig.AddLocal(Ev("b", "Due", FakeGoogle.LocalCalendarId));

        await rig.Service.CancelQueuedAsync(["a"], CancellationToken.None);

        Assert.Equal(["b"], rig.Service.GetQueue().Select(q => q.EventId).ToArray());
        Assert.Equal(1, rig.Service.CountQueuedEvents());
    }

    [Fact]
    [Trait("TestCase", "T243")]
    public async Task T243_CancellingWaitsForASyncThatIsRunningAndCanBeStopped()
    {
        using var rig = new SyncRig();
        await rig.SyncOkAsync();
        rig.AddLocal(Ev("a", "Uno", FakeGoogle.LocalCalendarId));
        await rig.Service.Gate.WaitAsync(); // a sync holds the gate

        var cancelling = rig.Service.CancelQueuedAsync(["a"], CancellationToken.None);
        await Task.Delay(300);
        Assert.False(cancelling.IsCompleted);
        Assert.Equal(1, rig.Service.CountQueuedEvents()); // nothing was done yet

        rig.Service.Gate.Release();
        Assert.Equal(1, (await cancelling).Cancelled);
        Assert.Equal(0, rig.Service.CountQueuedEvents());

        await rig.Service.Gate.WaitAsync();
        using var stop = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => rig.Service.CancelQueuedAsync(["a"], stop.Token));
        rig.Service.Gate.Release();
        Assert.Equal(1, rig.Service.Gate.CurrentCount);
    }

    [Fact]
    [Trait("TestCase", "T244")]
    public async Task T244_AnEventWithOtherPendingChangesInTheSameCalendarIsNotOverwrittenByTheDownloadThatBringsBackAnotherOne()
    {
        var (rig, original) = await SyncedEventAsync("Pranzo");
        using var _ = rig;
        rig.Google.Seed("Altro", new DateTime(2026, 11, 11, 9, 0, 0, DateTimeKind.Utc));
        await rig.SyncOkAsync();
        var other = rig.Local("Altro")!;
        var remote = rig.Store.GetRemoteInfo(original.Id)!.Value.RemoteId!;
        rig.Store.DeleteEvent(original.Id);
        rig.Store.EnqueueDelete(original.Id, original.CalendarId, remote, original);
        rig.Edit(other, other with { Title = "Altro modificato" });

        await rig.Service.CancelQueuedAsync([original.Id], CancellationToken.None); // the calendar is downloaded again at the next sync
        await rig.SyncOkAsync();

        Assert.NotNull(rig.Local("Pranzo")); // back
        Assert.NotNull(rig.Local("Altro modificato")); // the edit that was waiting went to Google and was not overwritten
        Assert.Equal("Altro modificato", rig.Google.Summary(rig.Store.GetRemoteInfo(other.Id)!.Value.RemoteId!));
    }

    [Fact]
    [Trait("TestCase", "T251")]
    public async Task T251_CancellingThousandsOfEventsTakesSecondsNotMinutes()
    {
        using var rig = new SyncRig();
        await rig.SyncOkAsync();
        var events = Enumerable.Range(0, 3000).Select(i => Ev("i" + i, "Importato " + i, FakeGoogle.LocalCalendarId)).ToList();
        rig.Store.ApplyImport([], events, remoteDestination: true); // an import of a file: one transaction
        Assert.Equal(3000, rig.Service.CountQueuedEvents());
        var watch = System.Diagnostics.Stopwatch.StartNew();

        var result = await rig.Service.CancelQueuedAsync(events.Select(e => e.Id).ToList(), CancellationToken.None);

        Assert.Equal(3000, result.Cancelled);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(15), $"Ci sono voluti {watch.Elapsed.TotalSeconds:0} secondi.");
        Assert.Equal(0, rig.Service.CountQueuedEvents());
        Assert.Empty(rig.Store.GetAllEvents().Where(e => e.Id.StartsWith("i", StringComparison.Ordinal)));
    }
}
