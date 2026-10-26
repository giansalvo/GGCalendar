using System.Net;
using System.Text.Json.Nodes;
using GGCalendar.App.Services;
using GGCalendar.Core.Localization;
using GGCalendar.Core.Models;
using GGCalendar.Core.Tests.Support;
using GGCalendar.Data;
using Microsoft.Data.Sqlite;

namespace GGCalendar.Core.Tests;

/// <summary>Creating and deleting calendars on Google (issue #16): online only, one request each, the queue and the local data kept right. On the fake Google.</summary>
public class GoogleCalendarTests
{
    private static string Local(string remote) => FakeGoogle.Account + "|" + remote;

    private static async Task<(SyncRig Rig, string Remote, string LocalId)> RigWithOwnedCalendarAsync(string name = "Lavoro")
    {
        var rig = new SyncRig();
        var remote = rig.Google.AddCalendar(name);
        await rig.SyncOkAsync();
        return (rig, remote, Local(remote));
    }

    // ---- the role is kept ----

    [Fact] // T195
    public async Task SyncKeepsWhoOwnsEachCalendar()
    {
        using var rig = new SyncRig();
        var mine = rig.Google.AddCalendar("Mio");
        var holidays = rig.Google.AddCalendar("Festività", role: "reader");
        var shared = rig.Google.AddCalendar("Condiviso", role: "writer");
        await rig.SyncOkAsync();

        var calendars = rig.Store.GetCalendars().ToDictionary(c => c.Id);
        Assert.True(calendars[FakeGoogle.LocalCalendarId].IsOwner);
        Assert.True(calendars[Local(mine)].IsOwner);
        Assert.False(calendars[Local(holidays)].IsOwner);
        Assert.False(calendars[Local(shared)].IsOwner); // a writer is not the owner
    }

    [Fact] // T195: a database of the version before the role has none until the next sync
    public async Task OldDatabaseHasUnknownRoleUntilTheNextSync()
    {
        var path = Path.Combine(Path.GetTempPath(), "ggcal-role-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var first = new CalendarStore(path);
            first.UpsertCalendar(new CalendarInfo(FakeGoogle.LocalCalendarId, FakeGoogle.Account, "Principale", "#1A73E8", IsOwner: true));
            using (var c = new SqliteConnection($"Data Source={path};Pooling=False"))
            {
                c.Open();
                using var cmd = c.CreateCommand();
                cmd.CommandText = "ALTER TABLE calendars DROP COLUMN is_owner; PRAGMA user_version = 5;"; // back to the layout of the version 5
                cmd.ExecuteNonQuery();
            }

            var reopened = new CalendarStore(path);
            Assert.Null(Assert.Single(reopened.GetCalendars()).IsOwner); // the migration adds the column, empty

            // A save that does not know the role does not erase one that is known.
            reopened.UpsertCalendar(new CalendarInfo(FakeGoogle.LocalCalendarId, FakeGoogle.Account, "Principale", "#1A73E8", IsOwner: false));
            reopened.UpsertCalendar(new CalendarInfo(FakeGoogle.LocalCalendarId, FakeGoogle.Account, "Principale 2", "#1A73E8"));
            var back = Assert.Single(reopened.GetCalendars());
            Assert.Equal((false, "Principale 2"), (back.IsOwner, back.Name));
        }
        finally
        {
            foreach (var f in Directory.GetFiles(Path.GetTempPath(), Path.GetFileName(path) + "*")) File.Delete(f);
        }

        await Task.CompletedTask;
    }

    // ---- creating ----

