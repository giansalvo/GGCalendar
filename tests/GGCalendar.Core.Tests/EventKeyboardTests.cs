using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GGCalendar.App.Controls;
using GGCalendar.App.ViewModels;
using GGCalendar.App.Views;
using GGCalendar.Core.Models;
using GGCalendar.Core.Subscriptions;
using GGCalendar.Core.Tests.Support;
using GGCalendar.Data;
using static GGCalendar.Core.Tests.Support.AppDriver;

namespace GGCalendar.Core.Tests;

/// <summary>The events reached and changed with the keyboard (issue #102, third phase): no Google account and no network.</summary>
[Collection(CollectionName)]
public class EventKeyboardTests
{
    private const int DayView = 0, Week = 1, Month = 2, Year = 3, Agenda = 4;
    // A Wednesday far from the events of the other tests.
    private static readonly DateTime D = new(2043, 5, 20);

    // ---- The moves, without a window ----

    [Theory]
    [Trait("TestCase", "T463")]
    [InlineData(Avalonia.Input.Key.Up, false, "09:45", "10:45", 0)]
    [InlineData(Avalonia.Input.Key.Down, false, "10:15", "11:15", 0)]
    [InlineData(Avalonia.Input.Key.Left, false, "10:00", "11:00", -1)]
    [InlineData(Avalonia.Input.Key.Right, false, "10:00", "11:00", 1)]
    [InlineData(Avalonia.Input.Key.Up, true, "10:00", "10:45", 0)]
    [InlineData(Avalonia.Input.Key.Down, true, "10:00", "11:15", 0)]
    public void AltAndTheArrowsMoveATimedEvent(Avalonia.Input.Key key, bool resize, string start, string end, int days)
    {
        var ev = new CalendarEvent("a", "c", "A", D.AddHours(10), D.AddHours(11));
        var day = D.AddDays(days);
        Assert.Equal((day + TimeSpan.Parse(start), day + TimeSpan.Parse(end)), Shortcuts.Moved(ev, key, resize, 15)!.Value);
    }

    [Fact]
    [Trait("TestCase", "T463")]
    public void WholeDayEventsMoveByDaysAndNothingGetsShorterThanAStep()
    {
        var day = new CalendarEvent("a", "c", "A", D, D.AddDays(1)) { IsAllDay = true };
        Assert.Null(Shortcuts.Moved(day, Avalonia.Input.Key.Up, false, 15));
        Assert.Null(Shortcuts.Moved(day, Avalonia.Input.Key.Down, false, 15));
        Assert.Equal((D.AddDays(1), D.AddDays(2)), Shortcuts.Moved(day, Avalonia.Input.Key.Right, false, 15)!.Value);
        Assert.Equal((D, D.AddDays(2)), Shortcuts.Moved(day, Avalonia.Input.Key.Down, true, 15)!.Value);
        Assert.Null(Shortcuts.Moved(day, Avalonia.Input.Key.Up, true, 15)); // one day is the least
        var shortest = new CalendarEvent("b", "c", "B", D.AddHours(10), D.AddHours(10).AddMinutes(15));
        Assert.Null(Shortcuts.Moved(shortest, Avalonia.Input.Key.Up, true, 15));
        Assert.Null(Shortcuts.Moved(shortest, Avalonia.Input.Key.Left, true, 15)); // Shift changes only the length
    }

    [Theory]
    [Trait("TestCase", "T477")]
    [InlineData("it", "+1 altro", "+3 altri")]
    [InlineData("en", "+1 more", "+3 more")]
    [InlineData("de", "+1 weiterer", "+3 weitere")]
    [InlineData("es", "+1 más", "+3 más")]
    [InlineData("fr", "+1 autre", "+3 autres")]
    // On the thread of the windows: the models made by the other tests listen to the language.
    public Task TheEventsThatDoNotFitInADayOfTheMonthAreCountedInTheLanguageShown(string language, string one, string many) => HeadlessApp.RunAsync(() =>
    {
        var shown = GGCalendar.Core.Localization.L.Language;
        try
        {
            GGCalendar.Core.Localization.L.Apply(language);
            Assert.Equal(one, MonthGrid.MoreText(1));
            Assert.Equal(many, MonthGrid.MoreText(3));
        }
        finally
        {
            GGCalendar.Core.Localization.L.Apply(shown);
        }

        return Task.CompletedTask;
    });

