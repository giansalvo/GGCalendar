using GGCalendar.App.ViewModels;
using GGCalendar.Core.Localization;
using GGCalendar.Core.Models;
using GGCalendar.Core.Tests.Support;
using GGCalendar.Data;

namespace GGCalendar.Core.Tests;

/// <summary>
/// The first edit of one occurrence of a series (issue #51): Google's instance has no version the app knows, so before the edit is sent
/// the instance is read and compared with the occurrence the person saw. The real <see cref="GGCalendar.App.Services.GoogleSyncService"/>
/// against an in-memory Google with a series of three days.
/// </summary>
public class OccurrenceFirstEditTests
{
    private static readonly DateTime Utc = new(2026, 10, 14, 7, 0, 0, DateTimeKind.Utc);
    private static string Suffix => L.T("sync.conflict_suffix");

    private sealed class Scene : IDisposable
    {
        public SyncRig Rig { get; } = new();
        public string Master { get; private set; } = "";
        public MainViewModel Vm { get; private set; } = null!;

        public static async Task<Scene> StartAsync()
        {
            var scene = new Scene();
            scene.Master = scene.Rig.Google.SeedSeries("Serie", Utc, 3);
            await scene.Rig.SyncOkAsync(); // the series comes to the app
            scene.Vm = new MainViewModel(scene.Rig.Store);
            return scene;
        }

        /// <summary>The occurrence shown <paramref name="day"/> days after the first one.</summary>
        public CalendarEvent Occurrence(int day)
        {
            var first = Utc.ToLocalTime().AddDays(day);
            return Vm.ExpandedBetween(first.Date, first.Date.AddDays(1)).Single(e => e.Start == first);
        }

        public static DateTime UtcOf(int day) => Utc.AddDays(day);

        public CalendarEvent? Exception(int day) =>
            Rig.Store.GetAllEvents().FirstOrDefault(e => e.SeriesMasterId is not null && e.OriginalStart == Utc.ToLocalTime().AddDays(day) && !e.Title.EndsWith(Suffix));

        public IEnumerable<CalendarEvent> Copies() => Rig.Store.GetAllEvents().Where(e => e.Title.EndsWith(Suffix));

        public void Dispose() => Rig.Dispose();
    }

    [Fact]
    [Trait("TestCase", "T337")]
    public async Task T337_AnInstanceChangedOnGoogleKeepsGoogleAndSavesTheEditAsACopy()
    {
        using var scene = await Scene.StartAsync();
        var occurrence = scene.Occurrence(1);
        scene.Vm.SaveOccurrence(occurrence, occurrence with { Title = "Titolo locale" }, SeriesScope.This);
        scene.Rig.Google.SetInstanceSummary(scene.Master, Scene.UtcOf(1), "Titolo Google");

        var result = await scene.Rig.SyncOkAsync();

        Assert.Single(result.Conflicts);
        Assert.Equal("Titolo Google", scene.Rig.Google.InstanceSummary(scene.Master, Scene.UtcOf(1))); // not overwritten
        Assert.Equal("Titolo Google", scene.Rig.Store.GetAllEvents().Single(e => e.SeriesMasterId is not null).Title);
        var copy = Assert.Single(scene.Copies());
        Assert.Equal("Titolo locale" + Suffix, copy.Title);
        Assert.Null(copy.SeriesMasterId); // a separate event, not another occurrence of the series
        Assert.NotNull(scene.Rig.Google.FindBySummary("Titolo locale" + Suffix)); // sent in the same sync
        Assert.Empty(scene.Rig.Store.GetPending());
    }