    [Fact] // T182
    public async Task CreatedCalendarIsOnGoogleAndHereWithNameColourAndRole()
    {
        using var rig = new SyncRig();
        await rig.SyncOkAsync();
        var info = await rig.Service.CreateCalendarAsync(FakeGoogle.Account, "  Palestra  ", "#8E24AA", CancellationToken.None);

        Assert.Equal("Palestra", info.Name);
        Assert.Equal("#8E24AA", info.ColorHex);
        Assert.True(info.IsOwner);
        Assert.StartsWith(FakeGoogle.Account + "|cal", info.Id);

        var remote = info.Id[(info.Id.IndexOf('|') + 1)..];
        Assert.True(rig.Google.HasCalendar(remote));
        Assert.Equal("Palestra", rig.Google.CalendarSummary(remote));
        Assert.Equal("#8E24AA", rig.Google.CalendarColor(remote));

        var body = JsonNode.Parse(Assert.Single(rig.Google.CreatedBodies))!;
        Assert.Equal("Palestra", (string?)body["summary"]);
        Assert.False(string.IsNullOrEmpty((string?)body["timeZone"])); // the zone of the PC, as Google names it
        var (_, query, patch) = Assert.Single(rig.Google.ColorPatches);
        Assert.Contains("colorRgbFormat=true", query);
        Assert.Equal("#8E24AA", (string?)JsonNode.Parse(patch)!["backgroundColor"]);
        Assert.NotNull((string?)JsonNode.Parse(patch)!["foregroundColor"]);

        var stored = rig.Store.GetCalendars().Single(c => c.Id == info.Id);
        Assert.Equal(("Palestra", "#8E24AA", true), (stored.Name, stored.ColorHex, stored.IsOwner));
        Assert.True(stored.IsVisible);
    }

    [Fact] // T182: the next sync finds it already and does not add it twice
    public async Task NextSyncDoesNotDuplicateTheCreatedCalendar()
    {
        using var rig = new SyncRig();
        await rig.SyncOkAsync();
        var info = await rig.Service.CreateCalendarAsync(FakeGoogle.Account, "Palestra", "#8E24AA", CancellationToken.None);
        await rig.SyncOkAsync();
        Assert.Single(rig.Store.GetCalendars(), c => c.Id == info.Id);
        Assert.Equal(2, rig.Store.GetCalendars().Count); // the primary one and the new one
        Assert.Equal("#8E24AA", rig.Store.GetCalendars().Single(c => c.Id == info.Id).ColorHex);
    }

    [Fact] // T182: an event can be made in it and goes to Google
    public async Task AnEventInTheCreatedCalendarGoesToGoogle()
    {
        using var rig = new SyncRig();
        await rig.SyncOkAsync();
        var info = await rig.Service.CreateCalendarAsync(FakeGoogle.Account, "Palestra", "#8E24AA", CancellationToken.None);
        var remote = info.Id[(info.Id.IndexOf('|') + 1)..];
        rig.AddLocal(new CalendarEvent("e1", info.Id, "Allenamento", new DateTime(2026, 10, 14, 9, 0, 0), new DateTime(2026, 10, 14, 10, 0, 0)) { Reminders = [] });
        await rig.SyncOkAsync();
        Assert.Equal(1, rig.Google.EventsIn(remote));
    }

    [Fact] // T183
    public async Task EmptyNameIsRefusedWithoutAskingGoogle()
    {
        using var rig = new SyncRig();
        rig.Google.Requests.Clear();
        foreach (var name in new[] { "", "   " })
        {
            var ex = await Assert.ThrowsAsync<GoogleCalendarException>(() => rig.Service.CreateCalendarAsync(FakeGoogle.Account, name, "#1A73E8", CancellationToken.None));
            Assert.Equal(GoogleCalendarProblem.EmptyName, ex.Problem);
        }

        Assert.Empty(rig.Google.Requests);
    }

    [Fact] // T183
    public async Task NameWithSpecialCharactersAndTooLongNameAreSent()
    {
        using var rig = new SyncRig();
        var special = "Città & «prova» \"1\" 😀";
        var info = await rig.Service.CreateCalendarAsync(FakeGoogle.Account, special, "#1A73E8", CancellationToken.None);
        Assert.Equal(special, rig.Google.CalendarSummary(info.Id[(info.Id.IndexOf('|') + 1)..]));

        var longName = new string('x', 400);
        var cut = await rig.Service.CreateCalendarAsync(FakeGoogle.Account, longName, "#1A73E8", CancellationToken.None);
        Assert.Equal(GoogleSyncService.MaxCalendarName, cut.Name.Length);
    }

