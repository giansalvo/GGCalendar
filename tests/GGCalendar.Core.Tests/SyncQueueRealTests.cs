using System.Diagnostics;
using System.Text;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GGCalendar.App.Services;
using GGCalendar.App.ViewModels;
using GGCalendar.App.Views;
using GGCalendar.Core.Ics;
using GGCalendar.Core.Localization;
using GGCalendar.Core.Tests.Support;
using GGCalendar.Data;
using static GGCalendar.Core.Tests.Support.AppDriver;

namespace GGCalendar.Core.Tests;

/// <summary>An import of thousands of events into the calendar of the test account, then the queue seen and cancelled while it is being sent (issue #19), with the real Google.</summary>
[Collection(CollectionName)]
[Trait("Category", "RealGoogle")]
public class SyncQueueRealTests
{
    private const int Events = 5000;

    private static string Report => Path.Combine(Path.GetTempPath(), "queue_5000_result.txt");

    private static byte[] IcsFile()
    {
        var text = new StringBuilder("BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:-//GGCalendar tests//EN\r\n");
        for (var i = 1; i <= Events; i++)
        {
            var start = new DateTime(2027, 3, 1).AddDays(i % 28).AddHours(8 + i % 10);
            text.Append("BEGIN:VEVENT\r\nUID:queue5000-").Append(NewTitle(i).Replace(' ', '-')).Append("@ggcalendar.test\r\n")
                .Append("SUMMARY:").Append(NewTitle(i)).Append("\r\n")
                .Append("DTSTART:").Append(start.ToString("yyyyMMdd'T'HHmmss")).Append("\r\n")
                .Append("DTEND:").Append(start.AddHours(1).ToString("yyyyMMdd'T'HHmmss")).Append("\r\nEND:VEVENT\r\n");
        }

        return Encoding.UTF8.GetBytes(text.Append("END:VCALENDAR\r\n").ToString());
    }

    /// <summary>Lets the windows work until <paramref name="done"/>, and says the longest time the window did not answer (the loop below could not turn).</summary>
    private static async Task<(TimeSpan Total, TimeSpan LongestStall)> MeasureAsync(Func<bool> done, TimeSpan timeout)
    {
        var total = Stopwatch.StartNew();
        var lap = Stopwatch.StartNew();
        var longest = TimeSpan.Zero;
        while (!done() && total.Elapsed < timeout)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(20);
            if (lap.Elapsed > longest) longest = lap.Elapsed;
            lap.Restart();
        }

