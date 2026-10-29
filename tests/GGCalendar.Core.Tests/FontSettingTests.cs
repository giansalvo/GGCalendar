using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using GGCalendar.App.Services;
using GGCalendar.App.ViewModels;
using GGCalendar.App.Views;
using GGCalendar.Core.Localization;
using GGCalendar.Core.Models;
using GGCalendar.Core.Tests.Support;
using GGCalendar.Data;
using static GGCalendar.Core.Tests.Support.AppDriver;

namespace GGCalendar.Core.Tests;

/// <summary>The font of the app chosen in the settings (issue #42).</summary>
[Collection(CollectionName)]
public class FontSettingTests
{
    private static readonly string[] SystemFonts = ["Arial", "Segoe UI", "Wingdings", "Segoe MDL2 Assets", "Segoe Fluent Icons", "@Meiryo", "Calibri", "arial", "Roboto", "Cambria"];

    // ---- T312: what is stored ----

    [Fact]
    [Trait("TestCase", "T312")]
    public void WithoutAChoiceTheDefaultFontIsUsed()
    {
        Assert.True(string.IsNullOrEmpty(new AppSettings().FontFamily));
        // a settings file written before the option existed has no font
        Assert.True(string.IsNullOrEmpty(JsonSerializer.Deserialize<AppSettings>("{\"Theme\":1}")!.FontFamily));
    }

    [Theory]
    [Trait("TestCase", "T312")]
    [InlineData("Roboto")]
    [InlineData("Plus Jakarta Sans")]
    [InlineData("Segoe UI")]
    public void TheChosenFontIsKeptBetweenStarts(string name)
    {
        var file = Path.Combine(Path.GetTempPath(), "ggcal-font-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var store = new SettingsStore(file);
            var settings = store.Load();
            settings.FontFamily = name;
            store.Save(settings);
            Assert.Equal(name, new SettingsStore(file).Load().FontFamily);
        }
        finally
        {
            File.Delete(file);
        }
    }

    // ---- T313: which font is used and what the list offers ----

    [Theory]
    [Trait("TestCase", "T313")]
    [InlineData(null, "Inter")]
    [InlineData("", "Inter")]
    [InlineData("  ", "Inter")]
    [InlineData("Inter", "Inter")]
    [InlineData("roboto", "Roboto")]
    [InlineData("Plus Jakarta Sans", "Plus Jakarta Sans")]
    [InlineData("Segoe UI", "Segoe UI")]
    [InlineData("calibri", "Calibri")]
    [InlineData("A Font Nobody Has", "Inter")]
    public void TheChosenFontIsUsedAndAMissingOneFallsBackToTheDefault(string? chosen, string expected) =>
        Assert.Equal(expected, AppFonts.Normalize(chosen, SystemFonts));

    [Fact]
    [Trait("TestCase", "T313")]
    public void TheListStartsWithTheFontsOfTheAppAndLeavesOutSymbolFonts()
    {
        var list = AppFonts.Choices(SystemFonts);

        Assert.Equal(["Inter", "Roboto", "Plus Jakarta Sans"], list.Take(3));
        Assert.Equal(["Arial", "Calibri", "Cambria", "Segoe UI"], list.Skip(3)); // sorted, once each; no Roboto twice
        Assert.DoesNotContain("Wingdings", list);
        Assert.DoesNotContain("Segoe MDL2 Assets", list);
        Assert.DoesNotContain("Segoe Fluent Icons", list);
        Assert.DoesNotContain("@Meiryo", list); // vertical writing variants
        Assert.Equal(["Inter", "Roboto", "Plus Jakarta Sans"], AppFonts.Choices([])); // no system fonts: still ours
    }

    [Theory]
    [Trait("TestCase", "T313")]
    [InlineData(null, "fonts:Inter#Inter")]
    [InlineData("Roboto", "fonts:Roboto#Roboto")]
    [InlineData("Plus Jakarta Sans", "fonts:PlusJakartaSans#Plus Jakarta Sans")]
    [InlineData("Calibri", "Calibri")]
    [InlineData("A Font Nobody Has", "fonts:Inter#Inter")]
    public void EachNameBecomesTheFamilyAvaloniaDrawsWith(string? chosen, string family) =>
        Assert.Equal(family, AppFonts.Resolve(chosen, SystemFonts).ToString());

