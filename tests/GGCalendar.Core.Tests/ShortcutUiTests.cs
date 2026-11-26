using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GGCalendar.App.Controls;
using GGCalendar.App.ViewModels;
using GGCalendar.App.Views;
using GGCalendar.Core.Models;
using GGCalendar.Core.Tests.Support;
using GGCalendar.Data;
using static GGCalendar.Core.Tests.Support.AppDriver;

namespace GGCalendar.Core.Tests;

/// <summary>The keyboard shortcuts pressed in the windows (issue #102, first phase): no Google account and no network.</summary>
[Collection(CollectionName)]
public class ShortcutUiTests
{
    private const int DayView = 0, Week = 1, Month = 2, Year = 3, Agenda = 4, SixDays = 5;
    private static readonly DateTime Chosen = new(2026, 5, 20);

    private static Task InKeyAppAsync(Func<MainWindow, MainViewModel, Task> test) => HeadlessApp.RunAsync(async () =>
    {
        var settingsFile = new SettingsStore().FilePath;
        var original = File.Exists(settingsFile) ? File.ReadAllBytes(settingsFile) : null;
        var window = OpenApp();
        try
        {
            var vm = Vm(window);
            vm.ViewModeIndex = Week;
            vm.SelectedDate = Chosen;
            Settle(window);
            Named<Panel>(window, "ViewHost").Focus();
            await test(window, vm);
        }
        finally
        {
            foreach (var owned in window.OwnedWindows.ToList()) owned.Close();
            window.Close();
            if (original is not null) File.WriteAllBytes(settingsFile, original);
        }
    });

    private static T Named<T>(Window window, string name) where T : Control => window.FindControl<T>(name)!;

    private static void Settle(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }

    private static void Key(Window window, PhysicalKey key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        window.KeyPressQwerty(key, modifiers);
        Dispatcher.UIThread.RunJobs();
    }

    private static async Task<T> DialogAsync<T>(Window owner) where T : Window =>
        await WaitForAsync(() => owner.OwnedWindows.OfType<T>().FirstOrDefault(), typeof(T).Name);

    private static async Task ClosedAsync(Window owner) => await WaitForAsync(() => owner.OwnedWindows.Count == 0 ? owner : null, "la chiusura della finestra");

    private static void ClickOn(Window window, Control control)
    {
        var center = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseDown(center, MouseButton.Left);
        window.MouseUp(center, MouseButton.Left);
        Settle(window);
    }

