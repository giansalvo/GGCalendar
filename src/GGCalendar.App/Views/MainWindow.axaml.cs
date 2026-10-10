using Avalonia.Controls;

namespace GGCalendar.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Opened += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            var start = (DataContext as ViewModels.MainViewModel)?.Settings.DayStartHour ?? 7;
            GridScroll.Offset = new Avalonia.Vector(0, start * Controls.WeekGrid.HourHeight);
        }, Avalonia.Threading.DispatcherPriority.Loaded);
    }
}