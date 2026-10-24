using GGCalendar.Core.Models;
using GGCalendar.Core.Tests.Support;
using GGCalendar.Data;

namespace GGCalendar.Core.Tests;

/// <summary>The real GoogleSyncService against an in-memory Google (see <see cref="FakeGoogle"/>).</summary>
public class SyncRegressionTests
{
    private static readonly DateTime Utc = new(2026, 10, 14, 7, 0, 0, DateTimeKind.Utc);

    [Fact]
    [Trait("TestCase", "T01")]
    public async Task T01_ThePullDoesNotResetTheColorOfAnEvent()
    {
        // The reported bug: the color is right after saving, then back to the calendar color after a few syncs.
        using var rig = new SyncRig();
        rig.AddLocal(SyncRig.NewEvent("e1", "Colorato", "#D50000"));

        await rig.SyncOkAsync(); // push
        Assert.Equal("#D50000", rig.Local("Colorato")!.ColorHex, ignoreCase: true);

        await rig.SyncOkAsync(); // first pull of the event
        Assert.Equal("#D50000", rig.Local("Colorato")!.ColorHex, ignoreCase: true);

        rig.Google.Touch(Assert.Single(rig.Google.Ids));
        await rig.SyncOkAsync(); // the event comes back as changed
        Assert.Equal("#D50000", rig.Local("Colorato")!.ColorHex, ignoreCase: true);
    }

    [Fact]
    [Trait("TestCase", "T11")]
    public async Task T11_AnEditMadeDuringTheSyncIsNotOverwrittenByThePull()
    {
        using var rig = new SyncRig();
        var original = rig.AddLocal(SyncRig.NewEvent("e1", "Originale"));
        await rig.SyncOkAsync();
        var remoteId = Assert.Single(rig.Google.Ids);

        // The user edits the event, then edits it again while the first edit is being sent.
        var first = rig.Edit(original, original with { Title = "Prima modifica" });
        rig.Google.OnPatch = _ =>
        {
            rig.Google.OnPatch = null;
            rig.Edit(first, first with { Title = "Seconda modifica" });
        };
        await rig.SyncOkAsync();

        Assert.Equal("Seconda modifica", rig.Store.GetEvent("e1")!.Title); // the pull skipped it
        Assert.True(rig.Store.HasPending("e1"));

        await rig.SyncOkAsync(); // the queued edit is sent
        Assert.False(rig.Store.HasPending("e1"));
        Assert.Equal("Seconda modifica", rig.Google.Summary(remoteId));
        Assert.Equal("Seconda modifica", rig.Store.GetEvent("e1")!.Title);
    }

    [Fact]
    [Trait("TestCase", "T12")]
    public async Task T12_AnExpiredSyncTokenTriggersAFullReloadWithoutLosingEvents()
    {
        using var rig = new SyncRig();
        rig.Google.Seed("Uno", Utc);
        rig.Google.Seed("Due", Utc.AddDays(1));
        await rig.SyncOkAsync();
        var token = rig.Store.GetSyncToken(FakeGoogle.LocalCalendarId);
        Assert.NotNull(token);
        rig.Google.Seed("Tre", Utc.AddDays(2));
        rig.Google.ExpireSyncTokens = true;
        rig.Google.Requests.Clear();

        await rig.SyncOkAsync();

        Assert.Equal(["Due", "Tre", "Uno"], rig.Store.GetAllEvents().Select(e => e.Title).Order().ToArray());
        Assert.NotNull(rig.Store.GetSyncToken(FakeGoogle.LocalCalendarId));
        Assert.Contains(rig.Google.Requests, r => r.Contains("syncToken="));
        Assert.Contains(rig.Google.Requests, r => r.Contains("/events?") && !r.Contains("syncToken="));
    }

    [Fact]
    [Trait("TestCase", "T11")]
    public async Task T11_AVersionChangeMadeByGoogleAloneIsNotAConflict()
    {
        using var rig = new SyncRig();
        var remoteId = rig.Google.Seed("Riunione", Utc);
        await rig.SyncOkAsync();
        var local = rig.Local("Riunione")!;

        rig.Edit(local, local with { Title = "Riunione spostata" });
        rig.Google.Touch(remoteId); // same content, new etag: the local etag is stale and the patch gets a 412

        var result = await rig.SyncOkAsync();

        Assert.Empty(result.Conflicts);
        Assert.Equal("Riunione spostata", rig.Google.Summary(remoteId));
        Assert.Empty(rig.Store.GetPending());
        Assert.Single(rig.Store.GetAllEvents());
    }

    [Fact]
    [Trait("TestCase", "T11")]
    public async Task T11_AnEventChangedOnBothSidesKeepsGoogleAndSavesTheLocalCopy()
    {
        using var rig = new SyncRig();
        var remoteId = rig.Google.Seed("Riunione", Utc);
        await rig.SyncOkAsync();
        var local = rig.Local("Riunione")!;

        rig.Edit(local, local with { Title = "Titolo locale" });
        rig.Google.SetSummary(remoteId, "Titolo Google");

        var result = await rig.SyncOkAsync();

        Assert.Single(result.Conflicts);
        Assert.Equal("Titolo Google", rig.Store.GetEvent(local.Id)!.Title);
        var copy = rig.Local("Titolo locale (conflitto)");
        Assert.NotNull(copy);
        Assert.NotEqual(local.Id, copy.Id);
        Assert.Equal("Titolo Google", rig.Google.Summary(remoteId));
        Assert.NotNull(rig.Google.FindBySummary("Titolo locale (conflitto)")); // the copy was sent in the same sync
        Assert.Empty(rig.Store.GetPending());
    }

    [Fact]
    [Trait("TestCase", "T13")]
    public async Task T13_AnEventDeletedOnGoogleIsRemovedByTheNextSync()
    {
        using var rig = new SyncRig();
        var gone = rig.Google.Seed("Da cancellare", Utc);
        rig.Google.Seed("Resta", Utc.AddDays(1));
        await rig.SyncOkAsync();
        Assert.Equal(2, rig.Store.GetAllEvents().Count);

        rig.Google.Remove(gone);
        await rig.SyncOkAsync(); // incremental: Google lists it as cancelled

        Assert.Equal("Resta", Assert.Single(rig.Store.GetAllEvents()).Title);
    }

    [Fact]
    [Trait("TestCase", "T13")]
    public async Task T13_AnEventMissingAfterAFullSyncIsRemoved()
    {
        using var rig = new SyncRig();
        var gone = rig.Google.Seed("Da cancellare", Utc);
        rig.Google.Seed("Resta", Utc.AddDays(1));
        await rig.SyncOkAsync();

        rig.Google.Remove(gone);
        rig.Store.ClearSyncTokens(); // forces a full sync, which does not list what was deleted
        await rig.SyncOkAsync();

        Assert.Equal("Resta", Assert.Single(rig.Store.GetAllEvents()).Title);
    }
}
