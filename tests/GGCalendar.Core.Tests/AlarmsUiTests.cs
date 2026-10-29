using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GGCalendar.App.Services;
using GGCalendar.App.ViewModels;
using GGCalendar.App.Views;
using GGCalendar.Core.Localization;
using GGCalendar.Core.Models;
using GGCalendar.Core.Reminders;
using GGCalendar.Core.Tests.Support;
using GGCalendar.Data;
using static GGCalendar.Core.Tests.Support.AppDriver;

namespace GGCalendar.Core.Tests;

/// <summary>The button and the window of the alarms on the real app (issue #18): no Google account and no network; the clock of the reminders is moved by the test.</summary>
[Collection(CollectionName)]
public class AlarmsUiTests
{
    private static readonly string Word = "sveglia" + Guid.NewGuid().ToString("N")[..6];

    private static DateTime _clock;

    private static Task InAlarmsAppAsync(Func<MainWindow, Task> test) => HeadlessApp.RunAsync(async () =>
    {
        var settingsFile = new SettingsStore().FilePath;
        var original = File.Exists(settingsFile) ? File.ReadAllBytes(settingsFile) : null;
        var language = L.Language;
        _clock = DateTime.Now;
        MainWindow.ReminderClock = () => _clock;
        var window = OpenApp();
        var visibility = new Dictionary<string, bool>();
        try
        {
            Clean(window);
            foreach (var c in Vm(window).Calendars) visibility[c.Id] = c.IsVisible;
            Vm(window).Settings.DefaultReminderSound = null; // the tests ring in silence
            await test(window);
        }
        finally
        {
            var last = CurrentWindow ?? window;
            foreach (var toast in Toasts().ToList()) toast.Close();
            last.Ringer.RemoveAll();
            Clean(last);
            foreach (var c in Vm(last).Calendars.Where(c => visibility.TryGetValue(c.Id, out var v) && c.IsVisible != v).ToList()) c.IsVisible = visibility[c.Id];
            last.Close();
            MainWindow.ReminderClock = null;
            L.Apply(language);
            if (original is not null) File.WriteAllBytes(settingsFile, original);
        }
    });

    private static void Clean(MainWindow window)
    {
        var vm = Vm(window);
        foreach (var ev in vm.AllEvents.Where(e => e.Title.Contains(Word, StringComparison.Ordinal) && e.SeriesMasterId is null).ToList()) vm.DeleteEvent(ev);
        foreach (var c in vm.Calendars.Where(c => c.IsLocal && c.Name.StartsWith(TestPrefix, StringComparison.Ordinal)).ToList()) vm.DeleteLocalCalendar(c.Id);
    }

    private static string LocalCalendar(MainWindow window) => Vm(window).Calendars.First(c => c.IsLocal && !c.Name.StartsWith(TestPrefix, StringComparison.Ordinal)).Id;

    private static EventReminder Alarm(int minutes) => new(ReminderMethod.Alarm, minutes);

    private static CalendarEvent Add(MainWindow window, string title, TimeSpan startsIn, string? calendar = null, params EventReminder[] reminders)
    {
        var start = _clock + startsIn;
        var ev = new CalendarEvent(Guid.NewGuid().ToString("N"), calendar ?? LocalCalendar(window), Word + " " + title, start, start.AddHours(1)) { Reminders = reminders };
        Vm(window).AddEvent(ev);
        Dispatcher.UIThread.RunJobs();
        return ev;
    }

    /// <summary>Hides every calendar, so that only what the test adds is in the list (the state is put back at the end).</summary>
    private static void HideAll(MainWindow window)
    {
        foreach (var c in Vm(window).Calendars.ToList()) c.IsVisible = false;
        Dispatcher.UIThread.RunJobs();
    }

    private static Button AlarmsButton(MainWindow window) => window.FindControl<Button>("AlarmsButton")!;

    private static string Tip(MainWindow window) => (string)ToolTip.GetTip(AlarmsButton(window))!;

    private static string Badge(MainWindow window) => window.FindControl<TextBlock>("AlarmsBadgeText")!.Text!;

