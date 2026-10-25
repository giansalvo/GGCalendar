using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using GGCalendar.Core.Localization;

namespace GGCalendar.App.Views;

/// <summary>A message with one OK button, for something the user must read before going on (a file that cannot be used, for example).</summary>
public sealed class WarningWindow : Window
{
    public WarningWindow(string title, string message)
    {
        Title = title;
        Width = 460;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var ok = new Button { Content = L.T("common.ok"), Classes = { "accent" }, IsDefault = true, IsCancel = true };
        Avalonia.Automation.AutomationProperties.SetAutomationId(ok, "Dialog.Ok");
        ok.Click += (_, _) => Close();
        Content = new StackPanel
        {
            Margin = new Thickness(24),
            Spacing = 12,
            Children =
            {
                new TextBlock { Text = title, FontSize = 18, FontWeight = Avalonia.Media.FontWeight.SemiBold, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { ok } },
            },
        };
    }
}
