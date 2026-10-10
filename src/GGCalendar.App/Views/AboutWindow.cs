using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace GGCalendar.App.Views;

/// <summary>Modal "about" dialog showing version, author and build date.</summary>
public sealed class AboutWindow : Window
{
    public AboutWindow()
    {
        Title = "Informazioni su GGCalendar";
        Width = 360;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var close = new Button { Content = "Chiudi", Classes = { "accent" }, IsDefault = true, IsCancel = true };
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
                    Text = $"Versione: {AppInfo.Version}\nAutore: {AppInfo.Author}\nData di build: {AppInfo.BuildDate}",
                    TextAlignment = TextAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                },
                close,
            },
        };
        close.HorizontalAlignment = HorizontalAlignment.Right;
    }
}