    [Fact] // T183: the text colour follows the background
    public async Task TextColourIsReadableOnTheBackground()
    {
        using var rig = new SyncRig();
        await rig.Service.CreateCalendarAsync(FakeGoogle.Account, "Scuro", "#1A237E", CancellationToken.None);
        await rig.Service.CreateCalendarAsync(FakeGoogle.Account, "Chiaro", "#F6BF26", CancellationToken.None);
        Assert.Equal(["#FFFFFF", "#000000"], rig.Google.ColorPatches.Select(p => (string?)JsonNode.Parse(p.Body)!["foregroundColor"]).ToArray());
    }

    [Fact] // T184
    public async Task NoNetworkCreatesNothing()
    {
        using var rig = new SyncRig();
        rig.Google.Offline = true;
        var ex = await Assert.ThrowsAsync<GoogleCalendarException>(() => rig.Service.CreateCalendarAsync(FakeGoogle.Account, "Palestra", "#1A73E8", CancellationToken.None));
        Assert.Equal(GoogleCalendarProblem.NoNetwork, ex.Problem);
        Assert.Single(rig.Store.GetCalendars()); // only the primary one
        Assert.Empty(rig.Google.CreatedBodies);
    }

    [Theory] // T184
    [InlineData(403, "rateLimitExceeded", GoogleCalendarProblem.RateLimited)]
    [InlineData(403, "quotaExceeded", GoogleCalendarProblem.RateLimited)]
    [InlineData(429, "rateLimitExceeded", GoogleCalendarProblem.RateLimited)]
    [InlineData(403, "forbidden", GoogleCalendarProblem.NotAllowed)]
    [InlineData(401, "authError", GoogleCalendarProblem.AuthExpired)]
    [InlineData(500, "backendError", GoogleCalendarProblem.Server)]
    [InlineData(400, "invalid", GoogleCalendarProblem.Other)]
    public async Task GoogleRefusalsHaveTheirOwnProblemAndLeaveNothingHere(int status, string reason, GoogleCalendarProblem expected)
    {
        using var rig = new SyncRig();
        rig.Google.NextWriteFails = ((HttpStatusCode)status, reason);
        var ex = await Assert.ThrowsAsync<GoogleCalendarException>(() => rig.Service.CreateCalendarAsync(FakeGoogle.Account, "Palestra", "#1A73E8", CancellationToken.None));
        Assert.Equal(expected, ex.Problem);
        Assert.Single(rig.Store.GetCalendars());
        Assert.Equal(1, rig.Google.CalendarIds.Count); // nothing on Google either
    }

    [Fact] // T184: the calendar is made but its colour is refused: it is kept
    public async Task AColourThatIsRefusedDoesNotLoseTheCalendar()
    {
        using var rig = new SyncRig();
        rig.Google.Offline = false;
        // The first write (the creation) goes; the second (the colour) is refused.
        var created = rig.Service.CreateCalendarAsync(FakeGoogle.Account, "Palestra", "#8E24AA", CancellationToken.None);
        var info = await created;
        Assert.True(rig.Google.HasCalendar(info.Id[(info.Id.IndexOf('|') + 1)..]));

        rig.Google.LimitAfterWrites = rig.Google.Writes + 1; // the next creation goes, its colour is over the limit
        var second = await rig.Service.CreateCalendarAsync(FakeGoogle.Account, "Altro", "#0B8043", CancellationToken.None);
        Assert.Contains(rig.Store.GetCalendars(), c => c.Id == second.Id);
        Assert.Equal("#0B8043", rig.Store.GetCalendars().Single(c => c.Id == second.Id).ColorHex); // the colour asked for, until the next sync
    }

    [Fact] // T184
    public async Task UnknownAccountIsRefused()
    {
        using var rig = new SyncRig();
        var ex = await Assert.ThrowsAsync<GoogleCalendarException>(() => rig.Service.CreateCalendarAsync("nobody@example.com", "X", "#1A73E8", CancellationToken.None));
        Assert.Equal(GoogleCalendarProblem.NoAccount, ex.Problem);
    }

