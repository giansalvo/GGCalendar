using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Threading;
using GGCalendar.App.Services;
using GGCalendar.App.Views;
using GGCalendar.Core.Models;
using GGCalendar.Core.Tests.Support;
using GGCalendar.Data;
using static GGCalendar.Core.Tests.Support.AppDriver;

namespace GGCalendar.Core.Tests;

/// <summary>Closing the window hides the app in the tray, and only one copy of the app runs (issue #30).</summary>
[Collection(CollectionName)]
public class TrayTests
{
    private static string TempFile() => Path.Combine(Path.GetTempPath(), "ggcal-tray-" + Guid.NewGuid().ToString("N") + ".json");

    private static string UniqueName() => "GGCalendar.Test." + Guid.NewGuid().ToString("N");

    private sealed class Rig
    {
        public required MainWindow Window { get; init; }
        public required TrayController Tray { get; init; }
        public required SettingsStore Store { get; init; }
        public int Notices { get; set; }
        public int Shutdowns { get; set; }
        public bool Closed { get; set; }
        public TaskCompletionSource? NoticeGate { get; set; }
    }

    /// <summary>The real app with a tray controller of its own (no icon: the headless session has no notification area) and a settings file of the test.</summary>
    private static Task InTrayAppAsync(Func<Rig, Task> test, bool noticeShown = true) => HeadlessApp.RunAsync(async () =>
    {
        var window = OpenApp();
        var file = TempFile();
        var store = new SettingsStore(file);
        // The window loads the settings of the installed app when it opens: the settings of the test come after.
        Assert.True(await WaitAsync(() => window.Scheduler is not null));
        Vm(window).Settings = new AppSettings { TrayNoticeShown = noticeShown };
        Rig? rig = null;
        var tray = new TrayController(window, store, () => rig!.Shutdowns++, withIcon: false);
        rig = new Rig { Window = window, Tray = tray, Store = store };
        tray.ShowNotice = async _ =>
        {
            rig.Notices++;
            if (rig.NoticeGate is { } gate) await gate.Task;
        };
        window.Closed += (_, _) => rig.Closed = true;
        try
        {
            await test(rig);
        }
        finally
        {
            tray.Exit();
            if (!rig.Closed) window.Close();
            Dispatcher.UIThread.RunJobs();
            File.Delete(file);
        }
    });

    private static async Task<bool> WaitAsync(Func<bool> done)
    {
        for (var i = 0; i < 60 && !done(); i++)
        {
            await Task.Delay(50);
            Dispatcher.UIThread.RunJobs();
        }

        return done();
    }

    // ---- T370, T371: one copy only ----

