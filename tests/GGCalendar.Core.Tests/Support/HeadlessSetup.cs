using Avalonia;
using Avalonia.Headless;
using AppType = GGCalendar.App.App;

namespace GGCalendar.Core.Tests.Support;

/// <summary>Runs a test body on the UI thread of the real application (styles, fonts) started without a screen.</summary>
public static class HeadlessApp
{
    private static readonly Lazy<HeadlessUnitTestSession> Session = new(() => HeadlessUnitTestSession.StartNew(typeof(HeadlessApp)));

    /// <summary>Entry point the headless session looks for.</summary>
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<AppType>().UseHeadless(new AvaloniaHeadlessPlatformOptions()).WithInterFont();

    /// <summary>Waits for the whole body, not only for its first await, and rethrows what it threw.</summary>
    public static Task RunAsync(Func<Task> test) =>
        Session.Value.Dispatch(async () =>
        {
            await test();
            return 0;
        }, CancellationToken.None);
}
