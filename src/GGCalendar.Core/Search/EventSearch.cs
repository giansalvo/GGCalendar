using System.Globalization;
using System.Text;
using GGCalendar.Core.Models;
using GGCalendar.Core.Recurrence;

namespace GGCalendar.Core.Search;

/// <summary>Where the text was found.</summary>
public enum SearchField { Title, Description, Location, Guests }

/// <summary>
/// What to look for. <see cref="Text"/> (the simple box) looks in the title, the description, the location and the guests; the other
/// boxes of the detailed panel narrow it down. All the words of a box must be found (AND), the boxes are combined (AND).
/// </summary>
public sealed record SearchQuery
{
    /// <summary>The words of the simple box: shorter than <see cref="EventSearch.MinTextLength"/> characters it is not used.</summary>
    public string Text { get; init; } = "";

    /// <summary>Words in the title or in the description.</summary>
    public string What { get; init; } = "";

    /// <summary>Words in the title.</summary>
    public string Title { get; init; } = "";

    /// <summary>Words in the description.</summary>
    public string Description { get; init; } = "";

    /// <summary>Words in the guests (their email addresses).</summary>
    public string Who { get; init; } = "";

    /// <summary>Words in the location.</summary>
    public string Where { get; init; } = "";

    /// <summary>Words that must not be found anywhere.</summary>
    public string Not { get; init; } = "";

    /// <summary>First and last day wanted (the whole day); null = no limit.</summary>
    public DateTime? From { get; init; }

    public DateTime? To { get; init; }

    /// <summary>The calendars to look in; null = all of them.</summary>
    public IReadOnlySet<string>? CalendarIds { get; init; }
}

/// <summary>One line of the result: an event (or an occurrence of a series) on one of the days it covers.</summary>
public sealed record SearchHit(CalendarEvent Event, DateTime Day, int DayNumber, int DayCount, IReadOnlyList<SearchField> Fields, string Snippet)
{
    public bool IsFirstDay => DayNumber == 1;
    public bool IsLastDay => DayNumber == DayCount;
}

/// <summary><see cref="EventCount"/> counts events and occurrences, not lines: an event of three days is one event on three lines.</summary>
public sealed record SearchResult(IReadOnlyList<SearchHit> Hits, int EventCount, bool Truncated)
{
    public static SearchResult Empty { get; } = new([], 0, false);
}

public static class EventSearch
{
    public const int MinTextLength = 2;

    /// <summary>The most lines a search returns.</summary>
    public const int MaxRows = 1000;

    /// <summary>How far after the first day the occurrences of a series are looked for when there is no last day.</summary>
    public const int HorizonYears = 1;

