using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.VisualTree;
using GGCalendar.App.ViewModels;
using GGCalendar.App.Views;
using GGCalendar.Core.Ics;
using GGCalendar.Core.Models;
using GGCalendar.Core.Tests.Support;
using GGCalendar.Data;
using Xunit.Abstractions;

namespace GGCalendar.Core.Tests;

/// <summary>
/// T109-T112: an import of thousands of events must not freeze the window. The database is a file on disk (as in the app): with the
/// database in memory the cost of writing to the disk does not show.
/// </summary>
[Collection(AppDriver.CollectionName)]
public class ImportSpeedTests(ITestOutputHelper output)
{
    private const string Google = "someone@example.com|someone@example.com";

    private static byte[] Feed(string name) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "ics", name + ".ics"));

    private sealed class Rig : IDisposable
    {
        private readonly string _folder = Path.Combine(Path.GetTempPath(), "ggcal-import-" + Guid.NewGuid().ToString("N"));

        public Rig(string calendarId)
        {
            Directory.CreateDirectory(_folder);
            Store = new CalendarStore(Path.Combine(_folder, "calendar.db"));
            Store.UpsertCalendar(new CalendarInfo(calendarId, "Prova", "Destinazione", "#1A73E8"));
            Vm = new MainViewModel(Store);
            CalendarId = calendarId;
        }

        public CalendarStore Store { get; }
        public MainViewModel Vm { get; }
        public string CalendarId { get; }

        public void Dispose()
        {
            try { Directory.Delete(_folder, recursive: true); }
            catch (IOException) { /* a temporary folder left behind is harmless */ }
        }
    }

    [Fact]
    [Trait("TestCase", "T109")]
    public async Task T109_FiveThousandEventsImportInAFileDatabaseInUnderThreeSeconds()
    {
        using var rig = new Rig("local-prova");
        var feed = IcsParser.AnalyzeFile(Feed("ggcal_5000"), rig.CalendarId);

        var clock = Stopwatch.StartNew();
        var (imported, skipped) = await rig.Vm.ImportEventsAsync(rig.CalendarId, feed.Events, ImportDuplicates.Skip);
        output.WriteLine($"{imported} eventi importati in {clock.Elapsed.TotalSeconds:F2} s");

        Assert.Equal((5000, 0), (imported, skipped));
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(3), $"Importazione troppo lenta: {clock.Elapsed.TotalSeconds:F1} s.");
        Assert.Equal(5000, new MainViewModel(rig.Store).EventsOf(rig.CalendarId).Count); // what the window shows is what the disk holds
    }

    [Fact]
    [Trait("TestCase", "T110")]
    public async Task T110_TheImportReturnsControlToTheWindowBeforeTheEventsAreWritten()
    {
        using var rig = new Rig("local-prova");
        var feed = IcsParser.AnalyzeFile(Feed("ggcal_5000"), rig.CalendarId);

        var clock = Stopwatch.StartNew();
        var running = rig.Vm.ImportEventsAsync(rig.CalendarId, feed.Events, ImportDuplicates.Skip);
        var untilFirstAwait = clock.Elapsed;
        await running;

        output.WriteLine($"fino al primo await {untilFirstAwait.TotalMilliseconds:F0} ms, in tutto {clock.Elapsed.TotalSeconds:F2} s");
        Assert.False(running.IsFaulted);
        Assert.True(untilFirstAwait < TimeSpan.FromMilliseconds(500), $"Il thread dell'interfaccia è rimasto occupato {untilFirstAwait.TotalMilliseconds:F0} ms.");
    }

    [Fact]
    [Trait("TestCase", "T111")]
    public async Task T111_AnImportIntoAGoogleCalendarQueuesOneCreationPerEventAndOneReplacementKeepsTheRemoteLink()
    {
        using var rig = new Rig(Google);
        var feed = IcsParser.AnalyzeFile(Feed("ggcal_5000"), rig.CalendarId);
        var queued = 0;
        rig.Vm.ChangeQueued += () => queued++;

        await rig.Vm.ImportEventsAsync(rig.CalendarId, feed.Events, ImportDuplicates.Skip);

        Assert.Equal(5000, rig.Store.GetPending().Count(p => p.Operation == OutboxOperation.Create));
        Assert.Equal(1, queued); // the sync is asked for once, not 5000 times

        // The first event has been sent to Google; importing the file again with "Replace" deletes it there and creates it again.
        var first = rig.Vm.EventsOf(rig.CalendarId).First();
        rig.Store.SetRemote(first.Id, "remote-1", "etag-1");
        await rig.Vm.ImportEventsAsync(rig.CalendarId, IcsParser.AnalyzeFile(Feed("ggcal_5000"), rig.CalendarId).Events, ImportDuplicates.Replace);

        var pending = rig.Store.GetPending();
        Assert.Single(pending, p => p.Operation == OutboxOperation.Delete);
        Assert.Equal(10_000, pending.Count(p => p.Operation == OutboxOperation.Create)); // the 5000 of the first import are still waiting, and 5000 new ones
        Assert.Equal(5000, rig.Vm.EventsOf(rig.CalendarId).Count);
    }

    [Fact]
    [Trait("TestCase", "T112")]
    public Task T112_TheSummaryWarnsThatManyEventsForGoogleWillTakeTimeToSend() => HeadlessApp.RunAsync(async () =>
    {
        var window = new ImportSummaryWindow("Calendario", 5000, 0, [], "Il calendario è su Google: i 5000 eventi saranno inviati uno alla volta nelle prossime sincronizzazioni e potrebbe volerci del tempo.");
        window.Show();
        await Task.Yield();

        var texts = window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        window.Close();

        Assert.Contains(texts, t => t is not null && t.StartsWith("Il calendario è su Google", StringComparison.Ordinal) && t.Contains("5000"));
    });
}