    // ---- In the window ----

    /// <summary>The app on a calendar of its own, the only one shown, with the keyboard on the view.</summary>
    private sealed class Rig(MainWindow window, MainViewModel vm, string calendar)
    {
        public MainWindow Window => window;
        public MainViewModel Vm => vm;
        public string Calendar => calendar;

        public CalendarEvent Add(string title, DateTime start, double hours = 1, string? rule = null)
        {
            var ev = new CalendarEvent(Guid.NewGuid().ToString("N"), calendar, title, start, start.AddHours(hours)) { Recurrence = rule };
            vm.AddEvent(ev);
            Settle();
            return ev;
        }

        public CalendarEvent AddWholeDay(string title, DateTime day, int days = 1)
        {
            var ev = new CalendarEvent(Guid.NewGuid().ToString("N"), calendar, title, day, day.AddDays(days)) { IsAllDay = true };
            vm.AddEvent(ev);
            Settle();
            return ev;
        }

        public void Show(int view, DateTime day)
        {
            vm.ViewModeIndex = view;
            vm.SelectedDate = day;
            Settle();
            window.FindControl<Panel>("ViewHost")!.Focus();
        }

        public void Settle()
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
        }

        public void Key(PhysicalKey key, RawInputModifiers modifiers = RawInputModifiers.None)
        {
            window.KeyPressQwerty(key, modifiers);
            window.KeyReleaseQwerty(key, modifiers);
            Settle();
        }

        public string? Selected => window.Selection is { } key ? window.SelectableEvents().FirstOrDefault(e => EventKey.Of(e) == key)?.Title : null;

        public CalendarEvent Event(string title) => Assert.Single(window.SelectableEvents(), e => e.Title == title);

        public bool OnView => window.FocusManager!.GetFocusedElement() is Control { Name: "ViewHost" or "Grid" };

