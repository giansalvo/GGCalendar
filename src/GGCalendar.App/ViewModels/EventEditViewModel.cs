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
using GGCalendar.Core.Reminders;
using GGCalendar.Core.Zones;
using GGCalendar.Core.Recurrence;
using GGCalendar.Core.Localization;

namespace GGCalendar.App.ViewModels;

public sealed record SoundOption(string Label, string? Path);
public sealed record ReminderMethodOption(ReminderMethod Method, string Label);
public sealed record ReminderUnitOption(string Label, int Minutes);
public sealed record ChoiceOption<T>(T Value, string Label);
public sealed record ColorOption(string? Hex, string Label);

public sealed partial class ReminderItemViewModel : ObservableObject
{
    public static IReadOnlyList<ReminderMethodOption> Methods =>
        [new(ReminderMethod.Popup, L.T("method.popup")), new(ReminderMethod.Alarm, L.T("method.alarm")), new(ReminderMethod.Email, L.T("method.email"))];

    public static IReadOnlyList<ReminderUnitOption> Units =>
        [new(L.T("unit.minutes"), 1), new(L.T("unit.hours"), 60), new(L.T("unit.days"), 1440), new(L.T("unit.weeks"), 10080)];

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

    /// <summary>The ready-made values of the "Quick choices" list.</summary>
    public static IReadOnlyList<QuickReminderOption> QuickOptions =>
    [
        new(L.T("quick.minutes", 0), 0), new(L.T("quick.minutes", 1), 1), new(L.T("quick.minutes", 5), 5), new(L.T("quick.minutes", 10), 10),
        new(L.T("quick.minutes", 15), 15), new(L.T("quick.minutes", 30), 30), new(L.T("quick.hours", 1), 60), new(L.T("quick.day", 1), 1440),
    ];

    /// <summary>The "Quick choices" box: choosing a value fills the number and the unit, then the box goes back to empty.</summary>
    [ObservableProperty] public partial QuickReminderOption? Quick { get; set; }

    partial void OnQuickChanged(QuickReminderOption? value)
    {
        if (value is null) return;
        var unit = Units.LastOrDefault(u => value.Minutes > 0 && value.Minutes % u.Minutes == 0) ?? Units[0];
        Unit = unit;
        Value = value.Minutes / unit.Minutes;
        Quick = null;
    }

    public int Minutes => (int)((Value ?? 0) * Unit.Minutes);

    /// <summary>"before the event", or "at the time of the event" for 0.</summary>
    public string Suffix => L.T(Minutes == 0 ? "edit.reminder_at_start" : "edit.reminder_before");

    private DateTime? _start;
    private bool _use24Hour = true;
    private bool _discardPast;
    private Func<DateTime> _clock = () => DateTime.Now;

    /// <summary>
    /// The start of the event, to say when the reminder goes off. <paramref name="discardPast"/>: a reminder whose moment has gone
    /// is not kept (an event that repeats keeps them: they work for the next occurrences).
    /// </summary>
    public void SetStart(DateTime? start, bool use24Hour, bool discardPast, Func<DateTime> clock)
    {
        _start = start;
        _use24Hour = use24Hour;
        _discardPast = discardPast;
        _clock = clock;
        OnPropertyChanged(nameof(Preview));
        OnPropertyChanged(nameof(Caption));
        OnPropertyChanged(nameof(IsPast));
    }

    /// <summary>The moment of the reminder is already behind us: it will not be saved.</summary>
    public bool IsPast => _discardPast && _start is { } start && start.AddMinutes(-Minutes) < _clock();

    /// <summary>"before the event · Rings at 08:56", the line under the number.</summary>
    public string Caption => Preview.Length == 0 ? Suffix : $"{Suffix} · {Preview}";

    /// <summary>"Rings at 08:56": the real moment of the reminder; empty while the start is not known.</summary>
    public string Preview
    {
        get
        {
            if (_start is not { } start) return "";
            var (day, time) = TimeEntry.WhenParts(start, Minutes, _use24Hour, L.Culture);
            var when = day is null ? L.T("edit.when_time", time) : L.T("edit.when_day_time", day, time);
            return L.T(Method.Method switch { ReminderMethod.Alarm => "edit.reminder_alarm_at", ReminderMethod.Email => "edit.reminder_email_at", _ => "edit.reminder_popup_at" }, when);
        }
    }

