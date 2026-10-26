using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GGCalendar.App.Services;
using GGCalendar.App.ViewModels;
using GGCalendar.App.Views;
using GGCalendar.Core.Localization;
using GGCalendar.Core.Models;
using GGCalendar.Core.Tests.Support;
using GGCalendar.Data;
using GGCalendar.Providers.Google;
using static GGCalendar.Core.Tests.Support.AppDriver;

namespace GGCalendar.Core.Tests;

/// <summary>
/// Creating and deleting calendars of a Google account on the real window (issue #16), with a fake Google behind it: the three entries of the
/// menus (the account, "Create", the calendar), the grey "delete", the window, the confirmation and what the status bar says.
/// </summary>
[Collection(CollectionName)]
public class GoogleCalendarUiTests
{
    private static string Local(string remote) => FakeGoogle.Account + "|" + remote;

    /// <summary>Runs the app with the Google service on <paramref name="fake"/> (account me@example.com); the calendars of that account are removed when it ends.</summary>
    private static Task InFakeGoogleAppAsync(FakeGoogle fake, Func<MainWindow, Task> test) => HeadlessApp.RunAsync(async () =>
    {
        var settingsFile = new SettingsStore().FilePath;
        var original = File.Exists(settingsFile) ? File.ReadAllBytes(settingsFile) : null;
        var accountsFile = Path.Combine(Path.GetTempPath(), $"ggcal-ui-accounts-{Guid.NewGuid():N}.json");
        var accounts = new GoogleAccountStore(accountsFile);
        accounts.AddOrReplace(FakeGoogle.Account, "refresh-token");
        MainWindow.GoogleFactory = () =>
        {
            var service = new GoogleSyncService(new CalendarStore(), accounts, new HttpClient(fake));
            service.Configure(new GoogleClientConfig("test.apps.googleusercontent.com", "secret"));
            return service;
        };
        var window = OpenApp();
        try
        {
            await WaitForAsync(() => !Vm(window).IsSyncing && Vm(window).Calendars.Any(c => c.Id == FakeGoogle.LocalCalendarId) ? Vm(window) : null, "la prima sincronizzazione");
            await test(window);
        }
        finally
        {
            var last = CurrentWindow ?? window;
            var store = new CalendarStore();
            foreach (var c in store.GetCalendars().Where(c => c.Id.StartsWith(FakeGoogle.Account + "|", StringComparison.Ordinal))) store.DeleteCalendar(c.Id);
            last.Close();
            MainWindow.GoogleFactory = null;
            if (original is not null) File.WriteAllBytes(settingsFile, original);
            if (File.Exists(accountsFile)) File.Delete(accountsFile);
        }
    });

    private static async Task SyncNowAsync(MainWindow window)
    {
        await WaitForQuietAsync(window);
        await SyncAsync(window);
    }

    private static MenuItem Item(ContextMenu menu, string automationId) =>
        menu.Items.OfType<MenuItem>().First(i => Avalonia.Automation.AutomationProperties.GetAutomationId(i) == automationId);

