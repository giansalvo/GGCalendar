using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using GGCalendar.Core.Recurrence;

namespace GGCalendar.App.ViewModels;

public sealed record FrequencyOption(RecurrenceFrequency Frequency, string Label);

public sealed partial class WeekdayToggle(DayOfWeek day, string label, bool isChecked) : ObservableObject
{
    public DayOfWeek Day { get; } = day;
    public string Label { get; } = label;

    [ObservableProperty] public partial bool IsChecked { get; set; } = isChecked;
}

/// <summary>State of the "Ricorrenza personalizzata" dialog.</summary>
public sealed partial class RecurrenceViewModel : ViewModelBase
{
    public static IReadOnlyList<FrequencyOption> Frequencies { get; } =
    [
        new(RecurrenceFrequency.Daily, "giorno"),
        new(RecurrenceFrequency.Weekly, "settimana"),
        new(RecurrenceFrequency.Monthly, "mese"),
        new(RecurrenceFrequency.Yearly, "anno"),
    ];

    public RecurrenceViewModel(RecurrenceRule? rule, DateTime startDay)
    {
        rule ??= new RecurrenceRule();
        Interval = rule.Interval;
        Frequency = Frequencies.First(f => f.Frequency == rule.Frequency);
        var selected = rule.Days.Count > 0 ? rule.Days : [startDay.DayOfWeek];
        string[] labels = ["L", "M", "M", "G", "V", "S", "D"];
        DayOfWeek[] days = [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday];
        for (var i = 0; i < 7; i++) Days.Add(new WeekdayToggle(days[i], labels[i], selected.Contains(days[i])));

        UntilDate = rule.Until ?? startDay.AddMonths(3);
        Count = rule.Count ?? 13;
        EndsOnDate = rule.Until is not null;
        EndsAfter = rule.Count is not null;
        EndsNever = !EndsOnDate && !EndsAfter;
    }

    public List<WeekdayToggle> Days { get; } = [];

    [ObservableProperty] public partial decimal? Interval { get; set; }
    [ObservableProperty] public partial FrequencyOption Frequency { get; set; }
    [ObservableProperty] public partial bool EndsNever { get; set; }
    [ObservableProperty] public partial bool EndsOnDate { get; set; }
    [ObservableProperty] public partial bool EndsAfter { get; set; }
    [ObservableProperty] public partial DateTime? UntilDate { get; set; }
    [ObservableProperty] public partial decimal? Count { get; set; }

    public bool IsWeekly => Frequency.Frequency == RecurrenceFrequency.Weekly;

    partial void OnFrequencyChanged(FrequencyOption value) => OnPropertyChanged(nameof(IsWeekly));

    public RecurrenceRule Build(DateTime startDay)
    {
        var weekdays = Days.Where(d => d.IsChecked).Select(d => d.Day).ToList();
        if (weekdays.Count == 0) weekdays.Add(startDay.DayOfWeek);
        return new RecurrenceRule
        {
            Frequency = Frequency.Frequency,
            Interval = Math.Max(1, (int)(Interval ?? 1)),
            Days = Frequency.Frequency == RecurrenceFrequency.Weekly ? weekdays : [],
            Until = EndsOnDate && UntilDate is { } u ? u.Date : null,
            Count = EndsAfter ? Math.Max(1, (int)(Count ?? 1)) : null,
        };
    }
}
