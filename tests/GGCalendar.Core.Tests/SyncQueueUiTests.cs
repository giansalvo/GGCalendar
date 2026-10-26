using Avalonia;
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

/// <summary>The status bar with the indicator of the queue and the window of the queue (issue #19), on the real window with a fake Google behind it.</summary>
[Collection(CollectionName)]
public class SyncQueueUiTests
{
    private static Button Indicator(MainWindow window) => window.FindControl<Button>("QueueButton")!;

    private static string Tip(MainWindow window) => (string)ToolTip.GetTip(Indicator(window))!;

    private static bool ShowsDot(MainWindow window) => window.FindControl<Avalonia.Controls.Shapes.Ellipse>("QueueDot")!.IsVisible;

    private static string Count(MainWindow window) => window.FindControl<TextBlock>("QueueCount")!.Text!;

    private static CalendarEvent NewEvent(MainWindow window, string title, int day = 5)
    {
        var ev = new CalendarEvent(Guid.NewGuid().ToString("N"), FakeGoogle.LocalCalendarId, title, new DateTime(2026, 12, day, 9, 0, 0), new DateTime(2026, 12, day, 10, 0, 0)) { Reminders = [] };
        Vm(window).AddEvent(ev);
        return ev;
    }

    private static async Task<SyncQueueWindow> OpenQueueAsync(MainWindow window)
    {
        Click(Indicator(window));
        return await WaitForAsync(() => window.OwnedWindows.OfType<SyncQueueWindow>().FirstOrDefault(), "la finestra della coda");
    }

    private static List<Control> InWindow(Visual root, string id) =>
        root.GetVisualDescendants().OfType<Control>().Where(c => Avalonia.Automation.AutomationProperties.GetAutomationId(c) == id).ToList();

