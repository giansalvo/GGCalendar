using System.Diagnostics;
using GGCalendar.Core.Models;
using GGCalendar.Core.Search;

namespace GGCalendar.Core.Tests;

/// <summary>The search of events (issue #8): the text, the boxes of the detailed panel, the dates, the series, the days of an event, the limits.</summary>
public class EventSearchTests
{
    private static readonly DateTime Today = new(2026, 10, 25);

    private static CalendarEvent Ev(string id, string title, DateTime start, double hours = 1, string description = "", string location = "",
        string calendar = "c1", params string[] guests) =>
        new(id, calendar, title, start, start.AddHours(hours), Location: location, Description: description)
        {
            Attendees = guests.Select(g => new EventAttendee(g)).ToList(),
        };

    private static SearchResult Find(IEnumerable<CalendarEvent> events, SearchQuery q) => EventSearch.Search(events, q, Today);

    private static string[] Ids(SearchResult r) => r.Hits.Select(h => h.Event.Id).Distinct().ToArray();

    // ---- the text ----

    [Theory] // T167
    [InlineData("Città", "citta")]
    [InlineData("CITTÀ", "citta")]
    [InlineData("Straße Müller", "straße muller")]
    [InlineData("  uno   due ", "  uno   due ")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void NormalizeLowersAndRemovesAccents(string? text, string expected) => Assert.Equal(expected, EventSearch.Normalize(text));

    [Fact] // T167
    public void WordsAreSplitOnAnySpace() => Assert.Equal(["uno", "due", "tre"], EventSearch.Words("  Uno\tdue\n  TRE "));

    [Fact] // T167
    public void ShortTextAlone_IsNotASearch()
    {
        var events = new[] { Ev("a", "Alfa", Today) };
        Assert.False(EventSearch.HasCriteria(new SearchQuery { Text = "a" }));
        Assert.False(EventSearch.HasCriteria(new SearchQuery { Text = "  " }));
        Assert.Empty(Find(events, new SearchQuery { Text = "a" }).Hits);
        Assert.True(EventSearch.HasCriteria(new SearchQuery { Text = "al" }));
        Assert.True(EventSearch.HasCriteria(new SearchQuery { Who = "x" }));
        Assert.True(EventSearch.HasCriteria(new SearchQuery { From = Today }));
    }

    [Fact] // T166
    public void FindsInTitleDescriptionLocationAndGuests_AndSaysWhere()
    {
        var events = new[]
        {
            Ev("t", "Riunione budget", Today),
            Ev("d", "Altro", Today, description: "Parlare del budget"),
            Ev("l", "Pranzo", Today, location: "Sala budget"),
            Ev("g", "Cena", Today, guests: "budget@example.com"),
            Ev("n", "Niente", Today, description: "nulla"),
        };
        var r = Find(events, new SearchQuery { Text = "budget" });
        Assert.Equal(["d", "g", "l", "t"], Ids(r).Order());
        Assert.Equal([SearchField.Title], r.Hits.Single(h => h.Event.Id == "t").Fields);
        Assert.Equal([SearchField.Description], r.Hits.Single(h => h.Event.Id == "d").Fields);
        Assert.Equal([SearchField.Location], r.Hits.Single(h => h.Event.Id == "l").Fields);
        Assert.Equal([SearchField.Guests], r.Hits.Single(h => h.Event.Id == "g").Fields);
    }

    [Fact] // T166
    public void SeveralFieldsAreAllReported()
    {
        var r = Find([Ev("a", "Alfa", Today, description: "progetto alfa", location: "Roma")], new SearchQuery { Text = "alfa" });
        Assert.Equal([SearchField.Title, SearchField.Description], r.Hits.Single().Fields);
    }

    [Fact] // T167
    public void IgnoresCaseAndAccents()
    {
        var events = new[] { Ev("a", "Gita in CITTÀ", Today), Ev("b", "Altro", Today, description: "citta vecchia") };
        Assert.Equal(["a", "b"], Ids(Find(events, new SearchQuery { Text = "Città" })).Order());
        Assert.Equal(["a", "b"], Ids(Find(events, new SearchQuery { Text = "CITTA" })).Order());
    }

    [Fact] // T167: every word must be found, in any field and in any order
    public void SeveralWordsMustAllBeFound()
    {
        var events = new[]
        {
            Ev("a", "Riunione Alfa", Today, description: "budget"),
            Ev("b", "Riunione Beta", Today, description: "budget"),
            Ev("c", "Alfa", Today, location: "Roma", guests: "mario@example.com"),
        };
        Assert.Equal(["a"], Ids(Find(events, new SearchQuery { Text = "alfa budget" })));
        Assert.Equal(["a"], Ids(Find(events, new SearchQuery { Text = "budget alfa" })));
        Assert.Equal(["c"], Ids(Find(events, new SearchQuery { Text = "alfa roma mario" })));
        Assert.Empty(Ids(Find(events, new SearchQuery { Text = "alfa nonesiste" })));
    }

    [Theory] // T167: nothing is a pattern
    [InlineData("100%")]
    [InlineData("a*b")]
    [InlineData("\"citato\"")]
    [InlineData("[x]")]
    [InlineData("a_b")]
    public void SpecialCharactersAreSearchedAsTheyAre(string text)
    {
        var events = new[] { Ev("hit", "Caso " + text + " fine", Today), Ev("miss", "Caso normale", Today) };
        Assert.Equal(["hit"], Ids(Find(events, new SearchQuery { Text = text })));
    }

    [Fact]
    public void EmptyAndMissingFieldsAreHarmless()
    {
        var events = new[] { new CalendarEvent("a", "c1", "", Today, Today.AddHours(1)), Ev("b", "Beta", Today, guests: "") };
        Assert.Empty(Find(events, new SearchQuery { Text = "zzz" }).Hits);
        Assert.Equal(["b"], Ids(Find(events, new SearchQuery { Text = "beta" })));
    }

    // ---- the boxes of the detailed panel ----

    [Fact] // T177
    public void WhatLooksInTitleAndDescriptionOnly()
    {
        var events = new[] { Ev("t", "Alfa", Today), Ev("d", "x", Today, description: "alfa"), Ev("l", "x", Today, location: "alfa"), Ev("g", "x", Today, guests: "alfa@e.it") };
        Assert.Equal(["d", "t"], Ids(Find(events, new SearchQuery { What = "alfa" })).Order());
    }

    [Fact] // T177
    public void WhoLooksInGuestsOnly()
    {
        var events = new[] { Ev("g", "x", Today, guests: "mario.rossi@example.com"), Ev("t", "mario", Today) };
        Assert.Equal(["g"], Ids(Find(events, new SearchQuery { Who = "Rossi" })));
        Assert.Equal(["g"], Ids(Find(events, new SearchQuery { Who = "mario example" })));
    }

    [Fact] // T177
    public void WhereLooksInLocationOnly()
    {
        var events = new[] { Ev("l", "x", Today, location: "Via Roma 1"), Ev("t", "Roma", Today) };
        Assert.Equal(["l"], Ids(Find(events, new SearchQuery { Where = "roma" })));
    }

    [Fact] // T177
    public void NotExcludesWhatIsFoundAnywhere()
    {
        var events = new[]
        {
            Ev("a", "Riunione", Today), Ev("b", "Riunione", Today, description: "annullata"),
            Ev("c", "Riunione", Today, location: "annullata"), Ev("d", "Riunione", Today, guests: "annullata@e.it"),
        };
        Assert.Equal(["a"], Ids(Find(events, new SearchQuery { Text = "riunione", Not = "annullata" })));
        Assert.Equal(["a", "b", "c", "d"], Ids(Find(events, new SearchQuery { Text = "riunione" })).Order());
    }

    [Fact] // T177: the boxes are combined
    public void BoxesAreCombined()
    {
        var events = new[]
        {
            Ev("ok", "Alfa", Today, location: "Roma", guests: "mario@e.it"),
            Ev("noWhere", "Alfa", Today, location: "Milano", guests: "mario@e.it"),
            Ev("noWho", "Alfa", Today, location: "Roma", guests: "anna@e.it"),
            Ev("noWhat", "Beta", Today, location: "Roma", guests: "mario@e.it"),
        };
        Assert.Equal(["ok"], Ids(Find(events, new SearchQuery { What = "alfa", Who = "mario", Where = "roma" })));
        Assert.Equal(["ok"], Ids(Find(events, new SearchQuery { Text = "alfa", Who = "mario", Where = "roma", Not = "milano" })));
    }

    // ---- the calendars ----

    [Fact] // T168
    public void OnlyTheChosenCalendarsAreSearched()
    {
        var events = new[] { Ev("a", "Alfa", Today, calendar: "c1"), Ev("b", "Alfa", Today, calendar: "c2"), Ev("c", "Alfa", Today, calendar: "c3") };
        var q = new SearchQuery { Text = "alfa" };
        Assert.Equal(["a", "b", "c"], Ids(Find(events, q)).Order());
        Assert.Equal(["b"], Ids(Find(events, q with { CalendarIds = new HashSet<string> { "c2" } })));
        Assert.Equal(["a", "c"], Ids(Find(events, q with { CalendarIds = new HashSet<string> { "c1", "c3" } })).Order());
        Assert.Empty(Find(events, q with { CalendarIds = new HashSet<string>() }).Hits);
    }

    // ---- the dates ----

    [Fact] // T178
    public void DatesKeepTheEventsThatTouchTheRange()
    {
        var events = new[]
        {
            Ev("before", "x", new DateTime(2026, 5, 1, 10, 0, 0)),
            Ev("first", "x", new DateTime(2026, 5, 10, 22, 0, 0), hours: 5), // goes over midnight into the 11th
            Ev("inside", "x", new DateTime(2026, 5, 15, 10, 0, 0)),
            Ev("last", "x", new DateTime(2026, 5, 20, 23, 0, 0)),
            Ev("after", "x", new DateTime(2026, 5, 21, 0, 0, 0)),
        };
        var q = new SearchQuery { From = new DateTime(2026, 5, 11), To = new DateTime(2026, 5, 20) };
        Assert.Equal(["first", "inside", "last"], Ids(Find(events, q)).Order());
        Assert.Equal(["after", "first", "inside", "last"], Ids(Find(events, new SearchQuery { From = new DateTime(2026, 5, 11) })).Order());
        Assert.Empty(Find(events, new SearchQuery { To = new DateTime(2026, 5, 20) }).Hits); // from today: all of these are over
        Assert.Empty(Find(events, new SearchQuery { From = new DateTime(2026, 6, 1), To = new DateTime(2026, 5, 1) }).Hits); // end before start
    }

    // ---- days of an event ----

    [Fact] // T170
    public void AnEventOfSeveralDaysIsOnEachDayItCovers()
    {
        var e = Ev("m", "Congresso", new DateTime(2026, 11, 2, 18, 0, 0), hours: 74); // until the 5th at 20:00
        var r = Find([e], new SearchQuery { Text = "congresso" });
        Assert.Equal([new DateTime(2026, 11, 2), new DateTime(2026, 11, 3), new DateTime(2026, 11, 4), new DateTime(2026, 11, 5)], r.Hits.Select(h => h.Day).ToArray());
        Assert.Equal([1, 2, 3, 4], r.Hits.Select(h => h.DayNumber).ToArray());
        Assert.All(r.Hits, h => Assert.Equal(4, h.DayCount));
        Assert.True(r.Hits[0].IsFirstDay);
        Assert.True(r.Hits[3].IsLastDay);
        Assert.Equal(1, r.EventCount); // one event on four lines
    }

    [Fact] // T170
    public void AnEventEndingAtMidnightDoesNotSpillOverTheNextDay()
    {
        var e = new CalendarEvent("a", "c1", "Serata", new DateTime(2026, 11, 2, 20, 0, 0), new DateTime(2026, 11, 3, 0, 0, 0));
        Assert.Single(Find([e], new SearchQuery { Text = "serata" }).Hits);
    }

    [Fact] // T170
    public void AllDayEventsCoverTheirDaysWithAnExclusiveEnd()
    {
        var oneDay = new CalendarEvent("a", "c1", "Festa", new DateTime(2026, 11, 2), new DateTime(2026, 11, 3), IsAllDay: true);
        var threeDays = new CalendarEvent("b", "c1", "Ferie", new DateTime(2026, 11, 2), new DateTime(2026, 11, 5), IsAllDay: true);
        Assert.Single(Find([oneDay], new SearchQuery { Text = "festa" }).Hits);
        Assert.Equal(3, Find([threeDays], new SearchQuery { Text = "ferie" }).Hits.Count);
    }

    [Fact] // T170, T178: only the days inside the dates are lines
    public void DaysOutsideTheDatesAreNotListed()
    {
        var e = new CalendarEvent("b", "c1", "Ferie", new DateTime(2026, 11, 2), new DateTime(2026, 11, 7), IsAllDay: true);
        var r = Find([e], new SearchQuery { Text = "ferie", From = new DateTime(2026, 11, 4), To = new DateTime(2026, 11, 5) });
        Assert.Equal([new DateTime(2026, 11, 4), new DateTime(2026, 11, 5)], r.Hits.Select(h => h.Day).ToArray());
        Assert.Equal([3, 4], r.Hits.Select(h => h.DayNumber).ToArray());
    }

    [Fact] // T170: order of the lines
    public void LinesAreInOrderOfDayAllDayFirstThenStart()
    {
        var events = new[]
        {
            Ev("late", "x alfa", new DateTime(2026, 11, 2, 15, 0, 0)),
            Ev("early", "x alfa", new DateTime(2026, 11, 2, 9, 0, 0)),
            new CalendarEvent("day", "c1", "x alfa", new DateTime(2026, 11, 2), new DateTime(2026, 11, 3), IsAllDay: true),
        };
        Assert.Equal(["day", "early", "late"], Find(events, new SearchQuery { Text = "alfa" }).Hits.Select(h => h.Event.Id).ToArray());
    }

    // ---- series ----

    private static CalendarEvent Weekly(string id, string title, DateTime start, string rule = "FREQ=WEEKLY") =>
        Ev(id, title, start) with { Recurrence = rule };

    [Fact] // T169
    public void EveryOccurrenceOfASeriesIsALine()
    {
        var series = Weekly("s", "Palestra", new DateTime(2026, 11, 3, 18, 0, 0), "FREQ=WEEKLY;COUNT=5");
        var r = Find([series], new SearchQuery { Text = "palestra" });
        Assert.Equal(5, r.Hits.Count);
        Assert.Equal(5, r.EventCount);
        Assert.Equal(Enumerable.Range(0, 5).Select(i => new DateTime(2026, 11, 3).AddDays(7 * i)).ToArray(), r.Hits.Select(h => h.Day).ToArray());
    }

    [Fact] // T169: a series without an end is looked for from today to a year later
    public void ASeriesWithoutAnEndStopsAtTheHorizon()
    {
        var daily = Weekly("s", "Allenamento", new DateTime(2020, 1, 1, 7, 0, 0), "FREQ=DAILY");
        var r = Find([daily], new SearchQuery { Text = "allenamento" });
        Assert.Equal(new DateTime(2026, 10, 25), r.Hits.First().Day); // nothing before today
        Assert.Equal(new DateTime(2027, 10, 25), r.Hits.Last().Day);
        Assert.Equal(366, r.Hits.Count);
    }

    [Fact] // T169, T178: with dates, exactly those
    public void WithDatesTheSeriesIsExpandedInsideThem()
    {
        var daily = Weekly("s", "Allenamento", new DateTime(2020, 1, 1, 7, 0, 0), "FREQ=DAILY");
        var r = Find([daily], new SearchQuery { Text = "allenamento", From = new DateTime(2029, 1, 1), To = new DateTime(2029, 1, 10) });
        Assert.Equal(10, r.Hits.Count);
        Assert.Equal(10, Find([daily], new SearchQuery { Text = "allenamento", From = new DateTime(2020, 3, 1), To = new DateTime(2020, 3, 10) }).Hits.Count);
    }

    [Fact] // T169, T178: one date only
    public void OneDateOnly()
    {
        var daily = Weekly("s", "Allenamento", new DateTime(2020, 1, 1, 7, 0, 0), "FREQ=DAILY");
        var onlyFrom = Find([daily], new SearchQuery { Text = "allenamento", From = new DateTime(2026, 1, 1) });
        Assert.Equal(new DateTime(2026, 1, 1), onlyFrom.Hits.First().Day);
        Assert.Equal(new DateTime(2027, 1, 1), onlyFrom.Hits.Last().Day); // a year after the first day
        Assert.Equal(366, onlyFrom.Hits.Count);

        var onlyTo = Find([daily], new SearchQuery { Text = "allenamento", To = new DateTime(2026, 12, 31) });
        Assert.Equal(Today, onlyTo.Hits.First().Day); // without a first day: from today
        Assert.Equal(new DateTime(2026, 12, 31), onlyTo.Hits.Last().Day);
    }

    [Fact] // T169: a modified occurrence takes the place of its own, a cancelled one is not shown
    public void ModifiedAndCancelledOccurrences()
    {
        var series = Weekly("s", "Palestra", new DateTime(2026, 10, 6, 18, 0, 0), "FREQ=WEEKLY;COUNT=4");
        var moved = Ev("m", "Palestra spostata", new DateTime(2026, 10, 14, 19, 0, 0)) with { SeriesMasterId = "s", OriginalStart = new DateTime(2026, 10, 13, 18, 0, 0) };
        var cancelled = Ev("x", "Palestra", new DateTime(2026, 10, 20, 18, 0, 0)) with { SeriesMasterId = "s", OriginalStart = new DateTime(2026, 10, 20, 18, 0, 0), IsCancelled = true };
        var r = Find([series, moved, cancelled], new SearchQuery { Text = "palestra", From = new DateTime(2026, 10, 1) });
        Assert.Equal([new DateTime(2026, 10, 6), new DateTime(2026, 10, 14), new DateTime(2026, 10, 27)], r.Hits.Select(h => h.Day).ToArray());
        Assert.Contains(r.Hits, h => h.Event.Id == "m");
    }

    [Fact] // T169: a series that ended long ago is not found without dates
    public void ASeriesThatEndedBeforeTheHorizonIsNotListed()
    {
        var old = Weekly("s", "Corso", new DateTime(2020, 1, 7, 18, 0, 0), "FREQ=WEEKLY;COUNT=10");
        Assert.Empty(Find([old], new SearchQuery { Text = "corso" }).Hits); // and nothing before today is listed anyway
        Assert.Equal(10, Find([old], new SearchQuery { Text = "corso", From = new DateTime(2020, 1, 1), To = new DateTime(2020, 12, 31) }).Hits.Count);
    }

    // ---- without dates every plain event is found, however old ----

    [Fact] // T178: with no first day the search starts today
    public void WithoutAFirstDayNothingBeforeTodayIsListed()
    {
        var events = new[]
        {
            Ev("old", "Alfa", new DateTime(2015, 3, 1, 10, 0, 0)), Ev("yesterday", "Alfa", Today.AddDays(-1).AddHours(10)),
            Ev("today", "Alfa", Today.AddHours(8)), Ev("far", "Alfa", new DateTime(2040, 3, 1, 10, 0, 0)),
        };
        Assert.Equal(["far", "today"], Ids(Find(events, new SearchQuery { Text = "alfa" })).Order());
        Assert.Equal(["far", "old", "today", "yesterday"], Ids(Find(events, new SearchQuery { Text = "alfa", From = new DateTime(2000, 1, 1) })).Order());
    }

    [Fact] // T170, T178: an event that began before today is listed from today on
    public void AnEventInProgressIsListedFromToday()
    {
        var e = new CalendarEvent("m", "c1", "Congresso", Today.AddDays(-2).AddHours(9), Today.AddDays(1).AddHours(18));
        var r = Find([e], new SearchQuery { Text = "congresso" });
        Assert.Equal([Today, Today.AddDays(1)], r.Hits.Select(h => h.Day).ToArray());
        Assert.Equal([3, 4], r.Hits.Select(h => h.DayNumber).ToArray()); // the third and the fourth day of four
    }

    // ---- limit ----

    [Fact] // T173
    public void MoreLinesThanTheLimitAreCut_AndSaid()
    {
        var events = Enumerable.Range(0, 1500).Select(i => Ev("e" + i, "Alfa " + i, new DateTime(2027, 1, 1).AddMinutes(i))).ToList();
        var r = Find(events, new SearchQuery { Text = "alfa" });
        Assert.Equal(EventSearch.MaxRows, r.Hits.Count);
        Assert.True(r.Truncated);
        Assert.Equal(EventSearch.MaxRows, r.EventCount);
        Assert.False(Find(events.Take(1000).ToList(), new SearchQuery { Text = "alfa" }).Truncated);
    }

    // ---- the text shown under a result ----

    [Fact] // T166
    public void SnippetIsAroundTheFirstWord_WithDotsWhereCut()
    {
        var text = string.Concat(Enumerable.Repeat("parola ", 30)) + "tesoro " + string.Concat(Enumerable.Repeat("altro ", 30));
        var s = EventSearch.Snippet(text, ["tesoro"]);
        Assert.Contains("tesoro", s);
        Assert.StartsWith("…", s);
        Assert.EndsWith("…", s);
        Assert.True(s.Length <= 94);

        Assert.Equal("tesoro nascosto", EventSearch.Snippet("tesoro nascosto", ["tesoro"]));
        var atEnd = EventSearch.Snippet(string.Concat(Enumerable.Repeat("x ", 80)) + "tesoro", ["tesoro"]);
        Assert.StartsWith("…", atEnd);
        Assert.EndsWith("tesoro", atEnd);
        Assert.Equal("", EventSearch.Snippet("", ["x"]));
    }

    [Fact] // T166
    public void SnippetOfAnEventShowsTheDescriptionElseTheGuests()
    {
        var withDescription = Find([Ev("a", "Alfa", Today, description: "Portare il libretto e un documento")], new SearchQuery { Text = "libretto" });
        Assert.Contains("libretto", withDescription.Hits.Single().Snippet);
        var withGuest = Find([Ev("b", "Beta", Today, guests: ["mario@e.it", "anna@e.it"])], new SearchQuery { Text = "anna" });
        Assert.Equal("anna@e.it", withGuest.Hits.Single().Snippet);
        Assert.Equal("", Find([Ev("c", "Gamma", Today)], new SearchQuery { Text = "gamma" }).Hits.Single().Snippet);
    }

    // ---- speed ----

    [Fact] // T176
    public void TwentyThousandEventsAreSearchedInUnderASecond()
    {
        var text = string.Concat(Enumerable.Repeat("Lorem ipsum dolor sit amet città ", 25));
        var events = Enumerable.Range(0, 20000).Select(i =>
            Ev("e" + i, "Evento " + i, new DateTime(2026, 1, 1).AddHours(i), description: text + i, location: "Via Roma " + i, guests: ["u" + i + "@example.com"])).ToList();
        var clock = Stopwatch.StartNew();
        var r = Find(events, new SearchQuery { Text = "citta 19999" });
        clock.Stop();
        Assert.Equal(["e19999"], Ids(r));
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(1), $"{clock.Elapsed.TotalMilliseconds:F0} ms");
    }
}
