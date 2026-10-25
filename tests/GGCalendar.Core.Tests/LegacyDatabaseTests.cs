using System.Diagnostics;
using GGCalendar.App.ViewModels;
using GGCalendar.Core.Models;
using GGCalendar.Data;
using Microsoft.Data.Sqlite;
using Xunit.Abstractions;

namespace GGCalendar.Core.Tests;

/// <summary>Databases of the versions already released (T15) and a calendar with thousands of events (T24). They need no Google and no installed data.</summary>
public class LegacyDatabaseTests(ITestOutputHelper output)
{
    private static readonly DateTime Day = new(2026, 10, 14);

    [Theory]
    [Trait("TestCase", "T15")]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void T15_ADatabaseOfAnOlderVersionOpensWithEverythingInIt(int version)
    {
        var path = Path.Combine(Path.GetTempPath(), $"ggcal-legacy-{Guid.NewGuid():N}.db");
        try
        {
            var first = new CalendarStore(path);
            first.UpsertCalendar(new CalendarInfo("local-personale", "Locale", "Personale", "#1A73E8"));
            first.UpsertCalendar(new CalendarInfo("me@example.com|me@example.com", "me@example.com", "me@example.com", "#9fe1e7"));
            first.UpsertEvent(new CalendarEvent("timed", "local-personale", "Riunione", Day.AddHours(10), Day.AddHours(11)));
            first.UpsertEvent(new CalendarEvent("allday", "local-personale", "Giornata", Day, Day.AddDays(1), true));
            first.UpsertEvent(new CalendarEvent("series", "local-personale", "Serie", Day.AddHours(9), Day.AddHours(10)) { Recurrence = "FREQ=DAILY;COUNT=5" });
            first.UpsertEvent(new CalendarEvent("moved", "local-personale", "Spostata", Day.AddDays(1).AddHours(14), Day.AddDays(1).AddHours(15))
            {
                SeriesMasterId = "series", OriginalStart = Day.AddDays(1).AddHours(9),
            });
            var remote = new CalendarEvent("remote", "me@example.com|me@example.com", "Da inviare", Day.AddHours(12), Day.AddHours(13));
            first.UpsertEvent(remote);
            first.Enqueue("remote", remote.CalendarId, OutboxOperation.Create, remote);

            // Back to the layout of an older version: its outbox has no "base" column.
            using (var c = new SqliteConnection($"Data Source={path};Pooling=False"))
            {
                c.Open();
                using var cmd = c.CreateCommand();
                cmd.CommandText = $"ALTER TABLE outbox DROP COLUMN base; PRAGMA user_version = {version};";
                cmd.ExecuteNonQuery();
            }

            var store = new CalendarStore(path);
            var vm = new MainViewModel(store);

            Assert.Equal(2, vm.Calendars.Count);
            Assert.Equal(5, vm.AllEvents.Count);
            Assert.Equal("remote", Assert.Single(store.GetPending()).EventId);
            var shown = vm.ExpandedBetween(Day, Day.AddDays(7)).OrderBy(e => e.Start).Select(e => (e.Title, e.Start)).ToList();
            Assert.Contains(("Riunione", Day.AddHours(10)), shown);
            Assert.Contains(("Giornata", Day), shown);
            Assert.Contains(("Da inviare", Day.AddHours(12)), shown);
            Assert.Contains(("Spostata", Day.AddDays(1).AddHours(14)), shown);
            Assert.Equal(4, shown.Count(e => e.Title == "Serie")); // 5 occurrences, one of them moved
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var f in Directory.GetFiles(Path.GetTempPath(), Path.GetFileName(path) + "*")) File.Delete(f);
        }
    }

    [Fact]
    [Trait("TestCase", "T24")]
    public void T24_ACalendarWithThousandsOfEventsOpensAndChangesViewInAcceptableTime()
    {
        var store = new CalendarStore(":memory:");
        store.UpsertCalendar(new CalendarInfo("c1", "Locale", "Personale", "#1A73E8"));
        var from = new DateTime(2025, 1, 1);
        for (var i = 0; i < 4000; i++)
        {
            var start = from.AddDays(i % 900).AddHours(7 + i % 12);
            store.UpsertEvent(new CalendarEvent($"e{i}", "c1", $"Evento {i}", start, start.AddMinutes(30 + i % 90)));
        }

        for (var i = 0; i < 700; i++)
            store.UpsertEvent(new CalendarEvent($"d{i}", "c1", $"Giorno {i}", from.AddDays(i), from.AddDays(i + 1 + i % 3), true));
        for (var i = 0; i < 300; i++)
        {
            var start = from.AddDays(i % 200).AddHours(8 + i % 8);
            var rule = (i % 3) switch { 0 => "FREQ=DAILY", 1 => "FREQ=WEEKLY;BYDAY=MO,WE", _ => "FREQ=MONTHLY" };
            store.UpsertEvent(new CalendarEvent($"s{i}", "c1", $"Serie {i}", start, start.AddHours(1)) { Recurrence = rule });
        }

        var clock = Stopwatch.StartNew();
        var vm = new MainViewModel(store);
        var opened = clock.Elapsed;
        output.WriteLine($"Apertura con {vm.AllEvents.Count} eventi: {opened.TotalMilliseconds:F0} ms");
        Assert.Equal(5000, vm.AllEvents.Count);
        Assert.True(opened < TimeSpan.FromSeconds(10), $"Apertura troppo lenta: {opened.TotalSeconds:F1} s.");

        var slowest = TimeSpan.Zero;
        foreach (var view in new[] { 0, 1, 2, 3, 4, 5 })
        {
            foreach (var month in new[] { 0, 6, 12, 18 })
            {
                clock.Restart();
                vm.ViewModeIndex = view;
                vm.SelectedDate = from.AddMonths(month).AddDays(10);
                var took = clock.Elapsed;
                if (took > slowest) slowest = took;
                Assert.True(vm.VisibleEvents.Count > 0, $"Vista {view}, mese +{month}: nessun evento visibile.");
            }
        }

        output.WriteLine($"Cambio di vista più lento: {slowest.TotalMilliseconds:F0} ms");
        Assert.True(slowest < TimeSpan.FromSeconds(5), $"Un cambio di vista ha richiesto {slowest.TotalSeconds:F1} s.");
    }
}
