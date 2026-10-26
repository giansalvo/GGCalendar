using System.Net;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GGCalendar.App.Controls;
using GGCalendar.App.Services;
using GGCalendar.App.ViewModels;
using GGCalendar.App.Views;
using GGCalendar.Core.Localization;
using GGCalendar.Core.Models;
using GGCalendar.Core.Subscriptions;
using GGCalendar.Core.Tests.Support;
using GGCalendar.Data;
using static GGCalendar.Core.Tests.Support.AppDriver;

namespace GGCalendar.Core.Tests;

/// <summary>
/// T154-T158, T161, T164: the public calendars on the real app (no Google account, no network: a fake site answers). Where to add one, the
/// window, the menus, the read-only calendar and the messages.
/// </summary>
[Collection(CollectionName)]
public class SubscriptionUiTests
{
    private const string Url = "https://calendar.example.com/public.ics";

    private static string Ics(string title, DateTime day, string uid = "u1") =>
        "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nX-WR-CALNAME:Pubblico di prova\r\n" +
        $"BEGIN:VEVENT\r\nUID:{uid}\r\nSUMMARY:{title}\r\nDTSTART:{day:yyyyMMdd}T100000\r\nDTEND:{day:yyyyMMdd}T110000\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n";

    /// <summary>Runs on the app with a fake site behind the addresses; the subscriptions and the settings are put back as they were.</summary>
    private static FakeSite? LastSite;

    private static Task InSubscribedAppAsync(FakeSite site, Func<MainWindow, Task> test) => HeadlessApp.RunAsync(async () =>
    {
        LastSite = site;
        var settingsFile = new SettingsStore().FilePath;
        var original = File.Exists(settingsFile) ? File.ReadAllBytes(settingsFile) : null;
        var previousFactory = SubscriptionService.FetcherFactory;
        SubscriptionService.FetcherFactory = () => new IcsFetcher(site);
        var window = OpenApp();
        try
        {
            RemoveAllSubscriptions(window);
            await test(window);
        }
        finally
        {
            var last = CurrentWindow ?? window;
            RemoveAllSubscriptions(last);
            last.Close();
            SubscriptionService.FetcherFactory = previousFactory;
            if (original is not null) File.WriteAllBytes(settingsFile, original);
        }
    });

    private static void RemoveAllSubscriptions(MainWindow window)
    {
        foreach (var s in new CalendarStore().GetSubscriptions()) window.Subscriptions.Remove(s.CalendarId);
        Vm(window).Reload();
        foreach (var cal in Vm(window).Calendars.Where(c => c.IsLocal && c.Name.StartsWith(TestPrefix, StringComparison.Ordinal)).ToList())
            Vm(window).DeleteLocalCalendar(cal.Id);
    }

    private static async Task<SubscriptionWindow> OpenAddWindowAsync(MainWindow window)
    {
        ChooseCreateMenuItem(window, "Menu.CreatePublicCalendar");
        return await WaitForAsync(() => window.OwnedWindows.OfType<SubscriptionWindow>().FirstOrDefault(), "la finestra del calendario pubblico");
    }

    private static async Task CheckAsync(SubscriptionWindow dialog, string url)
    {
        ((TextBox)FindById(dialog, "Sub.Url")).Text = url;
        Click(FindButton(dialog, "Sub.Check"));
        await WaitForAsync(() => !FindById(dialog, "Sub.Progress").IsVisible ? dialog : null, "la fine della lettura");
    }

    private static async Task<CalendarItemViewModel> SubscribeAsync(MainWindow window, string url = Url, string? name = null)
    {
        var dialog = await OpenAddWindowAsync(window);
        await CheckAsync(dialog, url);
        if (name is not null) ((TextBox)FindById(dialog, "Sub.Name")).Text = name;
        Click(FindButton(dialog, "Sub.Add"));
        await WaitForAsync(() => window.OwnedWindows.Count == 0 ? window : null, "la chiusura della finestra");
        return await WaitForAsync(() => Vm(window).Calendars.FirstOrDefault(c => c.IsSubscription), "il calendario abbonato");
    }

