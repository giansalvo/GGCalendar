using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GGCalendar.App.Controls;
using GGCalendar.App.ViewModels;
using GGCalendar.Core.Models;

namespace GGCalendar.App.Views;

/// <summary>The selected event and what the keyboard does with it (issue #102, third phase): in every view but the year one.</summary>
public partial class MainWindow
{
    private EventKey? _selection;

    /// <summary>The event selected in the view, or null.</summary>
    public EventKey? Selection => _selection;

    private void SetUpSelection()
    {
        // Before everything else: Tab would only move to the next control, and a scroll viewer would take some of the keys.
        AddHandler(KeyDownEvent, OnEventKey, RoutingStrategies.Tunnel);
        Grid.SelectionChanged += Select;
        ViewHost.AddHandler(PointerPressedEvent, OnViewPressed, handledEventsToo: true);
        DataContextChanged += (_, _) =>
        {
            if (Vm is { } vm) vm.PropertyChanged += OnViewModelChangedForSelection;
        };
        if (Vm is { } now) now.PropertyChanged += OnViewModelChangedForSelection;
    }

    private void OnViewModelChangedForSelection(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // Another view or another day: the selection belonged to what was shown.
        if (e.PropertyName is nameof(MainViewModel.ViewModeIndex) or nameof(MainViewModel.SelectedDate)) Select((EventKey?)null);
        // The lines of the schedule are made again: the selected one is marked again.
        else if (e.PropertyName == nameof(MainViewModel.AgendaDays)) Dispatcher.UIThread.Post(MarkAgendaLines, DispatcherPriority.Loaded);
    }

    private void Select(EventKey? key)
    {
        _selection = key;
        Grid.Selection = key;
        AllDay.Selection = key;
        Month.Selection = key;
        MarkAgendaLines();
    }

    private void Select(CalendarEvent ev) => Select(EventKey.Of(ev));

