using System.Security.Cryptography;
using System.Text;
using GGCalendar.Core.Ics;
using GGCalendar.Core.Models;
using GGCalendar.Core.Recurrence;

namespace GGCalendar.Core.Subscriptions;

/// <summary>Why a subscription could not be added or refreshed. The name is the key of the message (<c>subscription.error.&lt;name&gt;</c>).</summary>
public enum SubscriptionProblem
{
    InvalidUrl, NotHttps, HasCredentials, Duplicate, TooManySubscriptions,
    NoNetwork, Timeout, Tls, NotFound, AccessDenied, RateLimited, ServerError, HttpError,
    Empty, TooLarge, TooManyRedirects, RedirectNotHttps, NotACalendar, TooManyEvents, Unreadable,
}

public static class SubscriptionLimits
{
    public const int MaxSubscriptions = 10;
    public const int MaxEvents = 5000;
}

/// <summary>The days of a public calendar that are kept: both ends included, an empty end is unlimited.</summary>
public sealed record SubscriptionRange(DateTime? From, DateTime? To)
{
    public static SubscriptionRange All { get; } = new(null, null);

    public bool IsAll => From is null && To is null;

    /// <summary>The events that have at least one day inside the range; a series is kept when any of its occurrences is.</summary>
    public IReadOnlyList<CalendarEvent> Filter(IReadOnlyList<CalendarEvent> events)
    {
        if (IsAll) return events;
        var from = From?.Date ?? DateTime.MinValue;
        var to = To is { } t ? t.Date.AddDays(1) : DateTime.MaxValue;

        var keptMasters = new HashSet<string>();
        var result = new List<CalendarEvent>();
        foreach (var e in events.Where(e => e.SeriesMasterId is null))
        {
            var inside = e.Recurrence is null
                ? Overlaps(e, from, to)
                : RecurrenceExpander.Expand(e, from, to).Any();
            if (!inside) continue;
            result.Add(e);
            keptMasters.Add(e.Id);
        }

        result.AddRange(events.Where(e => e.SeriesMasterId is { } m && keptMasters.Contains(m)));
        return result;
    }

    private static bool Overlaps(CalendarEvent e, DateTime from, DateTime to) =>
        e.End > e.Start ? e.Start < to && e.End > from : e.Start >= from && e.Start < to;
}

/// <summary>What a downloaded file holds, ready to be stored: the events inside the range with stable ids, and what was left out.</summary>
public sealed record SubscriptionSnapshot(
    IReadOnlyList<CalendarEvent> Events, IcsReport Report, string Hash, int EventsBeforeRange,
    DateTime? FirstStart, DateTime? LastEnd, SubscriptionProblem? Problem)
{
    public bool IsOk => Problem is null;
}

public static class SubscriptionContent
{
    /// <summary>Reads the file, applies the range and the limit of events. Reading is done with the options of the .ics import (no reminders).</summary>
    public static SubscriptionSnapshot Process(byte[] bytes, string calendarId, SubscriptionRange range, IcsImportOptions? options = null)
    {
        var import = IcsParser.AnalyzeFile(bytes, calendarId, (options ?? new IcsImportOptions()) with { ImportAlarms = false });

        if (import.Report.Fatal is { } fatal)
            return Failed(import.Report, "", fatal == IcsIssueCode.TooLarge ? SubscriptionProblem.TooLarge : SubscriptionProblem.NotACalendar);

        var all = import.Events;
        var kept = range.Filter(all);
        if (kept.Count > SubscriptionLimits.MaxEvents) return Failed(import.Report, "", SubscriptionProblem.TooManyEvents, all.Count);

        var events = AssignIds(kept, calendarId);
        var hash = ContentHash(events);
        var first = events.Count > 0 ? events.Min(e => e.Start) : (DateTime?)null;
        var last = events.Count > 0 ? events.Where(e => e.Recurrence is null).Select(e => e.End).DefaultIfEmpty().Max() : (DateTime?)null;
        return new SubscriptionSnapshot(events, import.Report, hash, all.Count, first, last == default ? null : last, null);
    }

    private static SubscriptionSnapshot Failed(IcsReport report, string hash, SubscriptionProblem problem, int count = 0) =>
        new([], report, hash, count, null, null, problem);

    /// <summary>
    /// The parser gives each event a new random id; here the id comes from the UID (and the original start of a modified
    /// occurrence), so a refresh finds the same event again and writes only what changed.
    /// </summary>
    public static IReadOnlyList<CalendarEvent> AssignIds(IReadOnlyList<CalendarEvent> events, string calendarId)
    {
        // The feeds of Google list the events in a different order every time: the order of the file must not decide the ids.
        events = events.OrderBy(e => e.SeriesMasterId is null ? 0 : 1).ThenBy(e => e.IcsUid ?? "", StringComparer.Ordinal)
            .ThenBy(e => e.OriginalStart ?? DateTime.MinValue).ThenBy(e => e.Start).ThenBy(e => e.Title, StringComparer.Ordinal).ToList();
        var map = new Dictionary<string, string>();
        var used = new HashSet<string>();
        foreach (var e in events)
        {
            var key = (e.IcsUid ?? e.Id) + "#" + (e.SeriesMasterId is null ? "" : e.OriginalStart?.ToString("O") ?? "");
            var id = Make(calendarId, key);
            for (var n = 2; !used.Add(id); n++) id = Make(calendarId, key + "~" + n);
            map[e.Id] = id;
        }

        return events.Select(e => e with
        {
            Id = map[e.Id],
            SeriesMasterId = e.SeriesMasterId is { } m && map.TryGetValue(m, out var mapped) ? mapped : e.SeriesMasterId,
        }).ToList();
    }

    /// <summary>
    /// A fingerprint of what the events say, not of the bytes of the file: the public feeds of Google carry the time of the download
    /// (DTSTAMP) and list the events in another order every time, so the same calendar never gives the same bytes twice.
    /// </summary>
    public static string ContentHash(IReadOnlyList<CalendarEvent> events)
    {
        var text = string.Join('\n', events.OrderBy(e => e.Id, StringComparer.Ordinal).Select(e => e.Id + "|" + System.Text.Json.JsonSerializer.Serialize(e)));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }

    private static string Make(string calendarId, string key) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(calendarId + "|" + key)))[..32].ToLowerInvariant();

    public static SubscriptionProblem? From(FetchError error) => error switch
    {
        FetchError.InvalidUrl => SubscriptionProblem.InvalidUrl,
        FetchError.NoNetwork => SubscriptionProblem.NoNetwork,
        FetchError.Timeout => SubscriptionProblem.Timeout,
        FetchError.Tls => SubscriptionProblem.Tls,
        FetchError.NotFound => SubscriptionProblem.NotFound,
        FetchError.AccessDenied => SubscriptionProblem.AccessDenied,
        FetchError.RateLimited => SubscriptionProblem.RateLimited,
        FetchError.ServerError => SubscriptionProblem.ServerError,
        FetchError.Empty => SubscriptionProblem.Empty,
        FetchError.TooLarge => SubscriptionProblem.TooLarge,
        FetchError.TooManyRedirects => SubscriptionProblem.TooManyRedirects,
        FetchError.RedirectNotHttps => SubscriptionProblem.RedirectNotHttps,
        _ => SubscriptionProblem.HttpError,
    };

    public static SubscriptionProblem From(UrlProblem problem) => problem switch
    {
        UrlProblem.NotHttps => SubscriptionProblem.NotHttps,
        UrlProblem.HasCredentials => SubscriptionProblem.HasCredentials,
        _ => SubscriptionProblem.InvalidUrl,
    };
}