    private static void ClickItem(MenuItem item)
    {
        item.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>The menu of the calendar, opened.</summary>
    private static ContextMenu OpenCalendarMenu(MainWindow window, CalendarItemViewModel calendar)
    {
        var holder = window.GetVisualDescendants().OfType<Border>().First(b => b.DataContext == calendar && b.ContextMenu is not null);
        holder.ContextMenu!.DataContext = calendar; // the menu takes it from the control it opens on; headless, not always before the first read
        holder.ContextMenu.Open(holder);
        Dispatcher.UIThread.RunJobs();
        return holder.ContextMenu;
    }

    private static ContextMenu OpenAccountMenu(MainWindow window, AccountGroup group)
    {
        var header = window.GetVisualDescendants().OfType<Grid>().First(g => g.DataContext == group && g.ContextMenu is not null);
        header.ContextMenu!.DataContext = group;
        header.ContextMenu.Open(header);
        Dispatcher.UIThread.RunJobs();
        return header.ContextMenu;
    }

    private static CalendarItemViewModel Calendar(MainWindow window, string localId) => Vm(window).Calendars.First(c => c.Id == localId);

    // ---- T182: from the menu of the account ----

    [Fact]
    [Trait("TestCase", "T182")]
    public Task T182_NewCalendarFromTheMenuOfTheAccountAsksNameAndColourAndCreatesItOnGoogle() => InFakeGoogleAppAsync(new FakeGoogle(), async window =>
    {
        var vm = Vm(window);
        var group = vm.Accounts.First(a => a.AccountName == FakeGoogle.Account);
        var menu = OpenAccountMenu(window, group);
        Assert.True(Item(menu, "Menu.NewCalendarInAccount").IsEnabled);
        ClickItem(Item(menu, "Menu.NewCalendarInAccount"));

        var dialog = await WaitForAsync(() => window.OwnedWindows.OfType<GoogleCalendarWindow>().FirstOrDefault(), "la finestra del nuovo calendario");
        Assert.DoesNotContain(dialog.GetVisualDescendants().OfType<ComboBox>(), c => c.IsVisible); // the account was chosen by the menu
        Assert.False(FindButton(dialog, "Gcal.Create").IsEnabled); // no name yet
        ((TextBox)FindById(dialog, "Gcal.Name")).Text = "Palestra";
        Dispatcher.UIThread.RunJobs();
        Assert.True(FindButton(dialog, "Gcal.Create").IsEnabled);
        Click(FindButton(dialog, "Gcal.Color.2"));
        Click(FindButton(dialog, "Gcal.Create"));

        await WaitForAsync(() => vm.Calendars.FirstOrDefault(c => c.Name == "Palestra"), "il calendario creato");
        var created = vm.Calendars.First(c => c.Name == "Palestra");
        Assert.Equal(MainViewModel.CalendarPalette[2], created.ColorHex);
        Assert.True(created.IsGoogle);
        Assert.Equal(true, created.IsOwner);
        Assert.Equal(group.AccountName, created.AccountName);
        Assert.Equal("status.gcal_created", vm.Status!.Key);
    });

    [Fact]
    [Trait("TestCase", "T182")]
    public Task T182_TheCalendarIsOnTheFakeGoogleWithTheChosenColour()
    {
        var fake = new FakeGoogle();
        return InFakeGoogleAppAsync(fake, async window =>
        {
            var group = Vm(window).Accounts.First(a => a.AccountName == FakeGoogle.Account);
            ClickItem(Item(OpenAccountMenu(window, group), "Menu.NewCalendarInAccount"));
            var dialog = await WaitForAsync(() => window.OwnedWindows.OfType<GoogleCalendarWindow>().FirstOrDefault(), "la finestra del nuovo calendario");
            ((TextBox)FindById(dialog, "Gcal.Name")).Text = "Corso di nuoto";
            Dispatcher.UIThread.RunJobs();
            Click(FindButton(dialog, "Gcal.Color.3"));
            Click(FindButton(dialog, "Gcal.Create"));
            await WaitForAsync(() => Vm(window).Calendars.FirstOrDefault(c => c.Name == "Corso di nuoto"), "il calendario creato");
            var remote = Assert.Single(fake.CalendarIds, id => fake.CalendarSummary(id) == "Corso di nuoto");
            Assert.Equal(MainViewModel.CalendarPalette[3], fake.CalendarColor(remote));
        });
    }

    // ---- T183: from the Create menu ----

    [Fact]
    [Trait("TestCase", "T183")]
    public Task T183_NewGoogleCalendarFromTheCreateMenuWithOneAccountDoesNotAskTheAccount() => InFakeGoogleAppAsync(new FakeGoogle(), async window =>
    {
        var vm = Vm(window);
        Assert.True(vm.HasGoogleAccounts);
        var button = FindButton(window, "Main.Create");
        var item = ((MenuFlyout)button.Flyout!).Items.OfType<MenuItem>().First(i => Avalonia.Automation.AutomationProperties.GetAutomationId(i) == "Menu.CreateGoogleCalendar");
        item.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
        var dialog = await WaitForAsync(() => window.OwnedWindows.OfType<GoogleCalendarWindow>().FirstOrDefault(), "la finestra del nuovo calendario");
        Assert.DoesNotContain(dialog.GetVisualDescendants().OfType<ComboBox>(), c => c.IsVisible);
        ((TextBox)FindById(dialog, "Gcal.Name")).Text = "Dal menu Crea";
        Dispatcher.UIThread.RunJobs();
        Click(FindButton(dialog, "Gcal.Create"));
        await WaitForAsync(() => vm.Calendars.FirstOrDefault(c => c.Name == "Dal menu Crea"), "il calendario creato");
    });

    [Fact]
    [Trait("TestCase", "T183")]
    public Task T183_TheWindowAsksTheAccountOnlyWhenThereAreSeveralAndTheMenuDidNotChooseOne() => InFakeGoogleAppAsync(new FakeGoogle(), async window =>
    {
        string[] two = ["a@example.com", "b@example.com"];
        async Task<GoogleCalendarWindow> OpenAsync(IReadOnlyList<string> accounts, string? account)
        {
            var dialog = new GoogleCalendarWindow(accounts, account, "#1A73E8");
            _ = dialog.ShowDialog<GoogleCalendarRequest?>(window);
            return await WaitForAsync(() => window.OwnedWindows.OfType<GoogleCalendarWindow>().LastOrDefault(w => ReferenceEquals(w, dialog)), "la finestra");
        }

        var several = await OpenAsync(two, null);
        var combo = Assert.Single(several.GetVisualDescendants().OfType<ComboBox>());
        Assert.Equal(two, combo.ItemsSource!.Cast<string>().ToArray());
        Assert.Equal("a@example.com", combo.SelectedItem);
        several.Close(null);

        var chosen = await OpenAsync(two, "b@example.com"); // the menu of the account b
        Assert.DoesNotContain(chosen.GetVisualDescendants().OfType<ComboBox>(), c => c.IsVisible);
        ((TextBox)FindById(chosen, "Gcal.Name")).Text = "Per b";
        Dispatcher.UIThread.RunJobs();
        chosen.Close(null);
        await Task.CompletedTask;
    });

    [Fact]
    [Trait("TestCase", "T183")]
    public Task T183_NameIsRequiredAndAnEmptyOneKeepsCreateOff() => InFakeGoogleAppAsync(new FakeGoogle(), async window =>
    {
        var dialog = new GoogleCalendarWindow([FakeGoogle.Account], FakeGoogle.Account, "#1A73E8");
        var shown = dialog.ShowDialog<GoogleCalendarRequest?>(window);
        await WaitForAsync(() => window.OwnedWindows.OfType<GoogleCalendarWindow>().FirstOrDefault(), "la finestra");
        var name = (TextBox)FindById(dialog, "Gcal.Name");
        var create = FindButton(dialog, "Gcal.Create");
        Assert.False(create.IsEnabled);
        name.Text = "   ";
        Dispatcher.UIThread.RunJobs();
        Assert.False(create.IsEnabled);
        name.Text = "  Palestra  ";
        Dispatcher.UIThread.RunJobs();
        Assert.True(create.IsEnabled);
        Click(create);
        var request = await shown;
        Assert.Equal(new GoogleCalendarRequest(FakeGoogle.Account, "Palestra", "#1A73E8"), request);
    });

    [Fact]
    [Trait("TestCase", "T183")]
    public Task T183_CancelCreatesNothing() => InFakeGoogleAppAsync(new FakeGoogle(), async window =>
    {
        var before = Vm(window).Calendars.Count;
        ClickItem(Item(OpenAccountMenu(window, Vm(window).Accounts.First(a => a.AccountName == FakeGoogle.Account)), "Menu.NewCalendarInAccount"));
        var dialog = await WaitForAsync(() => window.OwnedWindows.OfType<GoogleCalendarWindow>().FirstOrDefault(), "la finestra");
        ((TextBox)FindById(dialog, "Gcal.Name")).Text = "Annullato";
        Dispatcher.UIThread.RunJobs();
        Click(FindButton(dialog, "Dialog.Cancel"));
        await WaitForAsync(() => window.OwnedWindows.Count == 0 ? window : null, "la chiusura");
        Assert.Equal(before, Vm(window).Calendars.Count);
    });

    [Fact]
    [Trait("TestCase", "T183")]
    public void TheCreateItemIsGreyWithoutGoogleAccounts()
    {
        var store = new CalendarStore(":memory:");
        var vm = new MainViewModel(store);
        Assert.False(vm.HasGoogleAccounts);
        store.UpsertCalendar(new CalendarInfo("me@example.com|me@example.com", "me@example.com", "Principale", "#1A73E8", IsOwner: true));
        vm.Reload();
        Assert.True(vm.HasGoogleAccounts);
    }

    // ---- T184: errors ----

    [Fact]
    [Trait("TestCase", "T184")]
    public Task T184_WhenGoogleCannotBeReachedNothingIsCreatedAndTheStatusSaysWhy()
    {
        var fake = new FakeGoogle();
        return InFakeGoogleAppAsync(fake, async window =>
        {
            var vm = Vm(window);
            var before = vm.Calendars.Count;
            fake.Offline = true;
            ClickItem(Item(OpenAccountMenu(window, vm.Accounts.First(a => a.AccountName == FakeGoogle.Account)), "Menu.NewCalendarInAccount"));
            var dialog = await WaitForAsync(() => window.OwnedWindows.OfType<GoogleCalendarWindow>().FirstOrDefault(), "la finestra");
            ((TextBox)FindById(dialog, "Gcal.Name")).Text = "Senza rete";
            Dispatcher.UIThread.RunJobs();
            Click(FindButton(dialog, "Gcal.Create"));
            await WaitForAsync(() => vm.Status?.Key == "error.gcal_create" ? vm : null, "il messaggio di errore");
            Assert.True(vm.SyncIsError);
            Assert.Equal(L.T("error.gcal_create", L.T("gcal.problem.NoNetwork")), vm.SyncStatus);
            Assert.Equal(before, vm.Calendars.Count);
            Assert.True(window.IsEnabled); // the window is usable again
        });
    }

    // ---- T185..T189, T195: deleting ----

    [Fact]
    [Trait("TestCase", "T188")]
    public Task T188_DeleteIsGreyForThePrimaryCalendarAndForThoseTheYouDoNotOwnWithTheReasonAsTooltip()
    {
        var fake = new FakeGoogle();
        var holidays = fake.AddCalendar("Festività in Italia", role: "reader");
        var mine = fake.AddCalendar("Mio");
        return InFakeGoogleAppAsync(fake, async window =>
        {
            await SyncNowAsync(window);
            var vm = Vm(window);

            // A calendar whose role is not known yet (before the first sync of this version).
            var store = new CalendarStore();
            store.UpsertCalendar(new CalendarInfo(Local("sconosciuto@group.calendar.google.com"), FakeGoogle.Account, "Sconosciuto", "#123456"));
            vm.Reload();
            await PauseAsync(TimeSpan.FromMilliseconds(100)); // the list is drawn again
            var unknown = Calendar(window, Local("sconosciuto@group.calendar.google.com"));
            Assert.Null(unknown.IsOwner);

            void Check(string id, bool enabled, string hint)
            {
                var calendar = Calendar(window, id);
                var menu = OpenCalendarMenu(window, calendar);
                var item = Item(menu, "Menu.DeleteGoogleCalendar");
                Assert.True(item.IsVisible);
                Assert.Equal(enabled, item.IsEnabled);
                Assert.Equal(enabled, calendar.CanDeleteFromGoogle);
                Assert.Equal(hint, calendar.DeleteHint);
                Assert.True(ToolTip.GetShowOnDisabled(item)); // a grey item shows its tooltip only when asked to
                if (!enabled) Assert.Equal(hint, ToolTip.GetTip(item) as string);
                menu.Close();
            }

            Check(FakeGoogle.LocalCalendarId, false, L.T("gcal.hint_primary"));
            Check(Local(holidays), false, L.T("gcal.hint_not_owner"));
            Check(unknown.Id, false, L.T("gcal.hint_unknown"));
            Check(Local(mine), true, "");

            // Clicking a grey item does nothing.
            var primaryMenu = OpenCalendarMenu(window, Calendar(window, FakeGoogle.LocalCalendarId));
            ClickItem(Item(primaryMenu, "Menu.DeleteGoogleCalendar"));
            await PauseAsync(TimeSpan.FromMilliseconds(200));
            Assert.Empty(window.OwnedWindows.OfType<ConfirmWindow>());
            Assert.Empty(fake.DeletedCalendars);
        });
    }

    [Fact]
    [Trait("TestCase", "T185")]
    public Task T185_DeleteAsksForConfirmationAndCancelKeepsEverything()
    {
        var fake = new FakeGoogle();
        var mine = fake.AddCalendar("Lavoro");
        for (var i = 0; i < 3; i++) fake.SeedIn(mine, "Evento " + i, new DateTime(2026, 11, 1).AddHours(i));
        return InFakeGoogleAppAsync(fake, async window =>
        {
            await SyncNowAsync(window);
            var vm = Vm(window);
            ClickItem(Item(OpenCalendarMenu(window, Calendar(window, Local(mine))), "Menu.DeleteGoogleCalendar"));
            var confirm = await WaitForAsync(() => window.OwnedWindows.OfType<ConfirmWindow>().FirstOrDefault(), "la conferma");
            var texts = confirm.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text ?? "").ToList();
            Assert.Contains(L.T("gcal.delete_title"), texts.Concat([confirm.Title ?? ""]));
            Assert.Contains(texts, t => t.Contains("Lavoro") && t.Contains("3") && t.Contains(FakeGoogle.Account));
            Click(FindButton(confirm, "Dialog.Cancel"));
            await WaitForAsync(() => window.OwnedWindows.Count == 0 ? window : null, "la chiusura");
            Assert.Empty(fake.DeletedCalendars);
            Assert.Contains(vm.Calendars, c => c.Id == Local(mine));
        });
    }

