using Avalonia.Threading;
using GGCalendar.App.Views;
using GGCalendar.Core.Tests.Support;
using GGCalendar.Data;
using static GGCalendar.Core.Tests.Support.AppDriver;

namespace GGCalendar.Core.Tests;

/// <summary>
/// T493-T496: the progress window of a long sending and the main window hidden in the tray (issue #111). A window that is not visible
/// cannot own another one: asking for it ended the app.
/// </summary>
[Collection(CollectionName)]
public class SyncProgressTrayTests
{
    /// <summary>Puts <paramref name="count"/> new events of the fake account in the queue and slows Google down, so that the sending can be watched.</summary>
    private static void Queue(FakeGoogle fake, int count)
    {
        var store = new CalendarStore();
        for (var i = 0; i < count; i++)
        {
            var ev = SyncRig.NewEvent(Guid.NewGuid().ToString("N"), "In coda " + i);
            store.UpsertEvent(ev);
            store.Enqueue(ev.Id, ev.CalendarId, OutboxOperation.Create, ev);
        }

        fake.Delay = TimeSpan.FromMilliseconds(400);
    }

    private static SyncProgressWindow? Progress(MainWindow window) => window.OwnedWindows.OfType<SyncProgressWindow>().FirstOrDefault();

    private static async Task StartSyncAsync(MainWindow window)
    {
        await WaitForQuietAsync(window);
        Click(FindButton(window, "Main.SyncNow"));
        await WaitForAsync(() => Vm(window).IsSyncing ? Vm(window) : null, "l'inizio della sincronizzazione");
    }

    private static async Task SyncEndsAsync(MainWindow window)
    {
        await WaitForAsync(() => Vm(window).IsSyncing ? null : Vm(window), "la fine della sincronizzazione", SyncTimeout);
        Dispatcher.UIThread.RunJobs();
    }

    [Fact]
    [Trait("TestCase", "T493")]
    public async Task T493_AHiddenWindowSendsManyChangesWithoutTheProgressWindow()
    {
        var fake = new FakeGoogle();
        await GoogleCalendarUiTests.InFakeGoogleAppAsync(fake, async window =>
        {
            Queue(fake, 120);
            window.Hide();

            await StartSyncAsync(window);
            var seen = false;
            await WaitForAsync(() =>
            {
                seen |= Progress(window) is not null;
                return Vm(window).IsSyncing ? null : Vm(window);
            }, "la fine della sincronizzazione", SyncTimeout);
            Dispatcher.UIThread.RunJobs();

            Assert.False(seen);
            Assert.False(window.IsVisible);
            Assert.Equal(120, fake.Ids.Count);
            Assert.True(Vm(window).Status?.Is("status.synced"));
        });
    }

    [Fact]
    [Trait("TestCase", "T494")]
    public async Task T494_TheProgressWindowComesWhenTheAppIsShownAgainDuringTheSending()
    {
        var fake = new FakeGoogle();
        await GoogleCalendarUiTests.InFakeGoogleAppAsync(fake, async window =>
        {
            Queue(fake, 150); // three requests of 50: progress is told three times
            window.Hide();
            await StartSyncAsync(window);
            await WaitForAsync(() => Vm(window).Status?.Is("status.sending") == true ? Vm(window) : null, "il primo avanzamento");
            Assert.Null(Progress(window));

            window.Show();

            var progress = await WaitForAsync(() => Progress(window), "la finestra di avanzamento", SyncTimeout);
            Assert.True(progress.IsVisible);
            await SyncEndsAsync(window);
            Assert.Null(Progress(window));
            Assert.Equal(150, fake.Ids.Count);
        });
    }

    [Theory]
    [Trait("TestCase", "T495")]
    [InlineData(30, false)]
    [InlineData(31, true)]
    public async Task T495_AVisibleWindowShowsTheProgressOnlyAboveThirtyChanges(int count, bool expected)
    {
        var fake = new FakeGoogle();
        await GoogleCalendarUiTests.InFakeGoogleAppAsync(fake, async window =>
        {
            Queue(fake, count);
            await StartSyncAsync(window);
            var seen = false;
            await WaitForAsync(() =>
            {
                seen |= Progress(window) is not null;
                return Vm(window).IsSyncing ? null : Vm(window);
            }, "la fine della sincronizzazione", SyncTimeout);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(expected, seen);
            Assert.Null(Progress(window));
            Assert.Equal(count, fake.Ids.Count);
        });
    }

    [Fact]
    [Trait("TestCase", "T496")]
    public async Task T496_HidingTheAppWhileTheProgressWindowIsOpenAndShowingItAgainKeepsTheProgressInSight()
    {
        var fake = new FakeGoogle();
        await GoogleCalendarUiTests.InFakeGoogleAppAsync(fake, async window =>
        {
            Queue(fake, 200); // four requests
            await StartSyncAsync(window);
            var first = await WaitForAsync(() => Progress(window), "la finestra di avanzamento", SyncTimeout);

            window.Hide();
            Dispatcher.UIThread.RunJobs();
            Assert.False(first.IsVisible); // it goes to the tray with the app

            window.Show();

            await WaitForAsync(() => Progress(window) is { IsVisible: true } p ? p : null, "la finestra di avanzamento di nuovo", SyncTimeout);
            await SyncEndsAsync(window);
            Assert.Null(Progress(window));
            Assert.Equal(200, fake.Ids.Count);
        });
    }
}
