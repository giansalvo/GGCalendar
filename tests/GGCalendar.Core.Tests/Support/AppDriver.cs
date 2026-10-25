using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GGCalendar.App.ViewModels;
using GGCalendar.App.Views;
using GGCalendar.Core.Models;
using GGCalendar.Data;
using Xunit.Sdk;

namespace GGCalendar.Core.Tests.Support;

/// <summary>What the user fills in when creating an event with the "Crea" button.</summary>
public sealed record EventSpec(string Title)
{
    public string CalendarId { get; init; } = AppDriver.TestCalendarId;
    public string? ColorHex { get; init; }
    /// <summary>RRULE of the repetition ("FREQ=DAILY"), or null for "Non si ripete".</summary>
    public string? Recurrence { get; init; }
    public bool AllDay { get; init; }
    /// <summary>Start of the event; for an all-day event only the date counts. Null keeps what the app proposes.</summary>
    public DateTime? Start { get; init; }
    /// <summary>Last day of an all-day event (inclusive, as shown in the editor).</summary>
    public DateTime? LastDay { get; init; }
    /// <summary>Replaces the standard reminders when set.</summary>
    public IReadOnlyList<EventReminder>? Reminders { get; init; }
}

/// <summary>
/// Drives the app's own windows like a user would (headless, on the real installed data) and gives the tests the few
/// helpers they all need. The tests that use it run one at a time (collection "RealApp") and with the app closed.
/// </summary>
public static class AppDriver
{
    public const string TestAccount = "carta.andrea.2000@gmail.com";
    public const string TestCalendarId = TestAccount + "|" + TestAccount;
    public const string TestPrefix = "[TEST GGCAL]";
    public const string CollectionName = "RealApp";

    public static readonly TimeSpan SyncTimeout = TimeSpan.FromSeconds(90);

    // One id per test session, so the events of a session can be told from those of an interrupted one.
    private static readonly string SessionId = Guid.NewGuid().ToString("N")[..6];

    /// <summary>A title for the n-th event of a test that needs many: the prefix, the session and a number.</summary>
    public static string NewTitle(int number) => $"{TestPrefix} {SessionId} {number:D3}";

    public static string NewTitle() =>
        $"{TestPrefix} {SessionId} {new string(Enumerable.Range(0, 6).Select(_ => (char)('a' + Random.Shared.Next(26))).ToArray())}";

    // The app

    // The window of the app that is open now: after a reopen it is the new one, and it is the one to clean up and close.
    private static MainWindow? _current;

    public static MainWindow OpenApp()
    {
        var window = new MainWindow { DataContext = new MainViewModel(new CalendarStore()) };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        _current = window;
        return window;
    }

    /// <summary>
    /// Runs a test on the real app: opens it, removes what an earlier run left, runs <paramref name="test"/>, and always
    /// cleans up and closes the app (also after a reopen) when it ends.
    /// </summary>
    public static Task InAppAsync(Func<MainWindow, Task> test) => HeadlessApp.RunAsync(async () =>
    {
        // The app saves the view it is left on as the default one: the tests switch views to read the events, so the
        // settings of the test folder are put back as they were when the test ends.
        var settingsFile = new SettingsStore().FilePath;
        var originalSettings = File.Exists(settingsFile) ? File.ReadAllBytes(settingsFile) : null;
        var window = OpenApp();
        try
        {
            await WaitForReadyAsync(window);
            await CleanUpAsync(window);
            await test(window);
        }
        finally
        {
            var last = _current ?? window;
            try
            {
                await CleanUpAsync(last);
            }
            finally
            {
                last.Close();
                _current = null;
                if (originalSettings is not null) File.WriteAllBytes(settingsFile, originalSettings);
            }
        }
    });

    public static MainViewModel Vm(MainWindow window) => (MainViewModel)window.DataContext!;

    /// <summary>Closes the app the way a user does and opens it again on the same data.</summary>
    public static async Task<MainWindow> ReopenAsync(MainWindow window)
    {
        window.Close();
        var reopened = OpenApp();
        await WaitForQuietAsync(reopened);
        return reopened;
    }

    // Waiting

    /// <summary>Lets the windows work until <paramref name="probe"/> returns something.</summary>
    public static async Task<T> WaitForAsync<T>(Func<T?> probe, string what, TimeSpan? timeout = null) where T : class
    {
        var until = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(15));
        while (DateTime.UtcNow < until)
        {
            Dispatcher.UIThread.RunJobs();
            if (probe() is { } found) return found;
            await Task.Delay(50);
        }

