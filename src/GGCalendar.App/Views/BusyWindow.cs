using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace GGCalendar.App.Views;

/// <summary>A small window with a message and an endless progress bar, shown while the app works on something that takes more than a moment.</summary>
public sealed class BusyWindow : Window
{
    public BusyWindow(string message)
    {
        Title = "GGCalendar";
        Width = 380;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Content = new StackPanel
        {
            Margin = new Thickness(24),
            Spacing = 14,
            Children =
            {
                new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                new ProgressBar { IsIndeterminate = true },
            },
        };
    }
}