    // ---- T314: the fonts and their licences come with the app ----

    [Theory]
    [Trait("TestCase", "T314")]
    [InlineData("Fonts/Roboto/Roboto-Regular.ttf")]
    [InlineData("Fonts/Roboto/Roboto-Italic.ttf")]
    [InlineData("Fonts/Roboto/Roboto-Medium.ttf")]
    [InlineData("Fonts/Roboto/Roboto-Bold.ttf")]
    [InlineData("Fonts/Roboto/Roboto-BoldItalic.ttf")]
    [InlineData("Fonts/PlusJakartaSans/PlusJakartaSans-Regular.ttf")]
    [InlineData("Fonts/PlusJakartaSans/PlusJakartaSans-Italic.ttf")]
    [InlineData("Fonts/PlusJakartaSans/PlusJakartaSans-Medium.ttf")]
    [InlineData("Fonts/PlusJakartaSans/PlusJakartaSans-SemiBold.ttf")]
    [InlineData("Fonts/PlusJakartaSans/PlusJakartaSans-Bold.ttf")]
    [InlineData("Fonts/PlusJakartaSans/PlusJakartaSans-BoldItalic.ttf")]
    [InlineData("FontLicenses/Inter-OFL.txt")]
    [InlineData("FontLicenses/Roboto-OFL.txt")]
    [InlineData("FontLicenses/PlusJakartaSans-OFL.txt")]
    public Task TheFontFilesAndTheirLicencesAreInsideTheApp(string file) => HeadlessApp.RunAsync(() =>
    {
        Assert.True(AssetLoader.Exists(new Uri("avares://GGCalendar.App/Assets/" + file)), file);
        return Task.CompletedTask;
    });

    // ---- T315: the settings window model ----

    [Fact]
    [Trait("TestCase", "T315")]
    public void TheSettingsOfferTheFontAndKeepTheChoice()
    {
        var vm = new SettingsViewModel(new AppSettings());
        Assert.Equal("Inter", vm.SelectedFont);
        Assert.Equal(["Inter", "Roboto", "Plus Jakarta Sans"], vm.FontChoices.Take(3));
        Assert.Null(vm.TryBuild()!.FontFamily); // the default is stored as "no choice"

        vm.SelectedFont = "Roboto";
        Assert.Equal("Roboto", vm.TryBuild()!.FontFamily);
        Assert.Equal("Roboto", new SettingsViewModel(new AppSettings { FontFamily = "Roboto" }).SelectedFont);
        Assert.Equal("Inter", new SettingsViewModel(new AppSettings { FontFamily = "A Font Nobody Has" }).SelectedFont);
    }

    // ---- T316: the whole app follows the choice ----

    private static async Task<SettingsWindow> OpenSettingsAsync(MainWindow window)
    {
        Click(FindButton(window, "Main.Settings"));
        return await WaitForAsync(() => window.OwnedWindows.OfType<SettingsWindow>().FirstOrDefault(), "la finestra delle impostazioni");
    }

