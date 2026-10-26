using System.Text.Json;
using System.Text.Json.Serialization;
using GGCalendar.Core;
using GGCalendar.Core.Models;
using GGCalendar.Core.Subscriptions;
using Microsoft.Data.Sqlite;

namespace GGCalendar.Data;

public enum OutboxOperation { Create, Update, Delete }

/// <summary>An event received from the remote provider, with the identifiers needed to keep it linked.</summary>
public sealed record RemoteEvent(CalendarEvent Event, string RemoteId, string? Etag);

/// <summary>
/// A local change not yet pushed to the remote provider. <paramref name="Base"/> is the event as it was before the change
/// (for updates), used to tell a real remote edit from a version change made by the provider itself.
/// </summary>
/// <param name="CreatedAt">When the change was queued (UTC).</param>
public sealed record OutboxItem(long Seq, string EventId, string CalendarId, OutboxOperation Operation, string? Payload, string? Base = null, DateTime? CreatedAt = null);

/// <summary>SQLite cache of calendars and events plus the queue of pending remote changes.</summary>
public sealed class CalendarStore
{
    private const int SchemaVersion = 6;

    private static readonly JsonSerializerOptions Json = new() { Converters = { new JsonStringEnumConverter() } };

    private readonly string _connectionString;

    public CalendarStore(string? path = null)
    {
        path ??= AppPaths.FileInDataDirectory("calendar.db");
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
                    created_at TEXT NOT NULL,
                    base TEXT
                );
                CREATE INDEX IF NOT EXISTS ix_outbox_event ON outbox(event_id);
                CREATE TABLE IF NOT EXISTS sync_state (
                    calendar_id TEXT PRIMARY KEY,
                    sync_token TEXT NOT NULL
                );
                CREATE TABLE IF NOT EXISTS subscriptions (
                    calendar_id TEXT PRIMARY KEY REFERENCES calendars(id) ON DELETE CASCADE,
                    url TEXT NOT NULL,
                    etag TEXT,
                    last_modified TEXT,
                    hash TEXT,
                    last_fetched TEXT,
                    last_synced TEXT,
                    last_error TEXT,
                    range_from TEXT,
                    range_to TEXT
                );
                """);

            // v4: outbox.base (databases created before it have the table without the column).
            using (var columns = Command(c, "SELECT COUNT(*) FROM pragma_table_info('outbox') WHERE name = 'base'", []))
            {
                if (Convert.ToInt64(columns.ExecuteScalar()) == 0) Run(c, "ALTER TABLE outbox ADD COLUMN base TEXT");
            }

            // v6: calendars.is_owner (whether the person owns a Google calendar; null until the next sync tells it).
            using (var columns = Command(c, "SELECT COUNT(*) FROM pragma_table_info('calendars') WHERE name = 'is_owner'", []))
            {
                if (Convert.ToInt64(columns.ExecuteScalar()) == 0) Run(c, "ALTER TABLE calendars ADD COLUMN is_owner INTEGER");
            }

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
        using var cmd = Command(c, "SELECT id, account_name, name, color, is_visible, is_owner FROM calendars ORDER BY account_name, sort_order, name", []);
        using var r = cmd.ExecuteReader();
        var list = new List<CalendarInfo>();
        while (r.Read()) list.Add(new CalendarInfo(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetInt32(4) != 0, r.IsDBNull(5) ? null : r.GetInt32(5) != 0));
        return list;
    });

    public void UpsertCalendar(CalendarInfo calendar) => WithConnection(c => Run(c, """
        INSERT INTO calendars (id, account_name, name, color, is_visible, is_owner) VALUES ($id, $account, $name, $color, $visible, $owner)
        ON CONFLICT(id) DO UPDATE SET account_name = $account, name = $name, color = $color, is_visible = $visible, is_owner = COALESCE($owner, is_owner)
        """,
        ("$id", calendar.Id), ("$account", calendar.AccountName), ("$name", calendar.Name), ("$color", calendar.ColorHex), ("$visible", calendar.IsVisible ? 1 : 0),
        ("$owner", calendar.IsOwner is { } owner ? (owner ? 1 : 0) : null)));

    public void SetCalendarVisible(string calendarId, bool visible) =>
        WithConnection(c => Run(c, "UPDATE calendars SET is_visible = $v WHERE id = $id", ("$v", visible ? 1 : 0), ("$id", calendarId)));

    public void ClearSyncTokens() => WithConnection(c => Run(c, "DELETE FROM sync_state", []));

    /// <summary>Deletes a calendar together with its events, its queued changes and its sync position.</summary>
    public void DeleteCalendar(string calendarId) => WithConnection(c =>
    {
        using var tx = c.BeginTransaction();
        Run(c, "DELETE FROM outbox WHERE calendar_id = $id", ("$id", calendarId));
        Run(c, "DELETE FROM sync_state WHERE calendar_id = $id", ("$id", calendarId));
        Run(c, "DELETE FROM subscriptions WHERE calendar_id = $id", ("$id", calendarId));
        Run(c, "DELETE FROM calendars WHERE id = $id", ("$id", calendarId));
        tx.Commit();
    });


    // Subscriptions to public calendars (read-only; the address is stored encrypted by the caller)

    private const string SubscriptionColumns = "calendar_id, url, etag, last_modified, hash, last_fetched, last_synced, last_error, range_from, range_to";

    private static SubscriptionRecord ReadSubscription(SqliteDataReader r) => new(
        r.GetString(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2), r.IsDBNull(3) ? null : r.GetString(3), r.IsDBNull(4) ? null : r.GetString(4),
        ReadDate(r, 5), ReadDate(r, 6), r.IsDBNull(7) ? null : r.GetString(7), ReadDate(r, 8), ReadDate(r, 9));

    private static DateTime? ReadDate(SqliteDataReader r, int i) =>
        r.IsDBNull(i) ? null : DateTime.Parse(r.GetString(i), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind);

    private static string? WriteDate(DateTime? d) => d?.ToString("O", System.Globalization.CultureInfo.InvariantCulture);

    public IReadOnlyList<SubscriptionRecord> GetSubscriptions() => WithConnection(c =>
    {
        using var cmd = Command(c, $"SELECT {SubscriptionColumns} FROM subscriptions ORDER BY calendar_id", []);
        using var r = cmd.ExecuteReader();
        var list = new List<SubscriptionRecord>();
        while (r.Read()) list.Add(ReadSubscription(r));
        return list;
    });

    public SubscriptionRecord? GetSubscription(string calendarId) => WithConnection(c =>
    {
        using var cmd = Command(c, $"SELECT {SubscriptionColumns} FROM subscriptions WHERE calendar_id = $id", [("$id", calendarId)]);
        using var r = cmd.ExecuteReader();
        return r.Read() ? ReadSubscription(r) : null;
    });

    /// <summary>Creates the calendar of a subscription with its record and its first events in one transaction.</summary>
    public void AddSubscription(CalendarInfo calendar, SubscriptionRecord record, IReadOnlyList<CalendarEvent> events) => WithConnection(c =>
    {
        using var tx = c.BeginTransaction();
        Run(c, "INSERT INTO calendars (id, account_name, name, color, is_visible) VALUES ($id, $account, $name, $color, $visible)",
            ("$id", calendar.Id), ("$account", calendar.AccountName), ("$name", calendar.Name), ("$color", calendar.ColorHex), ("$visible", calendar.IsVisible ? 1 : 0));
        WriteSubscription(c, record);
        foreach (var ev in events) Upsert(c, ev, null, null);
        tx.Commit();
    });

    /// <summary>Stores the bookkeeping of a subscription (validators, hash, times, last error, range); the events are not touched.</summary>
    public void UpdateSubscription(SubscriptionRecord record) => WithConnection(c => WriteSubscription(c, record));

    private void WriteSubscription(SqliteConnection c, SubscriptionRecord s) => Run(c, """
        INSERT INTO subscriptions (calendar_id, url, etag, last_modified, hash, last_fetched, last_synced, last_error, range_from, range_to)
        VALUES ($id, $url, $etag, $lm, $hash, $fetched, $synced, $error, $from, $to)
        ON CONFLICT(calendar_id) DO UPDATE SET url = $url, etag = $etag, last_modified = $lm, hash = $hash, last_fetched = $fetched,
            last_synced = $synced, last_error = $error, range_from = $from, range_to = $to
        """,
        ("$id", s.CalendarId), ("$url", s.EncryptedUrl), ("$etag", s.ETag), ("$lm", s.LastModified), ("$hash", s.Hash),
        ("$fetched", WriteDate(s.LastFetchedUtc)), ("$synced", WriteDate(s.LastSyncedUtc)), ("$error", s.LastError),
        ("$from", WriteDate(s.RangeFrom)), ("$to", WriteDate(s.RangeTo)));

    /// <summary>
    /// Replaces the events of a subscription with the new ones by difference, in one transaction together with its record:
    /// only the events that are new, changed or gone are written.
    /// </summary>
    public SubscriptionChanges ReplaceSubscriptionEvents(string calendarId, IReadOnlyList<CalendarEvent> events, SubscriptionRecord record) => WithConnection(c =>
    {
        using var tx = c.BeginTransaction();
        var existing = new Dictionary<string, string>();
        using (var cmd = Command(c, "SELECT id, data FROM events WHERE calendar_id = $c", [("$c", calendarId)]))
        using (var r = cmd.ExecuteReader())
            while (r.Read()) existing[r.GetString(0)] = r.GetString(1);

        int added = 0, changed = 0, removed = 0;
        var keep = new HashSet<string>();
        foreach (var ev in events)
        {
            keep.Add(ev.Id);
            var json = JsonSerializer.Serialize(ev, Json);
            if (!existing.TryGetValue(ev.Id, out var old)) added++;
            else if (old != json) changed++;
            else continue;
            Upsert(c, ev, null, null);
        }

        foreach (var id in existing.Keys.Where(id => !keep.Contains(id)))
        {
            Run(c, "DELETE FROM events WHERE id = $id", ("$id", id));
            removed++;
        }

        WriteSubscription(c, record);
        tx.Commit();
        return new SubscriptionChanges(added, changed, removed);
    });

    // Events

    public CalendarEvent? GetEvent(string eventId) =>
        WithConnection(c => ReadEvents(c, "SELECT data FROM events WHERE id = $id", [("$id", eventId)])).FirstOrDefault();

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

    public void UpsertEvent(CalendarEvent ev, string? remoteId = null, string? etag = null) => WithConnection(c => Upsert(c, ev, remoteId, etag));

    private void Upsert(SqliteConnection c, CalendarEvent ev, string? remoteId, string? etag) => Run(c, """
        INSERT INTO events (id, calendar_id, start_at, end_at, is_recurring, remote_id, etag, data)
        VALUES ($id, $cal, $start, $end, $rec, $remote, $etag, $data)
        ON CONFLICT(id) DO UPDATE SET calendar_id = $cal, start_at = $start, end_at = $end, is_recurring = $rec,
            remote_id = COALESCE($remote, remote_id), etag = COALESCE($etag, etag), data = $data
        """,
        ("$id", ev.Id), ("$cal", ev.CalendarId), ("$start", Stamp(ev.Start)), ("$end", Stamp(ev.End)),
        ("$rec", string.IsNullOrEmpty(ev.Recurrence) ? 0 : 1), ("$remote", remoteId), ("$etag", etag),
        ("$data", JsonSerializer.Serialize(ev, Json)));

    /// <summary>
    /// Applies an import in one transaction (one write to the disk instead of one per event): the events that the import replaces are
    /// removed with their modified occurrences, the new ones are stored, and for a calendar that lives on Google the matching deletions
    /// and creations are queued.
    /// </summary>
    public void ApplyImport(IReadOnlyList<CalendarEvent> removed, IReadOnlyList<CalendarEvent> added, bool remoteDestination) =>
        WithConnection(c =>
        {
            using var tx = c.BeginTransaction();
            var now = DateTime.UtcNow.ToString("O");
            foreach (var old in removed)
            {
                string? remoteId = null;
                if (remoteDestination)
                {
                    using var cmd = Command(c, "SELECT remote_id FROM events WHERE id = $id", [("$id", old.Id)]);
                    remoteId = cmd.ExecuteScalar() as string;
                }

                RemoveSeries(c, old.Id);
                if (remoteDestination && remoteId is not null)
                    Run(c, "INSERT INTO outbox (event_id, calendar_id, operation, payload, created_at, base) VALUES ($e, $c, 'Delete', $p, $t, $b)",
                        ("$e", old.Id), ("$c", old.CalendarId), ("$p", remoteId), ("$t", now), ("$b", JsonSerializer.Serialize(old, Json)));
            }

            foreach (var ev in added)
            {
                Upsert(c, ev, null, null);
                if (remoteDestination)
                    Run(c, "INSERT INTO outbox (event_id, calendar_id, operation, payload, created_at) VALUES ($e, $c, $op, $p, $t)",
                        ("$e", ev.Id), ("$c", ev.CalendarId), ("$op", OutboxOperation.Create.ToString()), ("$p", JsonSerializer.Serialize(ev, Json)), ("$t", now));
            }

            tx.Commit();
        });

    public void DeleteEvent(string eventId) => WithConnection(c => Run(c, "DELETE FROM events WHERE id = $id", ("$id", eventId)));

    /// <summary>Deletes an event together with the modified or cancelled occurrences that belong to it.</summary>
    public void DeleteSeries(string eventId) => WithConnection(c => RemoveSeries(c, eventId));

    private void RemoveSeries(SqliteConnection c, string eventId) =>
        Run(c, "DELETE FROM events WHERE id = $id OR json_extract(data, '$.SeriesMasterId') = $id", ("$id", eventId));

    /// <summary>
    /// Applies the result of a remote pull in one transaction: upserts, deletions (with their occurrences) and the new
    /// sync token, so an interrupted pull never stores a token for changes that were not saved.
    /// </summary>
    public void ApplyRemoteChanges(string calendarId, IReadOnlyList<RemoteEvent> upserts, IReadOnlyList<string> deletes, string? syncToken) =>
        WithConnection(c =>
        {
            using var tx = c.BeginTransaction();
            foreach (var u in upserts) Upsert(c, u.Event, u.RemoteId, u.Etag);
            foreach (var id in deletes) RemoveSeries(c, id);
            if (syncToken is null) Run(c, "DELETE FROM sync_state WHERE calendar_id = $c", ("$c", calendarId));
            else Run(c, "INSERT INTO sync_state (calendar_id, sync_token) VALUES ($c, $t) ON CONFLICT(calendar_id) DO UPDATE SET sync_token = $t",
                ("$c", calendarId), ("$t", syncToken));
            tx.Commit();
        });

    public (string? RemoteId, string? Etag)? GetRemoteInfo(string eventId) => WithConnection(c =>
    {
        using var cmd = Command(c, "SELECT remote_id, etag FROM events WHERE id = $id", [("$id", eventId)]);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return ((string?, string?)?)null;
        return (r.IsDBNull(0) ? null : r.GetString(0), r.IsDBNull(1) ? null : r.GetString(1));
    });

    // Outbox

    /// <param name="baseVersion">For updates: the event before the change (see <see cref="OutboxItem.Base"/>).</param>
    public long Enqueue(string eventId, string calendarId, OutboxOperation operation, CalendarEvent? snapshot = null, CalendarEvent? baseVersion = null) =>
        WithConnection(c =>
        {
            using var cmd = Command(c, """
                INSERT INTO outbox (event_id, calendar_id, operation, payload, created_at, base) VALUES ($e, $c, $op, $p, $t, $b);
                SELECT last_insert_rowid();
                """,
                [("$e", eventId), ("$c", calendarId), ("$op", operation.ToString()),
                 ("$p", snapshot is null ? null : JsonSerializer.Serialize(snapshot, Json)), ("$t", DateTime.UtcNow.ToString("O")),
                 ("$b", baseVersion is null ? null : JsonSerializer.Serialize(baseVersion, Json))]);
            return Convert.ToInt64(cmd.ExecuteScalar());
        });

    /// <summary>The events with a creation in the queue that are not on Google (remote_id is empty), for Google calendars (their id has a "|").</summary>
    private const string UnsentCreations = """
        SELECT DISTINCT o.event_id AS id FROM outbox o JOIN events e ON e.id = o.event_id
        WHERE o.operation = 'Create' AND e.remote_id IS NULL AND o.calendar_id LIKE '%|%'
        """;

    /// <summary>How many events are waiting to be created on Google (in the queue, and not on Google yet).</summary>
    public int CountUnsentCreations() => WithConnection(c =>
    {
        using var cmd = Command(c, $"SELECT COUNT(*) FROM ({UnsentCreations})", []);
        return Convert.ToInt32(cmd.ExecuteScalar());
    });

    /// <summary>
    /// Throws away the events that were created on this PC for a Google calendar and have not been sent yet: they are removed from the
    /// queue and from the PC (with the modified occurrences of a series among them), so that this PC and Google agree. What is already on
    /// Google, and the edits and deletions waiting for it, stay. Returns how many events were thrown away.
    /// </summary>
    public int DiscardUnsentCreations() => WithConnection(c =>
    {
        using var tx = c.BeginTransaction();
        Run(c, $"CREATE TEMP TABLE unsent_ids AS {UnsentCreations}");
        int count;
        using (var cmd = Command(c, "SELECT COUNT(*) FROM unsent_ids", [])) count = Convert.ToInt32(cmd.ExecuteScalar());
        Run(c, "DELETE FROM events WHERE id IN (SELECT id FROM unsent_ids) OR json_extract(data, '$.SeriesMasterId') IN (SELECT id FROM unsent_ids)");
        Run(c, "DELETE FROM outbox WHERE event_id IN (SELECT id FROM unsent_ids)");
        Run(c, "DROP TABLE unsent_ids");
        tx.Commit();
        return count;
    });

    public IReadOnlyList<OutboxItem> GetPending() => WithConnection(c =>
    {
        using var cmd = Command(c, "SELECT seq, event_id, calendar_id, operation, payload, base, created_at FROM outbox ORDER BY seq", []);
        using var r = cmd.ExecuteReader();
        var list = new List<OutboxItem>();
        while (r.Read())
            list.Add(new OutboxItem(r.GetInt64(0), r.GetString(1), r.GetString(2), Enum.Parse<OutboxOperation>(r.GetString(3)),
                r.IsDBNull(4) ? null : r.GetString(4), r.IsDBNull(5) ? null : r.GetString(5),
                DateTime.TryParse(r.GetString(6), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var at) ? at : null));
        return list;
    });

    /// <summary>
    /// How many events of Google calendars have changes waiting in the queue (an event with five edits, or a moved one, counts once;
    /// the calendars of this PC have no queue).
    /// </summary>
    public int CountPendingEvents() => WithConnection(c =>
    {
        using var cmd = Command(c, "SELECT COUNT(DISTINCT event_id) FROM outbox WHERE calendar_id LIKE '%|%'", []);
        return Convert.ToInt32(cmd.ExecuteScalar());
    });

    /// <summary>
    /// Gives up the creation of events that are not on Google yet: the events (with their modified occurrences) and every change queued
    /// for them are removed from this PC. Used to cancel a new event, or the copy of an event that was moved to another calendar.
    /// </summary>
    public void DiscardCreatedEvents(IReadOnlyCollection<string> eventIds) => WithConnection(c =>
    {
        if (eventIds.Count == 0) return;
        using var tx = c.BeginTransaction();
        // One pass over the events, however many are cancelled (thousands after an import): a statement for each would take minutes.
        Run(c, "CREATE TEMP TABLE discard_ids (id TEXT PRIMARY KEY)");
        foreach (var id in eventIds) Run(c, "INSERT OR IGNORE INTO discard_ids (id) VALUES ($id)", ("$id", id));
        Run(c, "CREATE TEMP TABLE discard_children AS SELECT id FROM events WHERE json_extract(data, '$.SeriesMasterId') IN (SELECT id FROM discard_ids)");
        Run(c, "INSERT OR IGNORE INTO discard_ids (id) SELECT id FROM discard_children");
        Run(c, "DELETE FROM outbox WHERE event_id IN (SELECT id FROM discard_ids)");
        Run(c, "DELETE FROM events WHERE id IN (SELECT id FROM discard_ids)");
        Run(c, "DROP TABLE discard_children");
        Run(c, "DROP TABLE discard_ids");
        tx.Commit();
    });

    /// <summary>Removes the changes queued for the events and leaves the events as they are (the caller put them back first, or they come back from Google).</summary>
    public void DiscardOutboxOf(IReadOnlyCollection<string> eventIds) => WithConnection(c =>
    {
        using var tx = c.BeginTransaction();
        foreach (var id in eventIds) Run(c, "DELETE FROM outbox WHERE event_id = $id", ("$id", id));
        tx.Commit();
    });

    public void Complete(long seq) => WithConnection(c => Run(c, "DELETE FROM outbox WHERE seq = $s", ("$s", seq)));

    public static CalendarEvent? ReadSnapshot(OutboxItem item) =>
        item.Payload is null ? null : JsonSerializer.Deserialize<CalendarEvent>(item.Payload, Json);

    public static CalendarEvent? ReadBase(OutboxItem item) =>
        item.Base is null ? null : JsonSerializer.Deserialize<CalendarEvent>(item.Base, Json);

    public bool HasPending(string eventId) => HasPendingAfter(eventId, 0);

    /// <summary>True when the event has queued changes newer than <paramref name="seq"/>.</summary>
    public bool HasPendingAfter(string eventId, long seq) => WithConnection(c =>
    {
        using var cmd = Command(c, "SELECT EXISTS (SELECT 1 FROM outbox WHERE event_id = $e AND seq > $s)", [("$e", eventId), ("$s", seq)]);
        return Convert.ToInt64(cmd.ExecuteScalar()) != 0;
    });

    /// <summary>Drops every queued change of an event (used when a conflict replaces the local version).</summary>
    public void DiscardPending(string eventId) => WithConnection(c => Run(c, "DELETE FROM outbox WHERE event_id = $e", ("$e", eventId)));

    /// <summary>Queues a remote deletion; the payload carries the remote id because the local row is already gone.</summary>
    /// <param name="deleted">The event as it was, kept to tell what is waiting in the queue (its title); older rows do not have it.</param>
    public long EnqueueDelete(string eventId, string calendarId, string remoteId, CalendarEvent? deleted = null) => WithConnection(c =>
    {
        using var cmd = Command(c, """
            INSERT INTO outbox (event_id, calendar_id, operation, payload, created_at, base) VALUES ($e, $c, 'Delete', $p, $t, $b);
            SELECT last_insert_rowid();
            """, [("$e", eventId), ("$c", calendarId), ("$p", remoteId), ("$t", DateTime.UtcNow.ToString("O")),
                  ("$b", deleted is null ? null : JsonSerializer.Serialize(deleted, Json))]);
        return Convert.ToInt64(cmd.ExecuteScalar());
    });

    /// <summary>Forgets the remote link, so the next push creates the event again (used when it moves to another calendar).</summary>
    public void ClearRemote(string eventId) =>
        WithConnection(c => Run(c, "UPDATE events SET remote_id = NULL, etag = NULL WHERE id = $id", ("$id", eventId)));

    public void SetRemote(string eventId, string remoteId, string? etag) =>
        WithConnection(c => Run(c, "UPDATE events SET remote_id = $r, etag = $e WHERE id = $id", ("$r", remoteId), ("$e", etag), ("$id", eventId)));

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
