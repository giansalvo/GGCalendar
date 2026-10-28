namespace GGCalendar.Core.Models;

public sealed class AppSettings
{
    /// <summary>
    /// Version of what the sync stores. 1: alarms come from Google (<see cref="EventReminder.AlarmProperty"/>), so the
    /// first start after the update downloads everything again once.
    /// </summary>
    public int SyncSchema { get; set; }

    /// <summary>The version of the app that showed the splash screen last; another version (or none) shows it again.</summary>
    public string? SplashVersion { get; set; }

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

    /// <summary>Optional override of the embedded Google OAuth client (advanced).</summary>
    public string? GoogleClientId { get; set; }
    public string? GoogleClientSecret { get; set; }
    /// <summary>Intervallo di sincronizzazione automatica in minuti; 0 = mai.</summary>
    public int SyncIntervalMinutes { get; set; } = 5;

    /// <summary>How often the public calendars (by address) are downloaded again, in minutes; 0 = only by hand.</summary>
    public int SubscriptionIntervalMinutes { get; set; } = 30;

    /// <summary>Calendars the search looks in: 0 = the ones shown, 1 = all, 2 = the ones in <see cref="SearchCalendarIds"/>.</summary>
    public int SearchScope { get; set; }

    public List<string> SearchCalendarIds { get; set; } = [];

    /// <summary>0 giorno, 1 settimana, 2 mese, 3 anno, 4 programma, 5 sei giorni.</summary>
    public int DefaultView { get; set; } = 1;
    public bool Use24HourClock { get; set; } = true;

    /// <summary>"system" (the language of Windows, read when the app starts) or a language code ("it", "en", "de", "es", "fr").</summary>
    public string Language { get; set; } = "system";
    public int DefaultEventMinutes { get; set; } = 60;
    public int SnapMinutes { get; set; } = 15;
    /// <summary>Second time zone of the time grids (a system zone id), or null for none.</summary>
    public string? SecondaryTimeZoneId { get; set; }

    /// <summary>Name of the column of the second time zone (up to 6 characters); null or empty: the name of its city.</summary>
    public string? SecondaryTimeZoneLabel { get; set; }
    public bool NotificationsEnabled { get; set; } = true;
    public string DefaultReminderSound { get; set; } = @"C:\Windows\Media\Windows Notify Calendar.wav";
    public int ReminderVolumePercent { get; set; } = 80;
    public string OllamaUrl { get; set; } = "http://localhost:11434";
    public string OllamaModel { get; set; } = "qwen3.6:latest";
    public string? OllamaFallbackModel { get; set; } = "gemma4:latest";
}
