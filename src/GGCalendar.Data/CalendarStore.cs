using System.Text.Json;
using System.Text.Json.Serialization;
using GGCalendar.Core.Models;
using Microsoft.Data.Sqlite;

namespace GGCalendar.Data;

public enum OutboxOperation { Create, Update, Delete }

/// <summary>A local change not yet pushed to the remote provider.</summary>
public sealed record OutboxItem(long Seq, string EventId, string CalendarId, OutboxOperation Operation, string? Payload);

/// <summary>SQLite cache of calendars and events plus the queue of pending remote changes.</summary>
public sealed class CalendarStore
{
    private const int SchemaVersion = 2;

    private static readonly JsonSerializerOptions Json = new() { Converters = { new JsonStringEnumConverter() } };

    private readonly string _connectionString;

    public CalendarStore(string? path = null)
    {
        path ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GGCalendar", "calendar.db");
        if (path != ":memory:") Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        _connectionString = new SqliteConnectionStringBuilder { DataSource = path, Pooling = false, ForeignKeys = true }.ToString();
        _keepAlive = path == ":memory:" ? Open() : null;
        Migrate();
    }

    // An in-memory database lives only as long as its connection, so it is kept open for tests.
    private readonly SqliteConnection? _keepAlive;

    private SqliteConnection Connection() => _keepAlive ?? Open();

    private SqliteConnection Open()
    {
        var c = new SqliteConnection(_connectionString);
        c.Open();
        return c;
    }

    private void Run(SqliteConnection c, string sql, params (string Name, object? Value)[] args)
    {
        using var cmd = Command(c, sql, args);
        cmd.ExecuteNonQuery();
    }

