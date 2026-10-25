using Avalonia.Controls;
using Avalonia.VisualTree;
using GGCalendar.App.ViewModels;
using GGCalendar.App.Views;
using GGCalendar.Core.Models;
using GGCalendar.Core.Tests.Support;
using static GGCalendar.Core.Tests.Support.AppDriver;

namespace GGCalendar.Core.Tests;

/// <summary>Reminders (T02, T03, T18, T19) and the alarm (T10), driven like a user would.</summary>
[Collection(CollectionName)]
[Trait("Category", "RealGoogle")]
public class ReminderUiTests
{
    // The standard reminder starts 30 minutes before the event: an event 31 minutes away notifies in about a minute.
    private static readonly TimeSpan NotificationWait = TimeSpan.FromMinutes(3);

    [Fact]
    [Trait("TestCase", "T02")]
    public Task T02_AnEventKeepsTheStandardReminderAfterSyncingAndReopening() => InAppAsync(async window =>
    {
        // The editor of a new event proposes the standard reminder: a notification 30 minutes before.
        ChooseCreateMenuItem(window, "Evento");
        var empty = await WaitForAsync(() => window.OwnedWindows.OfType<EventEditWindow>().FirstOrDefault(), "l'editor");
        var proposed = Assert.Single(((EventEditViewModel)empty.DataContext!).Reminders);
        Assert.Equal(("Notifica", 30m, "minuti"), (proposed.Method.Label, proposed.Value, proposed.Unit.Label));
        await CancelEditorAsync(window, empty);

        // The user saves it without touching the reminders, syncs and reopens the app and the event.
        var title = NewTitle();
        var created = await CreateEventAsync(window, new EventSpec(title));
        var day = created.Start.Date;
        await SyncAsync(window);
        window = await ReopenAsync(window);
        var (dialog, editor) = await OpenEditorAsync(window, title, day);
        var kept = Assert.Single(editor.Reminders);
        Assert.Equal(("Notifica", 30m, "minuti"), (kept.Method.Label, kept.Value, kept.Unit.Label));
        await CancelEditorAsync(window, dialog);

        // Google holds the same notification.
        using var google = await TestGoogle.ConnectAsync();
        var remote = await google.GetAsync(TestGoogle.RemoteIdOf(created.Id) ?? throw new Xunit.Sdk.XunitException("L'evento non è stato inviato a Google."));
        var reminders = remote.GetProperty("reminders");
        Assert.False(reminders.GetProperty("useDefault").GetBoolean());
        var onGoogle = Assert.Single(reminders.GetProperty("overrides").EnumerateArray());
        Assert.Equal(("popup", 30), (onGoogle.GetProperty("method").GetString(), onGoogle.GetProperty("minutes").GetInt32()));
    });

    [Fact]
    [Trait("TestCase", "T03")]
    public Task T03_TheReminderFiresAtTheRightTimeOnce() => InAppAsync(async window =>
    {
        Assert.True(Vm(window).Settings.NotificationsEnabled, "Le notifiche sono disattivate nelle impostazioni dell'app.");
        var calendar = await CreateLocalCalendarAsync(window, NewTitle());
        var title = NewTitle();
        var start = InMinutes(31);
        await CreateEventAsync(window, new EventSpec(title) { CalendarId = calendar, Start = start });

        var toast = await WaitForAsync(() => ToastFor(title), "la notifica del promemoria", NotificationWait);
        Assert.True(DateTime.Now >= start.AddMinutes(-30).AddSeconds(-1), "La notifica è comparsa prima dell'ora del promemoria.");
        Assert.Contains(toast.GetVisualDescendants(), v => v is TextBlock t && t.Text?.Contains(start.ToString(Vm(window).Settings.Use24HourClock ? "HH:mm" : "h:mm tt")) == true);
        Click(FindButton(toast, "Chiudi"));
        await WaitForAsync(() => ToastFor(title) is null ? window : null, "la chiusura della notifica");

        await PauseAsync(TimeSpan.FromSeconds(30));
        Assert.Null(ToastFor(title)); // it does not come back
    });

