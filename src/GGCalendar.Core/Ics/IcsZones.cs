using System.Globalization;

namespace GGCalendar.Core.Ics;

/// <summary>A time zone as the reader needs it: the UTC offset in force at a wall-clock time.</summary>
internal sealed class IcsZone(Func<DateTime, TimeSpan> offsetAt)
{
    public TimeSpan OffsetAt(DateTime wall) => offsetAt(DateTime.SpecifyKind(wall, DateTimeKind.Unspecified));

    public static IcsZone Utc { get; } = new(_ => TimeSpan.Zero);

    public static IcsZone Of(TimeZoneInfo zone) => new(wall => zone.GetUtcOffset(wall));
}

/// <summary>Finds the zone named by a TZID: first the system's, then the definitions written in the file (VTIMEZONE).</summary>
internal sealed class IcsZoneResolver(IcsDocument doc, IcsIssueList issues)
{
    private readonly Dictionary<string, IcsZone?> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lazy<Dictionary<string, IcsZone>> _fileZones = new(() => ReadDefinitions(doc));

    public IcsZone? Find(string rawName)
    {
        var name = rawName.Trim().Trim('"');
        if (name.Length == 0) return null;
        if (_cache.TryGetValue(name, out var cached)) return cached;
        var zone = FromSystem(name) ?? (_fileZones.Value.TryGetValue(name, out var defined) ? defined : null);
        if (zone is null) issues.Add(IcsIssueCode.UnknownTimeZone, name);
        _cache[name] = zone;
        return zone;
    }