    [Fact]
    [Trait("TestCase", "T429")]
    public Task T_goes_to_today_and_to_the_current_hour() => InKeyAppAsync((window, vm) =>
    {
        var scroll = Named<ScrollViewer>(window, "GridScroll");
        scroll.Offset = new Vector(0, 0);
        Settle(window);

        Key(window, PhysicalKey.T);
        Settle(window);
        Assert.Equal(DateTime.Today, vm.SelectedDate);
        var now = (DateTime.Now.TimeOfDay.TotalHours - vm.FirstHour) * WeekGrid.HourHeight;
        var most = Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height);
        var expected = Math.Clamp(now - scroll.Viewport.Height / 3, 0, most);
        Assert.InRange(scroll.Offset.Y, expected - 5, expected + 5);
        return Task.CompletedTask;
    });

    [Theory]
    [Trait("TestCase", "T430")]
    [InlineData(DayView, "2026-05-19", "2026-05-21")]
    [InlineData(Week, "2026-05-13", "2026-05-27")]
    [InlineData(Month, "2026-04-20", "2026-06-20")]
    [InlineData(Year, "2025-05-20", "2027-05-20")]
    [InlineData(Agenda, "2026-04-20", "2026-06-20")]
    [InlineData(SixDays, "2026-05-14", "2026-05-26")]
    public Task The_arrows_and_their_letters_move_by_a_period_in_each_view(int view, string before, string after) => InKeyAppAsync((window, vm) =>
    {
        vm.ViewModeIndex = view;
        Settle(window);
        foreach (var (key, expected) in new[]
                 {
                     (PhysicalKey.ArrowLeft, before), (PhysicalKey.K, before), (PhysicalKey.P, before),
                     (PhysicalKey.ArrowRight, after), (PhysicalKey.J, after), (PhysicalKey.N, after),
                 })
        {
            vm.SelectedDate = Chosen;
            Key(window, key);
            Assert.Equal(DateTime.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), vm.SelectedDate);
            Assert.Equal(view, vm.ViewModeIndex);
        }

        return Task.CompletedTask;
    });

    [Fact]
    [Trait("TestCase", "T431")]
    public Task Numbers_and_letters_change_the_view_and_Shift_W_the_weekend() => InKeyAppAsync((window, vm) =>
    {
        foreach (var (key, view) in new[]
                 {
                     (PhysicalKey.Digit1, DayView), (PhysicalKey.Digit2, Week), (PhysicalKey.Digit3, Month), (PhysicalKey.Digit4, SixDays),
                     (PhysicalKey.Digit5, Agenda), (PhysicalKey.Digit6, Year), (PhysicalKey.NumPad3, Month), (PhysicalKey.D, DayView),
                     (PhysicalKey.Y, Year), (PhysicalKey.W, Week),
                 })
        {
            Key(window, key);
            Assert.Equal(view, vm.ViewModeIndex);
        }

        var weekends = vm.ShowWeekends;
        Key(window, PhysicalKey.W, RawInputModifiers.Shift);
        Assert.Equal(!weekends, vm.ShowWeekends);
        Assert.Equal(Week, vm.ViewModeIndex);
        Key(window, PhysicalKey.W, RawInputModifiers.Shift);
        Assert.Equal(weekends, vm.ShowWeekends);
        return Task.CompletedTask;
    });

    [Theory]
    [Trait("TestCase", "T432")]
    [InlineData(PhysicalKey.C, RawInputModifiers.None)]
    [InlineData(PhysicalKey.N, RawInputModifiers.Control)]
    public Task C_creates_an_event_on_the_selected_day(PhysicalKey key, RawInputModifiers modifiers) => InKeyAppAsync(async (window, vm) =>
    {
        var day = DateTime.Today.AddDays(10);
        vm.SelectedDate = day;
        Settle(window);

        Key(window, key, modifiers);
        var dialog = await DialogAsync<EventEditWindow>(window);
        var edit = (EventEditViewModel)dialog.DataContext!;
        Assert.Equal(day, edit.StartDate);
        Assert.Equal(TimeSpan.FromHours(vm.Settings.DayStartHour), edit.StartTime);
        Key(dialog, PhysicalKey.Escape);
        await ClosedAsync(window);
        Assert.Equal(day, vm.SelectedDate);
    });

    [Theory]
    [Trait("TestCase", "T433")]
    [InlineData(PhysicalKey.S, RawInputModifiers.None)]
    [InlineData(PhysicalKey.Comma, RawInputModifiers.Control)]
    public Task S_opens_the_settings_and_Esc_closes_them_without_saving(PhysicalKey key, RawInputModifiers modifiers) => InKeyAppAsync(async (window, vm) =>
    {
        var before = vm.Settings;
        Key(window, key, modifiers);
        var dialog = await DialogAsync<SettingsWindow>(window);
        ((SettingsViewModel)dialog.DataContext!).Use24HourClock = !before.Use24HourClock;

        Key(dialog, PhysicalKey.Escape);
        await ClosedAsync(window);
        Assert.Same(before, vm.Settings);
    });

    [Fact]
    [Trait("TestCase", "T434")]
    public Task Esc_cancels_the_recurrence_and_the_choice_of_a_series() => InKeyAppAsync(async (window, _) =>
    {
        var recurrence = new RecurrenceWindow().ShowDialog<GGCalendar.Core.Recurrence.RecurrenceRule?>(window);
        var first = await DialogAsync<RecurrenceWindow>(window);
        Settle(first);
        Key(first, PhysicalKey.Escape);
        await ClosedAsync(window);
        Assert.Null(await recurrence);

        var scope = new SeriesScopeWindow("scope").ShowDialog<SeriesScope?>(window);
        var second = await DialogAsync<SeriesScopeWindow>(window);
        Settle(second);
        Key(second, PhysicalKey.Escape);
        await ClosedAsync(window);
        Assert.Null(await scope);
    });

    [Theory]
    [Trait("TestCase", "T435")]
    [InlineData(PhysicalKey.R)]
    [InlineData(PhysicalKey.F5)]
    public Task R_and_F5_sync_now(PhysicalKey key)
    {
        var fake = new FakeGoogle();
        return GoogleCalendarUiTests.InFakeGoogleAppAsync(fake, async window =>
        {
            await WaitForQuietAsync(window);
            Named<Panel>(window, "ViewHost").Focus();
            var requests = fake.Requests.Count;

            Key(window, key);
            await WaitForAsync(() => fake.Requests.Count > requests ? window : null, "la sincronizzazione");
            await WaitForQuietAsync(window);
        });
    }

    [Fact]
    [Trait("TestCase", "T436")]
    public Task Slash_and_Ctrl_F_open_the_search() => InKeyAppAsync(async (window, vm) =>
    {
        window.KeyPress(Avalonia.Input.Key.D7, RawInputModifiers.Shift, PhysicalKey.Digit7, "/"); // as on an Italian keyboard
        Settle(window);
        Assert.True(vm.Search.IsOpen);
        var box = Named<TextBox>(window, "SearchBox");
        await WaitForAsync(() => box.IsFocused ? box : null, "la casella di ricerca");

        Key(window, PhysicalKey.Escape);
        Assert.False(vm.Search.IsOpen);
        Named<Panel>(window, "ViewHost").Focus();
        Key(window, PhysicalKey.F, RawInputModifiers.Control);
        Assert.True(vm.Search.IsOpen);
    });

    [Fact]
    [Trait("TestCase", "T437")]
    public Task While_typing_the_single_keys_write_and_do_nothing_else() => InKeyAppAsync(async (window, vm) =>
    {
        Key(window, PhysicalKey.F, RawInputModifiers.Control);
        var box = Named<TextBox>(window, "SearchBox");
        await WaitForAsync(() => box.IsFocused ? box : null, "la casella di ricerca");
        foreach (var key in new[] { PhysicalKey.T, PhysicalKey.C, PhysicalKey.S, PhysicalKey.R, PhysicalKey.M, PhysicalKey.Digit3, PhysicalKey.ArrowLeft, PhysicalKey.ArrowRight })
            Key(window, key);
        Key(window, PhysicalKey.W, RawInputModifiers.Shift);

        Assert.True(vm.Search.IsOpen);
        Assert.Equal(Week, vm.ViewModeIndex);
        Assert.Equal(Chosen, vm.SelectedDate);
        Assert.Empty(window.OwnedWindows);
    });

    [Fact]
    [Trait("TestCase", "T437")]
    public Task A_drop_down_and_a_number_box_keep_their_keys() => InKeyAppAsync((window, vm) =>
    {
        // The two kinds of control are not in the main window outside the search: they are put there to press keys on them.
        var combo = new ComboBox { ItemsSource = new[] { "a", "b" }, SelectedIndex = 0 };
        var number = new NumericUpDown { Value = 1 };
        var host = Named<Panel>(window, "ViewHost");
        host.Children.Add(combo);
        host.Children.Add(number);
        Settle(window);

        foreach (var control in new Control[] { combo, number.GetVisualDescendants().OfType<TextBox>().First() })
        {
            Assert.True(control.Focus());
            foreach (var key in new[] { PhysicalKey.M, PhysicalKey.Digit3, PhysicalKey.ArrowRight, PhysicalKey.T }) Key(window, key);
            Assert.Equal(Week, vm.ViewModeIndex);
            Assert.Equal(Chosen, vm.SelectedDate);
        }

        return Task.CompletedTask;
    });

    [Fact]
    [Trait("TestCase", "T438")]
    public Task An_open_menu_keeps_the_single_keys() => InKeyAppAsync((window, vm) =>
    {
        var create = FindButton(window, b => b.Flyout is MenuFlyout && b.GetVisualDescendants().Any() && ((MenuFlyout)b.Flyout).Items.OfType<MenuItem>().Any(i => i.Name == "CreateEventItem"));
        ClickOn(window, create);
        Assert.True(create.Flyout!.IsOpen);

        foreach (var key in new[] { PhysicalKey.M, PhysicalKey.Digit3, PhysicalKey.T, PhysicalKey.ArrowRight }) Key(window, key);
        Assert.Equal(Week, vm.ViewModeIndex);
        Assert.Equal(Chosen, vm.SelectedDate);
        Assert.Empty(window.OwnedWindows);
        return Task.CompletedTask;
    });

    [Fact]
    [Trait("TestCase", "T439")]
    public Task On_a_button_the_shortcuts_work() => InKeyAppAsync((window, vm) =>
    {
        var button = FindButton(window, "Main.Search");
        Assert.True(button.Focus());
        Key(window, PhysicalKey.ArrowRight);
        Assert.Equal(Chosen.AddDays(7), vm.SelectedDate);
        Key(window, PhysicalKey.M);
        Assert.Equal(Month, vm.ViewModeIndex);
        Assert.False(vm.Search.IsOpen);
        return Task.CompletedTask;
    });

    [Fact]
    [Trait("TestCase", "T440")]
    public Task The_small_calendar_keeps_the_arrows_but_not_the_letters() => InKeyAppAsync((window, vm) =>
    {
        var mini = Named<Calendar>(window, "MiniCalendar");
        // As when a day was reached with the Tab key: a click on a day gives the keyboard back to the view.
        var day = mini.GetVisualDescendants().OfType<CalendarDayButton>().First(c => c.IsEffectivelyVisible && c.DataContext is DateTime d && d == Chosen);
        Assert.True(day.Focus());

        Key(window, PhysicalKey.ArrowRight);
        Assert.NotEqual(Chosen.AddDays(7), vm.SelectedDate); // the next day, as the small calendar does, not the next week
        Key(window, PhysicalKey.M);
        Assert.Equal(Month, vm.ViewModeIndex);
        return Task.CompletedTask;
    });

    [Theory]
    [Trait("TestCase", "T441")]
    [InlineData(Week)]
    [InlineData(Month)]
    [InlineData(Year)]
    public Task After_a_choice_in_the_small_calendar_the_arrows_move_the_view(int view) => InKeyAppAsync((window, vm) =>
    {
        vm.ViewModeIndex = view;
        Settle(window);
        var mini = Named<Calendar>(window, "MiniCalendar");
        Control cell = view == Week
            ? mini.GetVisualDescendants().OfType<CalendarDayButton>().First(c => c.IsEffectivelyVisible && c.DataContext is DateTime d && d == new DateTime(2026, 5, 12))
            : mini.GetVisualDescendants().OfType<CalendarButton>().Where(c => c.IsEffectivelyVisible).ElementAt(view == Month ? 7 : 9); // August, or 2028
        ClickOn(window, cell);
        var chosen = vm.SelectedDate!.Value;
        Assert.NotEqual(Chosen, chosen);

        Key(window, PhysicalKey.ArrowRight);
        Assert.Equal(view switch { Week => chosen.AddDays(7), Month => chosen.AddMonths(1), _ => chosen.AddYears(1) }, vm.SelectedDate);
        return Task.CompletedTask;
    });

    [Fact]
    [Trait("TestCase", "T442")]
    public Task Ctrl_Shift_A_opens_the_list_of_the_alarms_when_there_are_some() => InKeyAppAsync(async (window, vm) =>
    {
        var button = Named<Button>(window, "AlarmsButton");
        Assert.False(button.IsEnabled);
        Key(window, PhysicalKey.A, RawInputModifiers.Control | RawInputModifiers.Shift);
        Assert.False(button.Flyout!.IsOpen);

        var calendar = vm.CreateLocalCalendar("Sveglie " + Guid.NewGuid().ToString("N")[..6]);
        try
        {
            var start = DateTime.Now.AddDays(2);
            vm.AddEvent(new CalendarEvent(Guid.NewGuid().ToString("N"), calendar, "Sveglia", start, start.AddHours(1))
            {
                Reminders = [new EventReminder(ReminderMethod.Alarm, 10)],
            });
            await WaitForAsync(() => button.IsEnabled ? button : null, "la sveglia in attesa");
            Key(window, PhysicalKey.A, RawInputModifiers.Control | RawInputModifiers.Shift);
            Assert.True(button.Flyout!.IsOpen);
            button.Flyout.Hide();
        }
        finally
        {
            vm.DeleteLocalCalendar(calendar);
        }
    });

    [Theory]
    [Trait("TestCase", "T443")]
    [InlineData(PhysicalKey.S)]
    [InlineData(PhysicalKey.Enter)]
    public Task Ctrl_S_and_Ctrl_Enter_save_the_event(PhysicalKey key) => InKeyAppAsync(async (window, vm) =>
    {
        var title = "Scorciatoia " + Guid.NewGuid().ToString("N")[..6];
        var calendar = vm.CreateLocalCalendar(title);
        try
        {
            Key(window, PhysicalKey.C);
            var dialog = await DialogAsync<EventEditWindow>(window);
            Settle(dialog);
            var edit = (EventEditViewModel)dialog.DataContext!;
            edit.Title = title;
            edit.SelectedCalendar = edit.Calendars.Single(c => c.Id == calendar);

            // A time still being written when the keys are pressed is saved too.
            var time = dialog.GetVisualDescendants().OfType<TimeBox>().First(t => t.Name == "StartTimeBox").Box;
            Assert.True(time.Focus());
            time.Text = "07:13";

            Key(dialog, key, RawInputModifiers.Control);
            await ClosedAsync(window);
            var saved = Assert.Single(vm.AllEvents, e => e.Title == title);
            Assert.Equal(new TimeSpan(7, 13, 0), saved.Start.TimeOfDay);
        }
        finally
        {
            vm.DeleteLocalCalendar(calendar);
        }
    });

    [Fact]
    [Trait("TestCase", "T445")]
    public Task F1_and_the_question_mark_open_the_list_of_the_shortcuts() => InKeyAppAsync(async (window, vm) =>
    {
        foreach (var open in new Action[]
                 {
                     () => Key(window, PhysicalKey.F1),
                     () => window.KeyPress(Avalonia.Input.Key.OemOpenBrackets, RawInputModifiers.Shift, PhysicalKey.Minus, "?"),
                 })
        {
            open();
            var dialog = await DialogAsync<ShortcutsWindow>(window);
            Settle(dialog);
            var shown = dialog.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
            foreach (var (title, rows) in ShortcutsWindow.Sections())
            {
                Assert.Contains(title, shown);
                foreach (var (keys, action) in rows)
                {
                    Assert.Contains(keys, shown);
                    Assert.Contains(action, shown);
                }
            }

            Key(dialog, PhysicalKey.Escape);
            await ClosedAsync(window);
        }
    });

    [Fact]
    [Trait("TestCase", "T462")]
    public Task I_opens_the_information_window_and_Esc_closes_it() => InKeyAppAsync(async (window, vm) =>
    {
        Key(window, PhysicalKey.I);
        var dialog = await DialogAsync<AboutWindow>(window);
        Settle(dialog);
        Key(dialog, PhysicalKey.Escape);
        await ClosedAsync(window);
        Assert.Equal(Chosen, vm.SelectedDate);
        Assert.Equal(Week, vm.ViewModeIndex);
    });

    private static string Area(MainWindow window)
    {
        var path = ((Visual)window.FocusManager!.GetFocusedElement()!).GetSelfAndVisualAncestors().OfType<Control>().Select(c => c.Name).ToList();
        return new[] { "HeaderBar", "MiniCalendar", "CalendarList", "ViewHost" }.FirstOrDefault(path.Contains) ?? "";
    }

    [Theory]
    [Trait("TestCase", "T446")]
    [InlineData(Week)]
    [InlineData(Month)]
    [InlineData(Year)]
    [InlineData(Agenda)]
    public Task F6_goes_round_the_areas_in_both_directions(int view) => InKeyAppAsync((window, vm) =>
    {
        var calendar = vm.CreateLocalCalendar("Aree " + Guid.NewGuid().ToString("N")[..6]);
        try
        {
            vm.ViewModeIndex = view;
            Settle(window);
            var ring = Named<Border>(window, "ViewFocusRing");
            foreach (var area in new[] { "HeaderBar", "MiniCalendar", "CalendarList", "ViewHost", "HeaderBar" })
            {
                Key(window, PhysicalKey.F6);
                Settle(window);
                Assert.Equal(area, Area(window));
                Assert.Equal(area == "ViewHost", ring.IsVisible);
            }

            foreach (var area in new[] { "ViewHost", "CalendarList", "MiniCalendar", "HeaderBar" })
            {
                Key(window, PhysicalKey.F6, RawInputModifiers.Shift);
                Settle(window);
                Assert.Equal(area, Area(window));
            }

            Assert.Equal(Chosen, vm.SelectedDate);
            Assert.Equal(view, vm.ViewModeIndex);
        }
        finally
        {
            vm.DeleteLocalCalendar(calendar);
        }

        return Task.CompletedTask;
    });

    [Fact]
    [Trait("TestCase", "T446")]
    public Task F6_works_while_typing_and_the_view_is_reached_with_the_search_open() => InKeyAppAsync(async (window, vm) =>
    {
        Key(window, PhysicalKey.F, RawInputModifiers.Control);
        var box = Named<TextBox>(window, "SearchBox");
        await WaitForAsync(() => box.IsFocused ? box : null, "la casella di ricerca");
        Key(window, PhysicalKey.F6);
        Settle(window);
        Assert.Equal("MiniCalendar", Area(window)); // the box is in the bar
        Key(window, PhysicalKey.F6, RawInputModifiers.Shift);
        Key(window, PhysicalKey.F6, RawInputModifiers.Shift);
        Settle(window);
        Assert.Equal("ViewHost", Area(window));
        Assert.True(vm.Search.IsOpen);
    });

    [Theory]
    [Trait("TestCase", "T447")]
    [InlineData(Month, PhysicalKey.Enter)]
    [InlineData(Month, PhysicalKey.Space)]
    [InlineData(Year, PhysicalKey.Enter)]
    [InlineData(Year, PhysicalKey.Space)]
    public Task With_the_keyboard_a_month_or_a_year_of_the_small_calendar_is_chosen(int view, PhysicalKey choose) => InKeyAppAsync((window, vm) =>
    {
        vm.ViewModeIndex = view;
        Settle(window);
        Key(window, PhysicalKey.F6);
        Key(window, PhysicalKey.F6);
        Settle(window);
        var cell = Assert.IsType<CalendarButton>(window.FocusManager!.GetFocusedElement());
        Assert.Equal(Chosen.Year, Assert.IsType<DateTime>(cell.DataContext).Year);

        Key(window, PhysicalKey.ArrowRight);
        Settle(window);
        Assert.Equal(Chosen, vm.SelectedDate); // only moving
        var moved = Assert.IsType<CalendarButton>(window.FocusManager!.GetFocusedElement());
        Assert.NotSame(cell, moved); // the focus follows the highlight

        window.KeyPressQwerty(choose, RawInputModifiers.None);
        window.KeyReleaseQwerty(choose, RawInputModifiers.None);
        Settle(window);
        var expected = view == Month ? Chosen.AddMonths(1) : Chosen.AddYears(1);
        Assert.Equal(expected, vm.SelectedDate);
        Assert.Equal("ViewHost", Area(window));

        Key(window, PhysicalKey.ArrowRight); // the keyboard is back on the view
        Assert.Equal(view == Month ? expected.AddMonths(1) : expected.AddYears(1), vm.SelectedDate);
        return Task.CompletedTask;
    });

    [Fact]
    [Trait("TestCase", "T448")]
    public Task In_the_days_of_the_small_calendar_the_arrows_choose_and_the_focus_follows() => InKeyAppAsync((window, vm) =>
    {
        Key(window, PhysicalKey.F6);
        Key(window, PhysicalKey.F6);
        Settle(window);
        var cell = Assert.IsType<CalendarDayButton>(window.FocusManager!.GetFocusedElement());
        Assert.Equal(Chosen, cell.DataContext);

        foreach (var expected in new[] { Chosen.AddDays(1), Chosen.AddDays(2) })
        {
            Key(window, PhysicalKey.ArrowRight);
            Settle(window);
            Assert.Equal(expected, vm.SelectedDate);
            Assert.Equal(expected, Assert.IsType<CalendarDayButton>(window.FocusManager!.GetFocusedElement()).DataContext);
        }

        return Task.CompletedTask;
    });

    // ---- Second phase: longer and shorter steps, up and down, a date, the results of the search ----

    [Theory]
    [Trait("TestCase", "T452")]
    [InlineData(DayView, "2026-05-13", "2026-05-27")]
    [InlineData(SixDays, "2026-04-20", "2026-06-20")]
    [InlineData(Week, "2026-04-20", "2026-06-20")]
    [InlineData(Month, "2025-05-20", "2027-05-20")]
    [InlineData(Year, "2026-05-20", "2026-05-20")]
    [InlineData(Agenda, "2025-05-20", "2027-05-20")]
    public Task Ctrl_jumps_further_and_Shift_moves_by_a_day(int view, string back, string forward) => InKeyAppAsync((window, vm) =>
    {
        vm.ViewModeIndex = view;
        Settle(window);
        foreach (var (key, modifiers, expected) in new[]
                 {
                     (PhysicalKey.ArrowLeft, RawInputModifiers.Control, back), (PhysicalKey.ArrowRight, RawInputModifiers.Control, forward),
                     (PhysicalKey.ArrowLeft, RawInputModifiers.Shift, "2026-05-19"), (PhysicalKey.ArrowRight, RawInputModifiers.Shift, "2026-05-21"),
                 })
        {
            vm.SelectedDate = Chosen;
            Key(window, key, modifiers);
            Assert.Equal(DateTime.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), vm.SelectedDate);
            Assert.Equal(view, vm.ViewModeIndex);
        }

        Assert.Equal(vm.SelectedDate, Named<MonthGrid>(window, "Month").SelectedDay);
        Assert.Equal(vm.SelectedDate, Named<YearGrid>(window, "Year").SelectedDay);
        return Task.CompletedTask;
    });

    private static void AssertAt(ScrollViewer scroll, double wanted)
    {
        var expected = Math.Clamp(wanted, 0, Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height));
        Assert.InRange(scroll.Offset.Y, expected - 1, expected + 1);
    }

    [Theory]
    [Trait("TestCase", "T453")]
    [InlineData(DayView, false)]
    [InlineData(Week, false)]
    [InlineData(SixDays, true)]
    public Task Up_and_down_scroll_the_hours(int view, bool fromTheGrid) => InKeyAppAsync((window, vm) =>
    {
        vm.ViewModeIndex = view;
        window.Height = 600;
        Settle(window);
        if (fromTheGrid) Assert.True(Named<WeekGrid>(window, "Grid").Focus());
        var scroll = Named<ScrollViewer>(window, "GridScroll");
        var page = scroll.Viewport.Height;
        double At(int hour) => (hour - vm.FirstHour) * WeekGrid.HourHeight;
        var last = scroll.Extent.Height - page;
        Assert.True(last > page / 2, "la griglia deve essere più alta della finestra");

        Key(window, PhysicalKey.Home, RawInputModifiers.Control);
        AssertAt(scroll, 0);
        Key(window, PhysicalKey.ArrowDown);
        AssertAt(scroll, WeekGrid.HourHeight / 2);
        Key(window, PhysicalKey.ArrowDown);
        Key(window, PhysicalKey.ArrowUp);
        AssertAt(scroll, WeekGrid.HourHeight / 2);
        Key(window, PhysicalKey.Home, RawInputModifiers.Control);
        Key(window, PhysicalKey.PageDown);
        AssertAt(scroll, page);
        Key(window, PhysicalKey.PageUp);
        AssertAt(scroll, 0);
        Key(window, PhysicalKey.Home);
        AssertAt(scroll, At(vm.Settings.DayStartHour));
        Key(window, PhysicalKey.End);
        AssertAt(scroll, At(vm.Settings.DayEndHour) - page);
        Key(window, PhysicalKey.End, RawInputModifiers.Control);
        AssertAt(scroll, last);
        Assert.Equal(Chosen, vm.SelectedDate);
        return Task.CompletedTask;
    });

    [Theory]
    [Trait("TestCase", "T454")]
    [InlineData(Year, "YearScroll")]
    [InlineData(Agenda, "AgendaScroll")]
    public Task Up_and_down_scroll_the_year_and_the_schedule(int view, string name) => InKeyAppAsync((window, vm) =>
    {
        var calendar = vm.CreateLocalCalendar("Scorri " + Guid.NewGuid().ToString("N")[..6]);
        try
        {
            for (var i = 0; i < 25; i++)
            {
                var start = Chosen.AddDays(i).AddHours(10);
                vm.AddEvent(new CalendarEvent(Guid.NewGuid().ToString("N"), calendar, "Riga " + i, start, start.AddHours(1)));
            }

            vm.ViewModeIndex = view;
            window.Height = 500;
            window.Width = 900;
            Settle(window);
            var scroll = Named<ScrollViewer>(window, name);
            var page = scroll.Viewport.Height;
            var last = scroll.Extent.Height - page;
            Assert.True(last > 100, "la vista deve essere più alta della finestra");

            Key(window, PhysicalKey.Home);
            AssertAt(scroll, 0);
            Key(window, PhysicalKey.ArrowDown);
            AssertAt(scroll, 48);
            Key(window, PhysicalKey.ArrowUp);
            AssertAt(scroll, 0);
            Key(window, PhysicalKey.PageDown);
            AssertAt(scroll, page);
            Key(window, PhysicalKey.PageUp);
            AssertAt(scroll, 0);
            Key(window, PhysicalKey.End);
            AssertAt(scroll, last);
            Key(window, PhysicalKey.Home, RawInputModifiers.Control);
            AssertAt(scroll, 0);
            Key(window, PhysicalKey.End, RawInputModifiers.Control);
            AssertAt(scroll, last);
        }
        finally
        {
            vm.DeleteLocalCalendar(calendar);
        }

        return Task.CompletedTask;
    });

    [Fact]
    [Trait("TestCase", "T455")]
    public Task In_the_month_view_the_page_keys_change_month_and_the_others_do_nothing() => InKeyAppAsync((window, vm) =>
    {
        vm.ViewModeIndex = Month;
        Settle(window);
        foreach (var key in new[] { PhysicalKey.ArrowUp, PhysicalKey.ArrowDown, PhysicalKey.Home, PhysicalKey.End }) Key(window, key);
        Assert.Equal(Chosen, vm.SelectedDate);
        Key(window, PhysicalKey.PageDown);
        Assert.Equal(Chosen.AddMonths(1), vm.SelectedDate);
        Key(window, PhysicalKey.PageUp);
        Key(window, PhysicalKey.PageUp);
        Assert.Equal(Chosen.AddMonths(-1), vm.SelectedDate);
        return Task.CompletedTask;
    });

    [Fact]
    [Trait("TestCase", "T456")]
    public Task The_day_moved_in_the_year_view_is_scrolled_into_sight() => InKeyAppAsync(async (window, vm) =>
    {
        vm.ViewModeIndex = Year;
        window.Height = 500;
        window.Width = 900;
        Settle(window);
        var scroll = Named<ScrollViewer>(window, "YearScroll");
        var grid = Named<YearGrid>(window, "Year");
        vm.SelectedDate = new DateTime(2026, 12, 30);
        Settle(window);
        Key(window, PhysicalKey.Home);
        Key(window, PhysicalKey.ArrowRight, RawInputModifiers.Shift);
        await PauseAsync(TimeSpan.FromMilliseconds(100));
        Settle(window);
        var day = grid.RectOf(new DateTime(2026, 12, 31));
        Assert.InRange(day.Bottom, scroll.Offset.Y, scroll.Offset.Y + scroll.Viewport.Height + 1);
    });

    [Fact]
    [Trait("TestCase", "T457")]
    public Task Where_the_keys_that_move_belong_to_a_control_the_view_stays() => InKeyAppAsync(async (window, vm) =>
    {
        window.Height = 600;
        Settle(window);
        var scroll = Named<ScrollViewer>(window, "GridScroll");
        Key(window, PhysicalKey.F, RawInputModifiers.Control);
        var box = Named<TextBox>(window, "SearchBox");
        await WaitForAsync(() => box.IsFocused ? box : null, "la casella di ricerca");
        var at = scroll.Offset.Y;
        foreach (var modifiers in new[] { RawInputModifiers.Control, RawInputModifiers.Shift })
            foreach (var key in new[] { PhysicalKey.ArrowLeft, PhysicalKey.ArrowRight, PhysicalKey.Home, PhysicalKey.End }) Key(window, key, modifiers);
        Key(window, PhysicalKey.G);
        Assert.Equal(Chosen, vm.SelectedDate);
        Assert.Equal(at, scroll.Offset.Y);
        Assert.Empty(window.OwnedWindows);

        Key(window, PhysicalKey.Escape);
        var mini = Named<Calendar>(window, "MiniCalendar");
        var cell = mini.GetVisualDescendants().OfType<CalendarDayButton>().First(c => c.IsEffectivelyVisible && c.DataContext is DateTime d && d == Chosen);
        Assert.True(cell.Focus());
        Key(window, PhysicalKey.PageDown);
        Assert.Equal(at, scroll.Offset.Y);
    });

    [Theory]
    [Trait("TestCase", "T458")]
    [InlineData(PhysicalKey.G, RawInputModifiers.None)]
    [InlineData(PhysicalKey.G, RawInputModifiers.Control)]
    public Task G_goes_to_a_date(PhysicalKey key, RawInputModifiers modifiers) => InKeyAppAsync(async (window, vm) =>
    {
        vm.ViewModeIndex = Month;
        Settle(window);
        Key(window, key, modifiers);
        var dialog = await DialogAsync<GoToDateWindow>(window);
        Settle(dialog);
        var box = dialog.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "DateBox");
        Assert.Equal(Chosen, GoToDateWindow.Parse(box.Text, GGCalendar.Core.Localization.L.Culture));

        box.Text = "non è una data";
        Key(dialog, PhysicalKey.Enter);
        Assert.Single(window.OwnedWindows); // still open, with the message

        var target = new DateTime(2031, 3, 15);
        box.Text = target.ToString("d", GGCalendar.Core.Localization.L.Culture);
        Key(dialog, PhysicalKey.Enter);
        await ClosedAsync(window);
        Assert.Equal(target, vm.SelectedDate);
        Assert.Equal(Month, vm.ViewModeIndex);

        Key(window, key, modifiers);
        var again = await DialogAsync<GoToDateWindow>(window);
        Settle(again);
        Key(again, PhysicalKey.Escape);
        await ClosedAsync(window);
        Assert.Equal(target, vm.SelectedDate);
    });

    [Fact]
    [Trait("TestCase", "T459")]
    public Task The_arrows_enter_the_results_of_the_search_and_Enter_opens_one() => InKeyAppAsync(async (window, vm) =>
    {
        var word = "freccia" + Guid.NewGuid().ToString("N")[..6];
        var calendar = vm.CreateLocalCalendar(word);
        try
        {
            for (var i = 1; i <= 2; i++)
            {
                var start = DateTime.Today.AddDays(i).AddHours(10);
                vm.AddEvent(new CalendarEvent(Guid.NewGuid().ToString("N"), calendar, $"{word} {i}", start, start.AddHours(1)));
            }

            Key(window, PhysicalKey.F, RawInputModifiers.Control);
            var box = Named<TextBox>(window, "SearchBox");
            await WaitForAsync(() => box.IsFocused ? box : null, "la casella di ricerca");
            box.Text = word;
            Key(window, PhysicalKey.Enter);
            var results = Named<ItemsControl>(window, "SearchResults");
            List<Border> Lines() => results.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("searchrow")).ToList();
            await WaitForAsync(() => { Settle(window); return Lines().Count == 2 ? results : null; }, "i due risultati");

            Key(window, PhysicalKey.ArrowDown);
            Assert.True(Lines()[0].IsFocused);
            Key(window, PhysicalKey.ArrowDown);
            Assert.True(Lines()[1].IsFocused);
            Key(window, PhysicalKey.ArrowDown); // the last one: stays
            Assert.True(Lines()[1].IsFocused);
            Key(window, PhysicalKey.ArrowUp);
            Key(window, PhysicalKey.ArrowUp);
            Assert.True(box.IsFocused);

            Key(window, PhysicalKey.ArrowDown);
            Key(window, PhysicalKey.Enter);
            var editor = await DialogAsync<EventEditWindow>(window);
            Assert.Equal($"{word} 1", ((EventEditViewModel)editor.DataContext!).Title);
            Settle(editor);
            Key(editor, PhysicalKey.Escape);
            await ClosedAsync(window);
        }
        finally
        {
            vm.DeleteLocalCalendar(calendar);
        }
    });

    [Fact]
    [Trait("TestCase", "T460")]
    public Task The_tooltips_of_the_buttons_name_their_keys_in_every_language() => InKeyAppAsync((window, _) =>
    {
        var language = GGCalendar.Core.Localization.L.Language;
        try
        {
            foreach (var next in new[] { "de", "it" })
            {
                GGCalendar.Core.Localization.L.Apply(next);
                Settle(window);
                var ctrl = GGCalendar.Core.Localization.L.T("key.ctrl");
                foreach (var (name, text, keys) in new[]
                         {
                             ("TodayButton", "main.today", "(T)"), ("PreviousButton", "shortcuts.previous", "(←, K, P)"), ("NextButton", "shortcuts.next", "(→, J, N)"),
                             ("SearchButton", "search.tip", $"(/, {ctrl}+F)"), ("SyncButton", "main.sync_now", "(R, F5)"), ("SettingsButton", "main.settings", $"(S, {ctrl}+,)"), ("AboutButton", "main.about", "(I)"),
                         })
                    Assert.Equal($"{GGCalendar.Core.Localization.L.T(text)} {keys}", ToolTip.GetTip(Named<Button>(window, name)));
                Assert.EndsWith("+A)", (string)ToolTip.GetTip(Named<Button>(window, "AlarmsButton"))!);
            }
        }
        finally
        {
            GGCalendar.Core.Localization.L.Apply(language);
        }

        return Task.CompletedTask;
    });
}
