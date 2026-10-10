using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GGCalendar.App.Services;
using GGCalendar.Core.Models;
using GGCalendar.Core.Recurrence;

namespace GGCalendar.App.ViewModels;

public sealed record SoundOption(string Label, string? Path);
public sealed record ReminderMethodOption(ReminderMethod Method, string Label);
public sealed record ReminderUnitOption(string Label, int Minutes);
public sealed record ChoiceOption<T>(T Value, string Label);
public sealed record ColorOption(string? Hex, string Label);

public sealed partial class ReminderItemViewModel : ObservableObject
{
    public static IReadOnlyList<ReminderMethodOption> Methods { get; } =
        [new(ReminderMethod.Popup, "Notifica"), new(ReminderMethod.Alarm, "Sveglia"), new(ReminderMethod.Email, "Email")];

    public static IReadOnlyList<ReminderUnitOption> Units { get; } =
        [new("minuti", 1), new("ore", 60), new("giorni", 1440), new("settimane", 10080)];

    public ReminderItemViewModel(EventReminder r, IReadOnlyList<SoundOption> sounds)
    {
        Method = Methods.First(m => m.Method == r.Method);
        var unit = Units.LastOrDefault(u => r.Minutes > 0 && r.Minutes % u.Minutes == 0) ?? Units[0];
        Unit = unit;
        Value = r.Minutes / unit.Minutes;
        Sound = sounds.FirstOrDefault(s => s.Path == r.SoundPath) ?? sounds[0];
        SoundOptions = sounds;
    }

    public IReadOnlyList<SoundOption> SoundOptions { get; }

    [ObservableProperty] public partial ReminderMethodOption Method { get; set; }
    [ObservableProperty] public partial decimal? Value { get; set; }
    [ObservableProperty] public partial ReminderUnitOption Unit { get; set; }
    [ObservableProperty] public partial SoundOption Sound { get; set; }

    public bool IsPopup => Method.Method != ReminderMethod.Email;

    partial void OnMethodChanged(ReminderMethodOption value) => OnPropertyChanged(nameof(IsPopup));

    public EventReminder ToModel() =>
        new(Method.Method, (int)((Value ?? 0) * Unit.Minutes), IsPopup ? Sound.Path : null);
}

public sealed partial class AttendeeViewModel(EventAttendee a) : ObservableObject
{
    public string Email { get; } = a.Email;

    [ObservableProperty] public partial bool IsOptional { get; set; } = a.IsOptional;
}

public sealed partial class EventEditViewModel : ViewModelBase
{
    private static readonly Regex EmailPattern = new(@"^[^@\s,;]+@[^@\s,;]+\.[^@\s,;]+$", RegexOptions.Compiled);
    private readonly CalendarEvent _original;
    private readonly SoundService? _sound;
    private readonly string _defaultSound;
    private readonly int _volume;

    public EventEditViewModel(CalendarEvent ev, IReadOnlyList<CalendarItemViewModel> calendars, bool isNew, AppSettings settings, SoundService? sound = null)
    {
        _original = ev;
        _sound = sound;
        _defaultSound = settings.DefaultReminderSound;
        _volume = settings.ReminderVolumePercent;
        IsNew = isNew;
        Use24Hour = settings.Use24HourClock;
        TimeZoneText = TimeZoneInfo.TryFindSystemTimeZoneById(settings.PrimaryTimeZoneId, out var tz) ? tz.DisplayName : TimeZoneInfo.Local.DisplayName;
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

        BuildSoundOptions(ev.Reminders);
        foreach (var r in ev.Reminders) Reminders.Add(new ReminderItemViewModel(r, SoundOptions));
        foreach (var a in ev.Attendees) Attendees.Add(new AttendeeViewModel(a));

        var current = RecurrenceOptions.FirstOrDefault(o => o.Value == ev.Recurrence);
        if (current is null)
        {
            current = new ChoiceOption<string?>(ev.Recurrence, RecurrenceRule.Parse(ev.Recurrence)?.Describe() ?? ev.Recurrence ?? "");
            RecurrenceOptions.Insert(RecurrenceOptions.Count - 1, current);
        }

        Recurrence = current;
        Availability = AvailabilityOptions.First(o => o.Value == ev.Availability);
        Visibility = VisibilityOptions.First(o => o.Value == ev.Visibility);
        EventColor = ColorOptions.FirstOrDefault(c => c.Hex == ev.ColorHex) ?? ColorOptions[0];
        AddMeet = ev.AddMeet || ev.MeetLink is not null;
        MeetLink = ev.MeetLink;
        SendInvites = ev.SendInvites;
        GuestsCanModify = ev.GuestsCanModify;
        GuestsCanInvite = ev.GuestsCanInvite;
        GuestsCanSeeList = ev.GuestsCanSeeList;
    }

    public bool IsNew { get; }
    public bool Use24Hour { get; }
    public string TimeZoneText { get; }
    public string Header => IsNew ? "Nuovo evento" : "Modifica evento";
    public IReadOnlyList<CalendarItemViewModel> Calendars { get; }

