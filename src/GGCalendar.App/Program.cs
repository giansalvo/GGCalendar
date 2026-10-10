using Avalonia;
using System;

namespace GGCalendar.App;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            // Popups drawn inside the window instead of separate native windows: with monitors at different scaling
            // (e.g. 150% and 225%) a native popup could be sized for one monitor and shown on the other, cutting menus.
            .With(new Win32PlatformOptions { OverlayPopups = true })
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
