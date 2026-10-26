using System.Net;
using System.Text.Json.Nodes;
using GGCalendar.App.Services;
using GGCalendar.App.ViewModels;
using GGCalendar.Core.Models;
using GGCalendar.Core.Tests.Support;
using GGCalendar.Data;

namespace GGCalendar.Core.Tests;

/// <summary>Changing the name and the colour of a calendar (issue #7): the colours, a calendar of this PC, a calendar of Google (online only, per role).</summary>
public class CalendarEditTests
{
    private static string Local(string remote) => FakeGoogle.Account + "|" + remote;

    private static async Task<(SyncRig Rig, string Remote, string LocalId)> OwnedAsync(string name = "Lavoro", string color = "#9FE1E7")
    {
        var rig = new SyncRig();
        var remote = rig.Google.AddCalendar(name, color: color);
        await rig.SyncOkAsync();
        return (rig, remote, Local(remote));
    }

    private static string[] Writes(FakeGoogle google) => google.Requests.Where(r => r.StartsWith("PATCH ") || r.StartsWith("POST /calendar") || r.StartsWith("DELETE ")).ToArray();

    // ---- the colours ----

    [Fact] // T206
    public void TheTwentyFourColoursAreDistinctValidAndTheQuickOnesAreAmongThem()
    {
        Assert.Equal(24, CalendarColors.All.Count);
        Assert.Equal(24, CalendarColors.All.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(CalendarColors.All, c => Assert.Equal(c, CalendarColors.Normalize(c)));
        Assert.Equal(8, CalendarColors.Quick.Count);
        Assert.Equal(8, CalendarColors.Quick.Distinct().Count());
        Assert.All(CalendarColors.Quick, c => Assert.True(CalendarColors.IsInPalette(c), c));
        Assert.Equal(CalendarColors.Quick, MainViewModel.CalendarPalette);
    }

    [Theory] // T206
    [InlineData("#abcdef", "#ABCDEF")]
    [InlineData("#ABCDEF", "#ABCDEF")]
    [InlineData("abcdef", null)]
    [InlineData("#abc", null)]
    [InlineData("#gggggg", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void NormalizeWritesCapitalsAndRefusesWhatIsNotAColour(string? text, string? expected) => Assert.Equal(expected, CalendarColors.Normalize(text));

    [Fact] // T206
    public void TextColourIsBlackOrWhiteByTheBrightnessForEveryColour()
    {
        Assert.All(CalendarColors.All, c => Assert.Contains(CalendarColors.TextFor(c), new[] { "#000000", "#FFFFFF" }));
        Assert.Equal("#000000", CalendarColors.TextFor("#9FE1E7")); // light
        Assert.Equal("#FFFFFF", CalendarColors.TextFor("#1A237E")); // dark
        Assert.Equal("#000000", CalendarColors.TextFor("#FFF59D")); // light
        Assert.Equal("#000000", CalendarColors.TextFor("not a colour"));
    }

    // ---- a calendar of this PC ----

    [Fact] // T196
    public void LocalCalendarKeepsNameAndColourAndKeepsBeingVisible()
    {
        var store = new CalendarStore(":memory:");
        store.UpsertCalendar(new CalendarInfo("local-a", MainViewModel.LocalAccountName, "Personale", "#1A73E8", IsVisible: false));
        store.UpsertCalendar(new CalendarInfo("local-b", MainViewModel.LocalAccountName, "Altro", "#1A73E8"));
        var vm = new MainViewModel(store);

        vm.UpdateLocalCalendar("local-a", "  Zeta «città»  ", "#FA573C");
        var a = store.GetCalendars().Single(c => c.Id == "local-a");
        Assert.Equal(("Zeta «città»", "#FA573C", false), (a.Name, a.ColorHex, a.IsVisible)); // trimmed; visibility untouched
        Assert.Equal(["Altro", "Zeta «città»"], vm.Calendars.Where(c => c.IsLocal).Select(c => c.Name)); // the list is in order of name again
        Assert.Equal("#FA573C", vm.Calendars.Single(c => c.Id == "local-a").ColorHex);
        Assert.Equal("#FA573C", vm.CalendarColors["local-a"]); // what the views use for the events of the calendar
    }

    [Fact] // T196
    public void LocalEditRefusesEmptyNameAndWhatIsNotLocal()
    {
        var store = new CalendarStore(":memory:");
        store.UpsertCalendar(new CalendarInfo("local-a", MainViewModel.LocalAccountName, "Personale", "#1A73E8"));
        store.UpsertCalendar(new CalendarInfo("me@example.com|x@group.calendar.google.com", "me@example.com", "Google", "#123456", IsOwner: true));
        store.UpsertCalendar(new CalendarInfo("sub:abc", "Public", "Abbonato", "#654321"));
        var vm = new MainViewModel(store);

        vm.UpdateLocalCalendar("local-a", "   ", "#FA573C");
        vm.UpdateLocalCalendar("me@example.com|x@group.calendar.google.com", "Cambiato", "#FA573C");
        vm.UpdateLocalCalendar("sub:abc", "Cambiato", "#FA573C");
        vm.UpdateLocalCalendar("non-esiste", "X", "#FA573C");
        var all = store.GetCalendars().ToDictionary(c => c.Id);
        Assert.Equal(("Personale", "#1A73E8"), (all["local-a"].Name, all["local-a"].ColorHex));
        Assert.Equal(("Google", "#123456"), (all["me@example.com|x@group.calendar.google.com"].Name, all["me@example.com|x@group.calendar.google.com"].ColorHex)); // Google goes through Google
        Assert.Equal(("Abbonato", "#654321"), (all["sub:abc"].Name, all["sub:abc"].ColorHex)); // a subscription has its own window
    }

    // ---- who can change what ----

    [Fact] // T197, T199, T200, T201
    public void CalendarItemSaysWhatCanBeChanged()
    {
        var store = new CalendarStore(":memory:");
        store.UpsertCalendar(new CalendarInfo("local-a", MainViewModel.LocalAccountName, "Locale", "#1A73E8"));
        store.UpsertCalendar(new CalendarInfo(FakeGoogle.LocalCalendarId, FakeGoogle.Account, "Principale", "#1A73E8", IsOwner: true));
        store.UpsertCalendar(new CalendarInfo(Local("own@group"), FakeGoogle.Account, "Proprio", "#1A73E8", IsOwner: true));
        store.UpsertCalendar(new CalendarInfo(Local("read@group"), FakeGoogle.Account, "Altrui", "#1A73E8", IsOwner: false));
        store.UpsertCalendar(new CalendarInfo(Local("new@group"), FakeGoogle.Account, "Nuovo", "#1A73E8"));
        var vm = new MainViewModel(store);
        CalendarItemViewModel Of(string id) => vm.Calendars.Single(c => c.Id == id);

        Assert.True(Of("local-a").CanRename);
        Assert.Equal("", Of("local-a").RenameHint + Of("local-a").RenameNote);

        Assert.True(Of(Local("own@group")).CanRename);
        Assert.Equal("", Of(Local("own@group")).RenameHint);
        Assert.NotEqual("", Of(Local("own@group")).RenameNote); // the name changes for the others too

        Assert.False(Of(FakeGoogle.LocalCalendarId).CanRename);
        Assert.Equal(GGCalendar.Core.Localization.L.T("gcal.rename_hint_primary"), Of(FakeGoogle.LocalCalendarId).RenameHint);
        Assert.False(Of(Local("read@group")).CanRename);
        Assert.Equal(GGCalendar.Core.Localization.L.T("gcal.rename_hint_not_owner"), Of(Local("read@group")).RenameHint);
        Assert.False(Of(Local("new@group")).CanRename); // the role is not known before the first sync
        Assert.Equal(GGCalendar.Core.Localization.L.T("gcal.rename_hint_unknown"), Of(Local("new@group")).RenameHint);
    }

    // ---- Google: the colour ----

    [Fact] // T197
    public async Task ColourOfAnOwnedCalendarIsOneRequestAndTheNameIsNotTouched()
    {
        var (rig, remote, localId) = await OwnedAsync();
        using var _ = rig;
        rig.Google.Requests.Clear();

        var edit = await rig.Service.UpdateCalendarAsync(localId, null, "#a47ae2", CancellationToken.None);

        Assert.Null(edit.NotCompleted);
        var patch = Assert.Single(Writes(rig.Google));
        Assert.StartsWith("PATCH /calendar/v3/users/me/calendarList/cal", patch);
        Assert.Contains("colorRgbFormat=true", patch);
        Assert.Empty(rig.Google.Renames);
        var (_, _, body) = Assert.Single(rig.Google.ColorPatches);
        var json = JsonNode.Parse(body)!.AsObject();
        Assert.Equal("#A47AE2", (string?)json["backgroundColor"]);
        Assert.Equal("#000000", (string?)json["foregroundColor"]); // black on the colours of Google (they are light)
        Assert.False(json.ContainsKey("summaryOverride")); // the personal name is left as it is when only the colour changes

        Assert.Equal("#A47AE2", rig.Google.CalendarColor(remote));
        var stored = rig.Store.GetCalendars().Single(c => c.Id == localId);
        Assert.Equal(("Lavoro", "#A47AE2", true), (stored.Name, stored.ColorHex, stored.IsOwner));
    }

    [Fact] // T197: a colour of another palette still works
    public async Task AColourOutsideThePaletteGetsAReadableText()
    {
        var (rig, remote, localId) = await OwnedAsync();
        using var _ = rig;
        await rig.Service.UpdateCalendarAsync(localId, null, "#1A237E", CancellationToken.None);
        Assert.Equal("#FFFFFF", (string?)JsonNode.Parse(rig.Google.ColorPatches.Single().Body)!["foregroundColor"]);
    }

    [Fact] // T204: Google gives the new colour back at the next sync, the edit is not undone
    public async Task NextSyncKeepsTheEdit()
    {
        var (rig, remote, localId) = await OwnedAsync("Lavoro");
        using var _ = rig;
        await rig.Service.UpdateCalendarAsync(localId, "Studio", "#16A765", CancellationToken.None);
        await rig.SyncOkAsync();
        var stored = rig.Store.GetCalendars().Single(c => c.Id == localId);
        Assert.Equal(("Studio", "#16A765"), (stored.Name, stored.ColorHex));
    }

    // ---- Google: the name ----

    [Fact] // T198
    public async Task RenamingAnOwnedCalendarRenamesItAndClearsThePersonalName()
    {
        var (rig, remote, localId) = await OwnedAsync("Lavoro");
        using var _ = rig;
        rig.Google.SetPersonalName(remote, "Mio lavoro"); // the list shows this one, not "Lavoro"
        await rig.SyncOkAsync();
        Assert.Equal("Mio lavoro", rig.Store.GetCalendars().Single(c => c.Id == localId).Name);
        rig.Google.Requests.Clear();

        var edit = await rig.Service.UpdateCalendarAsync(localId, "  Studio  ", null, CancellationToken.None);

        Assert.Null(edit.NotCompleted);
        Assert.Equal(2, Writes(rig.Google).Length);
        Assert.StartsWith("PATCH /calendar/v3/calendars/cal", Writes(rig.Google)[0]); // the real name first
        Assert.StartsWith("PATCH /calendar/v3/users/me/calendarList/cal", Writes(rig.Google)[1]);
        Assert.Equal([(remote, "Studio")], rig.Google.Renames);
        Assert.Equal("Studio", rig.Google.CalendarSummary(remote));
        Assert.Null(rig.Google.PersonalNameOf(remote)); // so that "Studio" is what is seen
        var body = JsonNode.Parse(rig.Google.ColorPatches.Single().Body)!.AsObject();
        Assert.False(body.ContainsKey("backgroundColor")); // the colour was not asked
        Assert.True(body.ContainsKey("summaryOverride"));
        Assert.Equal("Studio", rig.Store.GetCalendars().Single(c => c.Id == localId).Name);

        await rig.SyncOkAsync();
        Assert.Equal("Studio", rig.Store.GetCalendars().Single(c => c.Id == localId).Name); // and the sync agrees
    }

    [Fact] // T198
    public async Task NameAndColourTogetherAreTwoRequestsAndBothAreSet()
    {
        var (rig, remote, localId) = await OwnedAsync("Lavoro");
        using var _ = rig;
        rig.Google.Requests.Clear();

        await rig.Service.UpdateCalendarAsync(localId, "Studio", "#FA573C", CancellationToken.None);

        Assert.Equal(2, Writes(rig.Google).Length);
        var body = JsonNode.Parse(rig.Google.ColorPatches.Single().Body)!.AsObject();
        Assert.Equal("#FA573C", (string?)body["backgroundColor"]);
        Assert.True(body.ContainsKey("summaryOverride"));
        var stored = rig.Store.GetCalendars().Single(c => c.Id == localId);
        Assert.Equal(("Studio", "#FA573C"), (stored.Name, stored.ColorHex));
    }

    [Fact] // T198: names with special characters, and a long one
    public async Task SpecialAndLongNames()
    {
        var (rig, remote, localId) = await OwnedAsync();
        using var _ = rig;
        await rig.Service.UpdateCalendarAsync(localId, "Città & «prova» \"1\" 😀", null, CancellationToken.None);
        Assert.Equal("Città & «prova» \"1\" 😀", rig.Google.CalendarSummary(remote));
        await rig.Service.UpdateCalendarAsync(localId, new string('x', 400), null, CancellationToken.None);
        Assert.Equal(GoogleSyncService.MaxCalendarName, rig.Google.CalendarSummary(remote)!.Length);
        Assert.Equal(GoogleSyncService.MaxCalendarName, rig.Store.GetCalendars().Single(c => c.Id == localId).Name.Length);
    }

    [Fact] // T198: nothing changed, nothing asked
    public async Task NoChangeIsNoRequest()
    {
        var (rig, remote, localId) = await OwnedAsync("Lavoro", "#9FE1E7");
        using var _ = rig;
        rig.Google.Requests.Clear();
        var same = await rig.Service.UpdateCalendarAsync(localId, "  Lavoro ", "#9fe1e7", CancellationToken.None);
        var nothing = await rig.Service.UpdateCalendarAsync(localId, null, null, CancellationToken.None);
        Assert.Empty(rig.Google.Requests);
        Assert.Equal("Lavoro", same.Calendar.Name);
        Assert.Equal("Lavoro", nothing.Calendar.Name);
    }

    [Fact] // T198, T205
    public async Task EmptyNameAndBadColourAreRefusedWithoutAskingGoogle()
    {
        var (rig, remote, localId) = await OwnedAsync();
        using var _ = rig;
        rig.Google.Requests.Clear();
        var empty = await Assert.ThrowsAsync<GoogleCalendarException>(() => rig.Service.UpdateCalendarAsync(localId, "   ", null, CancellationToken.None));
        Assert.Equal(GoogleCalendarProblem.EmptyName, empty.Problem);
        var bad = await Assert.ThrowsAsync<GoogleCalendarException>(() => rig.Service.UpdateCalendarAsync(localId, null, "rosso", CancellationToken.None));
        Assert.Equal(GoogleCalendarProblem.Other, bad.Problem);
        Assert.Empty(rig.Google.Requests);
    }

    // ---- Google: the primary calendar, the calendars of others, a role not known ----

    [Fact] // T200
    public async Task PrimaryCalendarKeepsItsNameButTakesAColour()
    {
        using var rig = new SyncRig();
        await rig.SyncOkAsync();
        rig.Google.Requests.Clear();

        var refused = await Assert.ThrowsAsync<GoogleCalendarException>(() => rig.Service.UpdateCalendarAsync(FakeGoogle.LocalCalendarId, "Altro nome", null, CancellationToken.None));
        Assert.Equal(GoogleCalendarProblem.Primary, refused.Problem);
        Assert.Empty(Writes(rig.Google));

        // The window sends the name it showed: the same, so only the colour changes.
        var name = rig.Store.GetCalendars().Single(c => c.Id == FakeGoogle.LocalCalendarId).Name;
        await rig.Service.UpdateCalendarAsync(FakeGoogle.LocalCalendarId, name, "#4986E7", CancellationToken.None);
        Assert.Single(Writes(rig.Google));
        Assert.Empty(rig.Google.Renames);
        Assert.Equal("#4986E7", rig.Google.CalendarColor(FakeGoogle.RemoteCalendar));
        Assert.Equal("#4986E7", rig.Store.GetCalendars().Single(c => c.Id == FakeGoogle.LocalCalendarId).ColorHex);
    }

    [Fact] // T199
    public async Task CalendarOfSomeoneElseKeepsItsNameButTakesAColour()
    {
        using var rig = new SyncRig();
        var holidays = rig.Google.AddCalendar("Festività in Italia", role: "reader");
        await rig.SyncOkAsync();
        rig.Google.Requests.Clear();

        var refused = await Assert.ThrowsAsync<GoogleCalendarException>(() => rig.Service.UpdateCalendarAsync(Local(holidays), "Feste", null, CancellationToken.None));
        Assert.Equal(GoogleCalendarProblem.NotOwner, refused.Problem);
        Assert.Empty(Writes(rig.Google));

        await rig.Service.UpdateCalendarAsync(Local(holidays), "Festività in Italia", "#16A765", CancellationToken.None);
        Assert.Single(Writes(rig.Google));
        Assert.Empty(rig.Google.Renames);
        Assert.Equal("Festività in Italia", rig.Google.CalendarSummary(holidays));
        Assert.Equal("#16A765", rig.Store.GetCalendars().Single(c => c.Id == Local(holidays)).ColorHex);
    }

    [Fact] // T201
    public async Task UnknownRoleAllowsOnlyTheColour()
    {
        using var rig = new SyncRig();
        var remote = rig.Google.AddCalendar("Nuovo");
        rig.Store.UpsertCalendar(new CalendarInfo(Local(remote), FakeGoogle.Account, "Nuovo", "#9FE1E7")); // before the first sync of this version: no role
        var refused = await Assert.ThrowsAsync<GoogleCalendarException>(() => rig.Service.UpdateCalendarAsync(Local(remote), "Altro", null, CancellationToken.None));
        Assert.Equal(GoogleCalendarProblem.NotOwner, refused.Problem);
        await rig.Service.UpdateCalendarAsync(Local(remote), null, "#FA573C", CancellationToken.None);
        Assert.Equal("#FA573C", rig.Google.CalendarColor(remote));
    }

    // ---- errors: nothing changes ----

    [Fact] // T202
    public async Task NoNetworkChangesNothing()
    {
        var (rig, remote, localId) = await OwnedAsync("Lavoro", "#9FE1E7");
        using var _ = rig;
        rig.Google.Offline = true;
        var ex = await Assert.ThrowsAsync<GoogleCalendarException>(() => rig.Service.UpdateCalendarAsync(localId, "Studio", "#FA573C", CancellationToken.None));
        Assert.Equal(GoogleCalendarProblem.NoNetwork, ex.Problem);
        var stored = rig.Store.GetCalendars().Single(c => c.Id == localId);
        Assert.Equal(("Lavoro", "#9FE1E7"), (stored.Name, stored.ColorHex));
    }

    [Theory] // T202
    [InlineData(403, "rateLimitExceeded", GoogleCalendarProblem.RateLimited)]
    [InlineData(429, "rateLimitExceeded", GoogleCalendarProblem.RateLimited)]
    [InlineData(403, "forbidden", GoogleCalendarProblem.NotAllowed)]
    [InlineData(401, "authError", GoogleCalendarProblem.AuthExpired)]
    [InlineData(500, "backendError", GoogleCalendarProblem.Server)]
    [InlineData(400, "invalid", GoogleCalendarProblem.Other)]
    public async Task GoogleRefusalsHaveTheirProblemAndChangeNothing(int status, string reason, GoogleCalendarProblem expected)
    {
        var (rig, remote, localId) = await OwnedAsync("Lavoro", "#9FE1E7");
        using var _ = rig;
        rig.Google.NextWriteFails = ((HttpStatusCode)status, reason);
        var ex = await Assert.ThrowsAsync<GoogleCalendarException>(() => rig.Service.UpdateCalendarAsync(localId, "Studio", "#FA573C", CancellationToken.None));
        Assert.Equal(expected, ex.Problem);
        var stored = rig.Store.GetCalendars().Single(c => c.Id == localId);
        Assert.Equal(("Lavoro", "#9FE1E7"), (stored.Name, stored.ColorHex));
        Assert.Equal("Lavoro", rig.Google.CalendarSummary(remote)); // the first request was the one that failed
    }

    [Fact] // T202: only the colour, and Google refuses it
    public async Task ColourRefusedLeavesTheOldOne()
    {
        var (rig, remote, localId) = await OwnedAsync("Lavoro", "#9FE1E7");
        using var _ = rig;
        rig.Google.NextWriteFails = (HttpStatusCode.Forbidden, "rateLimitExceeded");
        await Assert.ThrowsAsync<GoogleCalendarException>(() => rig.Service.UpdateCalendarAsync(localId, null, "#FA573C", CancellationToken.None));
        Assert.Equal("#9FE1E7", rig.Store.GetCalendars().Single(c => c.Id == localId).ColorHex);
        Assert.Equal("#9FE1E7", rig.Google.CalendarColor(remote));
    }

    [Fact] // T202: the owner is not the owner any more (changed on Google meanwhile)
    public async Task RenameRefusedByGoogleBecauseOfTheRoleChangesNothing()
    {
        var (rig, remote, localId) = await OwnedAsync("Lavoro");
        using var _ = rig;
        rig.Store.UpsertCalendar(new CalendarInfo(localId, FakeGoogle.Account, "Lavoro", "#9FE1E7", IsOwner: true));
        rig.Google.RemoveOwnership(remote); // another device gave the calendar away
        var ex = await Assert.ThrowsAsync<GoogleCalendarException>(() => rig.Service.UpdateCalendarAsync(localId, "Studio", null, CancellationToken.None));
        Assert.Equal(GoogleCalendarProblem.NotAllowed, ex.Problem);
        Assert.Equal("Lavoro", rig.Store.GetCalendars().Single(c => c.Id == localId).Name);
    }

    // ---- a result in part ----

    [Fact] // T203
    public async Task NameDoneAndColourRefusedKeepsTheNameAndSaysWhy()
    {
        var (rig, remote, localId) = await OwnedAsync("Lavoro", "#9FE1E7");
        using var _ = rig;
        rig.Google.LimitAfterWrites = rig.Google.Writes + 1; // the rename goes, the second request is over the limit

        var edit = await rig.Service.UpdateCalendarAsync(localId, "Studio", "#FA573C", CancellationToken.None);

        Assert.Equal(GoogleCalendarProblem.RateLimited, edit.NotCompleted);
        Assert.Equal("Studio", rig.Google.CalendarSummary(remote));
        Assert.Equal("#9FE1E7", rig.Google.CalendarColor(remote)); // the colour did not go
        var stored = rig.Store.GetCalendars().Single(c => c.Id == localId);
        Assert.Equal(("Studio", "#9FE1E7"), (stored.Name, stored.ColorHex)); // here as on Google
        Assert.Equal(("Studio", "#9FE1E7"), (edit.Calendar.Name, edit.Calendar.ColorHex));

        rig.Google.LimitAfterWrites = null;
        await rig.SyncOkAsync();
        Assert.Equal(("Studio", "#9FE1E7"), (rig.Store.GetCalendars().Single(c => c.Id == localId).Name, rig.Store.GetCalendars().Single(c => c.Id == localId).ColorHex));
    }

    // ---- with the sync ----

    [Fact] // T204
    public async Task EditWaitsForARunningSync()
    {
        var (rig, remote, localId) = await OwnedAsync();
        using var _ = rig;
        await rig.Service.Gate.WaitAsync(); // a sync is running
        var editing = rig.Service.UpdateCalendarAsync(localId, "Studio", null, CancellationToken.None);
        await Task.Delay(200);
        Assert.False(editing.IsCompleted);
        Assert.Equal("Lavoro", rig.Google.CalendarSummary(remote));
        rig.Service.Gate.Release();
        await editing;
        Assert.Equal("Studio", rig.Google.CalendarSummary(remote));
    }

    [Fact] // T204: a cancel while it waits
    public async Task EditCanBeCancelledWhileItWaits()
    {
        var (rig, remote, localId) = await OwnedAsync();
        using var _ = rig;
        await rig.Service.Gate.WaitAsync();
        using var cts = new CancellationTokenSource(150);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => rig.Service.UpdateCalendarAsync(localId, "Studio", null, cts.Token));
        rig.Service.Gate.Release();
        Assert.Equal("Lavoro", rig.Google.CalendarSummary(remote));
    }

    [Fact] // T207: events keep their own colour; the others follow the calendar
    public async Task EventsWithoutTheirOwnColourFollowTheCalendar()
    {
        var (rig, remote, localId) = await OwnedAsync("Lavoro", "#9FE1E7");
        using var _ = rig;
        rig.Google.SeedIn(remote, "Senza colore", new DateTime(2026, 11, 2));
        rig.Google.SeedIn(remote, "Con colore", new DateTime(2026, 11, 3));
        await rig.SyncOkAsync();
        var with = rig.Store.GetAllEvents().Single(e => e.Title == "Con colore");
        rig.Store.UpsertEvent(with with { ColorHex = "#D50000" });

        await rig.Service.UpdateCalendarAsync(localId, null, "#16A765", CancellationToken.None);
        await rig.SyncOkAsync();

        var vm = new MainViewModel(rig.Store);
        Assert.Equal("#16A765", vm.CalendarColors[localId]);
        Assert.Null(rig.Store.GetAllEvents().Single(e => e.Title == "Senza colore").ColorHex); // it takes the colour of the calendar
        Assert.Equal("#D50000", rig.Store.GetAllEvents().Single(e => e.Title == "Con colore").ColorHex);
    }
}
