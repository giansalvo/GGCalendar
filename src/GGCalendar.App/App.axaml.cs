using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using GGCalendar.App.ViewModels;
using GGCalendar.App.Views;

namespace GGCalendar.App;

public partial class App : Application
{
    /// <summary>The one running copy of the app (null in the tests, which start the app by themselves): a second start asks it to show the window.</summary>
    public static GGCalendar.App.Services.SingleInstance? Instance { get; set; }

    private GGCalendar.App.Services.TrayController? _tray;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var store = new GGCalendar.Data.SettingsStore();
        var settings = store.Load();
        // The language comes first: the texts of the view model and of the window are made in it.
        GGCalendar.Core.Localization.L.Apply(settings.Language);
        GGCalendar.App.Services.AppFonts.Apply(settings.FontFamily);
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (GGCalendar.Data.SplashGate.Claim(store, settings, AppInfo.Version))
            {
                // The main window is made and loads at once; the splash opens on top of it when it is shown and closes by itself.
                var main = CreateMainWindow();
                desktop.MainWindow = main;
                SplashWindow.ShowWhenOpened(main);
            }
            else
            {
                desktop.MainWindow = CreateMainWindow();
            }
        }

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: MainWindow window } classic && Instance is not null)
        {
            // Closing the window hides it in the tray (issue #30): the app ends only with "Esci", so the lifetime must not end with the window.
            classic.ShutdownMode = Avalonia.Controls.ShutdownMode.OnExplicitShutdown;
            _tray = new GGCalendar.App.Services.TrayController(window, store, () => classic.Shutdown());
            Instance.Activated += () => Avalonia.Threading.Dispatcher.UIThread.Post(_tray.Show);
            Instance.StartListening();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static MainWindow CreateMainWindow() => new()
    {
        DataContext = new MainViewModel(new GGCalendar.Data.CalendarStore()),
    };
}
