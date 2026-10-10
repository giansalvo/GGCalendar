using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using GGCalendar.Core.Models;

namespace GGCalendar.App.Views;

public enum ReminderAction { Dismiss, Snooze }

/// <summary>Small always-on-top toast shown when a popup reminder fires.</summary>
public sealed class ReminderNotificationWindow : Window
{
    public ReminderAction Result { get; private set; } = ReminderAction.Dismiss;

    public ReminderNotificationWindow(CalendarEvent ev, bool use24Hour)
    {
        Width = 360;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        Topmost = true;
        ShowInTaskbar = true;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Title = "Promemoria";

        var fmt = use24Hour ? "HH:mm" : "h:mm tt";
        var when = ev.IsAllDay ? ev.Start.ToString("dddd d MMMM") : $"{ev.Start.ToString("ddd d MMM")}, {ev.Start.ToString(fmt)} – {ev.End.ToString(fmt)}";

        var snooze = new Button { Content = "Posticipa 5 min" };
        var dismiss = new Button { Content = "Chiudi" };
        snooze.Click += (_, _) => { Result = ReminderAction.Snooze; Close(); };
        dismiss.Click += (_, _) => { Result = ReminderAction.Dismiss; Close(); };

        var panel = new StackPanel { Margin = new Thickness(20), Spacing = 8 };
        panel.Children.Add(new TextBlock { Text = "🔔 Promemoria", Opacity = 0.7, FontSize = 12 });
        panel.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(ev.Title) ? "(Senza titolo)" : ev.Title,
            FontSize = 20,
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap,
        });
        panel.Children.Add(new TextBlock { Text = when });
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
            Position = new PixelPoint(
                area.Value.Right - (int)((Width + 16) * scale),
                area.Value.Bottom - (int)((Bounds.Height + 16) * scale));
        };
    }
}
