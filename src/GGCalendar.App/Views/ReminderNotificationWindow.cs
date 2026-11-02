using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Controls.Primitives;
using GGCalendar.App.Localization;
using GGCalendar.Core.Localization;
using GGCalendar.Core.Models;

namespace GGCalendar.App.Views;

public enum ReminderAction { Dismiss, Snooze }

/// <summary>
/// Small always-on-top toast shown when a reminder fires. "Snooze" puts it off by the default length of the settings; an alarm also
/// offers every length (<see cref="AppSettings.SnoozeChoices"/>) in a row that never changes, each one putting it off at once.
/// </summary>
public sealed class ReminderNotificationWindow : Window
{
    public ReminderAction Result { get; private set; } = ReminderAction.Dismiss;

    /// <summary>The length chosen when <see cref="Result"/> is <see cref="ReminderAction.Snooze"/>.</summary>
    public int SnoozeMinutes { get; private set; }

    /// <summary>
    /// A toast appears on its own, often under the mouse of someone who is clicking something else: clicks in the first moments are ignored.
    /// </summary>
    public static TimeSpan GuardTime { get; set; } = TimeSpan.FromMilliseconds(600);

    private DateTime _shownAt = DateTime.UtcNow;

    private static readonly System.Collections.Generic.List<ReminderNotificationWindow> OpenToasts = [];

    public ReminderNotificationWindow(CalendarEvent ev, bool use24Hour, bool isAlarm = false, int snoozeMinutes = AppSettings.DefaultSnoozeMinutes)
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

        var snooze = new Button { Name = "ReminderSnooze", HorizontalContentAlignment = HorizontalAlignment.Center };
        var dismiss = new Button { Name = "ReminderDismiss" };
        Avalonia.Automation.AutomationProperties.SetAutomationId(snooze, "Reminder.Snooze");
        Avalonia.Automation.AutomationProperties.SetAutomationId(dismiss, "Reminder.Dismiss");
        this.Live(w => w.Title = L.T(isAlarm ? "reminder.alarm" : "reminder.reminder"));
        snooze.Live(b => b.Content = L.T("reminder.snooze", snoozeMinutes));
        dismiss.Live(b => b.Content = L.T(isAlarm ? "reminder.turn_off" : "reminder.dismiss"));
        void Act(ReminderAction action, int minutes)
        {
            if (DateTime.UtcNow - _shownAt < GuardTime) return;
            Result = action;
            SnoozeMinutes = minutes;
            Close();
        }

        snooze.Click += (_, _) => Act(ReminderAction.Snooze, snoozeMinutes);
        dismiss.Click += (_, _) => Act(ReminderAction.Dismiss, 0);

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
        if (isAlarm)
        {
            // The default length is the big button; below it all the lengths, always in the same places, so that a hand learns them; the button
            // that turns the alarm off is apart from them.
            snooze.HorizontalAlignment = HorizontalAlignment.Stretch;
            snooze.Margin = new Thickness(0, 8, 0, 0);
            panel.Children.Add(snooze);
            panel.Children.Add(new TextBlock { Opacity = 0.7, FontSize = 12, Margin = new Thickness(0, 4, 0, 0) }.Live(b => b.Text = L.T("reminder.snooze_for")));
            var choices = new UniformGrid { Rows = 1, Columns = AppSettings.SnoozeChoices.Count };
            foreach (var minutes in AppSettings.SnoozeChoices)
            {
                var choice = new Button { Margin = new Thickness(2, 0), Padding = new Thickness(0, 6), HorizontalContentAlignment = HorizontalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Stretch };
                if (minutes == snoozeMinutes) choice.FontWeight = FontWeight.Bold;
                Avalonia.Automation.AutomationProperties.SetAutomationId(choice, "Reminder.SnoozeChoice." + minutes);
                choice.Live(b => b.Content = L.T("snooze.choice", minutes));
                choice.Click += (_, _) => Act(ReminderAction.Snooze, minutes);
                choices.Children.Add(choice);
            }

            panel.Children.Add(choices);
            dismiss.HorizontalAlignment = HorizontalAlignment.Stretch;
            dismiss.HorizontalContentAlignment = HorizontalAlignment.Center;
            dismiss.Margin = new Thickness(0, 16, 0, 0);
            panel.Children.Add(dismiss);
        }
        else
        {
            panel.Children.Add(new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 8, 0, 0),
                Children = { snooze, dismiss },
            });
        }

        Content = panel;

        Opened += (_, _) =>
        {
            _shownAt = DateTime.UtcNow;
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

