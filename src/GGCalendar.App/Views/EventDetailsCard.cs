using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using GGCalendar.Core.Models;
using GGCalendar.Core.Recurrence;
using GGCalendar.Core.Localization;

namespace GGCalendar.App.Views;

/// <summary>
/// Read-only summary of an event shown on a single click (like Google Calendar): actions on top, then title, time,
/// recurrence, Meet, location, description, guests, reminders and calendar.
/// </summary>
public sealed class EventDetailsCard : Border
{
    private static CultureInfo Culture => L.Culture;
    private const string IconFont = "Segoe Fluent Icons, Segoe MDL2 Assets";

    public event Action? EditRequested;
    public event Action? DeleteRequested;
    public event Action? MailRequested;
    public event Action? ExportRequested;
    public event Action? JoinMeetRequested;
    public event Action? CloseRequested;

    /// <summary>A link of the description was clicked; the window asks the user before opening it.</summary>
    public event Action<Uri>? LinkRequested;

    /// <param name="ev">The clicked event (an occurrence for repeating events).</param>
    /// <param name="recurrence">RRULE of the series the event belongs to, if any.</param>
    public EventDetailsCard(CalendarEvent ev, string? recurrence, string colorHex, string calendarName, string accountName, bool use24Hour, bool dark, string? secondZoneText = null, bool readOnly = false)
    {
        IBrush fg = new SolidColorBrush(Color.Parse(dark ? "#E8EAED" : "#1F1F1F"));
        var muted = new SolidColorBrush(Color.Parse(dark ? "#BDC1C6" : "#5F6368"));
        Background = new SolidColorBrush(Color.Parse(dark ? "#2D2E30" : "#FFFFFF"));
        BorderBrush = new SolidColorBrush(Color.Parse(dark ? "#3C4043" : "#DADCE0"));
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(16);
        BoxShadow = BoxShadows.Parse("0 8 28 0 #50000000");
        Padding = new Thickness(20, 12, 20, 20);
        Width = 440;
        Focusable = true;
        KeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            CloseRequested?.Invoke();
            e.Handled = true;
        };

