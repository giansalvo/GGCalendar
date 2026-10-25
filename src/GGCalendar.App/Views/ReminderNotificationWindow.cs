using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using GGCalendar.App.Localization;
using GGCalendar.Core.Localization;
using GGCalendar.Core.Models;

namespace GGCalendar.App.Views;

public enum ReminderAction { Dismiss, Snooze }

/// <summary>Small always-on-top toast shown when a popup reminder fires.</summary>
public sealed class ReminderNotificationWindow : Window
{
    public ReminderAction Result { get; private set; } = ReminderAction.Dismiss;

    private static readonly System.Collections.Generic.List<ReminderNotificationWindow> OpenToasts = [];

    public ReminderNotificationWindow(CalendarEvent ev, bool use24Hour, bool isAlarm = false)
    {
        Width = 360;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        WindowStartupLocation = WindowStartupLocation.Manual;

        var fmt = use24Hour ? "HH:mm" : "h:mm tt";
        string When() => ev.IsAllDay ? ev.Start.ToString("dddd d MMMM", L.Culture)
            : $"{ev.Start.ToString("ddd d MMM", L.Culture)}, {ev.Start.ToString(fmt, L.Culture)} – {ev.End.ToString(fmt, L.Culture)}";

        var snooze = new Button { Name = "ReminderSnooze" };
        var dismiss = new Button { Name = "ReminderDismiss" };
        Avalonia.Automation.AutomationProperties.SetAutomationId(snooze, "Reminder.Snooze");
        Avalonia.Automation.AutomationProperties.SetAutomationId(dismiss, "Reminder.Dismiss");
        this.Live(w => w.Title = L.T(isAlarm ? "reminder.alarm" : "reminder.reminder"));
        snooze.Live(b => b.Content = L.T("reminder.snooze"));
        dismiss.Live(b => b.Content = L.T(isAlarm ? "reminder.turn_off" : "reminder.dismiss"));
        snooze.Click += (_, _) => { Result = ReminderAction.Snooze; Close(); };
        dismiss.Click += (_, _) => { Result = ReminderAction.Dismiss; Close(); };

        var panel = new StackPanel { Margin = new Thickness(20), Spacing = 8 };
        panel.Children.Add(new TextBlock { Opacity = 0.7, FontSize = 12 }.Live(b => b.Text = L.T(isAlarm ? "reminder.heading_alarm" : "reminder.heading_reminder")));
        panel.Children.Add(new TextBlock
        {
            FontSize = 20,
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap,
        }.Live(b => b.Text = string.IsNullOrWhiteSpace(ev.Title) ? L.T("common.untitled") : ev.Title));
        panel.Children.Add(new TextBlock().Live(b => b.Text = When()));
        if (ev.Location.Length > 0) panel.Children.Add(new TextBlock { Text = "📍 " + ev.Location, Opacity = 0.7 });
        panel.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 8, 0, 0),
            Children = { snooze, dismiss },
        });
        Content = panel;

        Opened += (_, _) =>
        {
            var area = Screens.Primary?.WorkingArea;
            if (area is null) return;
            var scale = Screens.Primary!.Scaling;
            var stacked = 0.0;
            foreach (var other in OpenToasts) stacked += other.Bounds.Height + 8;
            OpenToasts.Add(this);
            Position = new PixelPoint(
                area.Value.Right - (int)((Width + 16) * scale),
                area.Value.Bottom - (int)((Bounds.Height + 16 + stacked) * scale));
        };
        Closed += (_, _) => OpenToasts.Remove(this);
    }
}

