using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.VisualTree;
using GGCalendar.Core.Models;

namespace GGCalendar.App.Views;

public enum ShortcutCommand
{
    Today, Previous, Next, NewEvent, Sync, Settings, Search, View, Alarms, Weekends, Help, NextArea, PreviousArea,
    About, GoToDate, PreviousDay, NextDay, JumpBack, JumpForward,
    // What moves up and down in the view shown.
    LineUp, LineDown, PageUp, PageDown, Start, End, Top, Bottom,
}

/// <param name="View">The view asked for (only with <see cref="ShortcutCommand.View"/>), as in the view model.</param>
public readonly record struct Shortcut(ShortcutCommand Command, int View = -1);

/// <summary>The keyboard shortcuts of the main window (issue #102): which key does what, and when a key is left to the control that has the focus.</summary>
public static class Shortcuts
{
    private const int Day = 0, Week = 1, Month = 2, Year = 3, Agenda = 4, SixDays = 5;

    /// <summary>True for the commands that move up and down in the view.</summary>
    public static bool IsScroll(ShortcutCommand command) => command is >= ShortcutCommand.LineUp and <= ShortcutCommand.Bottom;

    /// <param name="symbol">The character the key writes: "/" and "?" are recognised by it, as their keys change with the keyboard.</param>
    public static Shortcut? Map(Key key, KeyModifiers modifiers, string? symbol)
    {
        if (symbol == "/" && modifiers is KeyModifiers.None or KeyModifiers.Shift) return new(ShortcutCommand.Search);
        if (symbol == "?" && modifiers is KeyModifiers.None or KeyModifiers.Shift) return new(ShortcutCommand.Help);
        return modifiers switch
        {
            KeyModifiers.None => key switch
            {
                Key.T => new(ShortcutCommand.Today),
                Key.Left or Key.K or Key.P => new(ShortcutCommand.Previous),
                Key.Right or Key.J or Key.N => new(ShortcutCommand.Next),
                Key.C => new(ShortcutCommand.NewEvent),
                Key.R or Key.F5 => new(ShortcutCommand.Sync),
                Key.S => new(ShortcutCommand.Settings),
                Key.I => new(ShortcutCommand.About),
                Key.F1 => new(ShortcutCommand.Help),
                Key.F6 => new(ShortcutCommand.NextArea),
                Key.G => new(ShortcutCommand.GoToDate),
                Key.Up => new(ShortcutCommand.LineUp),
                Key.Down => new(ShortcutCommand.LineDown),
                Key.PageUp => new(ShortcutCommand.PageUp),
                Key.PageDown => new(ShortcutCommand.PageDown),
                Key.Home => new(ShortcutCommand.Start),
                Key.End => new(ShortcutCommand.End),
                Key.D or Key.D1 or Key.NumPad1 => new(ShortcutCommand.View, Day),
                Key.W or Key.D2 or Key.NumPad2 => new(ShortcutCommand.View, Week),
                Key.M or Key.D3 or Key.NumPad3 => new(ShortcutCommand.View, Month),
                Key.X or Key.D4 or Key.NumPad4 => new(ShortcutCommand.View, SixDays),
                Key.A or Key.D5 or Key.NumPad5 => new(ShortcutCommand.View, Agenda),
                Key.Y or Key.D6 or Key.NumPad6 => new(ShortcutCommand.View, Year),
                _ => null,
            },
            KeyModifiers.Control => key switch
            {
                Key.N => new(ShortcutCommand.NewEvent),
                Key.F => new(ShortcutCommand.Search),
                Key.OemComma => new(ShortcutCommand.Settings),
                Key.G => new(ShortcutCommand.GoToDate),
                Key.Left => new(ShortcutCommand.JumpBack),
                Key.Right => new(ShortcutCommand.JumpForward),
                Key.Home => new(ShortcutCommand.Top),
                Key.End => new(ShortcutCommand.Bottom),
                _ => null,
            },
            KeyModifiers.Shift when key == Key.W => new Shortcut(ShortcutCommand.Weekends),
            KeyModifiers.Shift when key == Key.F6 => new Shortcut(ShortcutCommand.PreviousArea),
            KeyModifiers.Shift when key == Key.Left => new Shortcut(ShortcutCommand.PreviousDay),
            KeyModifiers.Shift when key == Key.Right => new Shortcut(ShortcutCommand.NextDay),
            KeyModifiers.Control | KeyModifiers.Shift when key == Key.A => new Shortcut(ShortcutCommand.Alarms),
            _ => null,
        };
    }