        // Actions
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 2 };
        if (!readOnly) actions.Children.Add(IconButton("", L.T("details.edit"), fg, () => EditRequested?.Invoke()));
        if (!readOnly) actions.Children.Add(IconButton("", L.T("edit.delete_event"), fg, () => DeleteRequested?.Invoke()));
        if (ev.Attendees.Count > 0) actions.Children.Add(IconButton("", L.T("details.mail"), fg, () => MailRequested?.Invoke()));
        if (!readOnly)
        {
            var more = IconButton("", L.T("details.more"), fg, null);
            var export = new MenuItem { Header = L.T("details.export") };
            export.Click += (_, _) => ExportRequested?.Invoke();
            more.Flyout = new MenuFlyout { Items = { export } };
            actions.Children.Add(more);
        }
        actions.Children.Add(IconButton("", L.T("common.close"), fg, () => CloseRequested?.Invoke()));

        var body = new StackPanel { Spacing = 14, Margin = new Thickness(0, 6, 0, 0) };

        // Title, time, recurrence
        var title = new StackPanel { Spacing = 2 };
        title.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(ev.Title) ? L.T("common.untitled") : ev.Title,
            FontSize = 22, Foreground = fg, TextWrapping = TextWrapping.Wrap,
        });
        title.Children.Add(Muted(When(ev, use24Hour), muted));
        if (secondZoneText is not null)
            title.Children.Add(new TextBlock { Text = secondZoneText, Foreground = muted, FontSize = 12, Name = "SecondZoneText" });
        if (RecurrenceRule.Parse(recurrence) is { } rule) title.Children.Add(Muted(rule.Describe(), muted));
        body.Children.Add(Row(new Border
        {
            Width = 16, Height = 16, CornerRadius = new CornerRadius(4), Margin = new Thickness(0, 8, 0, 0),
            Background = new SolidColorBrush(Color.Parse(colorHex)), VerticalAlignment = VerticalAlignment.Top,
        }, title));

        // Google Meet
        if (ev.MeetLink is { Length: > 0 } meet)
        {
            var join = new Button { Content = L.T("details.join_meet"), Classes = { "accent" }, CornerRadius = new CornerRadius(18), Padding = new Thickness(18, 6) };
            join.Click += (_, _) => JoinMeetRequested?.Invoke();
            var meetPanel = new StackPanel { Spacing = 4 };
            meetPanel.Children.Add(join);
            meetPanel.Children.Add(Muted(meet, muted));
            body.Children.Add(Row(Icon("", muted), meetPanel));
        }

        if (!string.IsNullOrWhiteSpace(ev.Location))
            body.Children.Add(Row(Icon("", muted), Text(ev.Location, fg)));

        if (!string.IsNullOrWhiteSpace(ev.Description))
        {
            var description = new DescriptionBlock(ev.Description, fg, new SolidColorBrush(Color.Parse(dark ? "#8AB4F8" : "#1A73E8")));
            description.LinkRequested += uri => LinkRequested?.Invoke(uri);
            Avalonia.Automation.AutomationProperties.SetAutomationId(description, "Details.Description");
            body.Children.Add(Row(Icon("", muted), description));
        }

        if (ev.Attendees.Count > 0)
        {
            var guests = new StackPanel { Spacing = 2 };
            guests.Children.Add(Text(L.N("details.guests", ev.Attendees.Count), fg));
            foreach (var a in ev.Attendees.Take(6)) guests.Children.Add(Muted(a.Email + (a.IsOptional ? L.T("details.optional") : ""), muted));
            if (ev.Attendees.Count > 6) guests.Children.Add(Muted(L.T("details.and_others", ev.Attendees.Count - 6), muted));
            body.Children.Add(Row(Icon("", muted), guests));
        }

        if (ev.Reminders.Count > 0)
        {
            var reminders = new StackPanel { Spacing = 2 };
            foreach (var r in ev.Reminders.OrderBy(r => r.Minutes)) reminders.Children.Add(Text(Reminder(r), fg));
            body.Children.Add(Row(Icon("", muted), reminders));
        }

        var calendar = new StackPanel { Spacing = 2 };
        calendar.Children.Add(Text(calendarName, fg));
        if (accountName != calendarName) calendar.Children.Add(Muted(accountName, muted));
        body.Children.Add(Row(Icon("", muted), calendar));

        var content = new StackPanel();
        content.Children.Add(actions);
        content.Children.Add(body);
        Child = content;
    }

    private static Grid Row(Control icon, Control content)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("36,*") };
        Grid.SetColumn(content, 1);
        grid.Children.Add(icon);
        grid.Children.Add(content);
        return grid;
    }

    private static TextBlock Icon(string glyph, IBrush brush) => new()
    {
        Text = glyph, FontFamily = new FontFamily(IconFont), FontSize = 18, Foreground = brush,
        VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 0, 0),
    };

    private static TextBlock Text(string s, IBrush brush) => new() { Text = s, Foreground = brush, TextWrapping = TextWrapping.Wrap };

    private static TextBlock Muted(string s, IBrush brush) => new() { Text = s, Foreground = brush, FontSize = 13, TextWrapping = TextWrapping.Wrap };

    private static Button IconButton(string glyph, string tip, IBrush brush, Action? click)
    {
        var button = new Button
        {
            Content = new TextBlock { Text = glyph, FontFamily = new FontFamily(IconFont), FontSize = 16, Foreground = brush },
            Width = 38, Height = 38, Padding = new Thickness(0), CornerRadius = new CornerRadius(19),
            Background = Brushes.Transparent,
            HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
        };
        ToolTip.SetTip(button, tip);
        if (click is not null) button.Click += (_, _) => click();
        return button;
    }

    /// <summary>"Mercoledì, 7 ottobre · 05:00 – 06:00", "Mercoledì, 7 ottobre" (all day) or a range across days.</summary>
    internal static string When(CalendarEvent ev, bool use24Hour)
    {
        string Day(DateTime d) => Capitalize(d.ToString("dddd, d MMMM", Culture));
        string Time(DateTime t) => t.ToString(use24Hour ? "HH:mm" : "h:mm tt", Culture);

        if (ev.IsAllDay)
        {
            var last = ev.End.Date.AddDays(-1);
            return last <= ev.Start.Date ? Day(ev.Start) : $"{Day(ev.Start)} – {Day(last)}";
        }

        return ev.End.Date == ev.Start.Date || ev.End == ev.Start.Date.AddDays(1)
            ? $"{Day(ev.Start)} · {Time(ev.Start)} – {Time(ev.End)}"
            : $"{Day(ev.Start)}, {Time(ev.Start)} – {Day(ev.End)}, {Time(ev.End)}";
    }

    /// <summary>"30 minuti prima", "Sveglia 1 ora prima", "Email 2 giorni prima", "All'inizio".</summary>
    internal static string Reminder(EventReminder r)
    {
        var when = BeforeStart(r.Minutes);
        return r.Method switch
        {
            ReminderMethod.Alarm => L.T("reminder.with_alarm", when),
            ReminderMethod.Email => L.T("reminder.with_email", when),
            _ => Capitalize(when),
        };
    }

    /// <summary>"all'inizio", "30 minuti prima", "1 ora prima", "2 giorni prima": when a reminder rings, relative to the start.</summary>
    internal static string BeforeStart(int minutes) => minutes switch
    {
        0 => L.T("reminder.at_start"),
        var m when m % 10080 == 0 => L.N("reminder.before_weeks", m / 10080),
        var m when m % 1440 == 0 => L.N("reminder.before_days", m / 1440),
        var m when m % 60 == 0 => L.N("reminder.before_hours", m / 60),
        var m => L.N("reminder.before_minutes", m),
    };

    private static string Capitalize(string s) => s.Length == 0 ? s : char.ToUpper(s[0], Culture) + s[1..];
}
