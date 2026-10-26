using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GGCalendar.App.Services;
using GGCalendar.App.ViewModels;
using GGCalendar.App.Views;
using GGCalendar.Core.Localization;
using GGCalendar.Core.Models;
using GGCalendar.Core.Tests.Support;
using GGCalendar.Data;
using static GGCalendar.Core.Tests.Support.AppDriver;
using static GGCalendar.Core.Tests.GoogleCalendarUiTests;

namespace GGCalendar.Core.Tests;

/// <summary>Disconnecting a Google account from the menu of the account and from the settings (issue #34), on the real window with a fake Google behind it.</summary>
[Collection(CollectionName)]
public class AccountDisconnectUiTests
{
    private static AccountGroup Group(MainWindow window, Func<AccountGroup, bool> match) => Vm(window).Accounts.First(match);

    private static AccountGroup GoogleGroup(MainWindow window) => Group(window, g => g.AccountName == FakeGoogle.Account);

    /// <summary>The menu of the header of the account, opened.</summary>
    private static ContextMenu OpenAccountMenu(MainWindow window, AccountGroup group)
    {
        var header = window.GetVisualDescendants().OfType<Grid>().First(g => g.DataContext == group && g.ContextMenu is not null);
        header.ContextMenu!.DataContext = group; // headless: the menu does not always take it before the first read
        header.ContextMenu.Open(header);
        Dispatcher.UIThread.RunJobs();
        return header.ContextMenu;
    }

    private static async Task<ConfirmWindow> AskAsync(MainWindow window)
    {
        ClickItem(Item(OpenAccountMenu(window, GoogleGroup(window)), "Menu.DisconnectAccount"));
        return await WaitForAsync(() => window.OwnedWindows.OfType<ConfirmWindow>().FirstOrDefault(), "la domanda");
    }