    private static string ErrorOf(SubscriptionWindow dialog) =>
        FindById(dialog, "Sub.Error") is { IsVisible: true } box ? ((SelectableTextBlock)box).Text ?? "" : "";

    // ---- T154: where it is added ----

    [Fact]
    [Trait("TestCase", "T154")]
    public Task T154_TheCreateMenuHasThePublicCalendarAndTheGroupAppearsWithTheFirstSubscription() => InSubscribedAppAsync(
        new FakeSite().Serve("/public.ics", Ics("Lezione", DateTime.Today.AddDays(1))), async window =>
    {
        var vm = Vm(window);
        Assert.DoesNotContain(vm.Accounts, a => a.IsSubscriptions); // no subscription: no group

        var dialog = await OpenAddWindowAsync(window);
        Assert.NotNull(dialog);
        Click(FindButton(dialog, "Dialog.Cancel"));
        await WaitForAsync(() => window.OwnedWindows.Count == 0 ? window : null, "la chiusura della finestra");

        await SubscribeAsync(window);
        var group = Assert.Single(vm.Accounts, a => a.IsSubscriptions);
        Assert.Equal(vm.Accounts.Count - 1, vm.Accounts.IndexOf(group)); // the last group
        Assert.Equal(L.T("main.public_calendars"), group.DisplayName);

        // The menu of the header of the group offers the same entry.
        var header = window.GetVisualDescendants().OfType<Grid>().First(g => g.DataContext == group && g.ContextMenu is not null);
        header.ContextMenu!.Open(header);
        Dispatcher.UIThread.RunJobs();
        var ids = header.ContextMenu.Items.OfType<MenuItem>().Where(i => i.IsVisible)
            .Select(i => Avalonia.Automation.AutomationProperties.GetAutomationId(i)).ToList();
        header.ContextMenu.Close();
        Assert.Contains("Menu.AddPublicCalendar", ids);
    });

    // ---- T155: the window ----

    [Fact]
    [Trait("TestCase", "T155")]
    public Task T155_AddIsOffOnlyWithAPreviewAnAddressRefusedShowsWhyAndCancelStopsTheRequest() => InSubscribedAppAsync(
        new FakeSite().Serve("/public.ics", Ics("Lezione", DateTime.Today.AddDays(1)))
            .Route("/slow", async (_, ct) => { await Task.Delay(Timeout.Infinite, ct); return new HttpResponseMessage(HttpStatusCode.OK); }),
        async window =>
    {
        var dialog = await OpenAddWindowAsync(window);
        var add = FindButton(dialog, "Sub.Add");
        Assert.False(add.IsEnabled);

        // An address that is refused: the reason is in the window, which stays open.
        await CheckAsync(dialog, "http://calendar.example.com/public.ics");
        Assert.Equal(SubscriptionMessages.Error(SubscriptionProblem.NotHttps).Text, ErrorOf(dialog));
        Assert.False(add.IsEnabled);
        Assert.Contains(dialog, window.OwnedWindows);

        // The paste button puts the text of the clipboard in the box.
        await Avalonia.Input.Platform.ClipboardExtensions.SetTextAsync(dialog.Clipboard!, " webcal://calendar.example.com/public.ics ");
        Click(FindButton(dialog, "Sub.Paste"));
        await WaitForAsync(() => ((TextBox)FindById(dialog, "Sub.Url")).Text == "webcal://calendar.example.com/public.ics" ? dialog : null, "il testo incollato");
        Click(FindButton(dialog, "Sub.Check"));
        await WaitForAsync(() => add.IsEnabled ? dialog : null, "l'anteprima");
        Assert.Equal("", ErrorOf(dialog));
        Assert.Equal("Pubblico di prova", ((TextBox)FindById(dialog, "Sub.Name")).Text);
        Assert.Equal(L.N("sub.preview_events", 1), ((SelectableTextBlock)FindById(dialog, "Sub.Preview")).Text!.Split(" · ")[0]);

        // Changing the address makes the preview obsolete.
        ((TextBox)FindById(dialog, "Sub.Url")).Text = Url + "x";
        Dispatcher.UIThread.RunJobs();
        Assert.False(add.IsEnabled);

        // While the calendar is read, Cancel stops the request and the window stays open.
        ((TextBox)FindById(dialog, "Sub.Url")).Text = "https://calendar.example.com/slow";
        Click(FindButton(dialog, "Sub.Check"));
        Assert.True(FindById(dialog, "Sub.Progress").IsVisible);
        Assert.Equal(L.T("sub.reading"), ((TextBlock)FindById(dialog, "Sub.Progress")).Text);
        Click(FindButton(dialog, "Dialog.Cancel"));
        await WaitForAsync(() => !FindById(dialog, "Sub.Progress").IsVisible ? dialog : null, "la fine della lettura annullata");
        Assert.Contains(dialog, window.OwnedWindows);
        Assert.Equal("", ErrorOf(dialog));

        Click(FindButton(dialog, "Dialog.Cancel")); // now it closes
        await WaitForAsync(() => window.OwnedWindows.Count == 0 ? window : null, "la chiusura della finestra");
        Assert.Empty(new CalendarStore().GetSubscriptions());
    });