        /// <summary>Tab until the event with the title is selected.</summary>
        public void SelectByTab(string title)
        {
            for (var i = 0; i < 20 && Selected != title; i++) Key(PhysicalKey.Tab);
            Assert.Equal(title, Selected);
        }
    }

    private static Task InEventsAppAsync(Func<Rig, Task> test) => HeadlessApp.RunAsync(async () =>
    {
        var settingsFile = new SettingsStore().FilePath;
        var original = File.Exists(settingsFile) ? File.ReadAllBytes(settingsFile) : null;
        var window = OpenApp();
        var vm = Vm(window);
        var hidden = vm.Calendars.Where(c => c.IsVisible).ToList();
        foreach (var c in hidden) c.IsVisible = false;
        var calendar = vm.CreateLocalCalendar("Tastiera " + Guid.NewGuid().ToString("N")[..6]);
        try
        {
            foreach (var c in vm.Calendars) c.IsVisible = c.Id == calendar;
            await test(new Rig(window, vm, calendar));
        }
        finally
        {
            foreach (var owned in window.OwnedWindows.ToList()) owned.Close();
            vm.DeleteLocalCalendar(calendar);
            foreach (var c in vm.Calendars.Where(c => hidden.Any(h => h.Id == c.Id))) c.IsVisible = true;
            window.Close();
            if (original is not null) File.WriteAllBytes(settingsFile, original);
        }
    });

    private static Avalonia.Controls.Primitives.Popup Card(Rig rig) => rig.Window.FindControl<Avalonia.Controls.Primitives.Popup>("DetailsPopup")!;

    private static async Task<T> DialogAsync<T>(Rig rig) where T : Window =>
        await WaitForAsync(() => rig.Window.OwnedWindows.OfType<T>().FirstOrDefault(), typeof(T).Name);

    private static async Task ClosedAsync(Rig rig)
    {
        await WaitForAsync(() => rig.Window.OwnedWindows.Count == 0 ? rig.Window : null, "la chiusura della finestra");
        rig.Settle();
    }

    [Theory]
    [Trait("TestCase", "T464")]
    [InlineData(Week)]
    [InlineData(Month)]
    [InlineData(Agenda)]
    public Task Tab_goes_through_the_events_in_the_order_of_time(int view) => InEventsAppAsync(rig =>
    {
        // Added out of order; the whole day one comes first on its day, the long one only once.
        rig.Add("Giovedì", D.AddDays(1).AddHours(9));
        rig.Add("Mercoledì pomeriggio", D.AddHours(15));
        rig.Add("Mercoledì mattina", D.AddHours(9));
        rig.AddWholeDay("Mercoledì tutto il giorno", D);
        rig.AddWholeDay("Tre giorni", D.AddDays(1), 3);
        rig.Show(view, D);
        string[] order = ["Mercoledì tutto il giorno", "Mercoledì mattina", "Mercoledì pomeriggio", "Tre giorni", "Giovedì"];

        foreach (var title in order)
        {
            rig.Key(PhysicalKey.Tab);
            Assert.Equal(title, rig.Selected);
            Assert.True(rig.OnView);
        }

        foreach (var title in order.Reverse().Skip(1))
        {
            rig.Key(PhysicalKey.Tab, RawInputModifiers.Shift);
            Assert.Equal(title, rig.Selected);
        }

        Assert.Equal(rig.Window.Selection, rig.Window.FindControl<MonthGrid>("Month")!.Selection);
        if (view == Agenda)
        {
            var marked = rig.Window.FindControl<ItemsControl>("AgendaList")!.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("selected")).ToList();
            Assert.Equal(order[0], Assert.IsType<AgendaItem>(Assert.Single(marked).DataContext).Title);
        }

        return Task.CompletedTask;
    });

    [Fact]
    [Trait("TestCase", "T465")]
    public Task Past_the_last_event_and_before_the_first_the_keyboard_leaves_the_view() => InEventsAppAsync(rig =>
    {
        rig.Add("Solo", D.AddHours(9));
        rig.Show(Week, D);
        rig.Key(PhysicalKey.Tab);
        rig.Key(PhysicalKey.Tab);
        Assert.Null(rig.Window.Selection);
        Assert.False(rig.OnView);
        Assert.Equal("TodayButton", ((Control)rig.Window.FocusManager!.GetFocusedElement()!).Name);

        rig.Show(Week, D);
        rig.Key(PhysicalKey.Tab, RawInputModifiers.Shift);
        Assert.Equal("Solo", rig.Selected); // from the end
        rig.Key(PhysicalKey.Tab, RawInputModifiers.Shift);
        Assert.Null(rig.Window.Selection);
        Assert.False(rig.OnView);

        rig.Show(Week, D.AddDays(70)); // no events: Tab is the usual one
        rig.Key(PhysicalKey.Tab);
        Assert.Null(rig.Window.Selection);

        rig.Show(Year, D); // the year view shows no single event
        rig.Key(PhysicalKey.Tab);
        Assert.Null(rig.Window.Selection);
        Assert.Empty(rig.Window.SelectableEvents());
        return Task.CompletedTask;
    });

    [Fact]
    [Trait("TestCase", "T466")]
    public Task Only_the_events_drawn_are_reached() => InEventsAppAsync(rig =>
    {
        rig.Vm.Settings = new AppSettings { HideOutsideHours = true, DayStartHour = 8, DayEndHour = 18 };
        rig.Add("Di notte", D.AddHours(5));
        rig.Add("Di giorno", D.AddHours(10));
        rig.Show(DayView, D);
        Assert.Equal(["Di giorno"], rig.Window.SelectableEvents().Select(e => e.Title));

        rig.Window.Height = 700; // a few lines for each day of the month: the rest is "+N"
        for (var i = 0; i < 12; i++) rig.Add($"Folla {i:00}", D.AddHours(11).AddMinutes(i));
        rig.Show(Month, D);
        var drawn = rig.Window.SelectableEvents().Select(e => e.Title).ToList();
        Assert.Equal("Di notte", drawn[0]);
        Assert.InRange(drawn.Count, 1, 13);
        Assert.DoesNotContain("Folla 11", drawn);
        return Task.CompletedTask;
    });

    [Fact]
    [Trait("TestCase", "T467")]
    public Task Space_shows_the_card_and_Esc_closes_one_level_at_a_time() => InEventsAppAsync(async rig =>
    {
        rig.Add("Riunione", D.AddHours(9));
        rig.Show(Week, D);
        rig.SelectByTab("Riunione");

        rig.Key(PhysicalKey.Space);
        Assert.True(Card(rig).IsOpen);
        Assert.IsType<EventDetailsCard>(Card(rig).Child);
        rig.Key(PhysicalKey.Escape);
        await PauseAsync(TimeSpan.FromMilliseconds(50));
        rig.Settle();
        Assert.False(Card(rig).IsOpen);
        Assert.Equal("Riunione", rig.Selected);
        Assert.True(rig.OnView);

        rig.Key(PhysicalKey.Escape);
        Assert.Null(rig.Window.Selection);
    });

    [Theory]
    [Trait("TestCase", "T468")]
    [InlineData(PhysicalKey.Enter)]
    [InlineData(PhysicalKey.E)]
    public Task Enter_and_E_open_the_editor_of_the_selected_event(PhysicalKey key) => InEventsAppAsync(async rig =>
    {
        rig.Add("Riunione", D.AddHours(9));
        rig.Show(Month, D);
        rig.SelectByTab("Riunione");
        rig.Key(key);
        var editor = await DialogAsync<EventEditWindow>(rig);
        Assert.Equal("Riunione", ((EventEditViewModel)editor.DataContext!).Title);
        editor.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        await ClosedAsync(rig);
        Assert.Equal("Riunione", rig.Selected);
        Assert.True(rig.OnView);
    });

    [Theory]
    [Trait("TestCase", "T469")]
    [InlineData(Week)]
    [InlineData(Year)]
    public Task Enter_with_nothing_selected_makes_an_event_on_the_selected_day(int view) => InEventsAppAsync(async rig =>
    {
        rig.Show(view, D);
        rig.Key(PhysicalKey.Enter);
        var editor = await DialogAsync<EventEditWindow>(rig);
        Assert.Equal(D, ((EventEditViewModel)editor.DataContext!).StartDate);
        editor.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        await ClosedAsync(rig);
    });

    [Theory]
    [Trait("TestCase", "T470")]
    [InlineData(PhysicalKey.Delete)]
    [InlineData(PhysicalKey.Backspace)]
    public Task Delete_and_Backspace_remove_the_selected_event(PhysicalKey key) => InEventsAppAsync(rig =>
    {
        rig.AddWholeDay("Resta", D);
        rig.Add("Via", D.AddHours(9));
        rig.Show(Week, D);
        rig.Key(key); // nothing selected: nothing happens
        Assert.Equal(2, rig.Window.SelectableEvents().Count);

        rig.SelectByTab("Via");
        rig.Key(key);
        Assert.Equal(["Resta"], rig.Window.SelectableEvents().Select(e => e.Title));
        Assert.Null(rig.Selected);
        return Task.CompletedTask;
    });

    [Fact]
    [Trait("TestCase", "T471")]
    public Task For_a_series_the_scope_is_asked_before_deleting_and_moving() => InEventsAppAsync(async rig =>
    {
        rig.Add("Serie", D.AddHours(9), rule: "FREQ=DAILY;COUNT=3");
        rig.Show(DayView, D);
        rig.SelectByTab("Serie");

        rig.Key(PhysicalKey.ArrowDown, RawInputModifiers.Alt);
        var scope = await DialogAsync<SeriesScopeWindow>(rig);
        scope.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        await ClosedAsync(rig);
        Assert.Equal(D.AddHours(9), rig.Event("Serie").Start);
        Assert.Equal("Serie", rig.Selected); // cancelled: nothing changed

        rig.Key(PhysicalKey.ArrowDown, RawInputModifiers.Alt);
        scope = await DialogAsync<SeriesScopeWindow>(rig);
        scope.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None); // the first choice: this event only
        await ClosedAsync(rig);
        var moved = D.AddHours(9).AddMinutes(rig.Vm.Settings.SnapMinutes);
        Assert.Equal(moved, rig.Event("Serie").Start);
        Assert.Equal("Serie", rig.Selected);

        rig.Show(DayView, D.AddDays(1));
        Assert.Equal(D.AddDays(1).AddHours(9), rig.Event("Serie").Start); // the others stay as they were
        rig.SelectByTab("Serie");
        rig.Key(PhysicalKey.Delete);
        scope = await DialogAsync<SeriesScopeWindow>(rig);
        scope.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        await ClosedAsync(rig);
        Assert.Empty(rig.Window.SelectableEvents());
        rig.Show(DayView, D.AddDays(2));
        Assert.Equal(D.AddDays(2).AddHours(9), rig.Event("Serie").Start);
    });

    [Fact]
    [Trait("TestCase", "T472")]
    public Task Alt_and_the_arrows_move_and_resize_the_selected_event() => InEventsAppAsync(rig =>
    {
        rig.Add("Riunione", D.AddHours(9));
        rig.Show(Week, D);
        rig.SelectByTab("Riunione");
        var step = TimeSpan.FromMinutes(rig.Vm.Settings.SnapMinutes);
        void Is(DateTime start, DateTime end)
        {
            var ev = rig.Event("Riunione");
            Assert.Equal((start, end), (ev.Start, ev.End));
            Assert.Equal("Riunione", rig.Selected);
            Assert.True(rig.OnView);
        }

        rig.Key(PhysicalKey.ArrowDown, RawInputModifiers.Alt);
        Is(D.AddHours(9) + step, D.AddHours(10) + step);
        rig.Key(PhysicalKey.ArrowDown, RawInputModifiers.Alt); // again, on the event just moved
        Is(D.AddHours(9) + 2 * step, D.AddHours(10) + 2 * step);
        rig.Key(PhysicalKey.ArrowRight, RawInputModifiers.Alt);
        Is(D.AddDays(1).AddHours(9) + 2 * step, D.AddDays(1).AddHours(10) + 2 * step);
        rig.Key(PhysicalKey.ArrowLeft, RawInputModifiers.Alt);
        rig.Key(PhysicalKey.ArrowUp, RawInputModifiers.Alt);
        rig.Key(PhysicalKey.ArrowUp, RawInputModifiers.Alt);
        Is(D.AddHours(9), D.AddHours(10));
        rig.Key(PhysicalKey.ArrowDown, RawInputModifiers.Alt | RawInputModifiers.Shift);
        Is(D.AddHours(9), D.AddHours(10) + step);
        rig.Key(PhysicalKey.ArrowUp, RawInputModifiers.Alt | RawInputModifiers.Shift);
        Is(D.AddHours(9), D.AddHours(10));
        Assert.Equal(D, rig.Vm.SelectedDate);
        return Task.CompletedTask;
    });

    [Theory]
    [Trait("TestCase", "T473")]
    [InlineData(DayView, 1)]
    [InlineData(Week, 1)] // from the Sunday of the week shown to the Monday after
    [InlineData(Agenda, -1)]
    public Task An_event_moved_out_of_the_days_shown_takes_the_view_with_it(int view, int towards) => InEventsAppAsync(rig =>
    {
        var day = view == Week ? D.AddDays(4) : D; // the Sunday of a week that starts on Monday
        rig.AddWholeDay("Viaggio", day);
        rig.Show(view, view == Week ? D : day);
        rig.SelectByTab("Viaggio");
        var direction = Math.Sign(towards);

        rig.Key(direction > 0 ? PhysicalKey.ArrowRight : PhysicalKey.ArrowLeft, RawInputModifiers.Alt);
        Assert.Equal(day.AddDays(direction), rig.Event("Viaggio").Start);
        Assert.Equal(day.AddDays(direction), rig.Vm.SelectedDate);
        Assert.Equal("Viaggio", rig.Selected);
        Assert.Equal(view, rig.Vm.ViewModeIndex);
        return Task.CompletedTask;
    });

    [Fact]
    [Trait("TestCase", "T474")]
    public Task An_event_of_a_public_calendar_is_only_selected_and_looked_at() => InEventsAppAsync(async rig =>
    {
        var id = SubscriptionRecord.IdPrefix + Guid.NewGuid().ToString("N");
        var store = new CalendarStore();
        var ev = new CalendarEvent(Guid.NewGuid().ToString("N"), id, "Festa", D.AddHours(9), D.AddHours(10));
        store.AddSubscription(new CalendarInfo(id, "Pubblici", "Pubblico", "#1A73E8"), new SubscriptionRecord(id, "x"), [ev]);
        try
        {
            await rig.Vm.ReloadAsync();
            foreach (var c in rig.Vm.Calendars.Where(c => c.Id == id)) c.IsVisible = true;
            rig.Show(Week, D);
            rig.SelectByTab("Festa");

            foreach (var (key, modifiers) in new[]
                     {
                         (PhysicalKey.Delete, RawInputModifiers.None), (PhysicalKey.Backspace, RawInputModifiers.None),
                         (PhysicalKey.ArrowDown, RawInputModifiers.Alt), (PhysicalKey.ArrowRight, RawInputModifiers.Alt),
                         (PhysicalKey.ArrowDown, RawInputModifiers.Alt | RawInputModifiers.Shift),
                     })
                rig.Key(key, modifiers);
            Assert.Equal((D.AddHours(9), D.AddHours(10)), (rig.Event("Festa").Start, rig.Event("Festa").End));

            rig.Key(PhysicalKey.Enter);
            Assert.True(Card(rig).IsOpen);
            Assert.Empty(rig.Window.OwnedWindows);
        }
        finally
        {
            store.DeleteCalendar(id);
        }
    });

    [Fact]
    [Trait("TestCase", "T475")]
    public Task A_click_selects_and_another_view_or_day_forgets_the_selection() => InEventsAppAsync(rig =>
    {
        rig.Add("Prima", D.AddHours(9));
        rig.Add("Seconda", D.AddHours(14));
        rig.Show(DayView, D);
        var scroll = rig.Window.FindControl<ScrollViewer>("GridScroll")!;
        scroll.Offset = new Vector(0, 7 * WeekGrid.HourHeight);
        rig.Settle();
        var grid = rig.Window.FindControl<WeekGrid>("Grid")!;
        // In the day view the event takes the whole column: its middle, half an hour after its start.
        var inGrid = new Point((grid.Gutter + grid.Bounds.Width) / 2, (9.5 - rig.Vm.FirstHour) * WeekGrid.HourHeight);
        rig.Window.CaptureRenderedFrame(); // the grid knows where its blocks are once drawn
        var at = grid.TranslatePoint(inGrid, rig.Window)!.Value;
        rig.Window.MouseDown(at, MouseButton.Left);
        rig.Window.MouseUp(at, MouseButton.Left);
        rig.Settle();
        Assert.Equal("Prima", rig.Selected);

        rig.Key(PhysicalKey.Escape); // the card
        rig.Key(PhysicalKey.Tab);
        Assert.Equal("Seconda", rig.Selected); // Tab goes on from the event clicked

        rig.Vm.SelectedDate = D.AddDays(1);
        Assert.Null(rig.Window.Selection);
        rig.Show(DayView, D);
        rig.Key(PhysicalKey.Tab);
        rig.Vm.ViewModeIndex = Week;
        Assert.Null(rig.Window.Selection);
        return Task.CompletedTask;
    });
}