    [Fact]
    [Trait("TestCase", "T316")]
    public Task T316_ChoosingAFontAndSavingChangesTheTextOfTheWholeAppAtOnceAndIsKept() => HeadlessApp.RunAsync(async () =>
    {
        var settingsFile = new SettingsStore().FilePath;
        var original = File.Exists(settingsFile) ? File.ReadAllBytes(settingsFile) : null;
        var window = OpenApp();
        try
        {
            Assert.Equal("fonts:Inter#Inter", window.FontFamily.ToString());

            // Cancel: the font stays.
            var dialog = await OpenSettingsAsync(window);
            ((ComboBox)FindById(dialog, "Settings.FontFamily")).SelectedItem = "Roboto";
            Click(FindButton(dialog, "Settings.Cancel"));
            await WaitForAsync(() => window.OwnedWindows.Count == 0 ? window : null, "la chiusura delle impostazioni");
            Assert.Equal("fonts:Inter#Inter", window.FontFamily.ToString());
            Assert.Null(new SettingsStore().Load().FontFamily);

            // Save: at once, with no restart, the window and the grids drawn by hand use the new font.
            dialog = await OpenSettingsAsync(window);
            Assert.Equal("Inter", ((ComboBox)FindById(dialog, "Settings.FontFamily")).SelectedItem);
            ((ComboBox)FindById(dialog, "Settings.FontFamily")).SelectedItem = "Plus Jakarta Sans";
            Click(FindButton(dialog, "Settings.Save"));
            await WaitForAsync(() => window.OwnedWindows.Count == 0 ? window : null, "la chiusura delle impostazioni");
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("fonts:PlusJakartaSans#Plus Jakarta Sans", window.FontFamily.ToString());
            Assert.Equal("fonts:PlusJakartaSans#Plus Jakarta Sans", AppFonts.Current.ToString());
            Assert.Equal("Plus Jakarta Sans", new SettingsStore().Load().FontFamily);

            // Back to the default: stored as no choice.
            dialog = await OpenSettingsAsync(window);
            ((ComboBox)FindById(dialog, "Settings.FontFamily")).SelectedItem = "Inter";
            Click(FindButton(dialog, "Settings.Save"));
            await WaitForAsync(() => window.OwnedWindows.Count == 0 ? window : null, "la chiusura delle impostazioni");
            Assert.Equal("fonts:Inter#Inter", window.FontFamily.ToString());
            Assert.Null(new SettingsStore().Load().FontFamily);
        }
        finally
        {
            window.Close();
            if (original is not null) File.WriteAllBytes(settingsFile, original);
            L.Apply(new SettingsStore().Load().Language);
            AppFonts.Apply(null);
        }
    });

    // ---- T317: the bundled fonts really load (with GGCALENDAR_TEST_SCREENSHOTS=1, which draws for real; otherwise it does nothing) ----

    [Theory]
    [Trait("TestCase", "T317")]
    [InlineData("fonts:Inter#Inter", "Inter")]
    [InlineData("fonts:Roboto#Roboto", "Roboto")]
    [InlineData("fonts:PlusJakartaSans#Plus Jakarta Sans", "Plus Jakarta Sans")]
    public Task TheFontsOfTheAppLoadWithTheirOwnNames(string key, string family) => HeadlessApp.RunAsync(() =>
    {
        if (!HeadlessApp.DrawsForReal) return Task.CompletedTask;
        foreach (var weight in new[] { Avalonia.Media.FontWeight.Normal, Avalonia.Media.FontWeight.SemiBold, Avalonia.Media.FontWeight.Bold })
        {
            var typeface = new Avalonia.Media.Typeface(new Avalonia.Media.FontFamily(key), Avalonia.Media.FontStyle.Normal, weight);
            Assert.True(Avalonia.Media.FontManager.Current.TryGetGlyphTypeface(typeface, out var glyphs), $"{key} {weight}");
            Assert.StartsWith(family, glyphs!.FamilyName);
        }

        return Task.CompletedTask;
    });

    // ---- T318: a picture of the main window in each font (with GGCALENDAR_TEST_SCREENSHOTS=1; otherwise it does nothing) ----

    public static string Folder => Path.Combine(Path.GetTempPath(), "GGCalendar_T318");

    [Theory]
    [Trait("TestCase", "T318")]
    [InlineData("Inter")]
    [InlineData("Roboto")]
    [InlineData("Plus Jakarta Sans")]
    [InlineData("Consolas")]
    public Task DrawsTheMainWindowInEachFont(string font) => HeadlessApp.RunAsync(async () =>
    {
        if (!HeadlessApp.DrawsForReal) return;
        Directory.CreateDirectory(Folder);
        var window = OpenApp();
        try
        {
            window.Width = 1280;
            window.Height = 800;
            AppFonts.Apply(font);
            await Task.Delay(500);
            Dispatcher.UIThread.RunJobs();
            var frame = Avalonia.Headless.HeadlessWindowExtensions.CaptureRenderedFrame(window);
            Assert.NotNull(frame);
            frame.Save(Path.Combine(Folder, font.Replace(' ', '_') + ".png"));
        }
        finally
        {
            window.Close();
            AppFonts.Apply(null);
        }
    });
}
