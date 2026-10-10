using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using GGCalendar.Core.Models;

namespace GGCalendar.App.ViewModels;

public sealed partial class EventEditViewModel : ViewModelBase
{
    private readonly CalendarEvent _original;

    public EventEditViewModel(CalendarEvent ev, IReadOnlyList<CalendarItemViewModel> calendars, bool isNew, bool use24Hour)
    {
        _original = ev;
        IsNew = isNew;
        Use24Hour = use24Hour;
        Calendars = calendars;
        SelectedCalendar = calendars.FirstOrDefault(c => c.Id == ev.CalendarId) ?? calendars.FirstOrDefault();

        Title = ev.Title;
        Location = ev.Location;
        Description = ev.Description;
        IsAllDay = ev.IsAllDay;
        var lastDay = ev.IsAllDay ? ev.End.Date.AddDays(-1) : ev.End.Date;
        StartDate = ev.Start.Date;
        EndDate = lastDay < ev.Start.Date ? ev.Start.Date : lastDay;
        StartTime = ev.IsAllDay ? TimeSpan.FromHours(9) : ev.Start.TimeOfDay;
        EndTime = ev.IsAllDay ? TimeSpan.FromHours(10) : ev.End.TimeOfDay;
    }

    public bool IsNew { get; }
    public bool Use24Hour { get; }
    public string Header => IsNew ? "Nuovo evento" : "Modifica evento";
    public IReadOnlyList<CalendarItemViewModel> Calendars { get; }

    [ObservableProperty] public partial CalendarItemViewModel? SelectedCalendar { get; set; }
    [ObservableProperty] public partial string Title { get; set; } = "";
    [ObservableProperty] public partial string Location { get; set; } = "";
    [ObservableProperty] public partial string Description { get; set; } = "";
    [ObservableProperty] public partial bool IsAllDay { get; set; }
    [ObservableProperty] public partial DateTime? StartDate { get; set; }
    [ObservableProperty] public partial DateTime? EndDate { get; set; }
    [ObservableProperty] public partial TimeSpan? StartTime { get; set; }
    [ObservableProperty] public partial TimeSpan? EndTime { get; set; }
    [ObservableProperty] public partial string Error { get; set; } = "";

    public bool IsTimed => !IsAllDay;

    partial void OnIsAllDayChanged(bool value) => OnPropertyChanged(nameof(IsTimed));

    /// <summary>Builds the edited event, or returns null and sets <see cref="Error"/> when the input is invalid.</summary>
    public CalendarEvent? TryBuild()
    {
        Error = "";
        if (SelectedCalendar is null) { Error = "Seleziona un calendario."; return null; }
        if (StartDate is null || EndDate is null) { Error = "Inserisci le date."; return null; }

        DateTime start, end;
        if (IsAllDay)
        {
            start = StartDate.Value.Date;
            end = EndDate.Value.Date.AddDays(1);
        }
        else
        {
            if (StartTime is null || EndTime is null) { Error = "Inserisci gli orari."; return null; }
            start = StartDate.Value.Date + StartTime.Value;
            end = EndDate.Value.Date + EndTime.Value;
        }

        if (end <= start) { Error = "La fine deve essere successiva all'inizio."; return null; }

        return _original with
        {
            CalendarId = SelectedCalendar.Id,
            Title = Title.Trim(),
            Location = Location.Trim(),
            Description = Description,
            IsAllDay = IsAllDay,
            Start = start,
            End = end,
        };
    }
}
