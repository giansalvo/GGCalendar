using GGCalendar.App.ViewModels;
using GGCalendar.Core.Models;
using GGCalendar.Core.Subscriptions;
using GGCalendar.Data;

namespace GGCalendar.Core.Tests;

/// <summary>
/// T363-T365: the reloads of the lists never put older data back (issue #58). A reload that started first and finishes last is dropped,
/// and what a reload reads is one coherent picture of the store.
/// </summary>
public class ReloadOrderTests
{
    private static CalendarInfo Local(string id, string name) => new(id, MainViewModel.LocalAccountName, name, "#1A73E8");

    private static string[] Names(MainViewModel vm) => vm.Calendars.Select(c => c.Name).Order().ToArray();

    // The older reload reads (one calendar), is held, a calendar is added, the newer reload reads and applies, then the older one is let go.
    private static async Task<MainViewModel> OlderReloadFinishesLast(Func<MainViewModel, Task> newer)
    {
        var store = new CalendarStore(":memory:");
        store.UpsertCalendar(Local("local-a", "A"));
        var vm = new MainViewModel(store);
        using var held = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var hold = true;
        vm.AfterReadHook = _ =>
        {
            if (!hold) return;
            hold = false;
            held.Set();
            release.Wait(TimeSpan.FromSeconds(10));
        };

        var older = vm.ReloadAsync();
        Assert.True(held.Wait(TimeSpan.FromSeconds(10)), "la ricarica più vecchia doveva aver letto");
        store.UpsertCalendar(Local("local-b", "B"));
        await newer(vm);
        Assert.Equal(["A", "B"], Names(vm));

        release.Set();
        await older;
        return vm;
    }

    [Fact]
    [Trait("TestCase", "T363")]
    public async Task AnOlderAsyncReloadThatFinishesLastDoesNotPutTheOldListBack()
    {
        var vm = await OlderReloadFinishesLast(v => v.ReloadAsync());
        Assert.Equal(["A", "B"], Names(vm));
        Assert.Contains(vm.ReloadTrace, line => line.Contains("scartato"));
    }

    [Fact]
    [Trait("TestCase", "T363")]
    public async Task AnOlderAsyncReloadDoesNotOverwriteASynchronousReload()
    {
        var vm = await OlderReloadFinishesLast(v =>
        {
            v.Reload();
            return Task.CompletedTask;
        });
        Assert.Equal(["A", "B"], Names(vm));
    }

    [Fact]
    [Trait("TestCase", "T364")]
    public async Task AReloadStartedLaterIsAppliedEvenIfItWasSlowToStart()
    {
        // The normal case stays as it was: reloads one after the other each show what they read.
        var store = new CalendarStore(":memory:");
        store.UpsertCalendar(Local("local-a", "A"));
        var vm = new MainViewModel(store);
        store.UpsertCalendar(Local("local-b", "B"));
        await vm.ReloadAsync();
        Assert.Equal(["A", "B"], Names(vm));
        store.DeleteCalendar("local-b");
        await vm.ReloadAsync();
        Assert.Equal(["A"], Names(vm));
        vm.Reload();
        Assert.Equal(["A"], Names(vm));
    }

    [Fact]
    [Trait("TestCase", "T365")]
    public async Task ASnapshotNeverHasASubscriptionWithoutItsCalendar()
    {
        var path = Path.Combine(Path.GetTempPath(), "ggcal-snapshot-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var store = new CalendarStore(path);
            using var stop = new CancellationTokenSource();
            var writer = Task.Run(() =>
            {
                for (var i = 0; !stop.IsCancellationRequested; i++)
                {
                    var id = SubscriptionRecord.IdPrefix + (i % 5);
                    store.AddSubscription(new CalendarInfo(id, "Calendari pubblici", "Abbonamento " + i, "#1A73E8"), new SubscriptionRecord(id, "x"), []);
                    store.DeleteCalendar(id);
                }
            });

            for (var i = 0; i < 400; i++)
            {
                var (calendars, _, subscriptions) = store.GetSnapshot();
                var ids = calendars.Select(c => c.Id).ToHashSet();
                Assert.All(subscriptions, s => Assert.Contains(s.CalendarId, ids));
            }

            stop.Cancel();
            await writer;
        }
        finally
        {
            foreach (var f in Directory.GetFiles(Path.GetTempPath(), Path.GetFileName(path) + "*")) File.Delete(f);
        }
    }
}
