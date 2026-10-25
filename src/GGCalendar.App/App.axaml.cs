using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using GGCalendar.App.ViewModels;
using GGCalendar.App.Views;

namespace GGCalendar.App;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // The language comes first: the texts of the view model and of the window are made in it.
        GGCalendar.Core.Localization.L.Apply(new GGCalendar.Data.SettingsStore().Load().Language);
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainViewModel(new GGCalendar.Data.CalendarStore()),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
