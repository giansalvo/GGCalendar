using System.Text.Json;
using System.Text.Json.Serialization;
using GGCalendar.Core;
using GGCalendar.Core.Reminders;

namespace GGCalendar.Data;

public sealed record SnoozedReminder(DateTime At, DueReminder Reminder);

/// <summary>What the reminder scheduler must remember across restarts.</summary>
public sealed class ReminderState
{
    /// <summary>End of the last checked interval; reminders due after it and before the next check are shown.</summary>
    public DateTime LastCheck { get; set; }

    /// <summary>Keys of the reminders already shown, with their fire time (used to prune old entries).</summary>
    public Dictionary<string, DateTime> Fired { get; set; } = [];

    public List<SnoozedReminder> Snoozed { get; set; } = [];
}

public sealed class ReminderStateStore(string? path = null)
{
    private static readonly JsonSerializerOptions Options = new() { Converters = { new JsonStringEnumConverter() } };

    public string FilePath { get; } = path ?? AppPaths.FileInDataDirectory("reminders.json");

    public ReminderState Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<ReminderState>(File.ReadAllText(FilePath), Options) ?? new ReminderState();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // A corrupt or unreadable file only loses the catch-up of missed reminders.
        }

        return new ReminderState();
    }

    public void Save(ReminderState state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(state, Options));
        File.Move(tmp, FilePath, overwrite: true);
    }
}