    private static SqliteCommand Command(SqliteConnection c, string sql, (string Name, object? Value)[] args)
    {
        var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in args) cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return cmd;
    }

    private void Migrate()
    {
        var c = Connection();
        try
        {
            using var version = c.CreateCommand();
            version.CommandText = "PRAGMA user_version";
            if (Convert.ToInt32(version.ExecuteScalar()) >= SchemaVersion) return;

            Run(c, """
                CREATE TABLE IF NOT EXISTS calendars (
                    id TEXT PRIMARY KEY,
                    account_name TEXT NOT NULL,
                    name TEXT NOT NULL,
                    color TEXT NOT NULL,
                    is_visible INTEGER NOT NULL DEFAULT 1,
                    sort_order INTEGER NOT NULL DEFAULT 0
                );
                CREATE TABLE IF NOT EXISTS events (
                    id TEXT PRIMARY KEY,
                    calendar_id TEXT NOT NULL REFERENCES calendars(id) ON DELETE CASCADE,
                    start_at TEXT NOT NULL,
                    end_at TEXT NOT NULL,
                    is_recurring INTEGER NOT NULL DEFAULT 0,
                    remote_id TEXT,
                    etag TEXT,
                    data TEXT NOT NULL
                );
                CREATE INDEX IF NOT EXISTS ix_events_range ON events(start_at, end_at);
                CREATE INDEX IF NOT EXISTS ix_events_calendar ON events(calendar_id);
                CREATE TABLE IF NOT EXISTS outbox (
                    seq INTEGER PRIMARY KEY AUTOINCREMENT,
                    event_id TEXT NOT NULL,
                    calendar_id TEXT NOT NULL,
                    operation TEXT NOT NULL,
                    payload TEXT,
                    created_at TEXT NOT NULL
                );
                CREATE TABLE IF NOT EXISTS sync_state (
                    calendar_id TEXT PRIMARY KEY,
                    sync_token TEXT NOT NULL
                );
                """);
            Run(c, $"PRAGMA user_version = {SchemaVersion}");
            Run(c, "PRAGMA journal_mode = WAL");
        }
        finally
        {
            if (_keepAlive is null) c.Dispose();
        }
    }

    private T WithConnection<T>(Func<SqliteConnection, T> action)
    {
        var c = Connection();
        try { return action(c); }
        finally { if (_keepAlive is null) c.Dispose(); }
    }

    private void WithConnection(Action<SqliteConnection> action) => WithConnection<object?>(c => { action(c); return null; });

    // Calendars

    public IReadOnlyList<CalendarInfo> GetCalendars() => WithConnection(c =>
    {
        using var cmd = Command(c, "SELECT id, account_name, name, color, is_visible FROM calendars ORDER BY account_name, sort_order, name", []);
        using var r = cmd.ExecuteReader();
        var list = new List<CalendarInfo>();
        while (r.Read()) list.Add(new CalendarInfo(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetInt32(4) != 0));
        return list;
    });

    public void UpsertCalendar(CalendarInfo calendar) => WithConnection(c => Run(c, """
        INSERT INTO calendars (id, account_name, name, color, is_visible) VALUES ($id, $account, $name, $color, $visible)
        ON CONFLICT(id) DO UPDATE SET account_name = $account, name = $name, color = $color, is_visible = $visible
        """,
        ("$id", calendar.Id), ("$account", calendar.AccountName), ("$name", calendar.Name), ("$color", calendar.ColorHex), ("$visible", calendar.IsVisible ? 1 : 0)));

    public void SetCalendarVisible(string calendarId, bool visible) =>
        WithConnection(c => Run(c, "UPDATE calendars SET is_visible = $v WHERE id = $id", ("$v", visible ? 1 : 0), ("$id", calendarId)));

    /// <summary>Deletes a calendar together with its events.</summary>
    public void ClearSyncTokens() => WithConnection(c => Run(c, "DELETE FROM sync_state", []));

    public void DeleteCalendar(string calendarId) =>
        WithConnection(c => Run(c, "DELETE FROM calendars WHERE id = $id", ("$id", calendarId)));

    // Events

    public IReadOnlyList<CalendarEvent> GetAllEvents() => WithConnection(c => ReadEvents(c, "SELECT data FROM events ORDER BY start_at", []));

    /// <summary>Events overlapping [from, to), plus every recurring event (the series start may precede the range).</summary>
    public IReadOnlyList<CalendarEvent> GetEvents(DateTime from, DateTime to) => WithConnection(c => ReadEvents(c,
        "SELECT data FROM events WHERE is_recurring = 1 OR (start_at < $to AND end_at > $from) ORDER BY start_at",
        [("$from", Stamp(from)), ("$to", Stamp(to))]));

    private static IReadOnlyList<CalendarEvent> ReadEvents(SqliteConnection c, string sql, (string, object?)[] args)
    {
        using var cmd = Command(c, sql, args);
        using var r = cmd.ExecuteReader();
        var list = new List<CalendarEvent>();
        while (r.Read())
        {
            if (JsonSerializer.Deserialize<CalendarEvent>(r.GetString(0), Json) is { } ev) list.Add(ev);
        }

        return list;
    }

    public void UpsertEvent(CalendarEvent ev, string? remoteId = null, string? etag = null) => WithConnection(c => Run(c, """
        INSERT INTO events (id, calendar_id, start_at, end_at, is_recurring, remote_id, etag, data)
        VALUES ($id, $cal, $start, $end, $rec, $remote, $etag, $data)
        ON CONFLICT(id) DO UPDATE SET calendar_id = $cal, start_at = $start, end_at = $end, is_recurring = $rec,
            remote_id = COALESCE($remote, remote_id), etag = COALESCE($etag, etag), data = $data
        """,
        ("$id", ev.Id), ("$cal", ev.CalendarId), ("$start", Stamp(ev.Start)), ("$end", Stamp(ev.End)),
        ("$rec", string.IsNullOrEmpty(ev.Recurrence) ? 0 : 1), ("$remote", remoteId), ("$etag", etag),
        ("$data", JsonSerializer.Serialize(ev, Json))));

    public void DeleteEvent(string eventId) => WithConnection(c => Run(c, "DELETE FROM events WHERE id = $id", ("$id", eventId)));

    public (string? RemoteId, string? Etag)? GetRemoteInfo(string eventId) => WithConnection(c =>
    {
        using var cmd = Command(c, "SELECT remote_id, etag FROM events WHERE id = $id", [("$id", eventId)]);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return ((string?, string?)?)null;
        return (r.IsDBNull(0) ? null : r.GetString(0), r.IsDBNull(1) ? null : r.GetString(1));
    });

    // Outbox

    public long Enqueue(string eventId, string calendarId, OutboxOperation operation, CalendarEvent? snapshot = null) => WithConnection(c =>
    {
        using var cmd = Command(c, """
            INSERT INTO outbox (event_id, calendar_id, operation, payload, created_at) VALUES ($e, $c, $op, $p, $t);
            SELECT last_insert_rowid();
            """,
            [("$e", eventId), ("$c", calendarId), ("$op", operation.ToString()),
             ("$p", snapshot is null ? null : JsonSerializer.Serialize(snapshot, Json)), ("$t", DateTime.UtcNow.ToString("O"))]);
        return Convert.ToInt64(cmd.ExecuteScalar());
    });

    public IReadOnlyList<OutboxItem> GetPending() => WithConnection(c =>
    {
        using var cmd = Command(c, "SELECT seq, event_id, calendar_id, operation, payload FROM outbox ORDER BY seq", []);
        using var r = cmd.ExecuteReader();
        var list = new List<OutboxItem>();
        while (r.Read())
            list.Add(new OutboxItem(r.GetInt64(0), r.GetString(1), r.GetString(2), Enum.Parse<OutboxOperation>(r.GetString(3)), r.IsDBNull(4) ? null : r.GetString(4)));
        return list;
    });

    public void Complete(long seq) => WithConnection(c => Run(c, "DELETE FROM outbox WHERE seq = $s", ("$s", seq)));

    public static CalendarEvent? ReadSnapshot(OutboxItem item) =>
        item.Payload is null ? null : JsonSerializer.Deserialize<CalendarEvent>(item.Payload, Json);

    public bool HasPending(string eventId) => GetPending().Any(p => p.EventId == eventId);

    /// <summary>Queues a remote deletion; the payload carries the remote id because the local row is already gone.</summary>
    public long EnqueueDelete(string eventId, string calendarId, string remoteId) => WithConnection(c =>
    {
        using var cmd = Command(c, """
            INSERT INTO outbox (event_id, calendar_id, operation, payload, created_at) VALUES ($e, $c, 'Delete', $p, $t);
            SELECT last_insert_rowid();
            """, [("$e", eventId), ("$c", calendarId), ("$p", remoteId), ("$t", DateTime.UtcNow.ToString("O"))]);
        return Convert.ToInt64(cmd.ExecuteScalar());
    });

    /// <summary>Forgets the remote link, so the next push creates the event again (used when it moves to another calendar).</summary>
    public void ClearRemote(string eventId) =>
        WithConnection(c => Run(c, "UPDATE events SET remote_id = NULL, etag = NULL WHERE id = $id", ("$id", eventId)));

    public void SetRemote(string eventId, string remoteId, string? etag) =>
        WithConnection(c => Run(c, "UPDATE events SET remote_id = $r, etag = $e WHERE id = $id", ("$r", remoteId), ("$e", etag), ("$id", eventId)));

    public string? FindLocalId(string calendarId, string remoteId) => WithConnection(c =>
    {
        using var cmd = Command(c, "SELECT id FROM events WHERE calendar_id = $c AND remote_id = $r", [("$c", calendarId), ("$r", remoteId)]);
        return cmd.ExecuteScalar() as string;
    });

    /// <summary>Events of a calendar that exist remotely: (local id, remote id).</summary>
    public IReadOnlyList<(string Id, string RemoteId)> GetSyncedEvents(string calendarId) => WithConnection(c =>
    {
        using var cmd = Command(c, "SELECT id, remote_id FROM events WHERE calendar_id = $c AND remote_id IS NOT NULL", [("$c", calendarId)]);
        using var r = cmd.ExecuteReader();
        var list = new List<(string, string)>();
        while (r.Read()) list.Add((r.GetString(0), r.GetString(1)));
        return list;
    });

    public string? GetSyncToken(string calendarId) => WithConnection(c =>
    {
        using var cmd = Command(c, "SELECT sync_token FROM sync_state WHERE calendar_id = $c", [("$c", calendarId)]);
        return cmd.ExecuteScalar() as string;
    });

    public void SetSyncToken(string calendarId, string? token) => WithConnection(c =>
    {
        if (token is null) Run(c, "DELETE FROM sync_state WHERE calendar_id = $c", ("$c", calendarId));
        else Run(c, "INSERT INTO sync_state (calendar_id, sync_token) VALUES ($c, $t) ON CONFLICT(calendar_id) DO UPDATE SET sync_token = $t",
            ("$c", calendarId), ("$t", token));
    });

    private static string Stamp(DateTime d) => d.ToString("yyyy-MM-dd'T'HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
}
