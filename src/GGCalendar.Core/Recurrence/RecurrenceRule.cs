using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace GGCalendar.Core.Recurrence;

public enum RecurrenceFrequency { Daily, Weekly, Monthly, Yearly }

/// <summary>Simple RRULE model (frequency, interval, weekdays, end by date or count).</summary>
public sealed record RecurrenceRule
{
    private static readonly string[] DayCodes = ["SU", "MO", "TU", "WE", "TH", "FR", "SA"];
    private static readonly string[] DayNames = ["domenica", "lunedì", "martedì", "mercoledì", "giovedì", "venerdì", "sabato"];

    public RecurrenceFrequency Frequency { get; init; } = RecurrenceFrequency.Weekly;
    public int Interval { get; init; } = 1;
    public IReadOnlyList<DayOfWeek> Days { get; init; } = [];
    public DateTime? Until { get; init; }
    public int? Count { get; init; }

    /// <summary>Parses an RRULE; returns null when it uses parts this model cannot represent.</summary>
    public static RecurrenceRule? Parse(string? rrule)
    {
        if (string.IsNullOrWhiteSpace(rrule)) return null;
        var rule = new RecurrenceRule();
        var days = new List<DayOfWeek>();
        var hasFreq = false;
        foreach (var part in rrule.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = part.Split('=', 2);
            if (kv.Length != 2) return null;
            var value = kv[1].Trim();
            switch (kv[0].Trim().ToUpperInvariant())
            {
                case "FREQ":
                    RecurrenceFrequency? freq = value.ToUpperInvariant() switch
                    {
                        "DAILY" => RecurrenceFrequency.Daily,
                        "WEEKLY" => RecurrenceFrequency.Weekly,
                        "MONTHLY" => RecurrenceFrequency.Monthly,
                        "YEARLY" => RecurrenceFrequency.Yearly,
                        _ => null,
                    };
                    if (freq is null) return null;
                    rule = rule with { Frequency = freq.Value };
                    hasFreq = true;
                    break;
                case "INTERVAL":
                    if (!int.TryParse(value, out var interval) || interval < 1) return null;
                    rule = rule with { Interval = interval };
                    break;
                case "COUNT":
                    if (!int.TryParse(value, out var count) || count < 1) return null;
                    rule = rule with { Count = count };
                    break;
                case "UNTIL":
                    if (!TryParseDate(value, out var until)) return null;
                    rule = rule with { Until = until };
                    break;
                case "BYDAY":
                    foreach (var code in value.Split(',', StringSplitOptions.RemoveEmptyEntries))
                    {
                        var idx = Array.IndexOf(DayCodes, code.Trim().ToUpperInvariant());
                        if (idx < 0) return null;
                        days.Add((DayOfWeek)idx);
                    }

                    break;
                default:
                    return null;
            }
        }

        if (!hasFreq || (rule.Count is not null && rule.Until is not null)) return null;
        return rule with { Days = days };
    }

    private static bool TryParseDate(string value, out DateTime date)
    {
        var ok = DateTime.TryParseExact(value.TrimEnd('Z'), ["yyyyMMdd", "yyyyMMdd'T'HHmmss"], CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
        date = date.Date;
        return ok;
    }

    public string ToRrule(bool allDay)
    {
        var parts = new List<string> { "FREQ=" + Frequency.ToString().ToUpperInvariant() };
        if (Interval > 1) parts.Add("INTERVAL=" + Interval);
        if (Frequency == RecurrenceFrequency.Weekly && Days.Count > 0)
            parts.Add("BYDAY=" + string.Join(',', OrderedDays().Select(d => DayCodes[(int)d])));
        if (Count is { } c) parts.Add("COUNT=" + c);
        else if (Until is { } u)
            parts.Add("UNTIL=" + (allDay ? u.ToString("yyyyMMdd", CultureInfo.InvariantCulture) : u.ToString("yyyyMMdd'T'235959'Z'", CultureInfo.InvariantCulture)));
        return string.Join(';', parts);
    }

    private IEnumerable<DayOfWeek> OrderedDays() => Days.Distinct().OrderBy(d => ((int)d + 6) % 7);

    /// <summary>Italian description, e.g. "Ogni 2 settimane il lunedì, giovedì, fino al 7 gen 2027".</summary>
    public string Describe()
    {
        var unit = Frequency switch
        {
            RecurrenceFrequency.Daily => Interval == 1 ? "giorno" : "giorni",
            RecurrenceFrequency.Weekly => Interval == 1 ? "settimana" : "settimane",
            RecurrenceFrequency.Monthly => Interval == 1 ? "mese" : "mesi",
            _ => Interval == 1 ? "anno" : "anni",
        };
        var text = Interval == 1 ? $"Ogni {unit}" : $"Ogni {Interval} {unit}";
        if (Frequency == RecurrenceFrequency.Weekly && Days.Count > 0)
            text += " il " + string.Join(", ", OrderedDays().Select(d => DayNames[(int)d]));
        if (Count is { } c) text += c == 1 ? ", 1 volta" : $", {c} volte";
        else if (Until is { } u) text += ", fino al " + u.ToString("d MMM yyyy", CultureInfo.GetCultureInfo("it-IT"));
        return text;
    }
}