    /// <summary>
    /// True when the key belongs to the control it comes from: single keys are off while something is typed or chosen and in menus
    /// and drop-down panels; the keys that move (arrows, Home, End, page keys), also with Ctrl, are left to those controls and to the
    /// ones that move with them (the small calendar, lists, sliders). Other combinations with Ctrl and the function keys always work.
    /// </summary>
    public static bool BelongsToControl(Key key, KeyModifiers modifiers, Visual? source)
    {
        var vertical = key is Key.Up or Key.Down or Key.PageUp or Key.PageDown or Key.Home or Key.End;
        var moves = vertical || key is Key.Left or Key.Right;
        if (source is null || key is >= Key.F1 and <= Key.F24 || (modifiers.HasFlag(KeyModifiers.Control) && !moves)) return false;
        var path = source.GetSelfAndVisualAncestors().ToList();
        if (path.Any(v => v is TextBox or ComboBox or ComboBoxItem or NumericUpDown or CalendarDatePicker or MenuBase or MenuItem or MenuFlyoutPresenter or FlyoutPresenter)) return true;
        // The card of an event is a popup too, but nothing is typed or chosen in it; up and down scroll its own text.
        var card = path.Any(v => v is EventDetailsCard);
        if (!card && path.Any(v => v is PopupRoot or OverlayPopupHost)) return true;
        return (card && vertical) || (moves && path.Any(v => v is Calendar or Slider or SelectingItemsControl));
    }

    /// <summary>
    /// Alt and the arrows on the selected event: up and down move its time by <paramref name="snapMinutes"/>, left and right by a day;
    /// with Shift (<paramref name="resize"/>) up shortens it and down lengthens it, by the same steps (by a day for a whole day event).
    /// Null when the keys change nothing.
    /// </summary>
    public static (DateTime Start, DateTime End)? Moved(CalendarEvent ev, Key key, bool resize, int snapMinutes)
    {
        var step = TimeSpan.FromMinutes(Math.Max(1, snapMinutes));
        var sign = key is Key.Left or Key.Up ? -1 : 1;
        if (resize)
        {
            if (key is not (Key.Up or Key.Down)) return null;
            if (ev.IsAllDay) step = TimeSpan.FromDays(1);
            var end = ev.End + sign * step;
            return end - ev.Start < step ? null : (ev.Start, end); // never shorter than one step
        }

        if (key is Key.Left or Key.Right) return (ev.Start.AddDays(sign), ev.End.AddDays(sign));
        if (key is not (Key.Up or Key.Down) || ev.IsAllDay) return null;
        return (ev.Start + sign * step, ev.End + sign * step);
    }

    /// <summary>
    /// Start of an event made from the keyboard or with "Crea" on <paramref name="day"/>: today the next full hour (not before the
    /// start of the day), any other day the start of the day. In the last hour of today there is no next one: the start of the day.
    /// </summary>
    public static DateTime NewEventStart(DateTime day, DateTime now, int dayStartHour)
    {
        var date = day.Date;
        if (date != now.Date) return date.AddHours(dayStartHour);
        var start = date.AddHours(Math.Max(now.Hour + 1, dayStartHour));
        return start.Date == date ? start : date.AddHours(dayStartHour);
    }
}
