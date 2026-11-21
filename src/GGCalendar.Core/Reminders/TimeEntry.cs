using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace GGCalendar.Core.Reminders;

/// <summary>
/// The time of an event typed by hand (to the minute) and moved with the arrows (5 minutes at a time),
/// and the moment a reminder goes off for a given event start.
/// </summary>
public static class TimeEntry
{
    public const int StepMinutes = 5;
    private static readonly TimeSpan LastMinute = new(23, 59, 0);
    private static readonly Regex Suffix = new(@"\s*([ap])\.?\s*(m\.?)?\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex WithSeparator = new(@"^(\d{1,2})\s*[:.,hH\s]\s*(\d{1,2})$", RegexOptions.Compiled);

    /// <summary>Reads "9:03", "903", "9.03", "9 03", "9h03", "9" and, on a 12-hour clock, "9:03 pm" or "9p". Null when it is not a time.</summary>
    public static TimeSpan? Parse(string? text, bool use24Hour)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var s = text.Trim();

        bool? pm = null;
        var suffix = Suffix.Match(s);
        if (suffix.Success && s.Length > suffix.Length)
        {
            if (use24Hour) return null;
            pm = char.ToLowerInvariant(suffix.Groups[1].Value[0]) == 'p';
            s = s[..suffix.Index].Trim();
        }

        int hours, minutes;
        var separated = WithSeparator.Match(s);
        if (separated.Success)
        {
            hours = int.Parse(separated.Groups[1].Value, CultureInfo.InvariantCulture);
            minutes = int.Parse(separated.Groups[2].Value, CultureInfo.InvariantCulture);
        }
        else if (s.Length is >= 1 and <= 4 && s.AsSpan().IndexOfAnyExceptInRange('0', '9') < 0)
        {
            var digits = s.Length;
            hours = int.Parse(digits <= 2 ? s : s[..(digits - 2)], CultureInfo.InvariantCulture);
            minutes = digits <= 2 ? 0 : int.Parse(s[(digits - 2)..], CultureInfo.InvariantCulture);
        }
        else return null;

        if (pm is { } afternoon)
        {
            if (hours is < 1 or > 12) return null;
            hours = hours % 12 + (afternoon ? 12 : 0);
        }

        if (hours > 23 || minutes > 59) return null;
        return new TimeSpan(hours, minutes, 0);
    }

    /// <summary>"09:03", or "9:03 AM" on a 12-hour clock.</summary>
    public static string Format(TimeSpan time, bool use24Hour)
    {
        var h = time.Hours;
        if (use24Hour) return $"{h:00}:{time.Minutes:00}";
        return $"{(h % 12 == 0 ? 12 : h % 12)}:{time.Minutes:00} {(h < 12 ? "AM" : "PM")}";
    }

    /// <summary>The next (direction &gt; 0) or previous multiple of 5 minutes, so 9:03 goes to 9:05 or 9:00. It stays inside the day.</summary>
    public static TimeSpan Step(TimeSpan time, int direction)
    {
        var minutes = (int)time.TotalMinutes;
        var rest = minutes % StepMinutes;
        var moved = direction >= 0 ? minutes + StepMinutes - rest : minutes - (rest == 0 ? StepMinutes : rest);
        if (moved < 0) return TimeSpan.Zero;
        var result = TimeSpan.FromMinutes(moved);
        return result > LastMinute ? time : result;
    }

    /// <summary>
    /// When a reminder of <paramref name="minutesBefore"/> goes off: the time, and the day ("Mon 23 Nov") when that is not the day
    /// of the event. For an all-day event the start is midnight.
    /// </summary>
    public static (string? Day, string Time) WhenParts(DateTime start, int minutesBefore, bool use24Hour, CultureInfo culture)
    {
        var at = start.AddMinutes(-minutesBefore);
        return (at.Date == start.Date ? null : at.ToString("ddd d MMM", culture), Format(at.TimeOfDay, use24Hour));
    }
}