    [Fact]
    [Trait("TestCase", "T18")]
    public Task T18_AShownReminderIsNotRepeatedAfterReopeningButTheNextOneStillFires() => InAppAsync(async window =>
    {
        var calendar = await CreateLocalCalendarAsync(window, NewTitle());
        var first = NewTitle() + " a";
        var second = NewTitle() + " b";
        await CreateEventAsync(window, new EventSpec(first) { CalendarId = calendar, Start = InMinutes(31) });
        await CreateEventAsync(window, new EventSpec(second) { CalendarId = calendar, Start = InMinutes(34) });

        var toast = await WaitForAsync(() => ToastFor(first), "la prima notifica", NotificationWait);
        Click(FindButton(toast, "Chiudi"));
        await WaitForAsync(() => ToastFor(first) is null ? window : null, "la chiusura della notifica");

        window = await ReopenAsync(window);

        // The second one fires about 3 minutes after the first; the first must not come back in the meantime.
        var next = await WaitForAsync(() =>
        {
            Assert.Null(ToastFor(first));
            return ToastFor(second);
        }, "la seconda notifica dopo la riapertura", TimeSpan.FromMinutes(5));
        Click(FindButton(next, "Chiudi"));
        await WaitForAsync(() => ToastFor(second) is null ? window : null, "la chiusura della notifica");
        Assert.Null(ToastFor(first));
    });

    [Fact]
    [Trait("TestCase", "T19")]
    public Task T19_ASnoozedReminderComesBackAfterFiveMinutes() => InAppAsync(async window =>
    {
        var calendar = await CreateLocalCalendarAsync(window, NewTitle());
        var title = NewTitle();
        await CreateEventAsync(window, new EventSpec(title) { CalendarId = calendar, Start = InMinutes(31) });

        var toast = await WaitForAsync(() => ToastFor(title), "la notifica del promemoria", NotificationWait);
        Click(FindButton(toast, "Posticipa 5 min"));
        var snoozedAt = DateTime.Now;
        await WaitForAsync(() => ToastFor(title) is null ? window : null, "la chiusura della notifica");

        // It stays away for about five minutes, then shows again (the app checks every 15 seconds).
        await PauseAsync(TimeSpan.FromMinutes(4));
        Assert.Null(ToastFor(title));
        var again = await WaitForAsync(() => ToastFor(title), "la notifica posticipata", TimeSpan.FromMinutes(2));
        var waited = DateTime.Now - snoozedAt;
        Assert.InRange(waited.TotalSeconds, 290, 335);
        Click(FindButton(again, "Chiudi"));
        await WaitForAsync(() => ToastFor(title) is null ? window : null, "la chiusura della notifica");
        await PauseAsync(TimeSpan.FromSeconds(30));
        Assert.Null(ToastFor(title)); // and only once
    });

    [Fact]
    [Trait("TestCase", "T10")]
    public Task T10_TheAlarmRingsAndStops() => InAppAsync(async window =>
    {
        // Semi-automatic: the test brings the app to the alarm; the person listens and says what was heard.
        var calendar = await CreateLocalCalendarAsync(window, NewTitle());
        TellThePerson("Test della sveglia (circa 7 minuti). Tra circa 2 minuti suonerà una sveglia: ascolta e annota quando inizia il suono e quando si ferma. Altri avvisi arriveranno più avanti.");

        // First alarm: it rings, the test turns it off with the button after a few seconds.
        var first = NewTitle() + " a";
        await CreateEventAsync(window, new EventSpec(first)
        {
            CalendarId = calendar, Start = InMinutes(3), Reminders = [new EventReminder(ReminderMethod.Alarm, 1)],
        });
        var toast = await WaitForAsync(() => ToastFor(first), "la notifica della sveglia", NotificationWait);
        Assert.Equal("Sveglia", toast.Title);
        await PauseAsync(TimeSpan.FromSeconds(8)); // the person listens
        Click(FindButton(toast, "Spegni"));
        await WaitForAsync(() => ToastFor(first) is null ? window : null, "la chiusura della sveglia");

        // Second alarm: nobody turns it off; it must stop by itself after at most two minutes, the notification stays.
        var second = NewTitle() + " b";
        await CreateEventAsync(window, new EventSpec(second)
        {
            CalendarId = calendar, Start = InMinutes(3), Reminders = [new EventReminder(ReminderMethod.Alarm, 1)],
        });
        var ringing = await WaitForAsync(() => ToastFor(second), "la seconda sveglia", NotificationWait);
        TellThePerson("Seconda sveglia: lasciala suonare senza spegnerla. Deve fermarsi da sola dopo al massimo 2 minuti.");
        await PauseAsync(TimeSpan.FromMinutes(2) + TimeSpan.FromSeconds(15));
        Assert.NotNull(ToastFor(second)); // the notification is still on screen
        Click(FindButton(ringing, "Spegni"));
        await WaitForAsync(() => ToastFor(second) is null ? window : null, "la chiusura della sveglia");
        TellThePerson("Test della sveglia finito: comunica se e quando hai sentito il suono, e quando si è fermato.");
    });
}
