using Avalonia.Controls;

namespace GGCalendar.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Grid.EventChanged += (ev, s, e) => Vm?.MoveEvent(ev, s, e);
        Grid.EventCreated += (s, e) => Vm?.CreateEvent(s, e);
        Grid.EventDeleted += ev => Vm?.DeleteEvent(ev);
        Opened += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            var start = (DataContext as ViewModels.MainViewModel)?.Settings.DayStartHour ?? 7;
            GridScroll.Offset = new Avalonia.Vector(0, start * Controls.WeekGrid.HourHeight);
        }, Avalonia.Threading.DispatcherPriority.Loaded);
    }

    private ViewModels.MainViewModel? Vm => DataContext as ViewModels.MainViewModel;
}