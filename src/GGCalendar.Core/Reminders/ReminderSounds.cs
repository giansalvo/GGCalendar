using GGCalendar.Core.Models;

namespace GGCalendar.Core.Reminders;

/// <summary>
/// Which sound a reminder plays (issue #32). A reminder has its own sound or none, else the default of its kind: popups have one default sound,
/// alarms another (an alarm rings in a loop for a longer time, so it needs a different sound than the short one of a popup). A file that
/// is no longer there is replaced by a sound of Windows, so that an alarm is never silent for a file that was moved.
/// </summary>
public static class ReminderSounds
{
    /// <summary>The largest file a user can choose as sound (10 MB, like the files read by the import).</summary>
    public const long MaxFileBytes = 10 * 1024 * 1024;

    /// <summary>The kinds of file the user can choose.</summary>
    public static readonly string[] Extensions = [".mp3", ".wav"];

    private static string Media => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media");

    /// <summary>The sound of Windows an alarm falls back on.</summary>
    public static string SystemAlarmSound => Path.Combine(Media, "Alarm01.wav");

    /// <summary>The sound of Windows a popup falls back on.</summary>
    public static string SystemReminderSound => Path.Combine(Media, "Windows Notify Calendar.wav");

    /// <summary>The path the reminder should play: null for silence. The file may not exist: see <see cref="Playable"/>.</summary>
    public static string? Choose(EventReminder reminder, AppSettings settings)
    {
        var isAlarm = reminder.Method == ReminderMethod.Alarm;
        return reminder.SoundPath switch
        {
            null => isAlarm ? settings.DefaultAlarmSound : settings.DefaultReminderSound,
            // "No sound" is for popups: an alarm that did not ring would be of no use, so it rings with the default of the alarms.
            EventReminder.NoSound => isAlarm ? settings.DefaultAlarmSound : null,
            var path => path,
        };
    }

    /// <summary>The file to play for <paramref name="chosen"/>: itself when it is there, else the sound of Windows of its kind, else null.</summary>
    public static string? Playable(string? chosen, bool isAlarm, Func<string, bool>? exists = null)
    {
        exists ??= File.Exists;
        if (string.IsNullOrWhiteSpace(chosen)) return null;
        if (exists(chosen)) return chosen;
        var system = isAlarm ? SystemAlarmSound : SystemReminderSound;
        return exists(system) ? system : null;
    }
}
