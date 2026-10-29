using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using GGCalendar.Core.Models;
using GGCalendar.Core.Recurrence;
using GGCalendar.Core.Localization;
using GGCalendar.Core.Sharing;

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

    /// <summary>A way of sharing was chosen in the row that opens over the share button (issue #45).</summary>
    public event Action<ShareTarget>? ShareRequested;

    /// <summary>The mouse (or the keyboard) is on a way of sharing: the key of the text saying what it does, for the status bar; null when it leaves.</summary>
    public event Action<string?>? ShareHint;
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
        actions.Children.Add(ShareButton(fg, ev.Attendees.Count > 0));
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

        if (ev.Attachments is { Count: > 0 } attachments)
        {
            var linkBrush = new SolidColorBrush(Color.Parse(dark ? "#8AB4F8" : "#1A73E8"));
            var list = new StackPanel { Spacing = 2 };
            Avalonia.Automation.AutomationProperties.SetAutomationId(list, "Details.Attachments");
            foreach (var attachment in attachments)
            {
                if (!attachment.CanOpen)
                {
                    list.Children.Add(Muted(attachment.DisplayName, muted));
                    continue;
                }

                var open = new Button
                {
                    Content = new TextBlock { Text = attachment.DisplayName, Foreground = linkBrush, TextDecorations = TextDecorations.Underline, TextTrimming = TextTrimming.CharacterEllipsis },
                    Background = Brushes.Transparent, Padding = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Left,
                    Cursor = new Cursor(StandardCursorType.Hand),
                };
                ToolTip.SetTip(open, attachment.Url);
                Avalonia.Automation.AutomationProperties.SetAutomationId(open, "Details.Attachment");
                open.Click += (_, _) => LinkRequested?.Invoke(new Uri(attachment.Url));
                list.Children.Add(open);
            }

            body.Children.Add(Row(Icon("\uE723", muted), list));
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

    /// <summary>The share button; it opens a row of ways to share over itself (WhatsApp, Telegram, email, disk, clipboard, other apps).</summary>
    private Button ShareButton(IBrush fg, bool hasGuests)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        row.Children.Add(ShareChoice(ShareTarget.WhatsApp, "WhatsApp", "share.hint.whatsapp", BrandImage("whatsapp")));
        row.Children.Add(ShareChoice(ShareTarget.Telegram, "Telegram", "share.hint.telegram", BrandImage("telegram")));
        row.Children.Add(ShareChoice(ShareTarget.Mail, "Email", "share.hint.mail", Glyph("", "#1A73E8")));
        // Only an event with guests: the same mail, to them. The icon has people next to the envelope, so that the two are not taken for each other.
        if (hasGuests) row.Children.Add(ShareChoice(ShareTarget.MailGuests, "Guests", "share.hint.mail_guests", GuestsGlyph()));
        row.Children.Add(ShareChoice(ShareTarget.Disk, "Disk", "share.hint.disk", Glyph("", "#C26401")));
        row.Children.Add(ShareChoice(ShareTarget.Clipboard, "Clipboard", "share.hint.clipboard", Glyph("", "#7A4FC9")));
        row.Children.Add(ShareChoice(ShareTarget.Apps, "Apps", "share.hint.apps", Glyph("", "#5F6368")));

        var flyout = new Flyout { Placement = PlacementMode.Top, Content = row, ShowMode = FlyoutShowMode.Standard };
        _shareFlyout = flyout;
        var button = new Button
        {
            Content = ShareGlyph(fg),
            Width = 38, Height = 38, Padding = new Thickness(0), CornerRadius = new CornerRadius(19),
            Background = Brushes.Transparent,
            HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
            Flyout = flyout,
        };
        ToolTip.SetTip(button, L.T("details.share"));
        Avalonia.Automation.AutomationProperties.SetAutomationId(button, "Details.Share");
        Avalonia.Automation.AutomationProperties.SetName(button, L.T("details.share"));
        return button;
    }

    private Flyout? _shareFlyout;

    private Button ShareChoice(ShareTarget target, string name, string hintKey, Control icon)
    {
        var button = new Button
        {
            Content = icon, Width = 40, Height = 40, Padding = new Thickness(0), CornerRadius = new CornerRadius(20),
            Background = Brushes.Transparent,
            HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
        };
        Avalonia.Automation.AutomationProperties.SetAutomationId(button, "Share." + target);
        Avalonia.Automation.AutomationProperties.SetName(button, name);
        // No tooltip: it hides the other icons. What the icon does is written in the status bar while the pointer is over it.
        button.PointerEntered += (_, _) => ShareHint?.Invoke(hintKey);
        button.PointerExited += (_, _) => ShareHint?.Invoke(null);
        button.GotFocus += (_, _) => ShareHint?.Invoke(hintKey);
        button.LostFocus += (_, _) => ShareHint?.Invoke(null);
        button.Click += (_, _) =>
        {
            ShareHint?.Invoke(null);
            _shareFlyout?.Hide();
            ShareRequested?.Invoke(target);
        };
        return button;
    }

    private static Image BrandImage(string name) => new()
    {
        Width = 24, Height = 24,
        Source = new Bitmap(AssetLoader.Open(new Uri($"avares://GGCalendar.App/Assets/Brand/{name}.png"))),
    };

    private static Grid GuestsGlyph()
    {
        var blue = new SolidColorBrush(Color.Parse("#1A73E8"));
        var grid = new Grid { Width = 28, Height = 24 };
        grid.Children.Add(new TextBlock { Text = "\uE715", FontFamily = new FontFamily(IconFont), FontSize = 20, Foreground = blue, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top });
        grid.Children.Add(new TextBlock { Text = "\uE716", FontFamily = new FontFamily(IconFont), FontSize = 13, Foreground = blue, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom });
        return grid;
    }

    private static TextBlock Glyph(string glyph, string color) => new()
    {
        Text = glyph, FontFamily = new FontFamily(IconFont), FontSize = 20, Foreground = new SolidColorBrush(Color.Parse(color)),
        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>The usual sign of sharing: three dots joined by two lines that open from one of them.</summary>
    private static Canvas ShareGlyph(IBrush brush)
    {
        var canvas = new Canvas { Width = 18, Height = 18 };
        void Line(double x1, double y1, double x2, double y2) => canvas.Children.Add(new Line { StartPoint = new Point(x1, y1), EndPoint = new Point(x2, y2), Stroke = brush, StrokeThickness = 1.3 });
        void Dot(double cx, double cy) => canvas.Children.Add(new Ellipse { Width = 4.4, Height = 4.4, Stroke = brush, StrokeThickness = 1.3, [Canvas.LeftProperty] = cx - 2.2, [Canvas.TopProperty] = cy - 2.2 });
        Line(5.7, 8.1, 12.3, 4.6);
        Line(5.7, 9.9, 12.3, 13.4);
        Dot(13.5, 3.7);
        Dot(4.5, 9);
        Dot(13.5, 14.3);
        return canvas;
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
