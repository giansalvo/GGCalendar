using Avalonia.Controls;
using Avalonia.Threading;
using GGCalendar.App.Services;
using GGCalendar.App.Views;
using GGCalendar.Core.Localization;
using GGCalendar.Core.Tests.Support;
using GGCalendar.Data;
using GGCalendar.Providers.Google;
using static GGCalendar.Core.Tests.Support.AppDriver;

namespace GGCalendar.Core.Tests;

/// <summary>
/// On a fresh install (no Google account) the "Google calendar" item of the "Create" menu stays grey and, pointed at, says how to connect an account
/// (settings → Google accounts → Add account). With an account it is the usual active item with no hint.
/// </summary>
[Collection(CollectionName)]
public class GoogleHintUiTests
{
    private static MenuItem CreateGoogleItem(MainWindow window)
    {
        var button = FindButton(window, "Main.Create");
        button.Flyout!.ShowAt(button);
        Dispatcher.UIThread.RunJobs();
        return ((MenuFlyout)button.Flyout!).Items.OfType<MenuItem>().First(i => Avalonia.Automation.AutomationProperties.GetAutomationId(i) == "Menu.CreateGoogleCalendar");
    }

    [Fact]
    [Trait("TestCase", "T257")]
    public Task T257_WithNoGoogleAccountTheItemIsGreyAndSaysHowToConnectOne() => HeadlessApp.RunAsync(async () =>
    {
        var settingsFile = new SettingsStore().FilePath;
        var original = File.Exists(settingsFile) ? File.ReadAllBytes(settingsFile) : null;
        var accountsFile = Path.Combine(Path.GetTempPath(), $"ggcal-ui-hint-{Guid.NewGuid():N}.json");
        var store = new CalendarStore();
        foreach (var c in store.GetCalendars().Where(c => c.Id.Contains('|'))) store.DeleteCalendar(c.Id); // calendars of Google left by other tests
        MainWindow.GoogleFactory = () => new GoogleSyncService(new CalendarStore(), new GoogleAccountStore(accountsFile), new HttpClient(new FakeGoogle()));
        var window = OpenApp();
        try
        {
            await WaitForQuietAsync(window);
            var item = CreateGoogleItem(window);
            Assert.False(Vm(window).HasGoogleAccounts);
            Assert.False(item.IsEnabled);
            Assert.Equal(L.T("main.create_google_hint"), Vm(window).CreateGoogleHint);
            Assert.Equal(L.T("main.create_google_hint"), ToolTip.GetTip(item));
            Assert.True(ToolTip.GetShowOnDisabled(item)); // a grey item shows it too
        }
        finally
        {
            window.Close();
            MainWindow.GoogleFactory = null;
            if (original is not null) File.WriteAllBytes(settingsFile, original);
            if (File.Exists(accountsFile)) File.Delete(accountsFile);
        }
    });

    [Fact]
    [Trait("TestCase", "T257")]
    public Task T257_WithAnAccountTheItemIsActiveAndHasNoHint() => GoogleCalendarUiTests.InFakeGoogleAppAsync(new FakeGoogle(), window =>
    {
        var item = CreateGoogleItem(window);
        Assert.True(Vm(window).HasGoogleAccounts);
        Assert.True(item.IsEnabled);
        Assert.Null(Vm(window).CreateGoogleHint);
        Assert.Null(ToolTip.GetTip(item));
        return Task.CompletedTask;
    });
}