    // ---- deleting ----

    [Fact] // T185, T190
    public async Task DeletingACalendarIsOneRequestWhateverTheNumberOfEvents()
    {
        var (rig, remote, localId) = await RigWithOwnedCalendarAsync();
        using var _ = rig;
        for (var i = 0; i < 300; i++) rig.Google.SeedIn(remote, "Evento " + i, new DateTime(2026, 11, 1).AddHours(i));
        await rig.SyncOkAsync();
        Assert.Equal(300, rig.Store.GetAllEvents().Count(e => e.CalendarId == localId));

        rig.Google.Requests.Clear();
        await rig.Service.DeleteCalendarAsync(localId, CancellationToken.None);

        var request = Assert.Single(rig.Google.Requests, r => !r.StartsWith("POST /token")); // (the access token is asked for apart)
        Assert.StartsWith("DELETE /calendar/v3/calendars/cal", request);
        Assert.Equal(0, rig.Google.Batches);
        Assert.False(rig.Google.HasCalendar(remote));
        Assert.DoesNotContain(rig.Store.GetCalendars(), c => c.Id == localId);
        Assert.DoesNotContain(rig.Store.GetAllEvents(), e => e.CalendarId == localId);
        Assert.Null(rig.Store.GetSyncToken(localId));
    }

    [Fact] // T185: the other calendars are not touched
    public async Task OtherCalendarsKeepTheirEventsAndQueue()
    {
        var (rig, remote, localId) = await RigWithOwnedCalendarAsync();
        using var _ = rig;
        var other = rig.Google.AddCalendar("Altro");
        rig.Google.SeedIn(other, "Resta", new DateTime(2026, 11, 2));
        rig.Google.Seed("Principale resta", new DateTime(2026, 11, 3));
        await rig.SyncOkAsync();
        rig.AddLocal(new CalendarEvent("keep", FakeGoogle.LocalCalendarId, "Da inviare", new DateTime(2026, 11, 4, 9, 0, 0), new DateTime(2026, 11, 4, 10, 0, 0)) { Reminders = [] });

        await rig.Service.DeleteCalendarAsync(localId, CancellationToken.None);

        Assert.Contains(rig.Store.GetCalendars(), c => c.Id == Local(other));
        Assert.Contains(rig.Store.GetAllEvents(), e => e.Title == "Resta");
        Assert.Contains(rig.Store.GetAllEvents(), e => e.Title == "Principale resta");
        Assert.Contains(rig.Store.GetPending(), p => p.EventId == "keep"); // the queue of the others is intact
    }

    [Fact] // T186: the changes waiting for the deleted calendar go with it
    public async Task WaitingChangesOfTheCalendarAreDiscarded()
    {
        var (rig, remote, localId) = await RigWithOwnedCalendarAsync();
        using var _ = rig;
        rig.Google.SeedIn(remote, "Esistente", new DateTime(2026, 11, 1));
        await rig.SyncOkAsync();
        var existing = rig.Store.GetAllEvents().Single(e => e.Title == "Esistente");
        rig.AddLocal(new CalendarEvent("new", localId, "Nuovo", new DateTime(2026, 11, 5, 9, 0, 0), new DateTime(2026, 11, 5, 10, 0, 0)) { Reminders = [] });
        rig.Edit(existing, existing with { Title = "Cambiato" });
        Assert.Equal(2, rig.Store.GetPending().Count(p => p.CalendarId == localId));

        await rig.Service.DeleteCalendarAsync(localId, CancellationToken.None);

        Assert.DoesNotContain(rig.Store.GetPending(), p => p.CalendarId == localId);
        var after = await rig.SyncAsync(); // nothing is sent to a calendar that is gone
        Assert.Empty(after.Errors);
        Assert.Equal(0, after.Pending);
    }