    [Fact]
    [Trait("TestCase", "T103")]
    public Task T103_NameColourAndDaysChosenInTheWindowAreUsedAndTheCountFollows() => InSubscribedAppAsync(
        new FakeSite().Serve("/public.ics",
            Ics("Vicino", DateTime.Today.AddDays(2), "a").Replace("END:VCALENDAR\r\n", "") +
            "BEGIN:VEVENT\r\nUID:b\r\nSUMMARY:Lontano\r\n" + $"DTSTART:{DateTime.Today.AddDays(200):yyyyMMdd}T100000\r\nDTEND:{DateTime.Today.AddDays(200):yyyyMMdd}T110000\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n"),
        async window =>
    {
        var dialog = await OpenAddWindowAsync(window);
        await CheckAsync(dialog, Url);
        var preview = (SelectableTextBlock)FindById(dialog, "Sub.Preview");
        Assert.StartsWith(L.N("sub.preview_events", 2), preview.Text);

        ((CalendarDatePicker)FindById(dialog, "Sub.To")).SelectedDate = DateTime.Today.AddDays(30);
        Dispatcher.UIThread.RunJobs();
        Assert.StartsWith(L.N("sub.preview_events", 1), preview.Text); // the number follows the choice

        ((CalendarDatePicker)FindById(dialog, "Sub.From")).SelectedDate = DateTime.Today.AddDays(40); // from after to
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(L.T("sub.range_invalid"), ErrorOf(dialog));
        Assert.False(FindButton(dialog, "Sub.Add").IsEnabled);
        ((CalendarDatePicker)FindById(dialog, "Sub.From")).SelectedDate = DateTime.Today;
        Dispatcher.UIThread.RunJobs();

        ((TextBox)FindById(dialog, "Sub.Name")).Text = "Il mio pubblico";
        Click(FindButton(dialog, "Sub.Color.2"));
        Click(FindButton(dialog, "Sub.Add"));
        await WaitForAsync(() => window.OwnedWindows.Count == 0 ? window : null, "la chiusura della finestra");

        var calendar = await WaitForAsync(() => Vm(window).Calendars.FirstOrDefault(c => c.IsSubscription), "il calendario abbonato");
        Assert.Equal("Il mio pubblico", calendar.Name);
        Assert.Equal(MainViewModel.CalendarPalette[2], calendar.ColorHex);
        Assert.Equal(["Vicino"], Vm(window).AllEvents.Where(e => e.CalendarId == calendar.Id).Select(e => e.Title));
        // (The message of the status bar can be replaced at once by another one: a refresh, a sync. It is checked in the unit tests of the messages.)
        Assert.Equal(L.N("status.sub_added", 1, "Il mio pubblico"), Msg.Count("status.sub_added", 1, "Il mio pubblico").Text);
    });

    // ---- T158: the menu of a calendar ----

