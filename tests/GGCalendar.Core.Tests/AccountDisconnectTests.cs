using System.Net;
using GGCalendar.App.Services;
using GGCalendar.Core.Models;
using GGCalendar.Core.Tests.Support;

namespace GGCalendar.Core.Tests;

/// <summary>Disconnecting a Google account (issue #34): what the question can tell, and what is removed, with a fake Google that sees the revoke.</summary>
public class AccountDisconnectTests
{
    private static CalendarEvent Event(string id, string calendar = FakeGoogle.LocalCalendarId) =>
        new(id, calendar, "Evento " + id, new DateTime(2026, 11, 3, 9, 0, 0), new DateTime(2026, 11, 3, 10, 0, 0)) { Reminders = [] };

    [Fact]
    [Trait("TestCase", "T227")]
    public async Task T227_TheQuestionCountsCalendarsEventsAndChangesNotYetSent()
    {
        using var rig = new SyncRig();
        rig.Google.SeedIn(FakeGoogle.RemoteCalendar, "Su Google", new DateTime(2026, 11, 5, 9, 0, 0));
        await rig.SyncOkAsync();
        rig.AddLocal(Event("a"));
        rig.AddLocal(Event("b"));

        var info = rig.Service.DescribeAccount(FakeGoogle.Account);

        Assert.Equal(1, info.Calendars);
        Assert.Equal(3, info.Events); // the one of Google and the two made here
        Assert.Equal(2, info.PendingChanges);

        await rig.SyncOkAsync();
        Assert.Equal(0, rig.Service.DescribeAccount(FakeGoogle.Account).PendingChanges); // sent: nothing is lost any more
    }

    [Fact]
    [Trait("TestCase", "T227")]
    public async Task T227_AnAccountWithNothingOnThisPcHasNothingToLose()
    {
        using var rig = new SyncRig();
        Assert.Equal(new GoogleSyncService.AccountDisconnectInfo(1, 0, 0), rig.Service.DescribeAccount(FakeGoogle.Account));
        Assert.Equal(new GoogleSyncService.AccountDisconnectInfo(0, 0, 0), rig.Service.DescribeAccount("altro@example.com"));
        await Task.CompletedTask;
    }

    [Fact]
    [Trait("TestCase", "T229")]
    public async Task T229_DisconnectingRevokesTheAccessAndRemovesTheAccountItsCalendarsAndItsEventsFromThisPc()
    {
        using var rig = new SyncRig();
        rig.Google.SeedIn(FakeGoogle.RemoteCalendar, "Su Google", new DateTime(2026, 11, 5, 9, 0, 0));
        await rig.SyncOkAsync();
        rig.AddLocal(Event("a"));

        var revoked = await rig.Service.RemoveAccountAsync(FakeGoogle.Account, CancellationToken.None);

        Assert.True(revoked);
        Assert.Equal(["refresh-token"], rig.Google.RevokedTokens);
        Assert.Empty(rig.Service.AccountEmails);
        Assert.Empty(rig.Store.GetCalendars().Where(c => c.Id.StartsWith(FakeGoogle.Account + "|", StringComparison.Ordinal)));
        Assert.Empty(rig.Store.GetAllEvents().Where(e => e.CalendarId == FakeGoogle.LocalCalendarId)); // the events go with the calendar
        Assert.Empty(rig.Store.GetPending()); // and the changes not sent
    }