    private IEnumerable<Border> AgendaLines() => AgendaList.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("agendarow"));

    private void MarkAgendaLines()
    {
        var marked = false; // an event of several days has a line for each: the first one is marked
        foreach (var line in AgendaLines())
        {
            var selected = !marked && line.DataContext is AgendaItem item && EventKey.Of(item.Event) == _selection;
            line.Classes.Set("selected", selected);
            marked |= selected;
        }
    }

    /// <summary>
    /// The events the view draws, in the order Tab goes through them: by day, those of the whole day before those with a time,
    /// each one once. Empty in the year view and while the results of a search cover the view.
    /// </summary>
    public IReadOnlyList<CalendarEvent> SelectableEvents()
    {
        if (Vm is not { } vm || vm.IsYear || vm.Search.ShowResults) return [];
        IEnumerable<CalendarEvent> events;
        if (vm.IsMonth) events = Month.DrawnEvents();
        else if (vm.IsAgenda) events = vm.AgendaDays.SelectMany(d => d.Items).Select(i => i.Event);
        else
        {
            var first = vm.WeekStart.Date;
            events = AllDay.DrawnEvents().Concat(Grid.DrawnEvents())
                .OrderBy(e => e.Start.Date < first ? first : e.Start.Date)
                .ThenBy(e => !e.IsAllDay).ThenBy(e => e.Start).ThenBy(e => e.End).ThenBy(e => e.Title, StringComparer.CurrentCulture);
        }

        return events.DistinctBy(EventKey.Of).ToList();
    }

    private CalendarEvent? SelectedEvent() => _selection is { } key ? SelectableEvents().FirstOrDefault(e => EventKey.Of(e) == key) : null;

    /// <summary>A click on the month, the schedule or the whole day events brings the keyboard to the view.</summary>
    private void OnViewPressed(object? sender, PointerPressedEventArgs e)
    {
        if (Vm?.Search.ShowResults == true) return;
        if (e.Source is Visual source && source.GetSelfAndVisualAncestors().Contains(Grid)) return; // the time grid takes it itself
        ViewHost.Focus(NavigationMethod.Pointer);
    }

    private void OnEventKey(object? sender, KeyEventArgs e)
    {
        // Only with the keyboard on the view: elsewhere these keys belong to the controls.
        if (Vm is not { } vm || !(ReferenceEquals(e.Source, ViewHost) || ReferenceEquals(e.Source, Grid)) || vm.Search.ShowResults) return;
        if (e.Key == Key.Tab)
        {
            if (e.KeyModifiers is KeyModifiers.None or KeyModifiers.Shift) e.Handled = MoveSelection(e.KeyModifiers == KeyModifiers.Shift ? -1 : 1);
            return;
        }

        var selected = SelectedEvent();
        if (selected is null)
        {
            // With nothing selected Enter makes an event on the selected day.
            if (e.Key != Key.Enter || e.KeyModifiers != KeyModifiers.None) return;
            e.Handled = true;
            _ = AfterEventActionAsync(NewEventOnAsync(vm.SelectedDate ?? DateTime.Today));
            return;
        }

        var readOnly = MainViewModel.IsReadOnly(selected);
        if (e.KeyModifiers == KeyModifiers.None)
        {
            switch (e.Key)
            {
                case Key.Space:
                    ShowSelected(selected);
                    break;
                case Key.Enter or Key.E:
                    // An event that cannot be changed has no editor: its card is shown.
                    if (readOnly) ShowSelected(selected);
                    else _ = AfterEventActionAsync(EditAsync(selected, isNew: false));
                    break;
                case Key.Delete or Key.Back:
                    if (!readOnly) _ = AfterEventActionAsync(DeleteAsync(selected));
                    break;
                default:
                    return;
            }

            e.Handled = true;
        }
        else if (e.KeyModifiers is KeyModifiers.Alt or (KeyModifiers.Alt | KeyModifiers.Shift) && e.Key is Key.Left or Key.Right or Key.Up or Key.Down)
        {
            e.Handled = true;
            if (!readOnly && Shortcuts.Moved(selected, e.Key, e.KeyModifiers.HasFlag(KeyModifiers.Shift), vm.Settings.SnapMinutes) is { } to)
                _ = AfterEventActionAsync(MoveSelectedAsync(selected, to.Start, to.End));
        }
    }

    /// <summary>Tab and Shift+Tab: the next or previous event; past the last one (or the first) the keyboard leaves the view.</summary>
    private bool MoveSelection(int direction)
    {
        var events = SelectableEvents();
        if (events.Count == 0) return false; // as without events: Tab goes to the next control
        var current = -1;
        for (var i = 0; i < events.Count; i++)
            if (EventKey.Of(events[i]) == _selection) current = i;
        var next = current >= 0 ? current + direction : direction > 0 ? 0 : events.Count - 1;
        CloseDetails();
        if (next >= 0 && next < events.Count)
        {
            Select(events[next]);
            BringSelectionIntoView(events[next]);
            return true;
        }

        Select((EventKey?)null);
        if (direction > 0) return FocusAreaAt(0);
        var calendars = CalendarList.GetVisualDescendants().OfType<Button>().LastOrDefault(b => b.Classes.Contains("calitem") && b.IsEffectivelyVisible);
        return calendars?.Focus(NavigationMethod.Tab) == true || FocusAreaAt(1);
    }

    private void BringSelectionIntoView(CalendarEvent ev)
    {
        if (Vm is not { } vm) return;
        if (vm.IsAgenda)
            AgendaLines().FirstOrDefault(l => l.Classes.Contains("selected"))?.BringIntoView();
        else if (vm.IsTimeGrid && !ev.IsAllDay)
        {
            // The start of the event (midnight for one begun before the days shown), a little below the top, unless it is already in sight.
            var hours = ev.Start.Date < vm.WeekStart.Date ? 0 : ev.Start.TimeOfDay.TotalHours;
            var y = Math.Max(0, (hours - vm.FirstHour) * WeekGrid.HourHeight);
            var view = GridScroll.Viewport.Height;
            if (y < GridScroll.Offset.Y || y + WeekGrid.HourHeight / 2 > GridScroll.Offset.Y + view)
                GridScroll.Offset = new Vector(0, Math.Clamp(y - view / 4, 0, Math.Max(0, GridScroll.Extent.Height - view)));
        }
    }

    /// <summary>Space: the card of the selected event, beside it.</summary>
    private void ShowSelected(CalendarEvent ev)
    {
        if (Vm is not { } vm) return;
        if (vm.IsAgenda && AgendaLines().FirstOrDefault(l => l.Classes.Contains("selected")) is { } line)
        {
            ShowDetailsOfLine(ev, line, AgendaList);
            return;
        }

        var key = EventKey.Of(ev);
        if (vm.IsMonth) ShowDetails(ev, Month, Month.RectOf(key));
        else if (ev.IsAllDay) ShowDetails(ev, AllDay, AllDay.RectOf(key));
        else ShowDetails(ev, Grid, Grid.RectOf(key));
    }

    /// <summary>After a window or a change made with the keyboard, the keyboard is on the view again.</summary>
    private async Task AfterEventActionAsync(Task action)
    {
        await action;
        if (!DetailsPopup.IsOpen) FocusView();
    }

    /// <summary>Moves the selected event (asking what to change for a series) and keeps it selected, going where it went.</summary>
    private async Task MoveSelectedAsync(CalendarEvent ev, DateTime start, DateTime end)
    {
        if (Vm is not { } vm) return;
        await MoveAsync(ev, start, end);

        // Moved out of the days shown: the view follows it.
        var day = start.Date;
        var shown = vm.IsMonth ? day >= vm.MonthGridStart.Date && day < vm.MonthGridStart.Date.AddDays(42)
            : vm.IsAgenda ? day >= (vm.SelectedDate ?? DateTime.Today).Date && day < (vm.SelectedDate ?? DateTime.Today).Date.AddDays(30)
            : day >= vm.WeekStart.Date && day < vm.WeekStart.Date.AddDays(vm.DayCount);
        var events = SelectableEvents();
        // A single occurrence changed becomes an event of its own, with another id: it is then known by its times and title.
        bool IsIt(CalendarEvent x) => x.Start == start && x.End == end && x.CalendarId == ev.CalendarId && x.Title == ev.Title;
        if (!shown && events.Any(x => IsIt(x) || EventKey.Of(x) == EventKey.Of(ev)) == false)
        {
            vm.SelectedDate = day;
            events = SelectableEvents();
        }

        var moved = events.FirstOrDefault(x => x.Id == ev.Id && x.Start == start) ?? events.FirstOrDefault(IsIt);
        // Not moved (the question about the series was cancelled): the selection stays.
        moved ??= events.FirstOrDefault(x => EventKey.Of(x) == EventKey.Of(ev));
        Select(moved is null ? null : EventKey.Of(moved));
        if (moved is not null) BringSelectionIntoView(moved);
    }
}