        return (total.Elapsed, longest);
    }

    [Fact]
    [Trait("TestCase", "T253")]
    public Task T253_AnImportOf5000EventsIsSeenAndCancelledInTheQueueWhileItIsBeingSent() => InAppAsync(async window =>
    {
        var vm = Vm(window);
        var report = new StringBuilder();
        void Say(string line) { report.AppendLine(line); File.WriteAllText(Report, report.ToString()); }
        try
        {
            // 1. The import of the file, as the person does it: the file is read and its events are put in the calendar of the test account.
            var watch = Stopwatch.StartNew();
            var parsed = await Task.Run(() => IcsParser.AnalyzeFile(IcsFile(), TestCalendarId));
            Say($"lettura del file: {watch.Elapsed.TotalSeconds:0.0} s, {parsed.Events.Count} eventi");
            watch.Restart();
            var (imported, _) = await vm.ImportEventsAsync(TestCalendarId, parsed.Events, ImportDuplicates.Copy);
            Say($"importazione in calendario: {watch.Elapsed.TotalSeconds:0.0} s, {imported} eventi");
            Assert.Equal(Events, imported);

            // 2. The sending starts (as soon as the change is queued the app syncs); the person lets it go on a little.
            Click(FindButton(window, "Main.SyncNow"));
            var sendingFor = await MeasureAsync(() => new CalendarStore().CountPendingEvents() <= Events - 500, TimeSpan.FromMinutes(3));
            var sent = Events - new CalendarStore().CountPendingEvents();
            Say($"invio iniziato: dopo {sendingFor.Total.TotalSeconds:0.0} s sono partiti {sent} eventi (finestra ferma al massimo {sendingFor.LongestStall.TotalMilliseconds:0} ms)");
            Assert.InRange(sent, 1, Events - 1);

            // 3. The hourglass: the sending is stopped and the list appears.
            var indicator = window.FindControl<Button>("QueueButton")!;
            Click(indicator);
            var opened = await MeasureAsync(() => window.OwnedWindows.OfType<SyncQueueWindow>().Any(), TimeSpan.FromMinutes(2));
            var queue = window.OwnedWindows.OfType<SyncQueueWindow>().FirstOrDefault() ?? throw new Xunit.Sdk.XunitException("La finestra della coda non si è aperta.");
            var waiting = new CalendarStore().CountPendingEvents();
            var rows = queue.GetVisualDescendants().OfType<Control>().Count(c => Avalonia.Automation.AutomationProperties.GetAutomationId(c) == "Queue.Row");
            Say($"clessidra: la finestra si apre dopo {opened.Total.TotalSeconds:0.0} s (l'invio è fermo: {!vm.IsSyncing}); in coda {waiting} eventi, righe disegnate {rows}, finestra ferma al massimo {opened.LongestStall.TotalMilliseconds:0} ms");
            Assert.False(vm.IsSyncing);

            // 4. "Annulla tutte" and its confirmation.
            Click((Button)queue.GetVisualDescendants().OfType<Control>().First(c => Avalonia.Automation.AutomationProperties.GetAutomationId(c) == "Queue.CancelAll"));
            var confirm = await WaitForAsync(() => queue.OwnedWindows.OfType<ConfirmWindow>().FirstOrDefault(), "la domanda");
            Click(FindButton(confirm, "Dialog.Ok"));
            var cancelled = await MeasureAsync(() => queue.IsEnabled && new CalendarStore().CountPendingEvents() == 0, TimeSpan.FromMinutes(5));
            Say($"annulla tutte: finito dopo {cancelled.Total.TotalSeconds:0.0} s, finestra ferma al massimo {cancelled.LongestStall.TotalMilliseconds:0} ms; in coda {new CalendarStore().CountPendingEvents()}");
            Assert.Equal(0, new CalendarStore().CountPendingEvents());
            Assert.True(window.FindControl<Avalonia.Controls.Shapes.Ellipse>("QueueDot")!.IsVisible);

            queue.Close();
            await WaitForAsync(() => window.OwnedWindows.Count == 0 ? window : null, "la chiusura della finestra");
            Say($"eventi rimasti sul PC dopo l'annullamento (già partiti per Google): {vm.AllEvents.Count(e => e.Title.StartsWith(TestPrefix, StringComparison.Ordinal))}");

            Assert.True(opened.Total < TimeSpan.FromSeconds(30), "La finestra ha impiegato troppo ad aprirsi.");
            Assert.True(cancelled.Total < TimeSpan.FromSeconds(60), "L'annullamento ha impiegato troppo.");
            Assert.True(cancelled.LongestStall < TimeSpan.FromSeconds(2), "La finestra è rimasta ferma troppo a lungo durante l'annullamento.");
        }
        finally
        {
            // What already reached Google (and the PC) is removed without queueing thousands of deletions: the events with the test prefix go
            // from this PC directly, and from Google by the cleanup of the driver.
            var store = new CalendarStore();
            foreach (var ev in store.GetAllEvents().Where(e => e.Title.StartsWith(TestPrefix, StringComparison.Ordinal) && e.SeriesMasterId is null).ToList()) store.DeleteEvent(ev.Id);
            store.DiscardOutboxOf(store.GetPending().Select(p => p.EventId).Distinct().ToList());
            await vm.ReloadAsync();
        }
    });
}
