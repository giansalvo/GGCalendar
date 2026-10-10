using Avalonia.Controls;
using Avalonia.Interactivity;
using GGCalendar.App.ViewModels;
using GGCalendar.Core.Models;

namespace GGCalendar.App.Views;

public sealed record EventEditResult(CalendarEvent? Event, bool Delete);

public partial class EventEditWindow : Window
{
    public EventEditWindow()
    {
        InitializeComponent();
        Opened += (_, _) => TitleBox.Focus();
    }

    private EventEditViewModel? Vm => DataContext as EventEditViewModel;

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        if (Vm?.TryBuild() is { } ev)
            Close(new EventEditResult(ev, false));
    }

    private void OnDelete(object? sender, RoutedEventArgs e) => Close(new EventEditResult(null, true));

    private void OnCancel(object? sender, RoutedEventArgs e) => Close((EventEditResult?)null);
}