    [Fact]
    [Trait("TestCase", "T338")]
    public async Task T338_AnInstanceNobodyChangedIsPatchedWithItsVersionAndTheNextEditIsProtected()
    {
        using var scene = await Scene.StartAsync();
        var occurrence = scene.Occurrence(1);
        scene.Vm.SaveOccurrence(occurrence, occurrence with { Title = "Solo questa" }, SeriesScope.This);
        scene.Rig.Google.Requests.Clear();

        var result = await scene.Rig.SyncOkAsync();

        Assert.Empty(result.Conflicts);
        Assert.Equal("Solo questa", scene.Rig.Google.InstanceSummary(scene.Master, Scene.UtcOf(1)));
        var instance = FakeGoogle.InstanceId(scene.Master, Scene.UtcOf(1));
        var requests = scene.Rig.Google.Requests.Where(r => r.Contains(instance)).ToList();
        Assert.Equal(2, requests.Count);
        Assert.StartsWith("GET", requests[0]); // read first
        Assert.StartsWith("PATCH", requests[1]);
        Assert.Empty(scene.Copies());

        // From now on the occurrence has its own version, as any other event: a second edit does not read it again.
        var exception = scene.Exception(1)!;
        Assert.NotNull(scene.Rig.Store.GetRemoteInfo(exception.Id)?.Etag);
        scene.Vm.UpdateEvent(exception with { Title = "Ancora" });
        scene.Rig.Google.Requests.Clear();
        await scene.Rig.SyncOkAsync();
        Assert.Equal("Ancora", scene.Rig.Google.InstanceSummary(scene.Master, Scene.UtcOf(1)));
        Assert.DoesNotContain(scene.Rig.Google.Requests, r => r.StartsWith("GET") && r.Contains(instance));
    }

    [Fact]
    [Trait("TestCase", "T339")]
    public async Task T339_AVersionChangeMadeByGoogleAloneIsNotAConflict()
    {
        using var scene = await Scene.StartAsync();
        scene.Rig.Google.TouchInstance(scene.Master, Scene.UtcOf(1)); // stored, same content (a Meet, a guest's reply)
        await scene.Rig.SyncOkAsync();
        var occurrence = scene.Occurrence(1);
        scene.Vm.SaveOccurrence(occurrence, occurrence with { Title = "Solo questa" }, SeriesScope.This);
        scene.Rig.Google.TouchInstance(scene.Master, Scene.UtcOf(1)); // and again after the person saw it

        var result = await scene.Rig.SyncOkAsync();

        Assert.Empty(result.Conflicts);
        Assert.Equal("Solo questa", scene.Rig.Google.InstanceSummary(scene.Master, Scene.UtcOf(1)));
        Assert.Empty(scene.Copies());
    }

    [Fact]
    [Trait("TestCase", "T340")]
    public async Task T340_AnOccurrenceDeletedOnGoogleKeepsTheEditAsACopy()
    {
        using var scene = await Scene.StartAsync();
        var occurrence = scene.Occurrence(1);
        scene.Vm.SaveOccurrence(occurrence, occurrence with { Title = "Titolo locale" }, SeriesScope.This);
        scene.Rig.Google.CancelInstance(scene.Master, Scene.UtcOf(1));

        var result = await scene.Rig.SyncOkAsync();

        Assert.Single(result.Conflicts);
        Assert.Equal("Titolo locale" + Suffix, Assert.Single(scene.Copies()).Title);
        Assert.Null(scene.Rig.Google.InstanceSummary(scene.Master, Scene.UtcOf(1))); // still deleted there
        Assert.Empty(scene.Rig.Store.GetPending());
    }

    [Fact]
    [Trait("TestCase", "T341")]
    public async Task T341_ASeriesDeletedOnGoogleKeepsTheEditAsACopyWithoutAnError()
    {
        using var scene = await Scene.StartAsync();
        var occurrence = scene.Occurrence(1);
        scene.Vm.SaveOccurrence(occurrence, occurrence with { Title = "Titolo locale" }, SeriesScope.This);
        scene.Rig.Google.Remove(scene.Master);

        var result = await scene.Rig.SyncOkAsync(); // no "rejected" error

        Assert.Single(result.Conflicts);
        Assert.Equal("Titolo locale" + Suffix, Assert.Single(scene.Copies()).Title);
        Assert.Empty(scene.Rig.Store.GetPending());
    }

    [Fact]
    [Trait("TestCase", "T342")]
    public async Task T342_ASeriesChangedOnGoogleIsAConflictForItsFirstEditedOccurrence()
    {
        using var scene = await Scene.StartAsync();
        var occurrence = scene.Occurrence(1);
        scene.Vm.SaveOccurrence(occurrence, occurrence with { Title = "Titolo locale" }, SeriesScope.This);
        scene.Rig.Google.SetSummary(scene.Master, "Serie rinominata"); // the whole series, so its instances too

        var result = await scene.Rig.SyncOkAsync();

        Assert.Single(result.Conflicts);
        Assert.Equal("Serie rinominata", scene.Rig.Google.InstanceSummary(scene.Master, Scene.UtcOf(1)));
        Assert.Equal("Titolo locale" + Suffix, Assert.Single(scene.Copies()).Title);
    }

