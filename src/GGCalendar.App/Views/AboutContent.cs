using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using GGCalendar.Core.Localization;

namespace GGCalendar.App.Views;

/// <summary>Logo, name, version, author, build date and legal notice: what the about window and the splash screen show.</summary>
public static class AboutContent
{
    public static IEnumerable<Control> Build() =>
    [
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
    ];
}
