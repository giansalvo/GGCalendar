using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GGCalendar.App.ViewModels;
using GGCalendar.App.Views;
using GGCalendar.Core.Localization;
using GGCalendar.Core.Tests.Support;
using GGCalendar.Data;
using static GGCalendar.Core.Tests.Support.AppDriver;

namespace GGCalendar.Core.Tests;

/// <summary>
/// T134-T135: the language selector of the Settings window on the real app (no Google account needed). The tests never write a text:
/// they find the controls by AutomationId and compare with the text the app has for the key in the language they chose.
/// </summary>
[Collection(CollectionName)]
public class LanguageUiTests
{
    private static async Task<SettingsWindow> OpenSettingsAsync(MainWindow window)
    {
        Click(FindButton(window, "Main.Settings"));
        return await WaitForAsync(() => window.OwnedWindows.OfType<SettingsWindow>().FirstOrDefault(), "la finestra delle impostazioni");
    }

    private static string TodayButtonText(MainWindow window) => (string)FindButton(window, "Main.Today").Content!;

    /// <summary>Runs on the app with the settings of the test folder put back (and the language) when it ends.</summary>
    private static Task InLocalAppAsync(Func<MainWindow, Task> test) => HeadlessApp.RunAsync(async () =>
    {
        var settingsFile = new SettingsStore().FilePath;
        var original = File.Exists(settingsFile) ? File.ReadAllBytes(settingsFile) : null;
        var window = OpenApp();
        try
        {
            await test(window);
        }
        finally
        {
            foreach (var w in new[] { window }) w.Close();
            if (original is not null) File.WriteAllBytes(settingsFile, original);
            L.Apply(new SettingsStore().Load().Language);
        }
    });

    [Fact]
    [Trait("TestCase", "T134")]
    public Task T134_TheSettingsOfferTheSystemLanguageAndTheFiveLanguagesOfTheApp() => InLocalAppAsync(async window =>
    {
        var dialog = await OpenSettingsAsync(window);
        var selector = (ComboBox)FindById(dialog, "Settings.SelectedLanguage");
        var options = selector.ItemsSource!.Cast<LanguageOption>().Select(o => o.Code).ToList();

        Assert.Equal(["system", "it", "en", "de", "es", "fr"], options);
        // The language saved is the one selected; a new installation has "system".
        Assert.Equal(new SettingsStore().Load().Language, ((LanguageOption)selector.SelectedItem!).Code);
        Assert.Equal("system", new SettingsViewModel(new GGCalendar.Core.Models.AppSettings()).SelectedLanguage.Code);
        Click(FindButton(dialog, "Settings.Cancel"));
        await WaitForAsync(() => window.OwnedWindows.Count == 0 ? window : null, "la chiusura delle impostazioni");
    });

    [Fact]
    [Trait("TestCase", "T135")]
    public Task T135_ChoosingALanguageAndSavingChangesTheWholeAppAtOnceAndIsKept() => InLocalAppAsync(async window =>
    {
        var vm = Vm(window);
        var before = L.Language;
        var other = before == "en" ? "de" : "en";
        var textBefore = TodayButtonText(window);
        vm.SetStatus("status.syncing");
        var statusBefore = vm.SyncStatus;
        var titleBefore = vm.Title;

        // Cancel: the language stays.
        var dialog = await OpenSettingsAsync(window);
        ((ComboBox)FindById(dialog, "Settings.SelectedLanguage")).SelectedItem =
            ((ComboBox)FindById(dialog, "Settings.SelectedLanguage")).ItemsSource!.Cast<LanguageOption>().First(o => o.Code == other);
        Click(FindButton(dialog, "Settings.Cancel"));
        await WaitForAsync(() => window.OwnedWindows.Count == 0 ? window : null, "la chiusura delle impostazioni");
        Assert.Equal(before, L.Language);
        Assert.Equal(textBefore, TodayButtonText(window));

        // Save: at once, with no restart, the window, the status message and the names of the months are in the new language.
        dialog = await OpenSettingsAsync(window);
        var selector = (ComboBox)FindById(dialog, "Settings.SelectedLanguage");
        selector.SelectedItem = selector.ItemsSource!.Cast<LanguageOption>().First(o => o.Code == other);
        Click(FindButton(dialog, "Settings.Save"));
        await WaitForAsync(() => window.OwnedWindows.Count == 0 ? window : null, "la chiusura delle impostazioni");
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(other, L.Language);
        Assert.Equal(L.T("main.today"), TodayButtonText(window));
        Assert.NotEqual(textBefore, TodayButtonText(window)); // the button really shows another text
        Assert.True(vm.Status!.Key == "status.syncing", $"Lo stato è {vm.Status.Key}: {vm.SyncStatus}"); // the message is the same one...
        Assert.Equal(L.T("status.syncing"), vm.SyncStatus);    // ...shown in the new language
        Assert.NotEqual(statusBefore, vm.SyncStatus);
        Assert.NotEqual(titleBefore, vm.Title);                // "Ottobre 2026" is "October 2026"
        Assert.Equal(other, new SettingsStore().Load().Language);

        // The choice is kept when the app is closed and opened again.
        window = await ReopenAsync(window);
        Assert.Equal(other, L.Language);
        Assert.Equal(L.T("main.today"), TodayButtonText(window));
        window.Close();
    });
}