    private static async Task<List<string>> MenuOfAsync(MainWindow window, CalendarItemViewModel calendar)
    {
        // The list is drawn again after a reload: the line of the calendar may not be there yet.
        var holder = await WaitForAsync(() => window.GetVisualDescendants().OfType<Border>().FirstOrDefault(b => b.DataContext is CalendarItemViewModel c && c.Id == calendar.Id && b.ContextMenu is not null), "la riga del calendario"); // by id: a reload makes new items
        // The menu takes its data context from the control it opens on; headless, that does not always happen before the first read.
        holder.ContextMenu!.DataContext = holder.DataContext;
        holder.ContextMenu.Open(holder);
        List<string> Read() => holder.ContextMenu.Items.OfType<MenuItem>().Where(i => i.IsVisible)
            .Select(i => Avalonia.Automation.AutomationProperties.GetAutomationId(i) is { Length: > 0 } id ? id : i.Header?.ToString() ?? "").ToList();

        // The bindings of the items are applied a moment after the menu opens: read until the list stops changing.
        var last = Read();
        for (var stable = 0; stable < 4;)
        {
            await PauseAsync(TimeSpan.FromMilliseconds(50));
            var now = Read();
            stable = now.SequenceEqual(last) ? stable + 1 : 0;
            last = now;
        }

        holder.ContextMenu.Close();
        await WaitForAsync(() => holder.ContextMenu.IsOpen ? null : holder, "la chiusura del menu");
        await PauseAsync(TimeSpan.FromMilliseconds(150));
        return last;
    }

    [Fact]
    [Trait("TestCase", "T158")]
    public Task T158_ASubscribedCalendarHasItsOwnMenuAndNoImportExportOrDelete() => InSubscribedAppAsync(
        new FakeSite().Serve("/public.ics", Ics("Lezione", DateTime.Today.AddDays(1))), async window =>
    {
        await SubscribeAsync(window);
        await PauseAsync(TimeSpan.FromMilliseconds(100)); // the list is drawn again after the reload
        var subscribed = Vm(window).Calendars.First(c => c.IsSubscription);
        var menu = await MenuOfAsync(window, subscribed);
        Assert.Equal(["Menu.SubRefresh", "Menu.SubEdit", "Menu.SubShowUrl", "Menu.SubRemove"], menu);
        var holder = await WaitForAsync(() => window.GetVisualDescendants().OfType<Border>().FirstOrDefault(b => b.DataContext is CalendarItemViewModel c && c.Id == subscribed.Id && b.ContextMenu is not null), "la riga");
        Assert.Equal(L.T("main.edit_calendar"), holder.ContextMenu!.Items.OfType<MenuItem>().First(i => Avalonia.Automation.AutomationProperties.GetAutomationId(i) == "Menu.SubEdit").Header); // T210: the same words as for the other calendars

        var local = Vm(window).Calendars.First(c => c.IsLocal);
        var localMenu = await MenuOfAsync(window, local);
        Assert.Equal(["Menu.EditCalendar", L.T("main.import_here"), L.T("main.export_calendar"), L.T("main.delete_calendar")], localMenu);
    });

    [Fact]
    [Trait("TestCase", "T107")]
    public Task T107_TheAddressIsShownOnlyOnRequestAndRemovingDeletesTheCalendar() => InSubscribedAppAsync(
        new FakeSite().Serve("/public.ics", Ics("Lezione", DateTime.Today.AddDays(1))), async window =>
    {
        var subscribed = await SubscribeAsync(window);
        Assert.DoesNotContain(Url, subscribed.Name);

        var holder = window.GetVisualDescendants().OfType<Border>().First(b => b.DataContext == subscribed && b.ContextMenu is not null);
        holder.ContextMenu!.Open(holder);
        Dispatcher.UIThread.RunJobs();
        holder.ContextMenu.Items.OfType<MenuItem>().First(i => Avalonia.Automation.AutomationProperties.GetAutomationId(i) == "Menu.SubShowUrl")
            .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
        var shown = await WaitForAsync(() => window.OwnedWindows.OfType<SubscriptionUrlWindow>().FirstOrDefault(), "la finestra con l'indirizzo");
        Assert.Equal(Url, ((TextBox)FindById(shown, "SubUrl.Text")).Text);
        Click(FindButton(shown, "Dialog.Cancel"));
        await WaitForAsync(() => window.OwnedWindows.Count == 0 ? window : null, "la chiusura");

        // The database holds it only encrypted.
        Assert.DoesNotContain("calendar.example.com", new CalendarStore().GetSubscription(subscribed.Id)!.EncryptedUrl);

        holder.ContextMenu.Items.OfType<MenuItem>().First(i => Avalonia.Automation.AutomationProperties.GetAutomationId(i) == "Menu.SubRemove")
            .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
        var confirm = await WaitForAsync(() => window.OwnedWindows.OfType<ConfirmWindow>().FirstOrDefault(), "la conferma");
        Click(FindButton(confirm, "Dialog.Ok"));
        await WaitForAsync(() => window.OwnedWindows.Count == 0 && Vm(window).Calendars.All(c => !c.IsSubscription) ? window : null, "la rimozione");
        Assert.Empty(new CalendarStore().GetSubscriptions());
        Assert.DoesNotContain(new CalendarStore().GetAllEvents(), e => e.CalendarId == subscribed.Id);
        Assert.DoesNotContain(Vm(window).Accounts, a => a.IsSubscriptions);
    });

