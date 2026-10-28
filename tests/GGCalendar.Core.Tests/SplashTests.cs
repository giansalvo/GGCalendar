using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GGCalendar.App.Views;
using GGCalendar.Core.Models;
using GGCalendar.Core.Tests.Support;
using GGCalendar.Data;

namespace GGCalendar.Core.Tests;

/// <summary>The splash screen of the first start after an install or an update.</summary>
public class SplashTests
{
    private static string TempFile() => Path.Combine(Path.GetTempPath(), "ggcal-splash-" + Guid.NewGuid().ToString("N") + ".json");

    private static async Task<bool> WaitAsync(Func<bool> done)
    {
        for (var i = 0; i < 60 && !done(); i++)
        {
            await Task.Delay(50);
            Dispatcher.UIThread.RunJobs();
        }

        return done();
    }

    // ---- T275: when it is due ----

    [Theory]
    [Trait("TestCase", "T275")]
    [InlineData(null, "1.0.3", true)]
    [InlineData("1.0.2", "1.0.3", true)]
    [InlineData("1.0.3", "1.0.3", false)]
    [InlineData("1.0.4", "1.0.3", true)]
    public void SplashIsDueOnlyTheFirstTimeOfAVersion(string? shown, string running, bool expected) =>
        Assert.Equal(expected, SplashPolicy.ShouldShow(new AppSettings { SplashVersion = shown }, running));

    [Fact]
    [Trait("TestCase", "T275")]
    public void SettingsWithoutTheFieldShowTheSplash()
    {
        var old = JsonSerializer.Deserialize<AppSettings>("{\"Theme\":1}")!;
        Assert.Null(old.SplashVersion);
        Assert.True(SplashPolicy.ShouldShow(old, "1.0.3"));
    }

    // ---- T276: remembered between starts ----