    /// <summary>Lower case, without accents (so "citta" finds "Città").</summary>
    public static string Normalize(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var ascii = true;
        foreach (var c in text)
            if (c > 127) { ascii = false; break; }
        if (ascii) return text.ToLowerInvariant();

        var sb = new StringBuilder(text.Length);
        foreach (var c in text.Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(char.ToLowerInvariant(c));
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    public static string[] Words(string? text) =>
        Normalize(text).Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries);

    /// <summary>True when the query has something to look for (a short simple text alone is not enough).</summary>
    public static bool HasCriteria(SearchQuery q) =>
        Words(q.Text) is { Length: > 0 } && q.Text.Trim().Length >= MinTextLength
        || Words(q.What).Length + Words(q.Title).Length + Words(q.Description).Length + Words(q.Who).Length + Words(q.Where).Length + Words(q.Not).Length > 0
        || q.From is not null || q.To is not null;

    public static SearchResult Search(IEnumerable<CalendarEvent> events, SearchQuery q, DateTime today, int maxRows = MaxRows)
    {
        if (!HasCriteria(q)) return SearchResult.Empty;
        var list = events as IReadOnlyList<CalendarEvent> ?? events.ToList();
        var general = q.Text.Trim().Length >= MinTextLength ? Words(q.Text) : [];
        var what = Words(q.What);
        var inTitle = Words(q.Title);
        var inDescription = Words(q.Description);
        var who = Words(q.Who);
        var where = Words(q.Where);
        var not = Words(q.Not);

        // The days wanted: from the first day given, or today (nothing before it is listed), to the last day given, or without an end.
        // A series is expanded only inside a window: from the first day to the last day given, or a year after the first.
        var firstDay = (q.From ?? today).Date;
        var lastDay = q.To?.Date;
        var windowFrom = firstDay;
        var windowTo = (lastDay ?? firstDay.AddYears(HorizonYears)).AddDays(1);

        // A modified occurrence takes the place of the one of its series; a cancelled one is not shown.
        var moved = list.Where(e => e.SeriesMasterId is not null && e.OriginalStart is not null)
            .GroupBy(e => e.SeriesMasterId!).ToDictionary(g => g.Key, g => g.Select(e => e.OriginalStart!.Value).ToList());

        var rows = new List<SearchHit>();
        var counted = new HashSet<(string, DateTime)>();
        foreach (var e in list)
        {
            if (e.IsCancelled) continue;
            if (q.CalendarIds is not null && !q.CalendarIds.Contains(e.CalendarId)) continue;
            if (!Matches(e, new Terms(general, what, inTitle, inDescription, who, where, not), out var fields, out var snippet)) continue;

            IEnumerable<CalendarEvent> occurrences;
            if (!string.IsNullOrEmpty(e.Recurrence))
            {
                var master = moved.TryGetValue(e.Id, out var starts) ? e with { ExcludedStarts = [.. e.ExcludedStarts, .. starts] } : e;
                occurrences = RecurrenceExpander.Expand(master, windowFrom, windowTo);
            }
            else
            {
                occurrences = [e];
            }

            foreach (var o in occurrences)
            {
                var days = DaysOf(o);
                var shown = 0;
                for (var i = 0; i < days.Count; i++)
                {
                    var day = days[i];
                    if (day < firstDay) continue;
                    if (lastDay is { } b && day > b) continue;
                    rows.Add(new SearchHit(o, day, i + 1, days.Count, fields, snippet));
                    shown++;
                }

                if (shown > 0) counted.Add((o.Id, o.Start));
            }
        }

        var ordered = rows.OrderBy(r => r.Day).ThenByDescending(r => r.Event.IsAllDay).ThenBy(r => r.Event.Start)
            .ThenBy(r => r.Event.Title, StringComparer.CurrentCultureIgnoreCase).ToList();
        var truncated = ordered.Count > maxRows;
        if (truncated) ordered = ordered.Take(maxRows).ToList();
        var eventCount = truncated ? ordered.Select(r => (r.Event.Id, r.Event.Start)).Distinct().Count() : counted.Count;
        return new SearchResult(ordered, eventCount, truncated);
    }

    /// <summary>The days an event covers; the end of an all-day event is the day after its last one.</summary>
    public static IReadOnlyList<DateTime> DaysOf(CalendarEvent e)
    {
        var first = e.Start.Date;
        var last = e.IsAllDay || (e.End.TimeOfDay == TimeSpan.Zero && e.End > e.Start) ? e.End.Date.AddDays(-1) : e.End.Date;
        if (last < first) last = first;
        var days = new List<DateTime>();
        for (var d = first; d <= last && days.Count < 3660; d = d.AddDays(1)) days.Add(d);
        return days;
    }

    private sealed record Terms(string[] General, string[] What, string[] Title, string[] Description, string[] Who, string[] Where, string[] Not);

    private static bool Matches(CalendarEvent e, Terms t, out IReadOnlyList<SearchField> fields, out string snippet)
    {
        fields = [];
        snippet = "";
        var title = Normalize(e.Title);
        // The text as the user reads it: the tags and the addresses hidden in them are not searched.
        var descriptionText = DescriptionText.ToPlainText(e.Description);
        var description = Normalize(descriptionText);
        var location = Normalize(e.Location);
        var guests = Normalize(string.Join(", ", e.Attendees.Select(a => a.Email)));
        var all = new[] { title, description, location, guests };
        static bool Has(string text, string word) => text.Contains(word, StringComparison.Ordinal);

        if (!t.General.All(w => all.Any(x => Has(x, w)))) return false;
        if (!t.What.All(w => Has(title, w) || Has(description, w))) return false;
        if (!t.Title.All(w => Has(title, w))) return false;
        if (!t.Description.All(w => Has(description, w))) return false;
        if (!t.Who.All(w => Has(guests, w))) return false;
        if (!t.Where.All(w => Has(location, w))) return false;
        if (t.Not.Any(w => all.Any(x => Has(x, w)))) return false;

        // Where it was found: in each field, the words that look in it.
        var inTitle = t.General.Concat(t.What).Concat(t.Title).Any(w => Has(title, w));
        var inDescription = t.General.Concat(t.What).Concat(t.Description).Any(w => Has(description, w));
        var inLocation = t.General.Concat(t.Where).Any(w => Has(location, w));
        var inGuests = t.General.Concat(t.Who).Any(w => Has(guests, w));
        var found = new List<SearchField>();
        if (inTitle) found.Add(SearchField.Title);
        if (inDescription) found.Add(SearchField.Description);
        if (inLocation) found.Add(SearchField.Location);
        if (inGuests) found.Add(SearchField.Guests);
        fields = found;
        if (inDescription) snippet = Snippet(descriptionText, t.General.Concat(t.What).Concat(t.Description).ToArray());
        else if (inGuests)
        {
            var words = t.General.Concat(t.Who).ToArray();
            snippet = string.Join(", ", e.Attendees.Select(a => a.Email).Where(m => words.Any(w => Has(Normalize(m), w))));
        }

        return true;
    }

    /// <summary>About 90 characters of the text around the first word found, with "…" where it is cut.</summary>
    public static string Snippet(string text, string[] words)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var flat = text.Replace('\r', ' ').Replace('\n', ' ');
        var n = Normalize(flat);
        var at = n.Length == flat.Length
            ? words.Select(w => n.IndexOf(w, StringComparison.Ordinal)).Where(i => i >= 0).DefaultIfEmpty(0).Min()
            : 0;
        var from = Math.Max(0, at - 30);
        var length = Math.Min(flat.Length - from, 90);
        var s = flat.Substring(from, length).Trim();
        return (from > 0 ? "…" : "") + s + (from + length < flat.Length ? "…" : "");
    }
}