    // ---- T161, T106: read-only ----

    [Fact]
    [Trait("TestCase", "T161")]
    public Task T161_EventsOfASubscribedCalendarCannotBeChangedAndNothingGoesToGoogle() => InSubscribedAppAsync(
        new FakeSite().Serve("/public.ics", Ics("Pubblico lezione", DateTime.Today.AddDays(1))), async window =>
    {
        var vm = Vm(window);
        var subscribed = await SubscribeAsync(window);
        var ev = vm.AllEvents.Single(e => e.CalendarId == subscribed.Id);
        Assert.Empty(ev.Reminders); // no reminders for these events
        var store = new CalendarStore();

        // The model refuses every change, in this calendar and towards it.
        vm.UpdateEvent(ev with { Title = "Cambiato" });
        vm.DeleteEvent(ev);
        vm.AddEvent(ev with { Id = "new", Title = "Nuovo" });
        vm.MoveEvent(ev, ev.Start.AddHours(2), ev.End.AddHours(2));
        Assert.Equal("Pubblico lezione", Assert.Single(store.GetAllEvents(), e => e.CalendarId == subscribed.Id).Title);
        var local = vm.Calendars.First(c => c.IsLocal);
        var mine = new CalendarEvent("mine", local.Id, "Mio", DateTime.Today.AddHours(9), DateTime.Today.AddHours(10));
        vm.AddEvent(mine);
        vm.ChangeCalendar(mine, subscribed.Id);
        Assert.Equal(local.Id, store.GetEvent("mine")!.CalendarId);
        vm.DeleteEvent(mine);
        Assert.Empty(store.GetPending());
        Assert.False(GGCalendar.App.Services.GoogleSyncService.IsGoogleCalendar(subscribed.Id));

        // A new event never goes to a public calendar, not even when it is the only one visible.
        foreach (var c in vm.Calendars.Where(c => !c.IsSubscription)) c.IsVisible = false;
        Assert.NotEqual(subscribed.Id, vm.NewEvent(DateTime.Today.AddHours(9), DateTime.Today.AddHours(10)).CalendarId);
        foreach (var c in vm.Calendars.Where(c => !c.IsSubscription)) c.IsVisible = true;

        // The grid never drags it.
        Assert.True(window.FindControl<WeekGrid>("Grid")!.IsReadOnly!(ev));

        // The card of the event has no Edit and no Delete (only Close), and a double click opens no editor.
        // A click can be lost while the list is still being drawn: the line is found and clicked again until the card is there.
        EventDetailsCard? card = null;
        for (var attempt = 0; attempt < 6 && card is null; attempt++)
        {
            var item = await FindInAgendaAsync(window, "Pubblico lezione", DateTime.Today.AddDays(1));
            var center = item.TranslatePoint(new Point(item.Bounds.Width / 2, item.Bounds.Height / 2), window)!.Value;
            window.MouseDown(center, MouseButton.Left);
            window.MouseUp(center, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            await PauseAsync(TimeSpan.FromMilliseconds(200));
            card = window.FindControl<Avalonia.Controls.Primitives.Popup>("DetailsPopup")?.Child as EventDetailsCard;
        }

        Assert.NotNull(card);
        Assert.Single(card.GetVisualDescendants().OfType<Button>());
        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        DoubleClick(window, await FindInAgendaAsync(window, "Pubblico lezione", DateTime.Today.AddDays(1))); // the line is found again: the list may have been drawn anew
        await PauseAsync(TimeSpan.FromMilliseconds(300));
        Assert.Empty(window.OwnedWindows.OfType<EventEditWindow>());

        // The editor of a new event does not offer the calendar.
        var editor = new EventEditViewModel(vm.NewEvent(DateTime.Today.AddHours(9), DateTime.Today.AddHours(10)), vm.Calendars.Where(c => !c.IsSubscription).ToList(), true, vm.Settings);
        Assert.DoesNotContain(editor.Calendars, c => c.IsSubscription);
    });

    [Fact]
    [Trait("TestCase", "T161")]
    public Task T161_ImportIntoASubscribedCalendarDoesNothing() => InSubscribedAppAsync(
        new FakeSite().Serve("/public.ics", Ics("Lezione", DateTime.Today.AddDays(1))), async window =>
    {
        var subscribed = await SubscribeAsync(window);
        var imported = Vm(window).ImportEvents(subscribed.Id,
            [new CalendarEvent("x", subscribed.Id, "Importato", DateTime.Today, DateTime.Today.AddHours(1))], ImportDuplicates.Copy);
        Assert.Equal((0, 0), imported);
        Assert.DoesNotContain(new CalendarStore().GetAllEvents(), e => e.Title == "Importato");
    });


    // ---- T159, T160: refresh interval and refresh at start ----

    private static async Task<SettingsWindow> OpenSettingsAsync(MainWindow window)
    {
        Click(FindButton(window, "Main.Settings"));
        return await WaitForAsync(() => window.OwnedWindows.OfType<SettingsWindow>().FirstOrDefault(), "la finestra delle impostazioni");
    }

    [Fact]
    [Trait("TestCase", "T159")]
    public Task T159_TheIntervalIsChosenInTheSettingsAndKeptAndRefreshNowRefreshesEverything() => InSubscribedAppAsync(
        new FakeSite().Serve("/public.ics", Ics("Lezione", DateTime.Today.AddDays(1))), async window =>
    {
        await SubscribeAsync(window);
        var dialog = await OpenSettingsAsync(window);
        var combo = (ComboBox)FindById(dialog, "Settings.SubscriptionIntervalIndex");
        var labels = combo.ItemsSource!.Cast<string>().ToList();
        Assert.Equal([L.T("settings.sub_never"), L.T("settings.sub_every_minutes", 15), L.T("settings.sub_every_minutes", 30), L.N("settings.sub_every_hour", 1),
            L.N("settings.sub_every_hour", 2), L.N("settings.sub_every_hour", 6), L.N("settings.sub_every_hour", 12), L.N("settings.sub_every_hour", 24)], labels);
        Assert.Equal(30, SettingsViewModel.SubscriptionIntervalOptions[combo.SelectedIndex]); // the default
        Assert.Equal(30, new GGCalendar.Core.Models.AppSettings().SubscriptionIntervalMinutes);

        // Refresh now: a new version of the file is downloaded at once.
        var site = LastSite!;
        var before = site.Requests.Count;
        var refreshNow = FindButton(dialog, "Settings.SubRefreshNow");
        Assert.True(refreshNow.IsEnabled);
        refreshNow.Command!.Execute(null);
        await WaitForAsync(() => ((TextBlock)FindById(dialog, "Settings.SubStatus")).Text == L.T("settings.sub_done", 1, 0) ? dialog : null, "la fine dell'aggiornamento");
        Assert.Equal(before + 1, site.Requests.Count);

        combo.SelectedIndex = Array.IndexOf(SettingsViewModel.SubscriptionIntervalOptions, 360);
        Dispatcher.UIThread.RunJobs();
        Click(FindButton(dialog, "Settings.Save"));
        await WaitForAsync(() => window.OwnedWindows.Count == 0 ? window : null, "la chiusura delle impostazioni");
        await WaitForAsync(() => new SettingsStore().Load().SubscriptionIntervalMinutes == 360 ? window : null, "il salvataggio delle impostazioni");
        window = await ReopenAsync(window);
        Assert.Equal(360, Vm(window).Settings.SubscriptionIntervalMinutes);
    });

    [Fact]
    [Trait("TestCase", "T160")]
    public Task T160_AtStartOnlyTheSubscriptionsOlderThanTheIntervalAreRefreshed() => HeadlessApp.RunAsync(async () =>
    {
        var settingsFile = new SettingsStore().FilePath;
        var original = File.Exists(settingsFile) ? File.ReadAllBytes(settingsFile) : null;
        var site = new FakeSite().Serve("/old.ics", Ics("Vecchio", DateTime.Today.AddDays(1), "old")).Serve("/new.ics", Ics("Nuovo", DateTime.Today.AddDays(1), "new"));
        var previous = SubscriptionService.FetcherFactory;
        SubscriptionService.FetcherFactory = () => new IcsFetcher(site);
        MainWindow? window = null;
        try
        {
            var store = new CalendarStore();
            foreach (var s in store.GetSubscriptions()) store.DeleteCalendar(s.CalendarId);
            var asOld = new SubscriptionService(store, new IcsFetcher(site), () => DateTime.UtcNow.AddHours(-2));
            var asNow = new SubscriptionService(store, new IcsFetcher(site));
            Assert.Null((asOld.Add((await asOld.PreviewAsync("https://calendar.example.com/old.ics")).Preview!, "Vecchio", "#123456", SubscriptionRange.All)).Problem);
            Assert.Null((asNow.Add((await asNow.PreviewAsync("https://calendar.example.com/new.ics")).Preview!, "Nuovo", "#654321", SubscriptionRange.All)).Problem);
            site.Requests.Clear();

            window = OpenApp(); // the interval is 30 minutes
            await WaitForAsync(() => site.Requests.Count > 0 ? site : null, "l'aggiornamento all'avvio");
            await PauseAsync(TimeSpan.FromMilliseconds(500));
            Assert.Equal(["/old.ics"], site.Requests.Select(r => r.AbsolutePath));
            var fresh = store.GetSubscriptions().ToDictionary(r => r.CalendarId);
            Assert.All(fresh.Values, r => Assert.True(DateTime.UtcNow - r.LastFetchedUtc < TimeSpan.FromMinutes(5))); // both are recent now
        }
        finally
        {
            if (window is not null)
            {
                RemoveAllSubscriptions(window);
                window.Close();
            }

            SubscriptionService.FetcherFactory = previous;
            if (original is not null) File.WriteAllBytes(settingsFile, original);
        }
    });

    // ---- T164: the messages ----

    [Theory]
    [Trait("TestCase", "T164")]
    [InlineData("it")]
    [InlineData("en")]
    [InlineData("fr")]
    [InlineData("de")]
    [InlineData("es")]
    public void T164_EveryProblemHasItsMessageInEveryLanguageWithItsNumbers(string language)
    {
        foreach (var problem in Enum.GetValues<SubscriptionProblem>())
            Assert.True(L.Has(language, "subscription.error." + problem), $"{language}: {problem}");

        var texts = L.Texts(language);
        string Text(SubscriptionProblem p) => string.Format(System.Globalization.CultureInfo.InvariantCulture, texts["subscription.error." + p],
            SubscriptionLimits.MaxSubscriptions, SubscriptionLimits.MaxEvents, 10);
        Assert.Contains("10", Text(SubscriptionProblem.TooManySubscriptions));
        Assert.Contains("10", Text(SubscriptionProblem.TooLarge));
        Assert.Contains("5000", Text(SubscriptionProblem.TooManyEvents));
        Assert.All(Enum.GetValues<SubscriptionProblem>(), p => Assert.DoesNotMatch(@"\{\d\}", Text(p)));

        // Every problem has its own sentence.
        Assert.Equal(Enum.GetValues<SubscriptionProblem>().Length, Enum.GetValues<SubscriptionProblem>().Select(Text).Distinct().Count());
    }
}