    /// <summary>The system's zone, also for "/mozilla.org/20050126_1/Europe/Rome" (the tail of the name) and for the Windows names Outlook writes.</summary>
    private static IcsZone? FromSystem(string name)
    {
        if (Try(name) is { } direct) return direct;
        var parts = name.Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 1; i < parts.Length; i++)
            if (Try(string.Join('/', parts[i..])) is { } tail) return tail;
        return WindowsZones.TryGetValue(name, out var iana) ? Try(iana) : null;
    }

    private static IcsZone? Try(string id)
    {
        try { return IcsZone.Of(TimeZoneInfo.FindSystemTimeZoneById(id)); }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentException) { return null; }
    }

    // ---- VTIMEZONE written in the file ----

    private sealed record Observance(DateTime Start, TimeSpan To, int? Month, int? Ordinal, DayOfWeek? Day);

    private static Dictionary<string, IcsZone> ReadDefinitions(IcsDocument doc)
    {
        var result = new Dictionary<string, IcsZone>(StringComparer.OrdinalIgnoreCase);
        foreach (var tz in doc.Components.Where(c => c.Name == "VTIMEZONE"))
        {
            var id = tz.Property("TZID")?.Value.Trim().Trim('"');
            if (string.IsNullOrEmpty(id)) continue;
            var observances = tz.Children.Where(c => c.Name is "STANDARD" or "DAYLIGHT").Select(ReadObservance).OfType<Observance>().ToList();
            if (observances.Count > 0) result[id] = new IcsZone(wall => OffsetAt(observances, wall));
        }

        return result;
    }

    private static Observance? ReadObservance(IcsComponent c)
    {
        if (c.Property("DTSTART") is not { } start || c.Property("TZOFFSETTO") is not { } to) return null;
        if (!DateTime.TryParseExact(start.Value.Trim(), "yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var from)) return null;
        if (ParseOffset(to.Value) is not { } offset) return null;
        int? month = null, ordinal = null;
        DayOfWeek? day = null;
        if (c.Property("RRULE") is { } rule)
        {
            foreach (var part in rule.Value.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = part.Split('=', 2);
                if (kv.Length != 2) continue;
                if (kv[0].Equals("BYMONTH", StringComparison.OrdinalIgnoreCase) && int.TryParse(kv[1], out var m)) month = m;
                if (kv[0].Equals("BYDAY", StringComparison.OrdinalIgnoreCase) && kv[1].Length >= 2)
                {
                    var code = kv[1][^2..].ToUpperInvariant();
                    var d = Array.IndexOf(["SU", "MO", "TU", "WE", "TH", "FR", "SA"], code);
                    if (d >= 0 && (kv[1].Length == 2 || int.TryParse(kv[1][..^2], out var n) && n != 0))
                    {
                        day = (DayOfWeek)d;
                        ordinal = kv[1].Length == 2 ? 1 : int.Parse(kv[1][..^2], CultureInfo.InvariantCulture);
                    }
                }
            }
        }

        // A rule that is not "every year, the n-th weekday of a month" counts as a single change at DTSTART.
        if (month is null || day is null) { month = null; ordinal = null; day = null; }
        return new Observance(from, offset, month, ordinal, day);
    }

    private static TimeSpan? ParseOffset(string value)
    {
        var v = value.Trim();
        if (v.Length is not (5 or 7) || v[0] is not ('+' or '-')) return null;
        if (!int.TryParse(v[1..3], out var h) || !int.TryParse(v[3..5], out var m)) return null;
        var span = new TimeSpan(h, m, 0);
        return v[0] == '-' ? -span : span;
    }

    private static TimeSpan OffsetAt(List<Observance> observances, DateTime wall)
    {
        TimeSpan? best = null;
        var bestOnset = DateTime.MinValue;
        foreach (var o in observances)
        {
            foreach (var onset in Onsets(o, wall.Year))
            {
                if (onset > wall || onset < bestOnset) continue;
                bestOnset = onset;
                best = o.To;
            }
        }

        // Before every change: the offset the first change moves away from is not known here; the earliest "to" is the best guess.
        return best ?? observances.OrderBy(o => o.Start).First().To;
    }

    private static IEnumerable<DateTime> Onsets(Observance o, int year)
    {
        if (o.Month is null) { yield return o.Start; yield break; }
        for (var y = Math.Max(o.Start.Year, year - 1); y <= year; y++)
        {
            var date = NthWeekday(y, o.Month.Value, o.Day!.Value, o.Ordinal!.Value);
            yield return date.Add(o.Start.TimeOfDay);
        }
    }

    private static DateTime NthWeekday(int year, int month, DayOfWeek day, int ordinal)
    {
        if (ordinal > 0)
        {
            var first = new DateTime(year, month, 1);
            return first.AddDays((day - first.DayOfWeek + 7) % 7 + 7 * (ordinal - 1));
        }

        var last = new DateTime(year, month, DateTime.DaysInMonth(year, month));
        return last.AddDays(-((last.DayOfWeek - day + 7) % 7) + 7 * (ordinal + 1));
    }

    /// <summary>Outlook and Exchange write Windows zone names; lower case here. Only used when the system does not know the name.</summary>
    private static readonly Dictionary<string, string> WindowsZones = new(StringComparer.OrdinalIgnoreCase)
    {
        ["w. europe standard time"] = "Europe/Berlin", ["central europe standard time"] = "Europe/Budapest",
        ["central european standard time"] = "Europe/Warsaw", ["romance standard time"] = "Europe/Paris",
        ["gmt standard time"] = "Europe/London", ["greenwich standard time"] = "Atlantic/Reykjavik",
        ["e. europe standard time"] = "Europe/Chisinau", ["fle standard time"] = "Europe/Kiev",
        ["gtb standard time"] = "Europe/Bucharest", ["turkey standard time"] = "Europe/Istanbul",
        ["russian standard time"] = "Europe/Moscow", ["greece standard time"] = "Europe/Athens",
        ["eastern standard time"] = "America/New_York", ["central standard time"] = "America/Chicago",
        ["mountain standard time"] = "America/Denver", ["pacific standard time"] = "America/Los_Angeles",
        ["us mountain standard time"] = "America/Phoenix", ["alaskan standard time"] = "America/Anchorage",
        ["hawaiian standard time"] = "Pacific/Honolulu", ["atlantic standard time"] = "America/Halifax",
        ["newfoundland standard time"] = "America/St_Johns", ["sa pacific standard time"] = "America/Bogota",
        ["e. south america standard time"] = "America/Sao_Paulo", ["argentina standard time"] = "America/Argentina/Buenos_Aires",
        ["central america standard time"] = "America/Guatemala", ["mexico standard time"] = "America/Mexico_City",
        ["canada central standard time"] = "America/Regina", ["utc"] = "UTC", ["tokyo standard time"] = "Asia/Tokyo",
        ["china standard time"] = "Asia/Shanghai", ["korea standard time"] = "Asia/Seoul", ["india standard time"] = "Asia/Kolkata",
        ["singapore standard time"] = "Asia/Singapore", ["arabian standard time"] = "Asia/Dubai", ["israel standard time"] = "Asia/Jerusalem",
        ["aus eastern standard time"] = "Australia/Sydney", ["e. australia standard time"] = "Australia/Brisbane",
        ["new zealand standard time"] = "Pacific/Auckland", ["south africa standard time"] = "Africa/Johannesburg",
        ["egypt standard time"] = "Africa/Cairo", ["w. central africa standard time"] = "Africa/Lagos",
    };
}
