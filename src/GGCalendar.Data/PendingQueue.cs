using System.Text.Json;
using GGCalendar.Core.Models;

namespace GGCalendar.Data;

public enum QueueKind
{
    /// <summary>Created here, not on Google yet.</summary>
    New,

    /// <summary>On Google, changed here.</summary>
    Edited,

    /// <summary>On Google, deleted here.</summary>
    Deleted,

    /// <summary>Moved to another calendar: deleted at the origin and created at the destination, both still waiting.</summary>
    Moved,
}

/// <summary>
/// What waits in the queue for one event (issue #19): the rows of the outbox grouped by event, with what the person would call the change.
/// <paramref name="CalendarId"/> is the calendar the event is in now; for a moved event <paramref name="FromCalendarId"/> is the one it left.
/// </summary>
public sealed record QueuedEvent(
    string EventId,
    string CalendarId,
    QueueKind Kind,
    string? Title,
    int Edits,
    DateTime? Since,
    string? FromCalendarId,
    IReadOnlyList<OutboxItem> Rows);

public static class PendingQueue
{
    private static readonly JsonSerializerOptions Json = new() { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };

    /// <summary>Groups the queue by event, in the order the first change of each was made. Rows of the calendars of this PC are not in the queue.</summary>
    public static IReadOnlyList<QueuedEvent> Group(IEnumerable<OutboxItem> rows) =>
        rows.Where(r => r.CalendarId.Contains('|')).GroupBy(r => r.EventId).Select(Make).OrderBy(q => q.Rows[0].Seq).ToList();

    private static QueuedEvent Make(IGrouping<string, OutboxItem> group)
    {
        var rows = group.OrderBy(r => r.Seq).ToList();
        var create = rows.LastOrDefault(r => r.Operation == OutboxOperation.Create);
        var delete = rows.FirstOrDefault(r => r.Operation == OutboxOperation.Delete);
        var kind = create is not null && delete is not null ? QueueKind.Moved
            : create is not null ? QueueKind.New
            : delete is not null ? QueueKind.Deleted
            : QueueKind.Edited;

        // The title: from the last snapshot of the event; a deletion keeps the event as it was (rows made before that have no title).
        var snapshot = rows.Where(r => r.Operation != OutboxOperation.Delete).Select(CalendarStore.ReadSnapshot).LastOrDefault(s => s is not null)
            ?? (delete?.Base is { } deleted ? Read(deleted) : null);
        var calendar = kind == QueueKind.Moved ? create!.CalendarId : rows[0].CalendarId;
        return new QueuedEvent(group.Key, calendar, kind, snapshot?.Title, rows.Count(r => r.Operation == OutboxOperation.Update),
            rows.Min(r => r.CreatedAt), kind == QueueKind.Moved ? delete!.CalendarId : null, rows);
    }

    private static CalendarEvent? Read(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<CalendarEvent>(json, Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
