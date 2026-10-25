using GGCalendar.App.ViewModels;
using GGCalendar.Core.Models;
using GGCalendar.Data;

namespace GGCalendar.Core.Tests;

/// <summary>How a series is shown once some of its occurrences were moved or removed (the view model merges them).</summary>
public class ModifiedOccurrenceTests
{
    private static readonly DateTime Monday = new(2026, 10, 5, 9, 0, 0);

    private static CalendarStore StoreWithSeries()
    {
        var store = new CalendarStore(":memory:");
        store.UpsertCalendar(new CalendarInfo("c1", "Locale", "Personale", "#1A73E8"));
        store.UpsertEvent(new CalendarEvent("m", "c1", "Serie", Monday, Monday.AddHours(1)) { Recurrence = "FREQ=DAILY;COUNT=5" });
        return store;
    }

    private static List<(string Title, DateTime Start)> Shown(CalendarStore store) =>
        new MainViewModel(store).ExpandedBetween(new DateTime(2026, 10, 1), new DateTime(2026, 11, 1))
            .OrderBy(e => e.Start).Select(e => (e.Title, e.Start)).ToList();

    [Fact]
    [Trait("TestCase", "T05")]
    public void T05_AMovedOccurrenceReplacesTheGeneratedOne()
    {
        var store = StoreWithSeries();
        store.UpsertEvent(new CalendarEvent("x", "c1", "Spostata", new(2026, 10, 6, 14, 0, 0), new(2026, 10, 6, 15, 0, 0))
        {
            SeriesMasterId = "m",
            OriginalStart = new(2026, 10, 6, 9, 0, 0),
        });

        var shown = Shown(store);

        Assert.Equal(5, shown.Count);
        Assert.Contains(("Spostata", new DateTime(2026, 10, 6, 14, 0, 0)), shown);
        Assert.DoesNotContain(shown, e => e.Start == new DateTime(2026, 10, 6, 9, 0, 0));
        Assert.Equal(4, shown.Count(e => e.Title == "Serie"));
    }

    [Fact]
    [Trait("TestCase", "T13")]
    public void T13_ACancelledOccurrenceDisappearsAndTheOthersStay()
    {
        var store = StoreWithSeries();
        store.UpsertEvent(new CalendarEvent("y", "c1", "Serie", new(2026, 10, 8, 9, 0, 0), new(2026, 10, 8, 10, 0, 0))
        {
            SeriesMasterId = "m",
            OriginalStart = new(2026, 10, 8, 9, 0, 0),
            IsCancelled = true,
        });

        var shown = Shown(store);

        Assert.Equal(
            [new DateTime(2026, 10, 5, 9, 0, 0), new DateTime(2026, 10, 6, 9, 0, 0), new DateTime(2026, 10, 7, 9, 0, 0), new DateTime(2026, 10, 9, 9, 0, 0)],
            shown.Select(e => e.Start).ToArray());
    }

    [Fact]
    [Trait("TestCase", "T13")]
    public void T13_DeletingTheSeriesAlsoRemovesItsModifiedOccurrences()
    {
        var store = StoreWithSeries();
        store.UpsertEvent(new CalendarEvent("x", "c1", "Spostata", new(2026, 10, 6, 14, 0, 0), new(2026, 10, 6, 15, 0, 0))
        {
            SeriesMasterId = "m",
            OriginalStart = new(2026, 10, 6, 9, 0, 0),
        });

        store.DeleteSeries("m");

        Assert.Empty(Shown(store));
    }
}