    /// <summary>Presses the button of the alarms and returns the controls of the window with the given identifier.</summary>
    private static async Task<AlarmsPanel> OpenListAsync(MainWindow window)
    {
        var button = AlarmsButton(window);
        if (window.AlarmsList is not { } panel) throw new Xunit.Sdk.XunitException("La finestra delle sveglie non c'è.");
        if (!button.IsEffectivelyEnabled) throw new Xunit.Sdk.XunitException("Il pulsante delle sveglie è grigio.");
        button.Flyout!.ShowAt(button); // what a click does (raising Click does not open the flyout)
        await PauseAsync(TimeSpan.FromMilliseconds(100));
        Dispatcher.UIThread.RunJobs();
        return panel;
    }

    private static List<Control> InList(AlarmsPanel panel, string id) =>
        panel.GetLogicalDescendants().OfType<Control>().Where(c => Avalonia.Automation.AutomationProperties.GetAutomationId(c) == id).ToList();

    private static List<Border> Rows(AlarmsPanel panel) => InList(panel, "Alarms.Row").OfType<Border>().ToList();

    private static List<string> TitlesIn(Control row) => row.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text ?? "").ToList();

    private static List<string> Titles(AlarmsPanel panel) =>
        Rows(panel).Select(r => TitlesIn(r).First(t => t.Contains(Word, StringComparison.Ordinal))).ToList();

    private static void Press(Control control)
    {
        if (control is Button button) button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }

    private static void ClickRow(Border row, MainWindow window)
    {
        var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, true);
        var properties = new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased);
        row.RaiseEvent(new PointerReleasedEventArgs(row, pointer, window, new Point(5, 5), 0, properties, KeyModifiers.None, MouseButton.Left)
        {
            RoutedEvent = InputElement.PointerReleasedEvent,
        });
        Dispatcher.UIThread.RunJobs();
    }

    private static void FireAt(MainWindow window, TimeSpan after)
    {
        _clock += after;
        window.Scheduler!.Check();
        Dispatcher.UIThread.RunJobs();
    }

    // ---- T212: the button ----

    [Fact]
    [Trait("TestCase", "T212")]
    public Task T212_TheButtonIsGreyWithoutAlarmsAndLivesWhenThereAreAndShowsTheirNumber() => InAlarmsAppAsync(async window =>
    {
        HideAll(window);
        var button = AlarmsButton(window);
        Assert.False(button.IsEnabled);
        Assert.Equal(L.T("alarms.tip_none"), Tip(window));
        Assert.False(window.FindControl<Border>("AlarmsBadge")!.IsVisible);

        Add(window, "uno", TimeSpan.FromHours(3), null, Alarm(10));
        Vm(window).Calendars.First(c => c.Id == LocalCalendar(window)).IsVisible = true;
        Dispatcher.UIThread.RunJobs();
        Assert.True(button.IsEnabled);
        Assert.Equal(L.T("alarms.tip"), Tip(window));
        Assert.Equal("1", Badge(window));
        Assert.NotNull(window.FindControl<TextBlock>("AlarmsGlyph")!.Foreground); // a null brush makes the icon invisible (it was, once)

        Add(window, "due", TimeSpan.FromHours(4), null, Alarm(10), Alarm(60));
        Assert.Equal("3", Badge(window)); // an event with two alarms counts twice
        Assert.NotNull(await OpenListAsync(window));
    });

    [Fact]
    [Trait("TestCase", "T225")]
    public Task T225_WithTheNotificationsOffTheButtonIsGreyAndSaysSo() => InAlarmsAppAsync(async window =>
    {
        Add(window, "uno", TimeSpan.FromHours(3), null, Alarm(10));
        Assert.True(AlarmsButton(window).IsEnabled);

        window.Scheduler!.Enabled = false;
        Dispatcher.UIThread.RunJobs();
        Assert.False(AlarmsButton(window).IsEnabled);
        Assert.Equal(L.T("alarms.tip_off"), Tip(window));

        window.Scheduler.Enabled = true;
        Dispatcher.UIThread.RunJobs();
        Assert.True(AlarmsButton(window).IsEnabled);
        await Task.CompletedTask;
    });

    // ---- T213: the list ----

    [Fact]
    [Trait("TestCase", "T213")]
    public Task T213_TheListShowsOnlyAlarmsSoonestFirstWithTheTimeLeft() => InAlarmsAppAsync(async window =>
    {
        HideAll(window);
        Vm(window).Calendars.First(c => c.Id == LocalCalendar(window)).IsVisible = true;
        Add(window, "tardi", TimeSpan.FromDays(3), null, Alarm(60));
        Add(window, "presto", TimeSpan.FromHours(5), null, Alarm(30));
        Add(window, "popup", TimeSpan.FromHours(6), null, new EventReminder(ReminderMethod.Popup, 10));
        Add(window, "tuttogiorno", TimeSpan.FromHours(30), null, Alarm(15));

        var panel = await OpenListAsync(window);

        Assert.Equal([Word + " presto", Word + " tuttogiorno", Word + " tardi"], Titles(panel));
        Assert.All(panel.GetLogicalDescendants().OfType<TextBlock>().Where(t => t.FontFamily.Name.Contains("Segoe")), t => Assert.NotNull(t.Foreground)); // the icons of the rows can be seen
        var remaining = InList(panel, "Alarms.Remaining").OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Equal(3, remaining.Count);
        Assert.Equal(AlarmsViewModel.Remaining(TimeSpan.FromHours(5) - TimeSpan.FromMinutes(30)), remaining[0]);
        Assert.Contains(L.T("alarms.section_coming"), panel.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text));
        Assert.Empty(InList(panel, "Alarms.StopAll").OfType<Button>().Where(b => b.IsVisible)); // nothing rings
    });

    [Fact]
    [Trait("TestCase", "T214")]
    public Task T214_ASnoozedAlarmIsInTheListMarkedAsPutOff() => InAlarmsAppAsync(async window =>
    {
        HideAll(window);
        Vm(window).Calendars.First(c => c.Id == LocalCalendar(window)).IsVisible = true;
        Add(window, "rinviata", TimeSpan.FromMinutes(40), null, Alarm(10));
        FireAt(window, TimeSpan.FromMinutes(31));
        var toast = Toasts().Single(t => ShowsTitle(t, Word + " rinviata"));
        Click(FindButton(toast, "Reminder.Snooze"));
        await WaitForAsync(() => Toasts().Any(t => ShowsTitle(t, Word + " rinviata")) ? null : window, "la chiusura della notifica");
        Assert.Empty(window.Ringer.Items);

        var panel = await OpenListAsync(window);

        var row = Assert.Single(Rows(panel));
        Assert.Contains(L.T("alarms.snoozed"), TitlesIn(row));
        Assert.Contains(AlarmsViewModel.Remaining(TimeSpan.FromMinutes(5)), TitlesIn(row));
    });

    // ---- T215: cancel an alarm ----

    [Fact]
    [Trait("TestCase", "T215")]
    public Task T215_CancellingAnAlarmAsksFirstThenRemovesItAndTheEventStays() => InAlarmsAppAsync(async window =>
    {
        HideAll(window);
        Vm(window).Calendars.First(c => c.Id == LocalCalendar(window)).IsVisible = true;
        var ev = Add(window, "annulla", TimeSpan.FromHours(3), null, Alarm(10));
        var panel = await OpenListAsync(window);

        Press(InList(panel, "Alarms.Cancel").Single());
        Assert.Single(InList(panel, "Alarms.Confirm"));
        Assert.Contains(L.T("alarms.confirm_text", ev.Title), panel.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text));

        Press(InList(panel, "Alarms.Keep").Single()); // "Mantieni"
        Assert.Empty(InList(panel, "Alarms.Confirm"));
        Assert.Single(Rows(panel));
        Assert.Equal("1", Badge(window));

        Press(InList(panel, "Alarms.Cancel").Single());
        Press(InList(panel, "Alarms.CancelConfirm").Single());

        Assert.Empty(Rows(panel));
        Assert.Equal(L.T("alarms.empty"), panel.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text).First(t => t == L.T("alarms.empty")));
        Assert.False(AlarmsButton(window).IsEnabled);
        Assert.True(Vm(window).Status?.Is("alarms.cancelled") == true);
        Assert.Contains(Vm(window).AllEvents, e => e.Id == ev.Id); // the event is still in the calendar

        FireAt(window, TimeSpan.FromMinutes(40)); // and the alarm does not ring
        Assert.Empty(Toasts().Where(t => ShowsTitle(t, ev.Title)));
    });

    // ---- T216: shown and hidden calendars ----

    [Fact]
    [Trait("TestCase", "T216")]
    public Task T216_HidingAndShowingACalendarChangesTheListAtOnce() => InAlarmsAppAsync(async window =>
    {
        HideAll(window);
        var calendar = Vm(window).Calendars.First(c => c.Id == LocalCalendar(window));
        calendar.IsVisible = true;
        Add(window, "nascondi", TimeSpan.FromHours(3), calendar.Id, Alarm(10));
        var panel = await OpenListAsync(window);
        Assert.Single(Rows(panel));

        calendar.IsVisible = false;
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(Rows(panel));
        Assert.False(AlarmsButton(window).IsEnabled);
        Assert.Equal(L.T("alarms.tip_none"), Tip(window));

        calendar.IsVisible = true;
        Dispatcher.UIThread.RunJobs();
        Assert.Single(Rows(panel));
        Assert.Equal("1", Badge(window));
    });

    [Fact]
    [Trait("TestCase", "T216")]
    public Task T216_ThereminderOfAHiddenCalendarDoesNotRing() => InAlarmsAppAsync(async window =>
    {
        HideAll(window);
        var calendar = Vm(window).Calendars.First(c => c.Id == LocalCalendar(window));
        Add(window, "muta", TimeSpan.FromMinutes(40), calendar.Id, Alarm(10));

        FireAt(window, TimeSpan.FromMinutes(31));

        Assert.Empty(Toasts().Where(t => ShowsTitle(t, Word + " muta")));
        Assert.Empty(window.Ringer.Items);
        await Task.CompletedTask;
    });

    [Fact]
    [Trait("TestCase", "T217")]
    public Task T217_ASnoozedAlarmOfAHiddenCalendarStillRingsAndIsMarked() => InAlarmsAppAsync(async window =>
    {
        HideAll(window);
        var calendar = Vm(window).Calendars.First(c => c.Id == LocalCalendar(window));
        calendar.IsVisible = true;
        Add(window, "nascosta", TimeSpan.FromMinutes(40), calendar.Id, Alarm(10));
        FireAt(window, TimeSpan.FromMinutes(31));
        Click(FindButton(Toasts().Single(t => ShowsTitle(t, Word + " nascosta")), "Reminder.Snooze"));
        await WaitForAsync(() => Toasts().Any(t => ShowsTitle(t, Word + " nascosta")) ? null : window, "la chiusura della notifica");

        calendar.IsVisible = false;
        Dispatcher.UIThread.RunJobs();
        Assert.True(AlarmsButton(window).IsEnabled); // the snoozed alarm is still there
        var panel = await OpenListAsync(window);
        Assert.Contains(L.T("alarms.snoozed_hidden"), TitlesIn(Assert.Single(Rows(panel))));

        FireAt(window, TimeSpan.FromMinutes(6));

        Assert.Contains(Toasts(), t => ShowsTitle(t, Word + " nascosta"));
        Assert.Single(window.Ringer.Items);
    });

    // ---- T218-T220: the alarms that ring ----

    [Fact]
    [Trait("TestCase", "T218")]
    public Task T218_AlarmsThatRingTogetherAreShownInASectionOneAtATimeAndTheButtonIsRed() => InAlarmsAppAsync(async window =>
    {
        HideAll(window);
        Vm(window).Calendars.First(c => c.Id == LocalCalendar(window)).IsVisible = true;
        Add(window, "primo", TimeSpan.FromMinutes(40), null, Alarm(10));
        Add(window, "secondo", TimeSpan.FromMinutes(40), null, Alarm(10));
        Add(window, "terzo", TimeSpan.FromHours(5), null, Alarm(10));

        FireAt(window, TimeSpan.FromMinutes(31));

        Assert.Equal([RingState.Ringing, RingState.Queued], window.Ringer.Items.Select(i => i.State).ToArray());
        Assert.Equal("3", Badge(window));
        var panel = await OpenListAsync(window);
        Assert.Contains(L.T("alarms.section_ringing"), panel.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text));
        Assert.Equal(2, InList(panel, "Alarms.Stop").Count);
        Assert.Single(InList(panel, "Alarms.Cancel")); // the one that does not ring yet
        var rows = Rows(panel);
        Assert.Contains(L.T("alarms.state_ringing"), TitlesIn(rows[0]));
        Assert.Contains(L.T("alarms.state_queued"), TitlesIn(rows[1]));
        Assert.True(InList(panel, "Alarms.StopAll").OfType<Button>().Single().IsVisible);
    });

    [Fact]
    [Trait("TestCase", "T219")]
    public Task T219_TurningOffTheRingingAlarmClosesItsNotificationAndTheNextOneRings() => InAlarmsAppAsync(async window =>
    {
        HideAll(window);
        Vm(window).Calendars.First(c => c.Id == LocalCalendar(window)).IsVisible = true;
        Add(window, "primo", TimeSpan.FromMinutes(40), null, Alarm(10));
        Add(window, "secondo", TimeSpan.FromMinutes(40), null, Alarm(10));
        FireAt(window, TimeSpan.FromMinutes(31));
        var first = window.Ringer.Current!.Reminder.Event.Title;
        var panel = await OpenListAsync(window);

        Press(InList(panel, "Alarms.Stop").First());

        Assert.Single(window.Ringer.Items);
        Assert.Equal(RingState.Ringing, window.Ringer.Items[0].State);
        Assert.NotEqual(first, window.Ringer.Current!.Reminder.Event.Title);
        Assert.Null(ToastFor(first));
        Assert.Single(InList(panel, "Alarms.Stop"));
    });

    [Fact]
    [Trait("TestCase", "T219")]
    public Task T219_TurnOffAllClosesAllNotificationsAndTheButtonGoesGrey() => InAlarmsAppAsync(async window =>
    {
        HideAll(window);
        Vm(window).Calendars.First(c => c.Id == LocalCalendar(window)).IsVisible = true;
        Add(window, "primo", TimeSpan.FromMinutes(40), null, Alarm(10));
        Add(window, "secondo", TimeSpan.FromMinutes(40), null, Alarm(10));
        FireAt(window, TimeSpan.FromMinutes(31));
        var panel = await OpenListAsync(window);

        Press(InList(panel, "Alarms.StopAll").Single());

        Assert.Empty(window.Ringer.Items);
        Assert.Empty(Toasts().Where(t => ShowsTitle(t, Word + " primo") || ShowsTitle(t, Word + " secondo")));
        Assert.Empty(Rows(panel));
        Assert.False(AlarmsButton(window).IsEnabled);
    });

    [Fact]
    [Trait("TestCase", "T219")]
    public Task T219_TurningOffFromTheNotificationRemovesTheAlarmFromTheList() => InAlarmsAppAsync(async window =>
    {
        HideAll(window);
        Vm(window).Calendars.First(c => c.Id == LocalCalendar(window)).IsVisible = true;
        Add(window, "unico", TimeSpan.FromMinutes(40), null, Alarm(10));
        FireAt(window, TimeSpan.FromMinutes(31));
        Assert.Single(window.Ringer.Items);

        Click(FindButton(Toasts().Single(t => ShowsTitle(t, Word + " unico")), "Reminder.Dismiss"));
        await WaitForAsync(() => window.Ringer.Items.Count == 0 ? window : null, "lo spegnimento");

        Assert.False(AlarmsButton(window).IsEnabled);
    });

    [Fact]
    [Trait("TestCase", "T220")]
    public Task T220_WhenTheSoundEndsTheAlarmIsStillInTheListUntilItIsTurnedOff() => InAlarmsAppAsync(async window =>
    {
        HideAll(window);
        Vm(window).Calendars.First(c => c.Id == LocalCalendar(window)).IsVisible = true;
        Add(window, "lunga", TimeSpan.FromMinutes(40), null, Alarm(10));
        FireAt(window, TimeSpan.FromMinutes(31));

        window.Ringer.Silence(window.Ringer.Current!.Key); // the time limit of the sound has passed
        Dispatcher.UIThread.RunJobs();

        var panel = await OpenListAsync(window);
        var row = Assert.Single(Rows(panel));
        Assert.Contains(L.T("alarms.state_silenced"), TitlesIn(row));
        Assert.Contains(L.T("alarms.detail_silenced"), TitlesIn(row));
        Assert.Single(InList(panel, "Alarms.Stop"));
        Assert.Equal(RingState.Silenced, window.Ringer.Items.Single().State);
    });

    // ---- T221: a row opens the event ----

    [Fact]
    [Trait("TestCase", "T221")]
    public Task T221_PressingARowShowsTheCardOfTheEvent() => InAlarmsAppAsync(async window =>
    {
        HideAll(window);
        Vm(window).Calendars.First(c => c.Id == LocalCalendar(window)).IsVisible = true;
        var ev = Add(window, "apri", TimeSpan.FromHours(3), null, Alarm(10));
        var panel = await OpenListAsync(window);

        ClickRow(Rows(panel).Single(), window);

        var card = await WaitForAsync(() => window.FindControl<Popup>("DetailsPopup")?.Child as EventDetailsCard, "la scheda dell'evento");
        Assert.Contains(card.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == ev.Title);
    });

    // ---- T222: the five languages ----

    [Theory]
    [InlineData("it")]
    [InlineData("en")]
    [InlineData("fr")]
    [InlineData("de")]
    [InlineData("es")]
    [Trait("TestCase", "T222")]
    public Task T222_TheTextsAreInTheChosenLanguage(string language) => InAlarmsAppAsync(async window =>
    {
        HideAll(window);
        Vm(window).Calendars.First(c => c.Id == LocalCalendar(window)).IsVisible = true;
        Add(window, "lingua", TimeSpan.FromHours(3), null, Alarm(30));
        L.Apply(language);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(L.T("alarms.tip"), Tip(window));

        var panel = await OpenListAsync(window);
        var texts = panel.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains(L.T("alarms.title"), texts);
        Assert.Contains(L.T("alarms.section_coming"), texts);
        Assert.Contains("ⓘ " + L.T("alarms.only_open"), texts);
        Assert.DoesNotContain(texts, t => t?.StartsWith("alarms.", StringComparison.Ordinal) == true); // no key left without its text
    });

    // ---- T330: the alarm that rings uses the time and the sound of the settings (issue #32) ----

    [Fact]
    [Trait("TestCase", "T330")]
    public Task T330_AnAlarmRingsForTheTimeSetInTheSettingsAndTheChangeCountsAtOnce() => InAlarmsAppAsync(async window =>
    {
        HideAll(window);
        Vm(window).Calendars.First(c => c.Id == LocalCalendar(window)).IsVisible = true;
        Add(window, "prima", TimeSpan.FromMinutes(40), null, Alarm(10));
        Add(window, "seconda", TimeSpan.FromMinutes(40), null, Alarm(10));

        Vm(window).Settings.AlarmDurationMinutes = 7;
        FireAt(window, TimeSpan.FromMinutes(31));
        Assert.Equal(TimeSpan.FromMinutes(7), window.AlarmLimit);

        // The second alarm waits in line and starts when the first is silenced: with the time set at that moment.
        Vm(window).Settings.AlarmDurationMinutes = 3;
        window.Ringer.Silence(window.Ringer.Current!.Key);
        Assert.Equal(TimeSpan.FromMinutes(3), window.AlarmLimit);
        await Task.CompletedTask;
    });

    [Fact]
    [Trait("TestCase", "T330")]
    public Task T330_TheTimeIsAlwaysBetweenOneAndTenMinutes() => InAlarmsAppAsync(async window =>
    {
        HideAll(window);
        Vm(window).Calendars.First(c => c.Id == LocalCalendar(window)).IsVisible = true;
        Add(window, "fuori limite", TimeSpan.FromMinutes(40), null, Alarm(10));
        Vm(window).Settings.AlarmDurationMinutes = 600; // a file edited by hand
        FireAt(window, TimeSpan.FromMinutes(31));
        Assert.Equal(TimeSpan.FromMinutes(10), window.AlarmLimit);
        await Task.CompletedTask;
    });

    [Fact]
    [Trait("TestCase", "T330")]
    public Task T330_AnAlarmPlaysTheSoundOfTheAlarmsAndAPopupTheOtherOne() => InAlarmsAppAsync(async window =>
    {
        HideAll(window);
        Vm(window).Calendars.First(c => c.Id == LocalCalendar(window)).IsVisible = true;
        var alarmFile = Path.Combine(Path.GetTempPath(), "ggcal-sveglia-" + Guid.NewGuid().ToString("N")[..8] + ".wav");
        var popupFile = Path.Combine(Path.GetTempPath(), "ggcal-popup-" + Guid.NewGuid().ToString("N")[..8] + ".wav");
        File.Copy(Path.Combine(SoundService.MediaFolder, "Alarm01.wav"), alarmFile);
        File.Copy(Path.Combine(SoundService.MediaFolder, "Alarm01.wav"), popupFile);
        try
        {
            Vm(window).Settings.DefaultAlarmSound = alarmFile;
            Vm(window).Settings.DefaultReminderSound = popupFile;
            Add(window, "sveglia", TimeSpan.FromMinutes(40), null, Alarm(10));
            Add(window, "muta", TimeSpan.FromMinutes(40), null, new EventReminder(ReminderMethod.Alarm, 10, EventReminder.NoSound));
            Add(window, "propria", TimeSpan.FromMinutes(40), null, new EventReminder(ReminderMethod.Alarm, 10, popupFile));
            FireAt(window, TimeSpan.FromMinutes(31));

            string SoundOf(string title) => window.Ringer.Items.Single(i => i.Reminder.Event.Title.EndsWith(" " + title, StringComparison.Ordinal)).Sound!;
            Assert.Equal(3, window.Ringer.Items.Count);
            Assert.Equal(alarmFile, SoundOf("sveglia")); // the default of the alarms, not the one of the popups
            Assert.Equal(alarmFile, SoundOf("muta")); // an alarm "without sound" still rings: with the sound of the alarms
            Assert.Equal(popupFile, SoundOf("propria")); // an alarm with a sound of its own plays that one
        }
        finally
        {
            window.Ringer.RemoveAll(); // the sound stops and lets go of the files
            Dispatcher.UIThread.RunJobs();
            foreach (var f in new[] { alarmFile, popupFile })
            {
                try { File.Delete(f); } catch (IOException) { } // a temporary file: if the player still holds it, the system cleans it later
            }
        }
    });

    [Fact]
    [Trait("TestCase", "T330")]
    public Task T330_AnAlarmWhoseFileIsGoneRingsWithTheSoundOfWindows() => InAlarmsAppAsync(async window =>
    {
        HideAll(window);
        Vm(window).Calendars.First(c => c.Id == LocalCalendar(window)).IsVisible = true;
        Vm(window).Settings.DefaultAlarmSound = Path.Combine(Path.GetTempPath(), "ggcal-sparito-" + Guid.NewGuid().ToString("N") + ".mp3");
        Add(window, "sparito", TimeSpan.FromMinutes(40), null, Alarm(10));
        FireAt(window, TimeSpan.FromMinutes(31));

        var expected = File.Exists(ReminderSounds.SystemAlarmSound) ? ReminderSounds.SystemAlarmSound : null;
        Assert.Equal(expected, window.Ringer.Current!.Sound);
        await Task.CompletedTask;
    });
}