        throw new XunitException($"Tempo scaduto in attesa di: {what}.");
    }

    public static async Task PauseAsync(TimeSpan time)
    {
        var until = DateTime.UtcNow + time;
        while (DateTime.UtcNow < until)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(50);
        }
    }

    /// <summary>The test folder starts empty: waits for the first sync to bring in the calendars of the test account.</summary>
    private static Task WaitForReadyAsync(MainWindow window)
    {
        var vm = Vm(window);
        return WaitForAsync(() =>
        {
            if (!vm.IsSyncing && vm.SyncStatus.StartsWith("Errore", StringComparison.Ordinal))
                throw new XunitException($"La prima sincronizzazione dell'account di prova è fallita ({vm.SyncStatus}). Se l'account è stato scollegato o il suo accesso revocato, ricollegalo nell'app installata e rilancia i test.");
            return !vm.IsSyncing && vm.Calendars.Any(c => c.Id == TestCalendarId) ? vm : null;
        }, "il calendario dell'account di prova (prima sincronizzazione)", SyncTimeout);
    }

    public static Task WaitForQuietAsync(MainWindow window)
    {
        var vm = Vm(window);
        return WaitForAsync(() => vm.IsSyncing ? null : vm, "la fine della sincronizzazione", SyncTimeout);
    }

    // Buttons and dialogs

    public static Button FindButton(Visual root, Func<Button, bool> match) =>
        root.GetVisualDescendants().OfType<Button>().FirstOrDefault(match)
        ?? throw new XunitException("Pulsante non trovato nella finestra.");

    public static Button FindButton(Visual root, string content) => FindButton(root, b => b.Content as string == content);

    public static void Click(Button button)
    {
        Assert.True(button.IsEnabled, "Il pulsante è disattivato.");
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Waits for any sync the app started by itself, presses "Sincronizza ora" and waits for that sync to end well.</summary>
    public static async Task SyncAsync(MainWindow window)
    {
        var vm = Vm(window);
        await WaitForQuietAsync(window);
        Click(FindButton(window, b => ToolTip.GetTip(b) as string == "Sincronizza ora"));
        Assert.True(vm.IsSyncing, "Il pulsante \"Sincronizza ora\" non ha avviato nessuna sincronizzazione (account non collegato?).");
        await WaitForQuietAsync(window);
        Assert.StartsWith("Sincronizzato", vm.SyncStatus);
    }

    // Events

    /// <summary>The "Crea" button, the editor filled in as <paramref name="spec"/> says, and "Salva". Returns the new event.</summary>
    public static async Task<CalendarEvent> CreateEventAsync(MainWindow window, EventSpec spec)
    {
        Click(FindButton(window, "＋  Crea"));
        var dialog = await WaitForAsync(() => window.OwnedWindows.OfType<EventEditWindow>().FirstOrDefault(), "la finestra dell'evento");
        var editor = (EventEditViewModel)dialog.DataContext!;
        dialog.FindControl<TextBox>("TitleBox")!.Text = spec.Title;

        var calendars = dialog.GetVisualDescendants().OfType<ComboBox>().First(c => c.ItemsSource is IReadOnlyList<CalendarItemViewModel>);
        calendars.SelectedItem = editor.Calendars.FirstOrDefault(c => c.Id == spec.CalendarId)
            ?? throw new XunitException($"Il calendario {spec.CalendarId} non è nell'app: collegare e sincronizzare l'account.");
        Assert.Equal(spec.CalendarId, editor.SelectedCalendar?.Id);

        if (spec.AllDay) editor.IsAllDay = true;
        if (spec.Start is { } start)
        {
            editor.StartDate = start.Date;
            if (!spec.AllDay) editor.StartTime = start.TimeOfDay;
        }

        if (spec.AllDay && spec.LastDay is { } last) editor.EndDate = last.Date;
        if (spec.Recurrence is { } rule) editor.Recurrence = editor.RecurrenceOptions.First(o => o.Value == rule);
        if (spec.Reminders is { } reminders)
        {
            editor.Reminders.Clear();
            foreach (var r in reminders) editor.Reminders.Add(new ReminderItemViewModel(r, editor.SoundOptions));
        }

        if (spec.ColorHex is { } hex)
        {
            var colors = dialog.GetVisualDescendants().OfType<ComboBox>().First(c => c.ItemsSource is IReadOnlyList<ColorOption>);
            colors.SelectedItem = editor.ColorOptions.First(c => string.Equals(c.Hex, hex, StringComparison.OrdinalIgnoreCase));
        }

        Click(FindButton(dialog, "Salva"));
        await WaitForAsync(() => window.OwnedWindows.Count == 0 ? window : null, "la chiusura dell'editor");
        return Vm(window).AllEvents.Single(e => e.Title == spec.Title);
    }

    /// <summary>The event in the agenda (the view that lists the days one by one) on <paramref name="day"/>.</summary>
    public static async Task<Border> FindInAgendaAsync(MainWindow window, string title, DateTime day)
    {
        var vm = Vm(window);
        vm.ViewModeIndex = 4;
        vm.SelectedDate = day.Date;
        Dispatcher.UIThread.RunJobs();
        return await WaitForAsync(() => window.GetVisualDescendants().OfType<Border>()
            .FirstOrDefault(b => b.DataContext is AgendaItem a && a.Event.Title == title && a.Event.Start.Date == day.Date),
            $"\"{title}\" nel programma del {day:dd/MM/yyyy}");
    }

    /// <summary>What the user reads in the agenda for the 30 days from <paramref name="from"/>: the events whose title starts with <paramref name="titleStart"/>.</summary>
    public static List<(DateTime Day, string Title, DateTime Start)> AgendaOf(MainWindow window, DateTime from, string titleStart)
    {
        var vm = Vm(window);
        vm.ViewModeIndex = 4;
        vm.SelectedDate = from.Date;
        Dispatcher.UIThread.RunJobs();
        return vm.AgendaDays.SelectMany(d => d.Items).Where(i => i.Title.StartsWith(titleStart, StringComparison.Ordinal))
            .Select(i => (i.Event.Start.Date, i.Title, i.Event.Start)).OrderBy(i => i.Start).ToList();
    }

    public static string ColorSeenByTheUser(MainWindow window, string title, DateTime day)
    {
        var vm = Vm(window);
        vm.ViewModeIndex = 4;
        vm.SelectedDate = day.Date;
        Dispatcher.UIThread.RunJobs();
        var item = vm.AgendaDays.SelectMany(d => d.Items).FirstOrDefault(i => i.Title == title && i.Event.Start.Date == day.Date);
        return item?.ColorHex ?? throw new XunitException($"L'evento \"{title}\" non si vede nel programma del {day:dd/MM/yyyy}.");
    }

    /// <summary>Double click on an event of the agenda, as a user does to open it.</summary>
    public static void DoubleClick(MainWindow window, Border item)
    {
        var center = item.TranslatePoint(new Point(item.Bounds.Width / 2, item.Bounds.Height / 2), window)
            ?? throw new XunitException("L'evento non è visibile nella finestra.");
        for (var i = 0; i < 2; i++)
        {
            window.MouseDown(center, MouseButton.Left);
            window.MouseUp(center, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
        }
    }

    /// <summary>Answers the question "which events of the series?" that the app asks for a repeating event.</summary>
    public static async Task ChooseScopeAsync(MainWindow window, SeriesScope scope)
    {
        var question = await WaitForAsync(() => window.OwnedWindows.OfType<SeriesScopeWindow>().FirstOrDefault(), "la domanda su quali eventi della serie");
        question.FindControl<RadioButton>(scope switch { SeriesScope.This => "ThisOne", SeriesScope.ThisAndFollowing => "ThisAndFollowing", _ => "AllEvents" })!.IsChecked = true;
        Click(FindButton(question, "OK"));
    }

    /// <summary>Opens an event (or one occurrence of a series) with a double click on it in the agenda and returns its editor.</summary>
    public static async Task<(EventEditWindow Dialog, EventEditViewModel Editor)> OpenEditorAsync(MainWindow window, string title, DateTime day, SeriesScope? scope = null)
    {
        DoubleClick(window, await FindInAgendaAsync(window, title, day));
        if (scope is { } s) await ChooseScopeAsync(window, s);
        var dialog = await WaitForAsync(() => window.OwnedWindows.OfType<EventEditWindow>().FirstOrDefault(), "la finestra dell'evento");
        return (dialog, (EventEditViewModel)dialog.DataContext!);
    }

    public static async Task CancelEditorAsync(MainWindow window, Window dialog)
    {
        Click(FindButton(dialog, "Annulla"));
        await WaitForAsync(() => window.OwnedWindows.Count == 0 ? window : null, "la chiusura della finestra");
    }

    /// <summary>The trash button of the editor; for a repeating event the app then asks which events to delete.</summary>
    public static async Task DeleteFromEditorAsync(MainWindow window, EventEditWindow dialog, SeriesScope? scope = null)
    {
        Click(FindButton(dialog, b => ToolTip.GetTip(b) as string == "Elimina evento"));
        if (scope is { } s) await ChooseScopeAsync(window, s);
        await WaitForAsync(() => window.OwnedWindows.Count == 0 ? window : null, "la chiusura dell'editor");
    }

    /// <summary>"Nuovo calendario locale" and the name typed in the question. Returns the id of the new calendar.</summary>
    public static async Task<string> CreateLocalCalendarAsync(MainWindow window, string name)
    {
        Click(FindButton(window, "＋  Nuovo calendario locale"));
        var prompt = await WaitForAsync(() => window.OwnedWindows.OfType<PromptWindow>().FirstOrDefault(), "la richiesta del nome del calendario");
        prompt.GetVisualDescendants().OfType<TextBox>().First().Text = name;
        Click(FindButton(prompt, "OK"));
        await WaitForAsync(() => window.OwnedWindows.Count == 0 ? window : null, "la chiusura della richiesta");
        return Vm(window).Calendars.Single(c => c.Name == name).Id;
    }

    /// <summary>Number of days of the agenda (30 from <paramref name="from"/>) that show an event titled <paramref name="title"/>.</summary>
    public static int AgendaDayCount(MainWindow window, DateTime from, string title)
    {
        var vm = Vm(window);
        vm.ViewModeIndex = 4;
        vm.SelectedDate = from.Date;
        Dispatcher.UIThread.RunJobs();
        return vm.AgendaDays.Count(d => d.Items.Any(i => i.Title == title));
    }

    // Reminder notifications

    /// <summary>The reminder notifications on screen (the app keeps them in a private list).</summary>
    public static IReadOnlyList<ReminderNotificationWindow> Toasts() =>
        ((System.Collections.IEnumerable)typeof(ReminderNotificationWindow)
            .GetField("OpenToasts", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.GetValue(null)!)
        .Cast<ReminderNotificationWindow>().ToList();

    public static bool ShowsTitle(ReminderNotificationWindow toast, string title) =>
        toast.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == title);

    public static ReminderNotificationWindow? ToastFor(string title) => Toasts().FirstOrDefault(t => ShowsTitle(t, title));

    /// <summary>A start time that is <paramref name="minutes"/> from now, to the minute.</summary>
    public static DateTime InMinutes(int minutes)
    {
        var t = DateTime.Now.AddMinutes(minutes);
        return new DateTime(t.Year, t.Month, t.Day, t.Hour, t.Minute, 0);
    }

    /// <summary>A message box for the person running a semi-automatic test; the test does not wait for it to be closed.</summary>
    public static void TellThePerson(string text)
    {
        _ = Task.Run(() => MessageBoxW(IntPtr.Zero, text, "Test GGCalendar", 0x40 | 0x40000));
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

    /// <summary>Changes the title in the editor and saves.</summary>
    public static async Task RetitleAsync(MainWindow window, EventEditWindow dialog, string newTitle)
    {
        dialog.FindControl<TextBox>("TitleBox")!.Text = newTitle;
        Click(FindButton(dialog, "Salva"));
        await WaitForAsync(() => window.OwnedWindows.Count == 0 ? window : null, "la chiusura dell'editor");
    }

    // Cleanup

    /// <summary>
    /// Removes what the tests created: in the app and, with a final check, on Google. Events with the test prefix in the test
    /// account's main calendar only; local test calendars (their name starts with the prefix) are deleted too.
    /// </summary>
    public static async Task CleanUpAsync(MainWindow window)
    {
        var vm = Vm(window);
        await WaitForQuietAsync(window);
        var leftovers = vm.AllEvents.Where(e => e.CalendarId == TestCalendarId && e.SeriesMasterId is null
            && e.Title.StartsWith(TestPrefix, StringComparison.Ordinal)).ToList();
        foreach (var ev in leftovers) vm.DeleteEvent(ev);
        foreach (var cal in vm.Calendars.Where(c => c.IsLocal && c.Name.StartsWith(TestPrefix, StringComparison.Ordinal)).ToList())
            vm.DeleteLocalCalendar(cal.Id);
        foreach (var ev in vm.AllEvents.Where(e => e.CalendarId.StartsWith("local-", StringComparison.Ordinal)
            && e.Title.StartsWith(TestPrefix, StringComparison.Ordinal) && e.SeriesMasterId is null).ToList())
            vm.DeleteEvent(ev);
        if (leftovers.Count > 0) await SyncAsync(window);

        using var google = await TestGoogle.ConnectAsync();
        if (await google.DeleteLeftoversAsync() > 0) await SyncAsync(window);
    }
}