    [Fact]
    [Trait("TestCase", "T276")]
    public void FirstStartClaimsTheSplashAndSavesTheVersionBeforeShowingIt()
    {
        var file = TempFile();
        try
        {
            var store = new SettingsStore(file);
            Assert.False(File.Exists(file));

            Assert.True(SplashGate.Claim(store, store.Load(), "1.0.3"));

            Assert.Equal("1.0.3", new SettingsStore(file).Load().SplashVersion);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    [Trait("TestCase", "T276")]
    public void SecondStartOfTheSameVersionHasNoSplashButAnUpdateHasIt()
    {
        var file = TempFile();
        try
        {
            var store = new SettingsStore(file);
            Assert.True(SplashGate.Claim(store, store.Load(), "1.0.3"));
            Assert.False(SplashGate.Claim(store, store.Load(), "1.0.3"));
            Assert.True(SplashGate.Claim(store, store.Load(), "1.0.4"));
            Assert.False(SplashGate.Claim(store, store.Load(), "1.0.4"));
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    [Trait("TestCase", "T276")]
    public void ClaimKeepsTheOtherSettings()
    {
        var file = TempFile();
        try
        {
            var store = new SettingsStore(file);
            store.Save(new AppSettings { Theme = 2, Language = "de", SyncIntervalMinutes = 15 });

            Assert.True(SplashGate.Claim(store, store.Load(), "1.0.3"));

            var after = store.Load();
            Assert.Equal((2, "de", 15), (after.Theme, after.Language, after.SyncIntervalMinutes));
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    [Trait("TestCase", "T276")]
    public void SplashIsShownEvenIfTheFileCannotBeWritten()
    {
        // A folder where the file should be: the save fails, the splash is shown anyway.
        var folder = Path.Combine(Path.GetTempPath(), "ggcal-splash-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var store = new SettingsStore(folder);
            Assert.True(SplashGate.Claim(store, new AppSettings(), "1.0.3"));
        }
        finally
        {
            Directory.Delete(folder);
        }
    }

    // ---- T277: what it shows ----

    [Fact]
    [Trait("TestCase", "T277")]
    public Task SplashHasTheContentOfAboutAndNoButtons() => HeadlessApp.RunAsync(async () =>
    {
        var splash = new SplashWindow();
        splash.Show();
        var about = new AboutWindow();
        about.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(splash.GetVisualDescendants().OfType<Button>());
            Assert.NotEmpty(about.GetVisualDescendants().OfType<Button>());

            var texts = splash.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).Where(t => !string.IsNullOrEmpty(t) && t != splash.Title).ToList();
            var aboutTexts = about.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).Where(t => !string.IsNullOrEmpty(t)).ToList();
            Assert.Contains("GGCalendar", texts);
            Assert.Contains(texts, t => t!.Contains(GGCalendar.App.AppInfo.Version));
            Assert.All(texts, t => Assert.Contains(t, aboutTexts));
            Assert.Equal(about.GetVisualDescendants().OfType<Avalonia.Controls.Image>().Count(), splash.GetVisualDescendants().OfType<Avalonia.Controls.Image>().Count());
            await Task.CompletedTask;
        }
        finally
        {
            about.Close();
            splash.Close();
        }
    });

    [Fact]
    [Trait("TestCase", "T277")]
    public Task SplashHasABorderAndATitleBarWithoutCaptionButtons() => HeadlessApp.RunAsync(async () =>
    {
        var splash = new SplashWindow();
        splash.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            var border = Assert.IsType<Border>(splash.Content);
            Assert.Equal(new Avalonia.Thickness(1), border.BorderThickness);
            var bar = splash.GetVisualDescendants().OfType<Border>().First(b => b.Child is TextBlock t && t.Text == splash.Title);
            Assert.Equal(Dock.Top, DockPanel.GetDock(bar));
            Assert.True(bar.Bounds.Height > 0);
            Assert.Equal(WindowDecorations.BorderOnly, splash.WindowDecorations);
            Assert.False(splash.CanResize);
            await Task.CompletedTask;
        }
        finally
        {
            splash.Close();
        }
    });

    // ---- T278: how it behaves ----

    [Fact]
    [Trait("TestCase", "T278")]
    public void SplashLastsFiveSeconds() => Assert.Equal(TimeSpan.FromSeconds(5), SplashWindow.DefaultDuration);

    [Fact]
    [Trait("TestCase", "T278")]
    public Task SplashIsOnTopOutOfTheTaskbarAndClosesByItself() => HeadlessApp.RunAsync(async () =>
    {
        var splash = new SplashWindow(TimeSpan.FromMilliseconds(300));
        var closed = false;
        splash.Closed += (_, _) => closed = true;
        splash.Show();
        try
        {
            Assert.True(splash.Topmost);
            Assert.False(splash.ShowInTaskbar);
            Assert.Equal(WindowStartupLocation.CenterScreen, splash.WindowStartupLocation);
            Assert.False(closed);
            Assert.True(await WaitAsync(() => closed));
        }
        finally
        {
            if (!closed) splash.Close();
        }
    });

    [Fact]
    [Trait("TestCase", "T278")]
    public Task SplashOpensOverTheMainWindowAndLeavesItOpen() => HeadlessApp.RunAsync(async () =>
    {
        var main = new Window { Width = 400, Height = 300 };
        SplashWindow.ShowWhenOpened(main, TimeSpan.FromMilliseconds(300));
        Assert.Empty(main.OwnedWindows);
        main.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            var splash = Assert.IsType<SplashWindow>(Assert.Single(main.OwnedWindows));
            Assert.Same(main, splash.Owner);
            Assert.True(splash.IsVisible);
            Assert.True(main.IsVisible);

            Assert.True(await WaitAsync(() => !splash.IsVisible));
            Assert.True(main.IsVisible);
            Assert.Empty(main.OwnedWindows);
        }
        finally
        {
            main.Close();
        }
    });
}
