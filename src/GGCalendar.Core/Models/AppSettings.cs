namespace GGCalendar.Core.Models;

public sealed class AppSettings
{
    public int DayStartHour { get; set; } = 7;
    public int DayEndHour { get; set; } = 22;
    public bool HideOutsideHours { get; set; }
    public DayOfWeek FirstDayOfWeek { get; set; } = DayOfWeek.Monday;
    public bool ShowWeekNumbers { get; set; } = true;
    public bool ShowWeekends { get; set; } = true;

    /// <summary>0 sistema, 1 chiaro, 2 scuro.</summary>
    public int Theme { get; set; }
    public bool DimPastEvents { get; set; } = true;
    public bool DefaultGuestsCanModify { get; set; }
    public bool DefaultGuestsCanInvite { get; set; }
    public bool DefaultGuestsCanSeeList { get; set; } = true;

    /// <summary>0 giorno, 1 settimana, 2 mese, 3 anno, 4 programma, 5 sei giorni.</summary>
    public int DefaultView { get; set; } = 1;
    public bool Use24HourClock { get; set; } = true;
    public int DefaultEventMinutes { get; set; } = 60;
    public int SnapMinutes { get; set; } = 15;
    public string PrimaryTimeZoneId { get; set; } = TimeZoneInfo.Local.Id;
    public string? SecondaryTimeZoneId { get; set; }
    public bool NotificationsEnabled { get; set; } = true;
    public string DefaultReminderSound { get; set; } = @"C:\Windows\Media\Windows Notify Calendar.wav";
    public int ReminderVolumePercent { get; set; } = 80;
    public string OllamaUrl { get; set; } = "http://localhost:11434";
    public string OllamaModel { get; set; } = "qwen3.6:latest";
    public string? OllamaFallbackModel { get; set; } = "gemma4:latest";
}
