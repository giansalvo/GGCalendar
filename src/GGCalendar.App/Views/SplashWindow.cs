using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using GGCalendar.Core.Localization;

namespace GGCalendar.App.Views;

/// <summary>The about window without the button, shown alone for a few seconds the first time the app runs after an install or an update.</summary>
public sealed class SplashWindow : Window
{
    public static readonly TimeSpan DefaultDuration = TimeSpan.FromSeconds(5);

    /// <summary>Shows a splash over <paramref name="main"/> as soon as it is open; the main window goes on loading under it.</summary>
    public static void ShowWhenOpened(Window main, TimeSpan? duration = null) =>
        main.Opened += (_, _) => new SplashWindow(duration).Show(main);

    public SplashWindow(TimeSpan? duration = null)
    {
        Title = L.T("about.title");
        Width = 440;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        Topmost = true;
        WindowDecorations = Avalonia.Controls.WindowDecorations.BorderOnly;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Avalonia.Automation.AutomationProperties.SetAutomationId(this, "Splash");

        var panel = new StackPanel { Margin = new Thickness(24), Spacing = 12 };
        foreach (var c in AboutContent.Build()) panel.Children.Add(c);
        // A border and a bar at the top with the title, drawn here so that there are no buttons to minimize, maximize or close.
        var bar = new Border
        {
            Background = new SolidColorBrush(Colors.Gray, 0.25),
            Padding = new Thickness(12, 6),
            Child = new TextBlock { Text = Title, FontSize = 12, VerticalAlignment = VerticalAlignment.Center },
        };
        DockPanel.SetDock(bar, Dock.Top);
        Content = new Border
        {
            BorderBrush = Brushes.Gray,
            BorderThickness = new Thickness(1),
            Child = new DockPanel { Children = { bar, panel } },
        };

        var timer = new DispatcherTimer { Interval = duration ?? DefaultDuration };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            Close();
        };
        Opened += (_, _) => timer.Start();
        Closed += (_, _) => timer.Stop();
    }
}