    private static string Texts(Visual root) => string.Join("\n", root.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text ?? ""));

    private static async Task CloseQueueAsync(MainWindow window, SyncQueueWindow queue)
    {
        queue.Close();
        await WaitForAsync(() => window.OwnedWindows.Count == 0 ? window : null, "la chiusura della finestra");
    }

    private static async Task AnswerAsync(Window owner, ConfirmWindow confirm, bool ok)
    {
        Click(FindButton(confirm, ok ? "Dialog.Ok" : "Dialog.Cancel"));
        await WaitForAsync(() => owner.OwnedWindows.OfType<ConfirmWindow>().Any() ? null : owner, "la chiusura della domanda");
    }

    private static Task<ConfirmWindow> AskedAsync(Window owner) =>
        WaitForAsync(() => owner.OwnedWindows.OfType<ConfirmWindow>().FirstOrDefault(), "la domanda");

    // ---- T245: the bar and the indicator ----

    [Fact]
    [Trait("TestCase", "T245")]
    public Task T245_TheStatusBarIsAlwaysThereWithAGreenDotWhenNothingWaits() => InFakeGoogleAppAsync(new FakeGoogle(), async window =>
    {
        await SyncNowAsync(window);

        var bar = Indicator(window).GetVisualAncestors().OfType<Border>().First();
        Assert.True(bar.IsEffectivelyVisible);
        Assert.True(ShowsDot(window));
        Assert.False(window.FindControl<TextBlock>("QueueGlyph")!.IsVisible);
        Assert.Equal(L.T("queue.tip_ok"), Tip(window));
        Assert.Contains(L.T("status.synced", DateTime.Now).Split(' ')[0], Texts(bar)); // the message of the sync is still on the left
    });

    [Fact]
    [Trait("TestCase", "T246")]
    public Task T246_TheHourglassShowsTheNumberOfEventsNotOfChangesAndTheDotComesBackWhenTheyAreSent()
    {
        var fake = new FakeGoogle();
        fake.Seed("Su Google", new DateTime(2026, 12, 20, 9, 0, 0, DateTimeKind.Utc));
        return InFakeGoogleAppAsync(fake, async window =>
        {
            await SyncNowAsync(window);
            var vm = Vm(window);
            fake.Offline = true; // the changes stay in the queue
            var a = NewEvent(window, "Uno");
            NewEvent(window, "Due", 6);
            var onGoogle = vm.AllEvents.First(e => e.Title == "Su Google");
            vm.UpdateEvent(onGoogle with { Title = "Su Google 1" });
            vm.UpdateEvent(vm.AllEvents.First(e => e.Id == onGoogle.Id) with { Title = "Su Google 2" });
            vm.UpdateEvent(vm.AllEvents.First(e => e.Id == a.Id) with { Title = "Uno modificato" });
            Dispatcher.UIThread.RunJobs();

            Assert.True(new CalendarStore().GetPending().Count > 3); // many rows...
            Assert.False(ShowsDot(window));
            Assert.Equal("3", Count(window)); // ...for three events
            Assert.Equal(L.N("queue.tip", 3), Tip(window));

            fake.Offline = false;
            await SyncNowAsync(window);
            Assert.True(ShowsDot(window));
            Assert.Equal(L.T("queue.tip_ok"), Tip(window));
        });
    }

    // ---- T247: the window ----

    [Fact]
    [Trait("TestCase", "T247")]
    public Task T247_TheWindowListsTheEventsByCalendarWithWhatWasDone()
    {
        var fake = FakeWithSynced();
        return InFakeGoogleAppAsync(fake, async window =>
    {
        await SyncNowAsync(window);
        var vm = Vm(window);
        fake.Offline = true;
        NewEvent(window, "Nuovo evento");
        var onGoogle = vm.AllEvents.First(e => e.Title == "Su Google");
        vm.UpdateEvent(onGoogle with { Title = "Modificato 1" });
        vm.UpdateEvent(vm.AllEvents.First(e => e.Id == onGoogle.Id) with { Title = "Modificato 2" });
        var toDelete = vm.AllEvents.First(e => e.Title == "Da eliminare");
        vm.DeleteEvent(toDelete);

        var queue = await OpenQueueAsync(window);

        Assert.Equal(3, InWindow(queue, "Queue.Row").Count);
        var text = Texts(queue);
        Assert.Contains(L.N("queue.info", 3), text);
        Assert.Contains("Nuovo evento", text);
        Assert.Contains("Modificato 2", text);
        Assert.Contains("Da eliminare", text); // a deleted event is named too
        Assert.Contains(L.T("queue.kind_edited_n", 2), text);
        Assert.Contains(L.T("queue.kind_deleted"), text);
        Assert.Contains(L.N("queue.sum_new", 1), text);
        Assert.Single(InWindow(queue, "Queue.CancelGroup")); // one calendar
        Assert.Equal(L.T("queue.cancel_all", 3), ((Button)InWindow(queue, "Queue.CancelAll").Single()).Content);
        await CloseQueueAsync(window, queue);
    });
    }

    private static FakeGoogle FakeWithSynced()
    {
        var fake = new FakeGoogle();
        fake.Seed("Su Google", new DateTime(2026, 12, 20, 9, 0, 0, DateTimeKind.Utc));
        fake.Seed("Da eliminare", new DateTime(2026, 12, 21, 9, 0, 0, DateTimeKind.Utc));
        return fake;
    }

    [Fact]
    [Trait("TestCase", "T247")]
    public Task T247_WithNothingWaitingTheWindowSaysAllIsFine() => InFakeGoogleAppAsync(new FakeGoogle(), async window =>
    {
        await SyncNowAsync(window);

        var queue = await OpenQueueAsync(window);

        Assert.Contains(L.T("queue.empty"), Texts(queue));
        Assert.False(((Button)InWindow(queue, "Queue.CancelAll").Single()).IsVisible);
        Assert.Empty(InWindow(queue, "Queue.Row"));
        await CloseQueueAsync(window, queue);
    });

    // ---- T248: cancel one, a calendar, all ----

    [Fact]
    [Trait("TestCase", "T248")]
    public Task T248_CancellingOneEventAsksFirstThenRemovesItAndTheOthersStay()
    {
        var fake = new FakeGoogle();
        return InFakeGoogleAppAsync(fake, async window =>
        {
            await SyncNowAsync(window);
            var vm = Vm(window);
            fake.Offline = true;
            NewEvent(window, "Uno");
            NewEvent(window, "Due", 6);
            var queue = await OpenQueueAsync(window);

            Click((Button)InWindow(queue, "Queue.Cancel").First());
            var confirm = await AskedAsync(queue);
            Assert.Contains(L.T("queue.confirm_title_one"), Texts(confirm));
            Assert.Contains("Uno", Texts(confirm));
            Assert.Contains(L.N("queue.explain_new", 1), Texts(confirm));
            Assert.Contains(L.T("queue.confirm_loss"), Texts(confirm));
            Assert.Equal(L.T("queue.confirm_button"), FindButton(confirm, "Dialog.Ok").Content);

            await AnswerAsync(queue, confirm, false); // "Annulla" in the question: nothing changes
            Assert.Equal(2, InWindow(queue, "Queue.Row").Count);
            Assert.Contains(vm.AllEvents, e => e.Title == "Uno");

            Click((Button)InWindow(queue, "Queue.Cancel").First());
            await AnswerAsync(queue, await AskedAsync(queue), true);
            await WaitForAsync(() => InWindow(queue, "Queue.Row").Count == 1 ? queue : null, "l'aggiornamento della lista");

            Assert.DoesNotContain(vm.AllEvents, e => e.Title == "Uno");
            Assert.Contains(vm.AllEvents, e => e.Title == "Due");
            Assert.Equal("1", Count(window));
            await CloseQueueAsync(window, queue);
        });
    }

    [Fact]
    [Trait("TestCase", "T248")]
    public Task T248_CancelAllEmptiesTheQueueTheIndicatorGoesGreenAndNothingIsSentToGoogle()
    {
        var fake = new FakeGoogle();
        return InFakeGoogleAppAsync(fake, async window =>
        {
            await SyncNowAsync(window);
            fake.Offline = true;
            for (var i = 1; i <= 5; i++) NewEvent(window, "Evento " + i, i + 1);
            var queue = await OpenQueueAsync(window);
            var writes = fake.Writes;

            Click((Button)InWindow(queue, "Queue.CancelAll").Single());
            var confirm = await AskedAsync(queue);
            Assert.Contains(L.T("queue.confirm_title_all"), Texts(confirm));
            Assert.Contains(L.N("queue.confirm_all", 5), Texts(confirm));
            await AnswerAsync(queue, confirm, true);
            await WaitForAsync(() => InWindow(queue, "Queue.Row").Count == 0 ? queue : null, "lo svuotamento della lista");

            Assert.True(ShowsDot(window));
            Assert.Equal(L.T("queue.tip_ok"), Tip(window));
            Assert.Contains(L.T("queue.empty"), Texts(queue));
            Assert.Equal(writes, fake.Writes);
            fake.Offline = false;
            await CloseQueueAsync(window, queue);
            await SyncNowAsync(window);
            Assert.Equal(0, fake.EventsIn(FakeGoogle.RemoteCalendar)); // nothing was ever sent
        });
    }

    [Fact]
    [Trait("TestCase", "T248")]
    public Task T248_ACancelledDeletionComesBackAfterTheNextSync()
    {
        var fake = FakeWithSynced();
        return InFakeGoogleAppAsync(fake, async window =>
        {
            await SyncNowAsync(window);
            var vm = Vm(window);
            fake.Offline = true;
            vm.DeleteEvent(vm.AllEvents.First(e => e.Title == "Da eliminare"));
            var queue = await OpenQueueAsync(window);
            fake.Offline = false; // the network is back: the sync that follows the cancel can bring the event back

            Click((Button)InWindow(queue, "Queue.Cancel").Single());
            await AnswerAsync(queue, await AskedAsync(queue), true);

            await WaitForAsync(() => vm.AllEvents.Any(e => e.Title == "Da eliminare") ? vm : null, "il ritorno dell'evento", SyncTimeout);
            Assert.DoesNotContain(fake.Requests, r => r.StartsWith("DELETE", StringComparison.Ordinal));
            Assert.Equal(2, fake.EventsIn(FakeGoogle.RemoteCalendar));
            await CloseQueueAsync(window, queue);
        });
    }

    // ---- T249: retry now ----

    [Fact]
    [Trait("TestCase", "T249")]
    public Task T249_RetryNowSendsTheQueueAndTheWindowShowsItEmpty()
    {
        var fake = new FakeGoogle();
        return InFakeGoogleAppAsync(fake, async window =>
        {
            await SyncNowAsync(window);
            fake.Offline = true;
            NewEvent(window, "Da inviare");
            var queue = await OpenQueueAsync(window);
            Assert.Single(InWindow(queue, "Queue.Row"));
            fake.Offline = false;

            Click((Button)InWindow(queue, "Queue.Retry").Single());
            await WaitForAsync(() => InWindow(queue, "Queue.Row").Count == 0 ? queue : null, "l'invio", SyncTimeout);

            Assert.True(ShowsDot(window));
            Assert.NotNull(fake.FindBySummary("Da inviare"));
            await CloseQueueAsync(window, queue);
        });
    }

    // ---- T251: many events; T252: the sending is stopped while the window is open ----

    private static void Import(int count)
    {
        var events = Enumerable.Range(0, count).Select(i => new CalendarEvent(Guid.NewGuid().ToString("N"), FakeGoogle.LocalCalendarId, $"Importato {i:D4}",
            new DateTime(2027, 1, 4).AddHours(8 + i % 10), new DateTime(2027, 1, 4).AddHours(9 + i % 10)) { Reminders = [] }).ToList();
        new CalendarStore().ApplyImport([], events, remoteDestination: true);
    }

    [Fact]
    [Trait("TestCase", "T251")]
    public Task T251_WithThousandsOfEventsTheWindowDrawsTheFirstOnesAndTheButtonsCoverAll()
    {
        var fake = new FakeGoogle();
        return InFakeGoogleAppAsync(fake, async window =>
        {
            await SyncNowAsync(window);
            fake.Offline = true;
            Import(400);

            var queue = await OpenQueueAsync(window);

            Assert.Equal(SyncQueueWindow.MaxRows, InWindow(queue, "Queue.Row").Count); // not 400 lines: the window would freeze
            Assert.Contains(L.N("queue.info", 400), Texts(queue));
            Assert.Contains(L.N("queue.more", 400 - SyncQueueWindow.MaxRows), Texts(queue));
            Assert.Equal(L.T("queue.cancel_all", 400), ((Button)InWindow(queue, "Queue.CancelAll").Single()).Content);

            Click((Button)InWindow(queue, "Queue.CancelAll").Single());
            await AnswerAsync(queue, await AskedAsync(queue), true);
            await WaitForAsync(() => InWindow(queue, "Queue.Row").Count == 0 && queue.IsEnabled ? queue : null, "l'annullamento", SyncTimeout);
            Assert.True(ShowsDot(window));
            Assert.Equal(0, new CalendarStore().CountPendingEvents());
            await CloseQueueAsync(window, queue);
        });
    }

    [Fact]
    [Trait("TestCase", "T252")]
    public Task T252_OpeningTheWindowStopsASendingInProgressAndHoldsTheAutomaticOnesUntilItIsClosed()
    {
        var fake = new FakeGoogle();
        return InFakeGoogleAppAsync(fake, async window =>
        {
            await SyncNowAsync(window);
            var vm = Vm(window);
            fake.Offline = true;
            Import(130);
            fake.Offline = false;
            fake.Delay = TimeSpan.FromMilliseconds(500); // a slow Google: the sending lasts a few seconds
            Click(FindButton(window, "Main.SyncNow"));
            await WaitForAsync(() => new CalendarStore().CountPendingEvents() <= 80 ? vm : null, "il primo invio", SyncTimeout); // 50 of the 130 are sent (counted in the queue: the fake is being written to meanwhile)
            Assert.True(vm.IsSyncing);

            var queue = await OpenQueueAsync(window); // stops the sending first

            Assert.False(vm.IsSyncing);
            var waiting = new CalendarStore().CountPendingEvents();
            Assert.InRange(waiting, 1, 129); // some were sent, the rest waits
            Assert.Contains(L.N("queue.info", waiting), Texts(queue));
            Assert.Contains(L.T("queue.paused"), Texts(queue));
            var requests = fake.Requests.Count;
            Click(FindButton(window, "Main.SyncNow")); // an automatic or manual sync of the main window does not start while the list is on show
            await PauseAsync(TimeSpan.FromMilliseconds(1500));
            Assert.Equal(requests, fake.Requests.Count);
            Assert.Equal(waiting, new CalendarStore().CountPendingEvents());

            fake.Delay = TimeSpan.Zero;
            await CloseQueueAsync(window, queue); // the sending goes on by itself
            await WaitForAsync(() => new CalendarStore().CountPendingEvents() == 0 && !vm.IsSyncing ? vm : null, "la ripresa dell'invio", SyncTimeout);
            Assert.True(ShowsDot(window));
            Assert.Equal(130, fake.EventsIn(FakeGoogle.RemoteCalendar));
        });
    }

    // ---- T256: the list on show is older than the queue ----

    /// <summary>What a sync does while the list is on show: the queued changes of the events are gone from the queue (sent), the events stay.</summary>
    private static void Sent(params string[] titles)
    {
        var store = new CalendarStore();
        foreach (var row in store.GetPending().Where(p => titles.Any(t => CalendarStore.ReadSnapshot(p)?.Title == t))) store.Complete(row.Seq);
    }

    [Fact]
    [Trait("TestCase", "T256")]
    public Task T256_IfAnEventIsSentWhileTheListIsOnShowTheQuestionAndTheListTellWhatIsReallyWaiting()
    {
        var fake = new FakeGoogle();
        return InFakeGoogleAppAsync(fake, async window =>
        {
            await SyncNowAsync(window);
            var vm = Vm(window);
            fake.Offline = true;
            NewEvent(window, "Uno");
            NewEvent(window, "Due", 6);
            NewEvent(window, "Tre", 7);
            var queue = await OpenQueueAsync(window);
            Assert.Equal(3, InWindow(queue, "Queue.Row").Count);

            Sent("Uno", "Due"); // a sync sent two of them while the list stayed as it was
            Assert.Equal(3, InWindow(queue, "Queue.Row").Count); // the list on show is older than the queue

            Click((Button)InWindow(queue, "Queue.CancelGroup").Single());
            var confirm = await AskedAsync(queue);

            Assert.Contains(L.N("queue.confirm_group", 1, vm.Calendars.First(c => c.Id == FakeGoogle.LocalCalendarId).Name), Texts(confirm)); // one event is waiting, not three
            Assert.Equal(1, InWindow(queue, "Queue.Row").Count); // and the list says the same
            await AnswerAsync(queue, confirm, true);
            await WaitForAsync(() => InWindow(queue, "Queue.Row").Count == 0 && queue.IsEnabled ? queue : null, "l'annullamento");

            Assert.DoesNotContain(vm.AllEvents, e => e.Title == "Tre"); // what was waiting is cancelled
            Assert.Contains(vm.AllEvents, e => e.Title == "Uno"); // what was sent stays
            Assert.Contains(vm.AllEvents, e => e.Title == "Due");
            await CloseQueueAsync(window, queue);
        });
    }

    [Fact]
    [Trait("TestCase", "T256")]
    public Task T256_CancelAllWhenEverythingWasSentMeanwhileSaysThereIsNothingToCancelAndChangesNothing()
    {
        var fake = new FakeGoogle();
        return InFakeGoogleAppAsync(fake, async window =>
        {
            await SyncNowAsync(window);
            var vm = Vm(window);
            fake.Offline = true;
            NewEvent(window, "Uno");
            NewEvent(window, "Due", 6);
            var queue = await OpenQueueAsync(window);
            Sent("Uno", "Due");

            Click((Button)InWindow(queue, "Queue.CancelAll").Single());
            var warning = await WaitForAsync(() => queue.OwnedWindows.OfType<WarningWindow>().FirstOrDefault(), "l'avviso");

            Assert.Contains(L.T("queue.cancel_none"), Texts(warning));
            Assert.Empty(queue.OwnedWindows.OfType<ConfirmWindow>()); // no question about cancelling nothing
            Assert.Empty(InWindow(queue, "Queue.Row")); // the list is drawn again as it is
            Assert.Contains(vm.AllEvents, e => e.Title == "Uno");
            Assert.Contains(vm.AllEvents, e => e.Title == "Due");
            warning.Close();
            await WaitForAsync(() => queue.OwnedWindows.Count == 0 ? queue : null, "la chiusura dell'avviso");
            await CloseQueueAsync(window, queue);
        });
    }

    // ---- T250: the five languages ----

    [Theory]
    [InlineData("it")]
    [InlineData("en")]
    [InlineData("fr")]
    [InlineData("de")]
    [InlineData("es")]
    [Trait("TestCase", "T250")]
    public Task T250_TheTextsAreInTheChosenLanguage(string language)
    {
        var fake = new FakeGoogle();
        return InFakeGoogleAppAsync(fake, async window =>
        {
            var original = L.Language;
            try
            {
                await SyncNowAsync(window);
                fake.Offline = true;
                NewEvent(window, "Uno");
                L.Apply(language);
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(L.N("queue.tip", 1), Tip(window));

                var queue = await OpenQueueAsync(window);
                var text = Texts(queue);
                Assert.Contains(L.T("queue.title"), text);
                Assert.Contains(L.N("queue.info", 1), text);
                Assert.DoesNotContain("queue.", text); // no key left without its text
                Click((Button)InWindow(queue, "Queue.Cancel").Single());
                var confirm = await AskedAsync(queue);
                Assert.DoesNotContain("queue.", Texts(confirm));
                Assert.Contains(L.N("queue.explain_new", 1), Texts(confirm));
                await AnswerAsync(queue, confirm, false);
                await CloseQueueAsync(window, queue);
            }
            finally
            {
                L.Apply(original);
            }
        });
    }
}