    [Fact]
    [Trait("TestCase", "T186")]
    public Task T185_ConfirmedDeleteRemovesTheCalendarOnGoogleAndHereWithOneRequest()
    {
        var fake = new FakeGoogle();
        var mine = fake.AddCalendar("Lavoro");
        for (var i = 0; i < 3; i++) fake.SeedIn(mine, "Evento " + i, new DateTime(2026, 11, 1).AddHours(i));
        return InFakeGoogleAppAsync(fake, async window =>
        {
            await SyncNowAsync(window);
            var vm = Vm(window);
            // A change waiting for this calendar: the confirmation says it is lost.
            vm.AddEvent(new CalendarEvent("waiting", Local(mine), "Da inviare", new DateTime(2026, 12, 1, 9, 0, 0), new DateTime(2026, 12, 1, 10, 0, 0)) { Reminders = [] });
            Assert.Equal(1, vm.PendingChangesFor(Local(mine)));

            fake.Requests.Clear();
            ClickItem(Item(OpenCalendarMenu(window, Calendar(window, Local(mine))), "Menu.DeleteGoogleCalendar"));
            var confirm = await WaitForAsync(() => window.OwnedWindows.OfType<ConfirmWindow>().FirstOrDefault(), "la conferma");
            Assert.Contains(confirm.GetVisualDescendants().OfType<TextBlock>(), t => (t.Text ?? "").Contains(L.N("gcal.delete_pending", 1)));
            Click(FindButton(confirm, "Dialog.Ok"));

            await WaitForAsync(() => vm.Calendars.All(c => c.Id != Local(mine)) ? vm : null, "la scomparsa del calendario");
            Assert.Equal([mine], fake.DeletedCalendars);
            Assert.Single(fake.Requests.Where(r => r.StartsWith("DELETE ")));
            Assert.DoesNotContain(vm.AllEvents, e => e.CalendarId == Local(mine));
            Assert.Equal(0, new CalendarStore().GetPending().Count(p => p.CalendarId == Local(mine)));
            Assert.Equal("status.gcal_deleted", vm.Status!.Key);
        });
    }