    private void TimingChanged()
    {
        OnPropertyChanged(nameof(Suffix));
        OnPropertyChanged(nameof(Preview));
        OnPropertyChanged(nameof(Caption));
        OnPropertyChanged(nameof(IsPast));
    }

    partial void OnMethodChanged(ReminderMethodOption value)
    {
        OnPropertyChanged(nameof(IsPopup));
        OnPropertyChanged(nameof(Preview));
        OnPropertyChanged(nameof(Caption));
    }

    partial void OnValueChanged(decimal? value) => TimingChanged();
    partial void OnUnitChanged(ReminderUnitOption value) => TimingChanged();

    public EventReminder ToModel() => new(Method.Method, Minutes, IsPopup ? Sound.Path : null);
}

public sealed record QuickReminderOption(string Label, int Minutes);

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
    private readonly Func<DateTime> _clock;
    private readonly AppSettings _settings;
    private readonly int _volume;

    public EventEditViewModel(CalendarEvent ev, IReadOnlyList<CalendarItemViewModel> calendars, bool isNew, AppSettings settings, SoundService? sound = null, Func<DateTime>? clock = null)
    {
        _clock = clock ?? (() => DateTime.Now);
        _original = ev;
        _sound = sound;
        _settings = settings;
        _volume = settings.ReminderVolumePercent;
        IsNew = isNew;
        Use24Hour = settings.Use24HourClock;
        TimeZoneText = TimeZoneInfo.Local.DisplayName;
        _secondZone = SecondZones.From(settings);
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
        RefreshReminderPreviews();
        foreach (var a in ev.Attendees) Attendees.Add(new AttendeeViewModel(a));
        foreach (var attachment in ev.Attachments ?? []) Attachments.Add(attachment);

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
    public string Header => IsNew ? L.T("edit.new_event") : L.T("edit.edit_event");
    public IReadOnlyList<CalendarItemViewModel> Calendars { get; }

    public ObservableCollection<ReminderItemViewModel> Reminders { get; } = [];
    public ObservableCollection<AttendeeViewModel> Attendees { get; } = [];

    /// <summary>The attachments (links). An event whose attachments were never read keeps them unread (null) until the person adds or removes one.</summary>
    public ObservableCollection<EventAttachment> Attachments { get; } = [];

    private bool _attachmentsTouched;
    public ObservableCollection<SoundOption> SoundOptions { get; } = [];

    public const string CustomRecurrence = "__custom__";

    private ChoiceOption<string?> _previousRecurrence = null!;

    /// <summary>Raised when the user picks "Personalizza…" so the view can open the custom recurrence dialog.</summary>
    public event Action? CustomRecurrenceRequested;

    public ObservableCollection<ChoiceOption<string?>> RecurrenceOptions { get; } =
    [
        new(null, L.T("recurrence.none")),
        new("FREQ=DAILY", L.T("recurrence.every_day")),
        new("FREQ=WEEKLY", L.T("recurrence.every_week")),
        new("FREQ=WEEKLY;BYDAY=MO,TU,WE,TH,FR", L.T("recurrence.weekdays")),
        new("FREQ=MONTHLY", L.T("recurrence.every_month")),
        new("FREQ=YEARLY", L.T("recurrence.every_year")),
        new(CustomRecurrence, L.T("recurrence.custom")),
    ];

    public IReadOnlyList<ChoiceOption<EventAvailability>> AvailabilityOptions { get; } =
        [new(EventAvailability.Busy, L.T("availability.busy")), new(EventAvailability.Free, L.T("availability.free"))];

    public IReadOnlyList<ChoiceOption<EventVisibility>> VisibilityOptions { get; } =
        [new(EventVisibility.Default, L.T("visibility.default")), new(EventVisibility.Public, L.T("visibility.public")), new(EventVisibility.Private, L.T("visibility.private"))];

    public IReadOnlyList<ColorOption> ColorOptions { get; } =
    [
        new(null, L.T("color.calendar")), new("#D50000", L.T("color.tomato")), new("#E67C73", L.T("color.flamingo")),
        new("#F4511E", L.T("color.tangerine")), new("#F6BF26", L.T("color.banana")), new("#33B679", L.T("color.sage")), new("#0B8043", L.T("color.basil")),
        new("#039BE5", L.T("color.peacock")), new("#3F51B5", L.T("color.blueberry")), new("#7986CB", L.T("color.lavender")), new("#8E24AA", L.T("color.grape")),
        new("#616161", L.T("color.graphite")),
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

    private readonly SecondZone? _secondZone;

    /// <summary>"NY: 08:00 – 09:00": the hours of the event in the second time zone of the settings, while it is being edited.</summary>
    public string? SecondZoneText
    {
        get
        {
            if (_secondZone?.Zone is not { } other || IsAllDay || StartDate is null || EndDate is null || StartTime is null || EndTime is null) return null;
            var start = StartDate.Value.Date + StartTime.Value;
            var end = EndDate.Value.Date + EndTime.Value;
            return L.T("time.there", _secondZone.Label, SecondZones.Range(start, end, TimeZoneInfo.Local, other, Use24Hour, L.Culture));
        }
    }

    public bool HasSecondZoneText => SecondZoneText is not null;

    private void SecondZoneChanged()
    {
        OnPropertyChanged(nameof(SecondZoneText));
        OnPropertyChanged(nameof(HasSecondZoneText));
    }

    partial void OnEndDateChanged(DateTime? value) => SecondZoneChanged();
    partial void OnEndTimeChanged(TimeSpan? value) => SecondZoneChanged();

    /// <summary>Tells each reminder when the event starts (midnight if it lasts all day), so it can show the moment it goes off.</summary>
    private void RefreshReminderPreviews()
    {
        DateTime? start = StartDate is { } d ? d.Date + (IsAllDay ? TimeSpan.Zero : StartTime ?? TimeSpan.Zero) : null;
        var discardPast = Recurrence is null || Recurrence.Value is null;
        foreach (var r in Reminders) r.SetStart(start, Use24Hour, discardPast, _clock);
    }

    private bool _shifting;

    partial void OnStartDateChanged(DateTime? oldValue, DateTime? newValue)
    {
        SecondZoneChanged();
        RefreshReminderPreviews();
        if (_shifting || oldValue is null || newValue is null || EndDate is null) return;
        _shifting = true;
        EndDate = EndDate.Value.Date + (newValue.Value.Date - oldValue.Value.Date);
        _shifting = false;
    }

    // Like Google Calendar: moving the start keeps the duration, so the end follows.
    partial void OnStartTimeChanged(TimeSpan? oldValue, TimeSpan? newValue)
    {
        SecondZoneChanged();
        RefreshReminderPreviews();
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
    [ObservableProperty] public partial string NewAttachmentUrl { get; set; } = "";
    [ObservableProperty] public partial string NewAttachmentTitle { get; set; } = "";
    /// <summary>Why the address in the boxes was not added; shown under the boxes.</summary>
    [ObservableProperty] public partial string AttachmentError { get; set; } = "";
    [ObservableProperty] public partial string Error { get; set; } = "";

    /// <summary>Typing another address makes the reason of the last refusal obsolete.</summary>
    partial void OnNewAttachmentUrlChanged(string value) => AttachmentError = "";

    public bool IsTimed => !IsAllDay;
    public bool HasMeetLink => !string.IsNullOrEmpty(MeetLink);
    public bool HasAttendees => Attendees.Count > 0;
    public bool HasAttachments => Attachments.Count > 0;
    public bool CanAddAttachment => !AttachmentsUnread && Attachments.Count < AttachmentLink.MaxCount;

    /// <summary>
    /// The event is on Google and its attachments were never read (it was synced before they were): a link added now would be sent alone and replace
    /// the ones Google has, so none can be added until the next sync reads them. An event of this PC has nothing to lose, and a new one starts with none.
    /// </summary>
    public bool AttachmentsUnread => !IsNew && _original.Attachments is null && GoogleSyncService.IsGoogleCalendar(_original.CalendarId);

    public bool CanAddReminder => Reminders.Count < 5;

    partial void OnRecurrenceChanged(ChoiceOption<string?> value)
    {
        RefreshReminderPreviews();
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

    partial void OnIsAllDayChanged(bool value)
    {
        OnPropertyChanged(nameof(IsTimed));
        SecondZoneChanged();
        RefreshReminderPreviews();
    }
    partial void OnMeetLinkChanged(string? value) => OnPropertyChanged(nameof(HasMeetLink));

    private void BuildSoundOptions(IEnumerable<EventReminder> existing)
    {
        SoundOptions.Add(new SoundOption(L.T("settings.default_sound"), null));
        SoundOptions.Add(new SoundOption(L.T("sound.none"), EventReminder.NoSound));
        if (Directory.Exists(SoundService.MediaFolder))
        {
            foreach (var f in Directory.EnumerateFiles(SoundService.MediaFolder, "*.wav").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                SoundOptions.Add(new SoundOption(L.T("sound.windows", Path.GetFileNameWithoutExtension(f)), f));
        }

        foreach (var p in existing.Select(r => r.SoundPath).Where(p => p is not null && p != EventReminder.NoSound).Distinct())
        {
            if (SoundOptions.All(s => s.Path != p)) SoundOptions.Add(new SoundOption(L.T("sound.file", Path.GetFileName(p)), p));
        }
    }

    /// <summary>Registers a user-chosen audio file and assigns it to the reminder.</summary>
    public void UseCustomSound(ReminderItemViewModel reminder, string path)
    {
        var option = SoundOptions.FirstOrDefault(s => s.Path == path);
        if (option is null)
        {
            option = new SoundOption(L.T("sound.file", Path.GetFileName(path)), path);
            SoundOptions.Add(option);
        }

        reminder.Sound = option;
    }

    [RelayCommand]
    private void AddReminder()
    {
        if (!CanAddReminder) return;
        Reminders.Add(new ReminderItemViewModel(new EventReminder(ReminderMethod.Popup, 10), SoundOptions));
        RefreshReminderPreviews();
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
        _sound?.Play(PreviewPathOf(r), _volume);
    }

    /// <summary>What the preview of a reminder plays: its sound, else the default of its kind (popup or alarm), as when it rings.</summary>
    public string? PreviewPathOf(ReminderItemViewModel r) => ReminderSounds.Choose(r.ToModel(), _settings);

    public void StopSound() => _sound?.Stop();

    [RelayCommand]
    private void AddAttendee()
    {
        Error = "";
        foreach (var raw in NewAttendee.Split([',', ';', ' ', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!EmailPattern.IsMatch(raw)) { Error = L.T("edit.error_email", raw); return; }
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

    [RelayCommand]
    private void AddAttachment()
    {
        AttachmentError = "";
        if (AttachmentsUnread || string.IsNullOrWhiteSpace(NewAttachmentUrl)) return;
        var attachment = AttachmentLink.Create(NewAttachmentUrl, NewAttachmentTitle);
        if (attachment is null) { AttachmentError = L.T("edit.error_attachment", NewAttachmentUrl.Trim()); return; }
        if (Attachments.Count >= AttachmentLink.MaxCount) { AttachmentError = L.T("edit.error_attachments_max", AttachmentLink.MaxCount); return; }
        if (!Attachments.Any(x => x.Url == attachment.Url))
        {
            Attachments.Add(attachment);
            _attachmentsTouched = true;
        }

        NewAttachmentUrl = "";
        NewAttachmentTitle = "";
        OnPropertyChanged(nameof(HasAttachments));
        OnPropertyChanged(nameof(CanAddAttachment));
    }

    [RelayCommand]
    private void RemoveAttachment(EventAttachment attachment)
    {
        Attachments.Remove(attachment);
        _attachmentsTouched = true;
        OnPropertyChanged(nameof(HasAttachments));
        OnPropertyChanged(nameof(CanAddAttachment));
    }

    /// <summary>Builds the edited event, or returns null and sets <see cref="Error"/> when the input is invalid.</summary>
    public CalendarEvent? TryBuild()
    {
        Error = "";
        if (!string.IsNullOrWhiteSpace(NewAttendee)) AddAttendee();
        if (Error.Length > 0) return null;
        AttachmentError = "";
        if (!string.IsNullOrWhiteSpace(NewAttachmentUrl)) AddAttachment();
        if (AttachmentError.Length > 0) { Error = AttachmentError; return null; }
        if (SelectedCalendar is null) { Error = L.T("edit.error_calendar"); return null; }
        if (StartDate is null || EndDate is null) { Error = L.T("edit.error_dates"); return null; }

        DateTime start, end;
        if (IsAllDay)
        {
            start = StartDate.Value.Date;
            end = EndDate.Value.Date.AddDays(1);
        }
        else
        {
            if (StartTime is null || EndTime is null) { Error = L.T("edit.error_times"); return null; }
            start = StartDate.Value.Date + StartTime.Value;
            end = EndDate.Value.Date + EndTime.Value;
        }

        if (end <= start) { Error = L.T("edit.error_end"); return null; }

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
            Attachments = _original.Attachments is null && !_attachmentsTouched ? null : Attachments.ToList(),
            Reminders = Reminders.Where(r => !r.IsPast).Select(r => r.ToModel()).ToList(),
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