    [Fact] // T187
    public async Task ThePrimaryCalendarIsRefusedWithoutAskingGoogle()
    {
        using var rig = new SyncRig();
        await rig.SyncOkAsync();
        rig.Google.Requests.Clear();
        var ex = await Assert.ThrowsAsync<GoogleCalendarException>(() => rig.Service.DeleteCalendarAsync(FakeGoogle.LocalCalendarId, CancellationToken.None));
        Assert.Equal(GoogleCalendarProblem.Primary, ex.Problem);
        Assert.Empty(rig.Google.Requests);
        Assert.True(GoogleSyncService.IsPrimaryCalendar(FakeGoogle.LocalCalendarId));
        Assert.False(GoogleSyncService.IsPrimaryCalendar(Local("cal1@group.calendar.google.com")));
        Assert.Contains(rig.Store.GetCalendars(), c => c.Id == FakeGoogle.LocalCalendarId);
    }

    [Fact] // T188
    public async Task ACalendarThePersonDoesNotOwnIsRefusedWithoutAskingGoogle()
    {
        using var rig = new SyncRig();
        var holidays = rig.Google.AddCalendar("Festività in Italia", role: "reader");
        await rig.SyncOkAsync();
        rig.Google.Requests.Clear();
        var ex = await Assert.ThrowsAsync<GoogleCalendarException>(() => rig.Service.DeleteCalendarAsync(Local(holidays), CancellationToken.None));
        Assert.Equal(GoogleCalendarProblem.NotOwner, ex.Problem);
        Assert.Empty(rig.Google.Requests);
        Assert.Contains(rig.Store.GetCalendars(), c => c.Id == Local(holidays));
    }

    [Fact] // T195: the role is not known (before the first sync): refused as well
    public async Task UnknownRoleIsRefused()
    {
        using var rig = new SyncRig();
        rig.Store.UpsertCalendar(new CalendarInfo(Local("x@group.calendar.google.com"), FakeGoogle.Account, "Sconosciuto", "#123456"));
        var ex = await Assert.ThrowsAsync<GoogleCalendarException>(() => rig.Service.DeleteCalendarAsync(Local("x@group.calendar.google.com"), CancellationToken.None));
        Assert.Equal(GoogleCalendarProblem.NotOwner, ex.Problem);
    }

    [Fact] // T185: already deleted elsewhere is not an error
    public async Task ACalendarAlreadyGoneOnGoogleIsCleanedHere()
    {
        var (rig, remote, localId) = await RigWithOwnedCalendarAsync();
        using var _ = rig;
        rig.Google.SeedIn(remote, "Evento", new DateTime(2026, 11, 1));
        await rig.SyncOkAsync();
        await rig.Service.DeleteCalendarAsync(localId, CancellationToken.None); // gone from both
        // the person's other device deleted it too, before this one knew:
        rig.Store.UpsertCalendar(new CalendarInfo(localId, FakeGoogle.Account, "Lavoro", "#123456", IsOwner: true));
        await rig.Service.DeleteCalendarAsync(localId, CancellationToken.None);
        Assert.DoesNotContain(rig.Store.GetCalendars(), c => c.Id == localId);
    }

    [Fact] // T184: no network or a refusal: nothing is deleted
    public async Task FailuresDeleteNothing()
    {
        var (rig, remote, localId) = await RigWithOwnedCalendarAsync();
        using var _ = rig;
        rig.Google.SeedIn(remote, "Evento", new DateTime(2026, 11, 1));
        await rig.SyncOkAsync();

        rig.Google.Offline = true;
        var offline = await Assert.ThrowsAsync<GoogleCalendarException>(() => rig.Service.DeleteCalendarAsync(localId, CancellationToken.None));
        Assert.Equal(GoogleCalendarProblem.NoNetwork, offline.Problem);
        rig.Google.Offline = false;

        rig.Google.NextWriteFails = (HttpStatusCode.Forbidden, "rateLimitExceeded");
        var limited = await Assert.ThrowsAsync<GoogleCalendarException>(() => rig.Service.DeleteCalendarAsync(localId, CancellationToken.None));
        Assert.Equal(GoogleCalendarProblem.RateLimited, limited.Problem);

        Assert.True(rig.Google.HasCalendar(remote));
        Assert.Contains(rig.Store.GetCalendars(), c => c.Id == localId);
        Assert.Contains(rig.Store.GetAllEvents(), e => e.CalendarId == localId);
    }