    public ObservableCollection<ReminderItemViewModel> Reminders { get; } = [];
    public ObservableCollection<AttendeeViewModel> Attendees { get; } = [];
    public ObservableCollection<SoundOption> SoundOptions { get; } = [];

    public const string CustomRecurrence = "__custom__";

    private ChoiceOption<string?> _previousRecurrence = null!;

    /// <summary>Raised when the user picks "Personalizza…" so the view can open the custom recurrence dialog.</summary>
    public event Action? CustomRecurrenceRequested;

    public ObservableCollection<ChoiceOption<string?>> RecurrenceOptions { get; } =
    [
        new(null, "Non si ripete"),
        new("FREQ=DAILY", "Ogni giorno"),
        new("FREQ=WEEKLY", "Ogni settimana"),
        new("FREQ=WEEKLY;BYDAY=MO,TU,WE,TH,FR", "Tutti i giorni feriali (lun-ven)"),
        new("FREQ=MONTHLY", "Ogni mese"),
        new("FREQ=YEARLY", "Ogni anno"),
        new(CustomRecurrence, "Personalizza…"),
    ];

    public IReadOnlyList<ChoiceOption<EventAvailability>> AvailabilityOptions { get; } =
        [new(EventAvailability.Busy, "Occupato"), new(EventAvailability.Free, "Libero")];

    public IReadOnlyList<ChoiceOption<EventVisibility>> VisibilityOptions { get; } =
        [new(EventVisibility.Default, "Visibilità predefinita"), new(EventVisibility.Public, "Pubblico"), new(EventVisibility.Private, "Privato")];

    public IReadOnlyList<ColorOption> ColorOptions { get; } =
    [
        new(null, "Colore del calendario"), new("#D50000", "Pomodoro"), new("#E67C73", "Fenicottero"),
        new("#F4511E", "Mandarino"), new("#F6BF26", "Banana"), new("#33B679", "Salvia"), new("#0B8043", "Basilico"),
        new("#039BE5", "Pavone"), new("#3F51B5", "Mirtillo"), new("#7986CB", "Lavanda"), new("#8E24AA", "Uva"),
        new("#616161", "Grafite"),
    ];

    [ObservableProperty] public partial CalendarItemViewModel? SelectedCalendar { get; set; }
    [ObservableProperty] public partial string Title { get; set; } = "";
    [ObservableProperty] public partial string Location { get; set; } = "";
    [ObservableProperty] public partial string Description { get; set; } = "";
    [ObservableProperty] public partial bool IsAllDay { get; set; }
    [ObservableProperty] public partial DateTime? StartDate { get; set; }
    [ObservableProperty] public partial DateTime? EndDate { get; set; }
    [ObservableProperty] public partial TimeSpan? StartTime { get; set; }
    [ObservableProperty] public partial TimeSpan? EndTime { get; set; }

    private bool _shifting;

    partial void OnStartDateChanged(DateTime? oldValue, DateTime? newValue)
    {
        if (_shifting || oldValue is null || newValue is null || EndDate is null) return;
        _shifting = true;
        EndDate = EndDate.Value.Date + (newValue.Value.Date - oldValue.Value.Date);
        _shifting = false;
    }

    // Like Google Calendar: moving the start keeps the duration, so the end follows.
    partial void OnStartTimeChanged(TimeSpan? oldValue, TimeSpan? newValue)
    {
        if (_shifting || oldValue is null || newValue is null || EndTime is null || EndDate is null) return;
        _shifting = true;
        var end = EndDate.Value.Date + EndTime.Value + (newValue.Value - oldValue.Value);
        EndDate = end.Date;
        EndTime = end.TimeOfDay;
        _shifting = false;
    }
    [ObservableProperty] public partial ChoiceOption<string?> Recurrence { get; set; }
    [ObservableProperty] public partial ChoiceOption<EventAvailability> Availability { get; set; }
    [ObservableProperty] public partial ChoiceOption<EventVisibility> Visibility { get; set; }
    [ObservableProperty] public partial ColorOption EventColor { get; set; }
    [ObservableProperty] public partial bool AddMeet { get; set; }
    [ObservableProperty] public partial string? MeetLink { get; set; }
    [ObservableProperty] public partial bool SendInvites { get; set; }
    [ObservableProperty] public partial bool GuestsCanModify { get; set; }
    [ObservableProperty] public partial bool GuestsCanInvite { get; set; }
    [ObservableProperty] public partial bool GuestsCanSeeList { get; set; }
    [ObservableProperty] public partial string NewAttendee { get; set; } = "";
    [ObservableProperty] public partial string Error { get; set; } = "";

    public bool IsTimed => !IsAllDay;
    public bool HasMeetLink => !string.IsNullOrEmpty(MeetLink);
    public bool HasAttendees => Attendees.Count > 0;
    public bool CanAddReminder => Reminders.Count < 5;

