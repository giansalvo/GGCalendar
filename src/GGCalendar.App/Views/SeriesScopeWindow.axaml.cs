using Avalonia.Controls;
using Avalonia.Interactivity;
using GGCalendar.App.ViewModels;

namespace GGCalendar.App.Views;

public partial class SeriesScopeWindow : Window
{
    public SeriesScopeWindow() : this("Evento ricorrente") { }

    public SeriesScopeWindow(string title)
    {
        InitializeComponent();
        Title = title;
        Heading.Text = title;
    }

    private void OnOk(object? sender, RoutedEventArgs e) =>
        Close(AllEvents.IsChecked == true ? SeriesScope.All
            : ThisAndFollowing.IsChecked == true ? SeriesScope.ThisAndFollowing
            : SeriesScope.This);

    private void OnCancel(object? sender, RoutedEventArgs e) => Close((SeriesScope?)null);
}