    [Fact]
    [Trait("TestCase", "T186")]
    public Task T185_WhenGoogleRefusesTheCalendarStaysAndTheStatusSaysWhy()
    {
        var fake = new FakeGoogle();
        var mine = fake.AddCalendar("Lavoro");
        return InFakeGoogleAppAsync(fake, async window =>
        {
            await SyncNowAsync(window);
            var vm = Vm(window);
            fake.NextWriteFails = (System.Net.HttpStatusCode.Forbidden, "rateLimitExceeded");
            ClickItem(Item(OpenCalendarMenu(window, Calendar(window, Local(mine))), "Menu.DeleteGoogleCalendar"));
            var confirm = await WaitForAsync(() => window.OwnedWindows.OfType<ConfirmWindow>().FirstOrDefault(), "la conferma");
            Click(FindButton(confirm, "Dialog.Ok"));
            await WaitForAsync(() => vm.Status?.Key == "error.gcal_delete" ? vm : null, "il messaggio");
            Assert.Equal(L.T("error.gcal_delete", L.T("gcal.problem.RateLimited")), vm.SyncStatus);
            Assert.Contains(vm.Calendars, c => c.Id == Local(mine));
            Assert.True(fake.HasCalendar(mine));
        });
    }

    // ---- the local group keeps its own meaning ----

    [Fact]
    [Trait("TestCase", "T182")]
    public Task T182_TheMenuOfTheLocalAccountMakesALocalCalendar() => InFakeGoogleAppAsync(new FakeGoogle(), async window =>
    {
        var vm = Vm(window);
        var local = vm.Accounts.First(a => a.Calendars.All(c => c.IsLocal));
        ClickItem(Item(OpenAccountMenu(window, local), "Menu.NewCalendarInAccount"));
        var prompt = await WaitForAsync(() => window.OwnedWindows.OfType<PromptWindow>().FirstOrDefault(), "la richiesta del nome");
        prompt.GetVisualDescendants().OfType<TextBox>().First().Text = TestPrefix + " locale";
        Click(FindButton(prompt, "Dialog.Ok"));
        await WaitForAsync(() => vm.Calendars.FirstOrDefault(c => c.Name == TestPrefix + " locale"), "il calendario locale");
        Assert.True(vm.Calendars.First(c => c.Name == TestPrefix + " locale").IsLocal);
        vm.DeleteLocalCalendar(vm.Calendars.First(c => c.Name == TestPrefix + " locale").Id);
    });
}
