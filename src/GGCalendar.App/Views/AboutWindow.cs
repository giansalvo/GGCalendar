using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using GGCalendar.Core.Localization;

namespace GGCalendar.App.Views;

/// <summary>Modal "about" dialog showing version, author and build date.</summary>
public sealed class AboutWindow : Window
{
    public AboutWindow()
    {
        Title = L.T("about.title");
        Width = 440;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var close = new Button { Content = L.T("common.close"), Classes = { "accent" }, IsDefault = true, IsCancel = true };
        Avalonia.Automation.AutomationProperties.SetAutomationId(close, "About.Close");
        close.Click += (_, _) => Close();

        var panel = new StackPanel { Margin = new Thickness(24), Spacing = 12 };
        foreach (var c in AboutContent.Build()) panel.Children.Add(c);
        panel.Children.Add(close);
        Content = panel;
        close.HorizontalAlignment = HorizontalAlignment.Right;
    }
}