    [Fact]
    [Trait("TestCase", "T370")]
    public void ASecondStartDoesNotRunAndAsksTheFirstToShowItsWindow()
    {
        var name = UniqueName();
        using var first = SingleInstance.TryAcquire(name);
        Assert.NotNull(first);
        var asked = new ManualResetEventSlim();
        first!.Activated += asked.Set;
        first.StartListening();

        Assert.Null(SingleInstance.TryAcquire(name));
        Assert.True(asked.Wait(TimeSpan.FromSeconds(5)), "the first copy was not asked to show its window");

        // Every further start asks again.
        asked.Reset();
        Assert.Null(SingleInstance.TryAcquire(name));
        Assert.True(asked.Wait(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    [Trait("TestCase", "T371")]
    public void AStartWhileTheFirstCopyIsStillStartingIsNotLost()
    {
        var name = UniqueName();
        using var first = SingleInstance.TryAcquire(name);
        Assert.NotNull(first);
        // The second start comes before the first copy listens: the request waits for it.
        Assert.Null(SingleInstance.TryAcquire(name));
        var asked = new ManualResetEventSlim();
        first!.Activated += asked.Set;
        first.StartListening();
        Assert.True(asked.Wait(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    [Trait("TestCase", "T371")]
    public void WhenTheFirstCopyEndsANewOneCanStart()
    {
        var name = UniqueName();
        var first = SingleInstance.TryAcquire(name);
        Assert.NotNull(first);
        first!.StartListening();
        first.Dispose();

        using var next = SingleInstance.TryAcquire(name);
        Assert.NotNull(next);
    }

    [Fact]
    [Trait("TestCase", "T371")]
    public void DifferentNamesDoNotAskEachOther()
    {
        using var a = SingleInstance.TryAcquire(UniqueName());
        using var b = SingleInstance.TryAcquire(UniqueName());
        Assert.NotNull(a);
        Assert.NotNull(b);
    }

    // ---- T372: closing hides ----

    [Fact]
    [Trait("TestCase", "T372")]
    public Task ClosingTheWindowHidesItAndTheAppKeepsRunning() => InTrayAppAsync(async rig =>
    {
        Assert.True(rig.Window.IsVisible);
        var scheduler = rig.Window.Scheduler;
        Assert.NotNull(scheduler);

        rig.Window.Close();
        Assert.True(await WaitAsync(() => !rig.Window.IsVisible));

        Assert.True(rig.Tray.IsHidden);
        Assert.False(rig.Closed);
        Assert.Equal(0, rig.Shutdowns);
        Assert.Same(scheduler, rig.Window.Scheduler);
    });

    [Fact]
    [Trait("TestCase", "T372")]
    public Task ClosingAgainHidesAgainAfterTheWindowWasShown() => InTrayAppAsync(async rig =>
    {
        var scheduler = rig.Window.Scheduler;
        var settings = Vm(rig.Window).Settings;
        for (var i = 0; i < 3; i++)
        {
            rig.Window.Close();
            Assert.True(await WaitAsync(() => !rig.Window.IsVisible));
            rig.Tray.Show();
            Assert.True(rig.Window.IsVisible);
        }

        // Showing the window again does not set the app up a second time (a second scheduler would ring every alarm twice).
        Assert.Same(scheduler, rig.Window.Scheduler);
        Assert.Same(settings, Vm(rig.Window).Settings);
        Assert.False(rig.Closed);
        Assert.Equal(0, rig.Shutdowns);
    });

    // ---- T373: showing again ----

    [Fact]
    [Trait("TestCase", "T373")]
    public Task ShowOpensTheHiddenWindow() => InTrayAppAsync(async rig =>
    {
        rig.Window.Close();
        Assert.True(await WaitAsync(() => !rig.Window.IsVisible));

        rig.Tray.Show();

        Assert.True(rig.Window.IsVisible);
        Assert.False(rig.Tray.IsHidden);
        Assert.Equal(WindowState.Normal, rig.Window.WindowState);
    });

    [Fact]
    [Trait("TestCase", "T373")]
    public Task MinimizingGoesToTheTrayAndShowOpensTheWindowNormal() => InTrayAppAsync(async rig =>
    {
        rig.Window.WindowState = WindowState.Minimized;
        Assert.True(await WaitAsync(() => !rig.Window.IsVisible));
        Assert.False(rig.Closed);
        Assert.Equal(0, rig.Shutdowns);

        rig.Tray.Show();
        Assert.True(rig.Window.IsVisible);
        Assert.Equal(WindowState.Normal, rig.Window.WindowState);

        // Already shown: nothing changes (a click on the icon of a visible window only brings it forward).
        rig.Tray.Show();
        Assert.True(rig.Window.IsVisible);
    });

    [Fact]
    [Trait("TestCase", "T374")]
    public Task MinimizingTheFirstTimeShowsTheMessageOverANormalWindow() => InTrayAppAsync(async rig =>
    {
        rig.NoticeGate = new TaskCompletionSource();
        rig.Window.WindowState = WindowState.Minimized;
        Assert.True(await WaitAsync(() => rig.Notices == 1));
        Assert.True(rig.Window.IsVisible);
        Assert.Equal(WindowState.Normal, rig.Window.WindowState);

        rig.NoticeGate.SetResult();
        Assert.True(await WaitAsync(() => !rig.Window.IsVisible));
        Assert.Equal(1, rig.Notices);
    }, noticeShown: false);

    [Fact]
    [Trait("TestCase", "T373")]
    public Task AStartOfTheAppShowsTheWindowHiddenInTheTray() => InTrayAppAsync(async rig =>
    {
        var name = UniqueName();
        using var first = SingleInstance.TryAcquire(name);
        first!.Activated += () => Dispatcher.UIThread.Post(rig.Tray.Show);
        first.StartListening();
        rig.Window.Close();
        Assert.True(await WaitAsync(() => !rig.Window.IsVisible));

        Assert.Null(SingleInstance.TryAcquire(name));

        Assert.True(await WaitAsync(() => rig.Window.IsVisible));
    });

    [Fact]
    [Trait("TestCase", "T373")]
    public Task TheSplashScreenIsNotShownAgainWhenTheWindowIsShownAgain() => HeadlessApp.RunAsync(async () =>
    {
        var window = new Window { Width = 200, Height = 100 };
        SplashWindow.ShowWhenOpened(window, TimeSpan.FromMinutes(5));
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            Assert.Single(window.OwnedWindows);
            foreach (var splash in window.OwnedWindows.ToList()) splash.Close();
            window.Hide();
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(window.OwnedWindows);
            await Task.CompletedTask;
        }
        finally
        {
            foreach (var splash in window.OwnedWindows.ToList()) splash.Close();
            window.Close();
        }
    });

    // ---- T374: the message of the first time ----

    [Fact]
    [Trait("TestCase", "T374")]
    public Task TheFirstTimeAMessageIsShownBeforeTheWindowIsHidden() => InTrayAppAsync(async rig =>
    {
        rig.NoticeGate = new TaskCompletionSource();
        rig.Window.Close();
        Assert.True(await WaitAsync(() => rig.Notices == 1));

        // The person has not answered yet: the window is still there.
        Assert.True(rig.Window.IsVisible);

        rig.NoticeGate.SetResult();
        Assert.True(await WaitAsync(() => !rig.Window.IsVisible));
        Assert.Equal(1, rig.Notices);
    }, noticeShown: false);

    [Fact]
    [Trait("TestCase", "T374")]
    public Task TheMessageIsShownOnlyTheFirstTimeAndItIsRemembered() => InTrayAppAsync(async rig =>
    {
        rig.Window.Close();
        Assert.True(await WaitAsync(() => !rig.Window.IsVisible));
        Assert.Equal(1, rig.Notices);
        Assert.True(rig.Store.Load().TrayNoticeShown);

        rig.Tray.Show();
        rig.Window.Close();
        Assert.True(await WaitAsync(() => !rig.Window.IsVisible));
        Assert.Equal(1, rig.Notices);
    }, noticeShown: false);

    [Fact]
    [Trait("TestCase", "T374")]
    public Task NoMessageWhenThePersonHasAlreadyBeenTold() => InTrayAppAsync(async rig =>
    {
        rig.Window.Close();
        Assert.True(await WaitAsync(() => !rig.Window.IsVisible));
        Assert.Equal(0, rig.Notices);
    });

    [Fact]
    [Trait("TestCase", "T374")]
    public void TheMessageIsRememberedInTheSettingsFileAndOldFilesHaveNotSeenIt()
    {
        Assert.False(new AppSettings().TrayNoticeShown);
        var file = TempFile();
        try
        {
            File.WriteAllText(file, "{\"DayStartHour\": 8}");
            Assert.False(new SettingsStore(file).Load().TrayNoticeShown);

            var store = new SettingsStore(file);
            var s = store.Load();
            s.TrayNoticeShown = true;
            store.Save(s);
            Assert.True(new SettingsStore(file).Load().TrayNoticeShown);
            Assert.Equal(8, new SettingsStore(file).Load().DayStartHour);
            Assert.True(JsonDocument.Parse(File.ReadAllText(file)).RootElement.TryGetProperty("TrayNoticeShown", out _));
        }
        finally
        {
            File.Delete(file);
        }
    }

    // ---- T375: quitting ----

    [Fact]
    [Trait("TestCase", "T375")]
    public Task ExitQuitsTheAppAndTheWindowClosesForReal() => InTrayAppAsync(async rig =>
    {
        rig.Window.Close();
        Assert.True(await WaitAsync(() => !rig.Window.IsVisible));

        rig.Tray.Exit();
        Assert.Equal(1, rig.Shutdowns);
        Assert.True(rig.Tray.IsExiting);

        // The lifetime closes the windows: now the close is not turned into a hide.
        rig.Window.Close();
        Assert.True(await WaitAsync(() => rig.Closed));
    });

    [Fact]
    [Trait("TestCase", "T375")]
    public Task AfterExitTheIconCannotShowTheWindowAgain() => InTrayAppAsync(async rig =>
    {
        rig.Window.Close();
        Assert.True(await WaitAsync(() => !rig.Window.IsVisible));
        rig.Tray.Exit();
        rig.Tray.Show();
        Assert.False(rig.Window.IsVisible);
    });

    [Fact]
    [Trait("TestCase", "T375")]
    public Task TheWindowWithoutATrayClosesAsBefore() => HeadlessApp.RunAsync(async () =>
    {
        // The tests and anything that opens the window without the tray keep the old behaviour: close means close.
        var window = OpenApp();
        var closed = false;
        window.Closed += (_, _) => closed = true;
        window.Close();
        Assert.True(await WaitAsync(() => closed));
    });
}