    [Fact]
    [Trait("TestCase", "T229")]
    public async Task T229_WhatIsOnGoogleIsNotTouched()
    {
        using var rig = new SyncRig();
        rig.Google.SeedIn(FakeGoogle.RemoteCalendar, "Su Google", new DateTime(2026, 11, 5, 9, 0, 0));
        await rig.SyncOkAsync();
        var writes = rig.Google.Writes;

        await rig.Service.RemoveAccountAsync(FakeGoogle.Account, CancellationToken.None);

        Assert.Equal(writes, rig.Google.Writes); // no event and no calendar was changed or deleted there
        Assert.DoesNotContain(rig.Google.Requests, r => r.StartsWith("DELETE", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, true)]
    [InlineData(HttpStatusCode.BadRequest, true)] // already invalid: that is the outcome wanted
    [InlineData(HttpStatusCode.InternalServerError, false)]
    [Trait("TestCase", "T230")]
    public async Task T230_TheAccountIsRemovedHereWhateverGoogleAnswersToTheRevoke(HttpStatusCode answer, bool expectedRevoked)
    {
        using var rig = new SyncRig();
        await rig.SyncOkAsync();
        rig.Google.RevokeStatus = answer;

        var revoked = await rig.Service.RemoveAccountAsync(FakeGoogle.Account, CancellationToken.None);

        Assert.Equal(expectedRevoked, revoked);
        Assert.Empty(rig.Service.AccountEmails);
        Assert.Empty(rig.Store.GetCalendars().Where(c => c.Id.StartsWith(FakeGoogle.Account + "|", StringComparison.Ordinal)));
    }

    [Fact]
    [Trait("TestCase", "T230")]
    public async Task T230_WithoutNetworkTheAccountIsRemovedHereAndTheAccessIsNotRevoked()
    {
        using var rig = new SyncRig();
        await rig.SyncOkAsync();
        rig.Google.Offline = true;

        var revoked = await rig.Service.RemoveAccountAsync(FakeGoogle.Account, CancellationToken.None);

        Assert.False(revoked);
        Assert.Empty(rig.Google.RevokedTokens);
        Assert.Empty(rig.Service.AccountEmails);
        Assert.Empty(rig.Store.GetCalendars().Where(c => c.Id.StartsWith(FakeGoogle.Account + "|", StringComparison.Ordinal)));
    }

    [Fact]
    [Trait("TestCase", "T231")]
    public async Task T231_ItWaitsForASyncThatIsRunningBeforeRemovingAnything()
    {
        using var rig = new SyncRig();
        await rig.SyncOkAsync();
        await rig.Service.Gate.WaitAsync(); // a sync holds the gate

        var removal = rig.Service.RemoveAccountAsync(FakeGoogle.Account, CancellationToken.None);
        await Task.Delay(300);
        Assert.False(removal.IsCompleted);
        Assert.Single(rig.Service.AccountEmails); // nothing happened yet
        Assert.Empty(rig.Google.RevokedTokens);

        rig.Service.Gate.Release();
        Assert.True(await removal);
        Assert.Empty(rig.Service.AccountEmails);
        Assert.Equal(1, rig.Service.Gate.CurrentCount); // and the gate is free again
    }

    [Fact]
    [Trait("TestCase", "T231")]
    public async Task T231_WaitingCanBeCancelledAndNothingIsRemoved()
    {
        using var rig = new SyncRig();
        await rig.SyncOkAsync();
        await rig.Service.Gate.WaitAsync();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => rig.Service.RemoveAccountAsync(FakeGoogle.Account, cts.Token));

        Assert.Single(rig.Service.AccountEmails);
        rig.Service.Gate.Release();
        Assert.Equal(1, rig.Service.Gate.CurrentCount);
    }

    [Fact]
    [Trait("TestCase", "T233")]
    public async Task T233_TheCalendarsOfOtherAccountsAndOfThisPcAreNotTouched()
    {
        using var rig = new SyncRig();
        await rig.SyncOkAsync();
        rig.Store.UpsertCalendar(new CalendarInfo("altro@example.com|altro@example.com", "altro@example.com", "Altro", "#16A765"));
        rig.Store.UpsertCalendar(new CalendarInfo("local-mio", "Locale", "Mio", "#A47AE2"));
        rig.Store.UpsertEvent(Event("o", "altro@example.com|altro@example.com"));
        rig.Store.UpsertEvent(Event("l", "local-mio"));

        await rig.Service.RemoveAccountAsync(FakeGoogle.Account, CancellationToken.None);

        var ids = rig.Store.GetCalendars().Select(c => c.Id).ToList();
        Assert.Contains("altro@example.com|altro@example.com", ids);
        Assert.Contains("local-mio", ids);
        Assert.DoesNotContain(FakeGoogle.LocalCalendarId, ids);
        Assert.Equal(["l", "o"], rig.Store.GetAllEvents().Select(e => e.Id).Order().ToArray());
    }

    [Fact]
    [Trait("TestCase", "T233")]
    public async Task T233_DisconnectingAnAccountThatIsNotThereChangesNothing()
    {
        using var rig = new SyncRig();
        await rig.SyncOkAsync();

        var revoked = await rig.Service.RemoveAccountAsync("sconosciuto@example.com", CancellationToken.None);

        Assert.False(revoked);
        Assert.Single(rig.Service.AccountEmails);
        Assert.Contains(rig.Store.GetCalendars(), c => c.Id == FakeGoogle.LocalCalendarId);
    }

    [Fact]
    [Trait("TestCase", "T234")]
    public void T234_TheQuestionNamesTheAccountAndSaysWhatGoesAway()
    {
        var text = AccountDisconnect.Text("me@example.com", new GoogleSyncService.AccountDisconnectInfo(3, 1, 0));

        Assert.Contains("me@example.com", text);
        Assert.Contains("3", text);
        Assert.EndsWith(GGCalendar.Core.Localization.L.T("account.disconnect_nothing_pending"), text); // it says that nothing is waiting, not a warning
        Assert.DoesNotContain(GGCalendar.Core.Localization.L.N("account.disconnect_pending", 1), text);
    }

    [Fact]
    [Trait("TestCase", "T234")]
    public void T234_TheQuestionWarnsWhenChangesNotYetSentWouldBeLost()
    {
        var text = AccountDisconnect.Text("me@example.com", new GoogleSyncService.AccountDisconnectInfo(1, 5, 2));
        Assert.DoesNotContain(GGCalendar.Core.Localization.L.T("account.disconnect_nothing_pending"), text);

        Assert.EndsWith(GGCalendar.Core.Localization.L.N("account.disconnect_pending", 2), text);
    }

    [Fact]
    [Trait("TestCase", "T234")]
    public void T234_AnAccountWithNothingHereGetsNoListOfWhatGoesAway()
    {
        var text = AccountDisconnect.Text("me@example.com", new GoogleSyncService.AccountDisconnectInfo(0, 0, 0));

        Assert.DoesNotContain(GGCalendar.Core.Localization.L.T("account.disconnect_removes"), text);
    }
}
