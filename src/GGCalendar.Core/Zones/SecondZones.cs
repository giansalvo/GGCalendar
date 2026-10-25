using System.Text;
using GGCalendar.Core.Models;

namespace GGCalendar.Core.Zones;

/// <summary>
/// The second time zone of the time grids (Day, 6 days, Week): a second column of hours next to the one of this PC, with its own
/// short name. As on GGCalendar Mobile, events, days and the database stay in the time zone of the PC: only the labels of the hours
/// are converted, line by line, with the rules of the zone (summer time, half hours), so that the hour of a line is the real hour
/// there on that day.
/// </summary>
public sealed record SecondZone(string ZoneId, string Label)
{
    /// <summary>The zone as this PC knows it, or null when it does not exist here (a settings file taken from another PC).</summary>
    public TimeZoneInfo? Zone => SecondZones.Find(ZoneId);
}

/// <summary>One line of the second column: the hour there, its date, and whether the date is worth writing (first line, or the date changes).</summary>
public readonly record struct ZoneRow(TimeOnly Time, DateOnly Date, bool ShowDate);

public static class SecondZones
{
    /// <summary>The longest name the column has room for.</summary>
    public const int MaxLabel = 6;

    public static TimeZoneInfo? Find(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        return TimeZoneInfo.TryFindSystemTimeZoneById(id, out var zone) ? zone : null;
    }

    /// <summary>Trimmed and cut to <see cref="MaxLabel"/> characters, counted as people see them (an emoji is one).</summary>
    public static string ClampLabel(string? text)
    {
        var trimmed = (text ?? "").Trim();
        var builder = new StringBuilder();
        var count = 0;
        foreach (var rune in trimmed.EnumerateRunes())
        {
            if (count == MaxLabel) break;
            builder.Append(rune.ToString());
            count++;
        }

        return builder.ToString();
    }

    /// <summary>
    /// The name offered first: the initials of a city of several words ("America/New_York" gives "NY"), else its first six letters ("Tokyo").
    /// A Windows zone id is read through its IANA name.
    /// </summary>
    public static string DefaultLabel(string zoneId)
    {
        var iana = zoneId.Contains('/') ? zoneId : TimeZoneInfo.TryConvertWindowsIdToIanaId(zoneId, out var converted) ? converted : zoneId;
        var city = iana[(iana.LastIndexOf('/') + 1)..].Replace('_', ' ').Trim();
        var words = city.Split([' ', '-'], StringSplitOptions.RemoveEmptyEntries);
        return words.Length > 1 ? ClampLabel(string.Concat(words.Select(w => char.ToUpperInvariant(w[0])))) : ClampLabel(city);
    }

    /// <summary>The name the user typed, or the default when it is empty.</summary>
    public static string LabelOf(string zoneId, string? custom) =>
        ClampLabel(custom) is { Length: > 0 } label ? label : DefaultLabel(zoneId);

    /// <summary>
    /// The second zone to draw, or null when it is off, unknown to this PC or the same as the PC's.
    /// </summary>
    public static SecondZone? From(AppSettings settings, TimeZoneInfo? local = null)
    {
        local ??= TimeZoneInfo.Local;
        if (string.IsNullOrEmpty(settings.SecondaryTimeZoneId)) return null;
        var zone = Find(settings.SecondaryTimeZoneId);
        if (zone is null || zone.Id == local.Id) return null;
        return new SecondZone(settings.SecondaryTimeZoneId, LabelOf(settings.SecondaryTimeZoneId, settings.SecondaryTimeZoneLabel));
    }

    /// <summary>
    /// The 24 lines of <paramref name="day"/> (each at its hour in the zone of the PC) read in <paramref name="second"/>. Each line is
    /// converted on its own, so the answer is right on the days a zone changes its clock, whichever of the two does it, and for
    /// zones with half hours.
    /// </summary>
    public static IReadOnlyList<ZoneRow> Rows(DateTime day, TimeZoneInfo local, TimeZoneInfo second)
    {
        var rows = new List<ZoneRow>(24);
        DateOnly? previous = null;
        for (var hour = 0; hour < 24; hour++)
        {
            var there = Convert(day.Date.AddHours(hour), local, second);
            var date = DateOnly.FromDateTime(there);
            rows.Add(new ZoneRow(TimeOnly.FromDateTime(there), date, hour == 0 || date != previous));
            previous = date;
        }

        return rows;
    }

    /// <summary>
    /// A local time of the PC read in another zone. An hour that does not exist (the clock moved forward) is the first one that does;
    /// an hour that happens twice (the clock moved back) is the first of the two.
    /// </summary>
    public static DateTime Convert(DateTime local, TimeZoneInfo localZone, TimeZoneInfo other)
    {
        var t = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        if (localZone.IsInvalidTime(t)) t = t.AddHours(1);
        var utc = localZone.IsAmbiguousTime(t)
            ? DateTime.SpecifyKind(t - localZone.GetAmbiguousTimeOffsets(t).Max(), DateTimeKind.Utc)
            : TimeZoneInfo.ConvertTimeToUtc(t, localZone);
        return TimeZoneInfo.ConvertTimeFromUtc(utc, other);
    }

    /// <summary>
    /// The hours of a timed event in another zone: "08:00 – 09:00", with "(-1)" or "(+1)" where the day there is not the same
    /// (written once when the start and the end are both a day away, else after the one that is).
    /// </summary>
    public static string Range(DateTime start, DateTime end, TimeZoneInfo localZone, TimeZoneInfo other, bool use24Hour, System.Globalization.CultureInfo culture)
    {
        var format = use24Hour ? "HH:mm" : "h:mm tt";
        var s = Convert(start, localZone, other);
        var e = Convert(end, localZone, other);
        var startShift = (s.Date - start.Date).Days;
        var endShift = (e.Date - end.Date).Days;

        string Mark(int shift) => shift == 0 ? "" : $" ({(shift > 0 ? "+" : "-")}{Math.Abs(shift)})";

        return startShift == endShift
            ? $"{s.ToString(format, culture)} – {e.ToString(format, culture)}{Mark(endShift)}"
            : $"{s.ToString(format, culture)}{Mark(startShift)} – {e.ToString(format, culture)}{Mark(endShift)}";
    }

    /// <summary>"GMT+01", "GMT-05", "GMT+05:30": the offset of a zone from UTC, the same in every language.</summary>
    public static string OffsetLabel(TimeSpan offset)
    {
        var sign = offset < TimeSpan.Zero ? "-" : "+";
        var abs = offset.Duration();
        return abs.Minutes == 0 ? $"GMT{sign}{abs.Hours:00}" : $"GMT{sign}{abs.Hours:00}:{abs.Minutes:00}";
    }

    /// <summary>The offset label of a zone at the middle of a day.</summary>
    public static string OffsetLabel(TimeZoneInfo zone, DateTime day) =>
        OffsetLabel(zone.GetUtcOffset(DateTime.SpecifyKind(day.Date.AddHours(12), DateTimeKind.Unspecified)));

    /// <summary>True when the hours of the two lists are the same line by line: a day column then needs no labels of its own.</summary>
    public static bool SameHours(IReadOnlyList<ZoneRow> a, IReadOnlyList<ZoneRow> b) =>
        a.Count == b.Count && a.Zip(b).All(p => p.First.Time == p.Second.Time);
}