    [Fact]
    [Trait("TestCase", "T343")]
    public async Task T343_TwoEditsOfTheSameOccurrenceBeforeTheSyncMakeOneCopy()
    {
        using var scene = await Scene.StartAsync();
        var occurrence = scene.Occurrence(1);
        scene.Vm.SaveOccurrence(occurrence, occurrence with { Title = "Prima" }, SeriesScope.This);
        scene.Vm.UpdateEvent(scene.Exception(1)! with { Title = "Seconda" });
        scene.Rig.Google.SetInstanceSummary(scene.Master, Scene.UtcOf(1), "Titolo Google");

        var result = await scene.Rig.SyncOkAsync();

        Assert.Single(result.Conflicts);
        Assert.Equal("Seconda" + Suffix, Assert.Single(scene.Copies()).Title); // the last version, once
        Assert.Equal("Titolo Google", scene.Rig.Google.InstanceSummary(scene.Master, Scene.UtcOf(1)));
        Assert.Empty(scene.Rig.Store.GetPending());
    }

    [Fact]
    [Trait("TestCase", "T344")]
    public async Task T344_AttachmentsAddedOnGoogleAreNotLost()
    {
        using var scene = await Scene.StartAsync();
        var occurrence = scene.Occurrence(1);
        scene.Vm.SaveOccurrence(occurrence, occurrence with { Title = "Titolo locale" }, SeriesScope.This);
        scene.Rig.Google.SetInstanceAttachments(scene.Master, Scene.UtcOf(1), ("https://example.com/verbale.pdf", "Verbale"));

        var result = await scene.Rig.SyncOkAsync();

        Assert.Single(result.Conflicts);
        Assert.Equal("Serie", scene.Rig.Google.InstanceSummary(scene.Master, Scene.UtcOf(1))); // Google's version, with its attachment
        Assert.Single(scene.Rig.Google.AttachmentsOf(FakeGoogle.InstanceId(scene.Master, Scene.UtcOf(1)))!);
    }

    [Fact]
    [Trait("TestCase", "T345")]
    public async Task T345_DraggingAnOccurrenceIsProtectedLikeAnEdit()
    {
        using var scene = await Scene.StartAsync();
        var occurrence = scene.Occurrence(1);
        scene.Vm.MoveOccurrence(occurrence, occurrence.Start.AddHours(3), occurrence.End.AddHours(3), SeriesScope.This);
        scene.Rig.Google.SetInstanceSummary(scene.Master, Scene.UtcOf(1), "Titolo Google");

        var result = await scene.Rig.SyncOkAsync();

        Assert.Single(result.Conflicts);
        Assert.Equal("Titolo Google", scene.Rig.Google.InstanceSummary(scene.Master, Scene.UtcOf(1)));
        var copy = Assert.Single(scene.Copies());
        Assert.Equal(occurrence.Start.AddHours(3), copy.Start); // the move is kept in the copy
    }

    [Fact]
    [Trait("TestCase", "T345")]
    public async Task T345_DraggingAnOccurrenceNobodyChangedMovesItWithoutConflict()
    {
        using var scene = await Scene.StartAsync();
        var occurrence = scene.Occurrence(1);
        scene.Vm.MoveOccurrence(occurrence, occurrence.Start.AddHours(3), occurrence.End.AddHours(3), SeriesScope.This);

        var result = await scene.Rig.SyncOkAsync();

        Assert.Empty(result.Conflicts);
        Assert.Empty(scene.Copies());
        Assert.Equal(1, scene.Rig.Google.Ids.Count(id => id.Contains('_'))); // the occurrence is now stored apart
    }

