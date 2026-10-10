using Avalonia.Controls;
using Avalonia.Interactivity;
using GGCalendar.App.ViewModels;

namespace GGCalendar.App.Views;

public partial class DeleteRecurringWindow : Window
{
    public DeleteRecurringWindow() => InitializeComponent();

    private void OnOk(object? sender, RoutedEventArgs e) =>
        Close(AllEvents.IsChecked == true ? DeleteScope.All
            : ThisAndFollowing.IsChecked == true ? DeleteScope.ThisAndFollowing
            : DeleteScope.This);

    private void OnCancel(object? sender, RoutedEventArgs e) => Close((DeleteScope?)null);
}