    partial void OnRecurrenceChanged(ChoiceOption<string?> value)
    {
        if (value.Value == CustomRecurrence) CustomRecurrenceRequested?.Invoke();
        else _previousRecurrence = value;
    }

    public string? CurrentRecurrenceRule => _previousRecurrence.Value;
    public DateTime StartDay => (StartDate ?? DateTime.Today).Date;

    /// <summary>Applies the dialog outcome; a null rule means the dialog was cancelled.</summary>
    public void ApplyCustomRecurrence(RecurrenceRule? rule)
    {
        if (rule is null) { Recurrence = _previousRecurrence; return; }
        var rrule = rule.ToRrule(IsAllDay);
        var option = RecurrenceOptions.FirstOrDefault(o => o.Value == rrule);
        if (option is null)
        {
            option = new ChoiceOption<string?>(rrule, rule.Describe());
            RecurrenceOptions.Insert(RecurrenceOptions.Count - 1, option);
        }

        Recurrence = option;
    }

    partial void OnIsAllDayChanged(bool value) => OnPropertyChanged(nameof(IsTimed));
    partial void OnMeetLinkChanged(string? value) => OnPropertyChanged(nameof(HasMeetLink));

    private void BuildSoundOptions(IEnumerable<EventReminder> existing)
    {
        SoundOptions.Add(new SoundOption("Suono predefinito", null));
        SoundOptions.Add(new SoundOption("Nessun suono", EventReminder.NoSound));
        if (Directory.Exists(SoundService.MediaFolder))
        {
            foreach (var f in Directory.EnumerateFiles(SoundService.MediaFolder, "*.wav").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                SoundOptions.Add(new SoundOption("Windows: " + Path.GetFileNameWithoutExtension(f), f));
        }

        foreach (var p in existing.Select(r => r.SoundPath).Where(p => p is not null && p != EventReminder.NoSound).Distinct())
        {
            if (SoundOptions.All(s => s.Path != p)) SoundOptions.Add(new SoundOption("File: " + Path.GetFileName(p), p));
        }
    }

    /// <summary>Registers a user-chosen audio file and assigns it to the reminder.</summary>
    public void UseCustomSound(ReminderItemViewModel reminder, string path)
    {
        var option = SoundOptions.FirstOrDefault(s => s.Path == path);
        if (option is null)
        {
            option = new SoundOption("File: " + Path.GetFileName(path), path);
            SoundOptions.Add(option);
        }

        reminder.Sound = option;
    }

    [RelayCommand]
    private void AddReminder()
    {
        if (!CanAddReminder) return;
        Reminders.Add(new ReminderItemViewModel(new EventReminder(ReminderMethod.Popup, 10), SoundOptions));
        OnPropertyChanged(nameof(CanAddReminder));
    }

    [RelayCommand]
    private void RemoveReminder(ReminderItemViewModel r)
    {
        Reminders.Remove(r);
        OnPropertyChanged(nameof(CanAddReminder));
    }

    [RelayCommand]
    private void PreviewSound(ReminderItemViewModel r)
    {
        var path = r.Sound.Path switch
        {
            null => _defaultSound,
            EventReminder.NoSound => null,
            var p => p,
        };
        _sound?.Play(path, _volume);
    }

    public void StopSound() => _sound?.Stop();

    [RelayCommand]
    private void AddAttendee()
    {
        Error = "";
        foreach (var raw in NewAttendee.Split([',', ';', ' ', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!EmailPattern.IsMatch(raw)) { Error = $"Indirizzo email non valido: {raw}"; return; }
            if (!Attendees.Any(a => a.Email.Equals(raw, StringComparison.OrdinalIgnoreCase)))
                Attendees.Add(new AttendeeViewModel(new EventAttendee(raw)));
        }

        NewAttendee = "";
        OnPropertyChanged(nameof(HasAttendees));
    }

    [RelayCommand]
    private void RemoveAttendee(AttendeeViewModel a)
    {
        Attendees.Remove(a);
        OnPropertyChanged(nameof(HasAttendees));
    }

    /// <summary>Builds the edited event, or returns null and sets <see cref="Error"/> when the input is invalid.</summary>
    public CalendarEvent? TryBuild()
    {
        Error = "";
        if (!string.IsNullOrWhiteSpace(NewAttendee)) AddAttendee();
        if (Error.Length > 0) return null;
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
            Attendees = Attendees.Select(a => new EventAttendee(a.Email, a.IsOptional)).ToList(),
            Reminders = Reminders.Select(r => r.ToModel()).ToList(),
            Recurrence = Recurrence.Value,
            ColorHex = EventColor.Hex,
            Availability = Availability.Value,
            Visibility = Visibility.Value,
            AddMeet = AddMeet,
            MeetLink = AddMeet ? MeetLink : null,
            SendInvites = SendInvites,
            GuestsCanModify = GuestsCanModify,
            GuestsCanInvite = GuestsCanInvite,
            GuestsCanSeeList = GuestsCanSeeList,
        };
    }
}