    [Fact]
    [Trait("TestCase", "T346")]
    public async Task T346_AnEditFollowedByADragBeforeTheSyncMakesOneCopy()
    {
        using var scene = await Scene.StartAsync();
        var occurrence = scene.Occurrence(1);
        scene.Vm.SaveOccurrence(occurrence, occurrence with { Title = "Titolo locale" }, SeriesScope.This);
        var exception = scene.Exception(1)!;
        scene.Vm.MoveEvent(exception, exception.Start.AddHours(2), exception.End.AddHours(2)); // a plain event now: Update queued after the Create
        scene.Rig.Google.SetInstanceSummary(scene.Master, Scene.UtcOf(1), "Titolo Google");

        var result = await scene.Rig.SyncOkAsync();

        Assert.Single(result.Conflicts);
        var copy = Assert.Single(scene.Copies());
        Assert.Equal("Titolo locale" + Suffix, copy.Title);
        Assert.Equal(exception.Start.AddHours(2), copy.Start);
        Assert.Empty(scene.Rig.Store.GetPending());
    }

    [Fact]
    [Trait("TestCase", "T347")]
    public async Task T347_ARowQueuedByAnOlderVersionWithoutTheSeenOccurrenceIsSentAsBefore()
    {
        using var scene = await Scene.StartAsync();
        var occurrence = scene.Occurrence(1);
        var old = occurrence with
        {
            Id = "old", Title = "Titolo locale", Recurrence = null, ExcludedStarts = [], SeriesMasterId = scene.Rig.Store.GetAllEvents().Single(e => e.SeriesMasterId is null).Id,
            OriginalStart = occurrence.Start, OccurrenceIndex = null,
        };
        scene.Rig.Store.UpsertEvent(old);
        scene.Rig.Store.Enqueue(old.Id, old.CalendarId, OutboxOperation.Create, old); // no base, as the older versions did
        scene.Rig.Google.SetInstanceSummary(scene.Master, Scene.UtcOf(1), "Titolo Google");

        var result = await scene.Rig.SyncOkAsync();

        Assert.Empty(result.Conflicts); // nothing to compare with: unchanged behaviour
        Assert.Equal("Titolo locale", scene.Rig.Google.InstanceSummary(scene.Master, Scene.UtcOf(1)));
    }

    [Fact]
    [Trait("TestCase", "T348")]
    public async Task T348_OnlyTheEditOfOneOccurrenceCarriesTheSeenOccurrence()
    {
        using var scene = await Scene.StartAsync();
        var occurrence = scene.Occurrence(1);

        scene.Vm.SaveOccurrence(occurrence, occurrence with { Title = "Solo questa" }, SeriesScope.This);
        var create = scene.Rig.Store.GetPending().Single(p => p.Operation == OutboxOperation.Create);
        var seen = CalendarStore.ReadBase(create)!;
        Assert.Equal("Serie", seen.Title); // as it was before the edit
        Assert.Equal(occurrence.Start, seen.Start);
        Assert.Null(seen.Recurrence); // an instance has no rule and no excluded dates
        Assert.Empty(seen.ExcludedStarts);
        Assert.Equal(occurrence.Start, seen.OriginalStart);

        foreach (var row in scene.Rig.Store.GetPending()) scene.Rig.Store.Complete(row.Seq);
        scene.Vm.SaveOccurrence(scene.Occurrence(2), scene.Occurrence(2) with { Title = "Questa e le seguenti" }, SeriesScope.ThisAndFollowing);
        Assert.All(scene.Rig.Store.GetPending().Where(p => p.Operation == OutboxOperation.Create), p => Assert.Null(p.Base));
    }

    [Fact]
    [Trait("TestCase", "T349")]
    public async Task T349_CancellingTheQueuedEditBringsTheOccurrenceBack()
    {
        using var scene = await Scene.StartAsync();
        var occurrence = scene.Occurrence(1);
        scene.Vm.SaveOccurrence(occurrence, occurrence with { Title = "Solo questa" }, SeriesScope.This);
        var exception = scene.Exception(1)!;
        Assert.Equal(QueueKind.New, Assert.Single(scene.Rig.Service.GetQueue()).Kind);

        await scene.Rig.Service.CancelQueuedAsync([exception.Id], CancellationToken.None);

        Assert.Empty(scene.Rig.Store.GetPending());
        Assert.Null(scene.Exception(1));
        Assert.Equal("Serie", new MainViewModel(scene.Rig.Store).ExpandedBetween(occurrence.Start.Date, occurrence.Start.Date.AddDays(1)).Single().Title);
    }
}
