using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
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

        Content = new StackPanel
        {
            Margin = new Thickness(24),
            Spacing = 12,
            Children =
            {
                new Image
                {
                    Source = new Bitmap(AssetLoader.Open(new System.Uri("avares://GGCalendar.App/Assets/GGCalendar_logo.jpg"))),
                    Width = 64,
                    Height = 64,
                    HorizontalAlignment = HorizontalAlignment.Center,
                },
                new TextBlock { Text = "GGCalendar", FontSize = 22, FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Center },
                new SelectableTextBlock
                {
                    Text = L.T("about.details", AppInfo.Version, AppInfo.Author, AppInfo.BuildDate),
                    TextAlignment = TextAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                },
                new SelectableTextBlock
                {
                    Text = AppInfo.LegalNotice,
                    TextWrapping = TextWrapping.Wrap,
                    TextAlignment = TextAlignment.Center,
                    FontSize = 12,
                    Opacity = 0.75,
                },
                close,
            },
        };
        close.HorizontalAlignment = HorizontalAlignment.Right;
    }
}
