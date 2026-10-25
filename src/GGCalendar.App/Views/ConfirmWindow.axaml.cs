using Avalonia.Controls;
using Avalonia.Interactivity;
using GGCalendar.Core.Localization;

namespace GGCalendar.App.Views;

public partial class ConfirmWindow : Window
{
    public ConfirmWindow() : this(L.T("dialog.confirm"), "", L.T("common.ok")) { }

    public ConfirmWindow(string title, string message, string okText)
    {
        InitializeComponent();
        Title = title;
        Heading.Text = title;
        Message.Text = message;
        OkButton.Content = okText;
    }

    private void OnOk(object? sender, RoutedEventArgs e) => Close(true);

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}
