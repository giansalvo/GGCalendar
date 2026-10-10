using Avalonia.Controls;
using Avalonia.Interactivity;
using GGCalendar.App.ViewModels;
using GGCalendar.Core.Models;

namespace GGCalendar.App.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
    }

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        if ((DataContext as SettingsViewModel)?.TryBuild() is { } settings)
            Close(settings);
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close((AppSettings?)null);
}
