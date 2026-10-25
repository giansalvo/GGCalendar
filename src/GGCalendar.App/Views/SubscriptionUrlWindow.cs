using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using GGCalendar.Core.Localization;

namespace GGCalendar.App.Views;

/// <summary>Shows the address of a subscription in the clear, only because the person asked for it.</summary>
public sealed class SubscriptionUrlWindow : Window
{
    public SubscriptionUrlWindow(string url)
    {
        Title = L.T("sub.url_title");
        Width = 560;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var box = new TextBox { Text = url, IsReadOnly = true, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        var copy = new Button { Content = L.T("common.copy") };
        var close = new Button { Content = L.T("common.close"), IsCancel = true };
        AutomationProperties.SetAutomationId(box, "SubUrl.Text");
        AutomationProperties.SetAutomationId(copy, "SubUrl.Copy");
        AutomationProperties.SetAutomationId(close, "Dialog.Cancel");
        copy.Click += async (_, _) =>
        {
            if (Clipboard is { } clipboard) await Avalonia.Input.Platform.ClipboardExtensions.SetTextAsync(clipboard, url);
        };
        close.Click += (_, _) => Close();

        Content = new StackPanel
        {
            Margin = new Thickness(24),
            Spacing = 12,
            Children =
            {
                new TextBlock { Text = L.T("sub.url_warning"), TextWrapping = Avalonia.Media.TextWrapping.Wrap, Opacity = 0.75 },
                box,
                new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { copy, close } },
            },
        };
    }
}