    private static string TextOf(ConfirmWindow confirm) =>
        string.Join("\n", confirm.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text ?? ""));

    private static async Task AnswerAsync(MainWindow window, ConfirmWindow confirm, bool ok)
    {
        Click(FindButton(confirm, ok ? "Dialog.Ok" : "Dialog.Cancel"));
        await WaitForAsync(() => window.OwnedWindows.Count == 0 ? window : null, "la chiusura della domanda");
    }

    [Fact]
    [Trait("TestCase", "T226")]
    public Task T226_TheMenuOfAGoogleAccountHasDisconnectAndTheOtherGroupsDoNot() => InFakeGoogleAppAsync(new FakeGoogle(), async window =>
    {
        var google = Item(OpenAccountMenu(window, GoogleGroup(window)), "Menu.DisconnectAccount");
        Assert.True(google.IsVisible);
        Assert.Equal(L.T("menu.disconnect_account"), google.Header);

        var local = Group(window, g => g.DisplayName == L.T("main.local_account"));
        Assert.False(Item(OpenAccountMenu(window, local), "Menu.DisconnectAccount").IsVisible);
        await Task.CompletedTask;
    });

    [Fact]
    [Trait("TestCase", "T227")]
    public Task T227_TheQuestionNamesTheAccountAndCountsWhatGoesAway() => InFakeGoogleAppAsync(FakeWithEvents(3), async window =>
    {
        await SyncNowAsync(window);

        var confirm = await AskAsync(window);

        var text = TextOf(confirm);
        Assert.Contains(AccountDisconnect.Title, text);
        Assert.Contains(FakeGoogle.Account, text);
        Assert.Contains(L.N("account.disconnect_calendars", 1), text);
        Assert.Contains(L.N("account.disconnect_events", 3), text);
        Assert.DoesNotContain(L.N("account.disconnect_pending", 1), text);
        Assert.Contains(L.T("account.disconnect_nothing_pending"), text); // nothing waits: the question says so
        Assert.Equal(AccountDisconnect.Button, FindButton(confirm, "Dialog.Ok").Content);
        await AnswerAsync(window, confirm, false);
    });

    private static FakeGoogle FakeWithEvents(int count)
    {
        var fake = new FakeGoogle();
        for (var i = 0; i < count; i++) fake.SeedIn(FakeGoogle.RemoteCalendar, "Evento " + i, new DateTime(2026, 11, 3).AddHours(9 + i));
        return fake;
    }

    [Fact]
    [Trait("TestCase", "T228")]
    public Task T228_CancelKeepsTheAccountItsCalendarsAndTheAccessOnGoogle() => InFakeGoogleAppAsync(FakeWithEvents(2), async window =>
    {
        await SyncNowAsync(window);
        var vm = Vm(window);
        var events = vm.AllEvents.Count;
        var confirm = await AskAsync(window);

        await AnswerAsync(window, confirm, false);

        Assert.Contains(vm.Accounts, g => g.AccountName == FakeGoogle.Account);
        Assert.Contains(vm.Calendars, c => c.Id == FakeGoogle.LocalCalendarId);
        Assert.Equal(events, vm.AllEvents.Count);
        Assert.NotNull(window.Subscriptions); // the window is usable
        Assert.True(window.IsEnabled);
    });

    [Fact]
    [Trait("TestCase", "T228")]
    public Task T228_CancelDoesNotRevokeAnything()
    {
        var fake = FakeWithEvents(1);
        return InFakeGoogleAppAsync(fake, async window =>
        {
            await SyncNowAsync(window);
            var confirm = await AskAsync(window);

            await AnswerAsync(window, confirm, false);

            Assert.Empty(fake.RevokedTokens);
            Assert.Contains(Vm(window).Accounts, g => g.AccountName == FakeGoogle.Account);
        });
    }

    [Fact]
    [Trait("TestCase", "T229")]
    public Task T229_ConfirmingRemovesTheAccountAndItsCalendarsAndEventsAndRevokesTheAccess()
    {
        var fake = FakeWithEvents(2);
        return InFakeGoogleAppAsync(fake, async window =>
        {
            await SyncNowAsync(window);
            var vm = Vm(window);
            var confirm = await AskAsync(window);

            await AnswerAsync(window, confirm, true);
            await WaitForAsync(() => vm.Status?.Is("settings.disconnected_revoked") == true ? vm : null, "il messaggio", SyncTimeout);

            Assert.DoesNotContain(vm.Accounts, g => g.AccountName == FakeGoogle.Account);
            Assert.DoesNotContain(vm.Calendars, c => c.Id.StartsWith(FakeGoogle.Account + "|", StringComparison.Ordinal));
            Assert.DoesNotContain(vm.AllEvents, e => e.CalendarId.StartsWith(FakeGoogle.Account + "|", StringComparison.Ordinal));
            Assert.Equal(["refresh-token"], fake.RevokedTokens);
            Assert.True(window.IsEnabled);
            Assert.Contains(vm.Accounts, g => g.DisplayName == L.T("main.local_account")); // the calendars of this PC are still there
        });
    }

    [Fact]
    [Trait("TestCase", "T229")]
    public Task T229_AfterDisconnectingTheSyncHasNothingToDoAndTheAlarmsOfThatAccountAreGone()
    {
        var fake = FakeWithEvents(1);
        return InFakeGoogleAppAsync(fake, async window =>
        {
            await SyncNowAsync(window);
            var vm = Vm(window);
            var confirm = await AskAsync(window);
            await AnswerAsync(window, confirm, true);
            await WaitForAsync(() => vm.Status?.Is("settings.disconnected_revoked") == true ? vm : null, "il messaggio", SyncTimeout);
            var requests = fake.Requests.Count;

            Click(FindButton(window, "Main.SyncNow"));
            await PauseAsync(TimeSpan.FromMilliseconds(400));

            Assert.Equal(requests, fake.Requests.Count); // no account: nothing is asked of Google any more
            Assert.DoesNotContain(window.Scheduler!.Pending(), a => a.Event.CalendarId.StartsWith(FakeGoogle.Account + "|", StringComparison.Ordinal));
        });
    }

    [Fact]
    [Trait("TestCase", "T230")]
    public Task T230_WithoutNetworkTheAccountGoesAwayAndTheMessageSaysTheAccessWasNotRevoked()
    {
        var fake = FakeWithEvents(1);
        return InFakeGoogleAppAsync(fake, async window =>
        {
            await SyncNowAsync(window);
            var vm = Vm(window);
            var confirm = await AskAsync(window);
            fake.Offline = true;

            await AnswerAsync(window, confirm, true);
            await WaitForAsync(() => vm.Status?.Is("settings.disconnected_not_revoked") == true ? vm : null, "il messaggio", SyncTimeout);

            Assert.DoesNotContain(vm.Accounts, g => g.AccountName == FakeGoogle.Account);
            Assert.Empty(fake.RevokedTokens);
        });
    }

    [Fact]
    [Trait("TestCase", "T227")]
    public Task T227_T231_TheQuestionWarnsAboutChangesNotYetSentAndTheyAreLost()
    {
        var fake = new FakeGoogle();
        return InFakeGoogleAppAsync(fake, async window =>
        {
            await SyncNowAsync(window);
            var vm = Vm(window);
            fake.Offline = true; // the change stays in the queue
            vm.AddEvent(new CalendarEvent(Guid.NewGuid().ToString("N"), FakeGoogle.LocalCalendarId, "Non inviato", new DateTime(2026, 11, 9, 9, 0, 0), new DateTime(2026, 11, 9, 10, 0, 0)) { Reminders = [] });
            Assert.Equal(1, vm.PendingChangesFor(FakeGoogle.LocalCalendarId));

            var confirm = await AskAsync(window);

            Assert.Contains(L.N("account.disconnect_pending", 1), TextOf(confirm));
            await AnswerAsync(window, confirm, true);
            await WaitForAsync(() => vm.Status?.Is("settings.disconnected_not_revoked") == true ? vm : null, "il messaggio", SyncTimeout);
            Assert.Empty(new CalendarStore().GetPending().Where(p => p.CalendarId == FakeGoogle.LocalCalendarId));
            Assert.DoesNotContain(vm.AllEvents, e => e.Title == "Non inviato");
        });
    }

    [Fact]
    [Trait("TestCase", "T227")]
    public Task T227_AnEventMadeWithTheEditorAndNotYetSentIsCountedInTheMenuQuestion()
    {
        var fake = new FakeGoogle();
        return InFakeGoogleAppAsync(fake, async window =>
        {
            await SyncNowAsync(window);
            fake.Offline = true;
            var title = NewTitle();
            await CreateEventAsync(window, new EventSpec(title) { CalendarId = FakeGoogle.LocalCalendarId, Start = DateTime.Today.AddDays(5).AddHours(10) });

            var confirm = await AskAsync(window);

            Assert.Contains(L.N("account.disconnect_pending", 1), TextOf(confirm));
            await AnswerAsync(window, confirm, false);
        });
    }

    [Fact]
    [Trait("TestCase", "T227")]
    public Task T227_T232_TheSettingsQuestionWarnsAboutChangesNotYetSentToo()
    {
        var fake = new FakeGoogle();
        return InFakeGoogleAppAsync(fake, async window =>
        {
            await SyncNowAsync(window);
            fake.Offline = true;
            Vm(window).AddEvent(new CalendarEvent(Guid.NewGuid().ToString("N"), FakeGoogle.LocalCalendarId, "Non inviato", new DateTime(2026, 11, 9, 9, 0, 0), new DateTime(2026, 11, 9, 10, 0, 0)) { Reminders = [] });
            Click(FindButton(window, "Main.Settings"));
            var settings = await WaitForAsync(() => window.OwnedWindows.OfType<SettingsWindow>().FirstOrDefault(), "le impostazioni");

            Click(FindButton(settings, "Settings.Disconnect"));
            var confirm = await WaitForAsync(() => settings.OwnedWindows.OfType<ConfirmWindow>().FirstOrDefault(), "la domanda");

            Assert.Contains(L.N("account.disconnect_pending", 1), TextOf(confirm));
            Click(FindButton(confirm, "Dialog.Cancel"));
            await WaitForAsync(() => settings.OwnedWindows.Count == 0 ? settings : null, "la chiusura della domanda");
            settings.Close(null);
            await WaitForAsync(() => window.OwnedWindows.Count == 0 ? window : null, "la chiusura delle impostazioni");
        });
    }

    [Fact]
    [Trait("TestCase", "T232")]
    public Task T232_TheSettingsAskTheSameQuestionAndCancelKeepsTheAccount()
    {
        var fake = FakeWithEvents(2);
        return InFakeGoogleAppAsync(fake, async window =>
        {
            await SyncNowAsync(window);
            Click(FindButton(window, "Main.Settings"));
            var settings = await WaitForAsync(() => window.OwnedWindows.OfType<SettingsWindow>().FirstOrDefault(), "le impostazioni");
            var vm = (SettingsViewModel)settings.DataContext!;
            Assert.Contains(FakeGoogle.Account, vm.GoogleAccounts);

            Click(FindButton(settings, "Settings.Disconnect"));
            var confirm = await WaitForAsync(() => settings.OwnedWindows.OfType<ConfirmWindow>().FirstOrDefault(), "la domanda");
            Assert.Contains(FakeGoogle.Account, TextOf(confirm));
            Assert.Contains(L.N("account.disconnect_events", 2), TextOf(confirm));

            Click(FindButton(confirm, "Dialog.Cancel"));
            await WaitForAsync(() => settings.OwnedWindows.Count == 0 ? settings : null, "la chiusura della domanda");
            Assert.Contains(FakeGoogle.Account, vm.GoogleAccounts);
            Assert.Empty(fake.RevokedTokens);

            settings.Close(null);
            await WaitForAsync(() => window.OwnedWindows.Count == 0 ? window : null, "la chiusura delle impostazioni");
        });
    }

    [Fact]
    [Trait("TestCase", "T232")]
    public Task T232_FromTheSettingsConfirmingDisconnectsAndTheMainWindowFollows()
    {
        var fake = FakeWithEvents(1);
        return InFakeGoogleAppAsync(fake, async window =>
        {
            await SyncNowAsync(window);
            var main = Vm(window);
            Click(FindButton(window, "Main.Settings"));
            var settings = await WaitForAsync(() => window.OwnedWindows.OfType<SettingsWindow>().FirstOrDefault(), "le impostazioni");
            var vm = (SettingsViewModel)settings.DataContext!;
            Click(FindButton(settings, "Settings.Disconnect"));
            var confirm = await WaitForAsync(() => settings.OwnedWindows.OfType<ConfirmWindow>().FirstOrDefault(), "la domanda");

            Click(FindButton(confirm, "Dialog.Ok"));
            await WaitForAsync(() => vm.GoogleAccounts.Contains(FakeGoogle.Account) ? null : vm, "lo scollegamento");

            Assert.Equal(["refresh-token"], fake.RevokedTokens);
            Assert.Equal(L.T("settings.disconnected_revoked", FakeGoogle.Account), vm.GoogleStatus);
            settings.Close(null);
            await WaitForAsync(() => window.OwnedWindows.Count == 0 ? window : null, "la chiusura delle impostazioni");
            await WaitForAsync(() => main.Accounts.Any(g => g.AccountName == FakeGoogle.Account) ? null : main, "l'aggiornamento della finestra");
            Assert.DoesNotContain(main.Calendars, c => c.Id == FakeGoogle.LocalCalendarId);
        });
    }

    [Theory]
    [InlineData("it")]
    [InlineData("en")]
    [InlineData("fr")]
    [InlineData("de")]
    [InlineData("es")]
    [Trait("TestCase", "T235")]
    public Task T235_TheMenuAndTheQuestionAreInTheChosenLanguage(string language)
    {
        var fake = FakeWithEvents(1);
        return InFakeGoogleAppAsync(fake, async window =>
        {
            var original = L.Language;
            try
            {
                await SyncNowAsync(window);
                L.Apply(language);
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(L.T("menu.disconnect_account"), Item(OpenAccountMenu(window, GoogleGroup(window)), "Menu.DisconnectAccount").Header);
                Assert.DoesNotContain("menu.", (string)Item(OpenAccountMenu(window, GoogleGroup(window)), "Menu.DisconnectAccount").Header!);

                var confirm = await AskAsync(window);
                var text = TextOf(confirm);
                Assert.Contains(L.T("account.disconnect_title"), text);
                Assert.Contains(L.T("account.disconnect_removes"), text);
                Assert.DoesNotContain("account.disconnect", text); // no key left without its text
                await AnswerAsync(window, confirm, false);
            }
            finally
            {
                L.Apply(original);
            }
        });
    }
}