    // ---- with the sync ----

    [Fact] // T191: the creation or the deletion waits for a sync that is running
    public async Task DeletionWaitsForARunningSync()
    {
        var (rig, remote, localId) = await RigWithOwnedCalendarAsync();
        using var _ = rig;
        await rig.Service.Gate.WaitAsync(); // a sync is running
        var deleting = rig.Service.DeleteCalendarAsync(localId, CancellationToken.None);
        await Task.Delay(200);
        Assert.False(deleting.IsCompleted);
        Assert.True(rig.Google.HasCalendar(remote));
        rig.Service.Gate.Release();
        await deleting;
        Assert.False(rig.Google.HasCalendar(remote));
    }

    [Fact] // T191
    public async Task CreationWaitsForARunningSyncAndCanBeCancelledWhileWaiting()
    {
        using var rig = new SyncRig();
        await rig.Service.Gate.WaitAsync();
        using var cts = new CancellationTokenSource(150);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => rig.Service.CreateCalendarAsync(FakeGoogle.Account, "Palestra", "#1A73E8", cts.Token));
        Assert.Single(rig.Google.CalendarIds);
        rig.Service.Gate.Release();
        Assert.Equal(1, rig.Service.Gate.CurrentCount); // the gate is free again
    }

    [Fact] // T191: a sync does not start while a calendar is being deleted
    public async Task SyncIsSkippedWhileTheGateIsHeld()
    {
        using var rig = new SyncRig();
        await rig.Service.Gate.WaitAsync();
        Assert.False(await rig.Service.Gate.WaitAsync(0)); // what the window does before a sync: it does not wait
        rig.Service.Gate.Release();
    }

    [Fact] // T192: Google deleted it but this PC kept it (closed in between): the next sync cleans
    public async Task NextSyncCleansWhatGoogleDeleted()
    {
        var (rig, remote, localId) = await RigWithOwnedCalendarAsync();
        using var _ = rig;
        rig.Google.SeedIn(remote, "Evento", new DateTime(2026, 11, 1));
        await rig.SyncOkAsync();
        // the deletion reached Google only
        var other = new HttpClient(rig.Google);
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"https://www.googleapis.com/calendar/v3/calendars/{Uri.EscapeDataString(remote)}");
        (await other.SendAsync(request)).EnsureSuccessStatusCode();
        Assert.Contains(rig.Store.GetCalendars(), c => c.Id == localId);

        await rig.SyncOkAsync();
        Assert.DoesNotContain(rig.Store.GetCalendars(), c => c.Id == localId);
        Assert.DoesNotContain(rig.Store.GetAllEvents(), e => e.CalendarId == localId);
    }

    [Fact] // T192: Google created it but this PC did not save it: the next sync adds it
    public async Task NextSyncAddsWhatGoogleCreated()
    {
        using var rig = new SyncRig();
        await rig.SyncOkAsync();
        var remote = rig.Google.AddCalendar("Creato e perso");
        await rig.SyncOkAsync();
        var added = Assert.Single(rig.Store.GetCalendars(), c => c.Id == Local(remote));
        Assert.True(added.IsOwner);
    }

    // ---- messages ----

    [Fact] // T194
    public void EveryProblemHasItsMessageInEveryLanguage()
    {
        foreach (var language in new[] { "it", "en", "fr", "de", "es" })
        {
            var texts = L.Texts(language);
            foreach (var problem in Enum.GetValues<GoogleCalendarProblem>())
                Assert.True(texts.ContainsKey("gcal.problem." + problem), $"{language}: {problem}");
        }

        Assert.Equal("gcal.problem.NotOwner", new GoogleCalendarException(GoogleCalendarProblem.NotOwner).Reason.Key);
    }
}
