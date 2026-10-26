using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GGCalendar.Core.Models;
using GGCalendar.Data;
using GGCalendar.Providers.Google;
using GGCalendar.Core.Localization;

namespace GGCalendar.App.Services;

/// <param name="Pending">Changes still waiting to be sent to Google (a long queue, or Google asked to slow down).</param>
/// <param name="RetryAt">When the sending of the changes still waiting should be tried again, if Google asked to slow down.</param>
public sealed record SyncResult(int Pulled, int Pushed, IReadOnlyList<Msg> Errors, IReadOnlyList<Msg> Conflicts, bool Changed,
    int Pending = 0, DateTimeOffset? RetryAt = null, bool Cancelled = false);

/// <summary>
/// Two-way sync with Google Calendar. Local calendar ids are "{email}|{googleCalendarId}".
/// Local edits win until they are pushed (outbox); remote changes arrive through per-calendar sync tokens.
/// </summary>
public sealed class GoogleSyncService(CalendarStore store, GoogleAccountStore accounts, HttpClient http)
{
    public static string LocalCalendarId(string email, string remoteId) => $"{email}|{remoteId}";

    public static bool IsGoogleCalendar(string localId) => localId.Contains('|');

    private static string RemoteCalendarId(string localId) => localId[(localId.IndexOf('|') + 1)..];

    private static string AccountOf(string localId) => localId[..localId.IndexOf('|')];

    /// <summary>The primary calendar of an account has the email of the account as its id on Google: it cannot be deleted.</summary>
    public static bool IsPrimaryCalendar(string localId) =>
        IsGoogleCalendar(localId) && string.Equals(AccountOf(localId), RemoteCalendarId(localId), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// One thing at a time touches the calendars of the accounts: a sync, the creation or the deletion of a calendar, the disconnection of an
    /// account. A sync that starts while another runs is skipped; the others wait their turn.
    /// </summary>
    public SemaphoreSlim Gate { get; } = new(1, 1);

    private GoogleClientConfig _config = GoogleClientConfig.Embedded;

    /// <summary>The pause between two writes to Google while many are waiting (a few changes are sent at once). Google refuses a burst.</summary>
    public TimeSpan PushPace { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>Many changes waiting: from this many on, the writes are spaced by <see cref="PushPace"/>.</summary>
    private const int ManyWaiting = 30;

    /// <summary>The clock (replaceable in tests).</summary>
    public Func<DateTimeOffset> Clock { get; set; } = () => DateTimeOffset.UtcNow;

    /// <summary>Accounts for which Google asked to slow down: until when to leave them alone, and how many times in a row.</summary>
    private readonly Dictionary<string, (DateTimeOffset Until, int Strikes)> _slowedDown = [];

    public void Configure(GoogleClientConfig config) => _config = config;

    public bool IsConfigured => _config.IsConfigured;

    /// <summary>Opens the browser, signs in and stores the account. Returns its email.</summary>
    public async Task<string> AddAccountAsync(CancellationToken ct)
    {
        if (!_config.IsConfigured) throw new GoogleAuthException(L.T("auth.not_configured"));
        var tokens = await GoogleAuth.SignInAsync(http, _config.ClientId, _config.ClientSecret, ct);
        if (tokens.RefreshToken is null) throw new GoogleAuthException(L.T("auth.no_refresh_token"));
        var email = await new GoogleCalendarClient(http, _config, tokens.RefreshToken).GetAccountEmailAsync(ct);
        accounts.AddOrReplace(email, tokens.RefreshToken);
        return email;
    }

    /// <summary>What disconnecting an account takes away from this PC: its calendars, their events (series count once) and the changes not yet sent to Google.</summary>
    public sealed record AccountDisconnectInfo(int Calendars, int Events, int PendingChanges);

    /// <summary>What <see cref="RemoveAccountAsync"/> would take away from this PC (shown before the person confirms).</summary>
    public AccountDisconnectInfo DescribeAccount(string email)
    {
        var ids = store.GetCalendars().Where(c => IsGoogleCalendar(c.Id) && AccountOf(c.Id) == email).Select(c => c.Id).ToHashSet();
        var events = store.GetAllEvents().Count(e => ids.Contains(e.CalendarId) && e.SeriesMasterId is null && !e.IsCancelled);
        var pending = store.GetPending().Count(p => ids.Contains(p.CalendarId));
        return new AccountDisconnectInfo(ids.Count, events, pending);
    }

    /// <summary>
    /// Revokes the app's access on Google, then forgets the account and its calendars on this PC (even if Google could
    /// not be reached). Returns whether the access was revoked. It waits for a sync that is running (see <see cref="Gate"/>):
    /// the calendars cannot go away while their changes are being sent.
    /// </summary>
    public async Task<bool> RemoveAccountAsync(string email, CancellationToken ct)
    {
        await Gate.WaitAsync(ct);
        try
        {
            var account = accounts.Load().FirstOrDefault(a => a.Email.Equals(email, StringComparison.OrdinalIgnoreCase));
            var revoked = account is not null && await GoogleAuth.RevokeAsync(http, GoogleAccountStore.Unprotect(account.ProtectedRefreshToken), ct);
            accounts.Remove(email);
            foreach (var c in store.GetCalendars().Where(c => IsGoogleCalendar(c.Id) && AccountOf(c.Id) == email))
                store.DeleteCalendar(c.Id);
            return revoked;
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>
    /// What the local copy still decides about the reminders of an event that comes from Google: only the custom sounds
    /// when Google told which reminders are alarms, otherwise alarms and sounds both (the older events).
    /// </summary>
    private static IReadOnlyList<EventReminder> KeepLocalReminderOptions(CalendarEvent remote, IReadOnlyList<EventReminder> local) =>
        remote.AlarmsFromProvider ? EventReminder.KeepLocalSounds(remote.Reminders, local) : EventReminder.KeepLocalOptions(remote.Reminders, local);

    /// <summary>Forgets the incremental sync position: the next sync downloads everything again.</summary>
    public void ResetSync() => store.ClearSyncTokens();

    // ---------- The queue of the changes waiting for Google (issue #19) ----------

    /// <summary>How many events have changes waiting to be sent to Google (the number shown in the status bar).</summary>
    public int CountQueuedEvents() => store.CountPendingEvents();

    /// <summary>What waits to be sent, one entry per event.</summary>
    public IReadOnlyList<QueuedEvent> GetQueue() => PendingQueue.Group(store.GetPending());

    /// <summary>When Google, which asked to slow down, will be tried again (null when it did not).</summary>
    public DateTimeOffset? NextRetry()
    {
        var now = Clock();
        return _slowedDown.Values.Where(v => v.Until > now).Select(v => (DateTimeOffset?)v.Until).Min();
    }

    /// <summary>
    /// What cancelling changes did: how many events were cancelled (only those still waiting count: some may have been sent meanwhile),
    /// and whether some events come back from Google at the next sync (the calendar is downloaded again).
    /// </summary>
    public sealed record QueueCancelResult(int Cancelled, bool RestoreFromGoogle);

    /// <summary>
    /// Cancels the changes waiting for the events, so that this PC shows what Google has (issue #19): a new event goes away, an edited one
    /// goes back to the version before the edit, a deleted one and a moved one come back from Google at the next sync (their calendar is
    /// downloaded again). It waits for a sync that is running (see <see cref="Gate"/>).
    /// </summary>
    public async Task<QueueCancelResult> CancelQueuedAsync(IReadOnlyCollection<string> eventIds, CancellationToken ct)
    {
        await Gate.WaitAsync(ct);
        try
        {
            // Thousands of events can wait (an import): the work is done off the thread of the window, so that it does not freeze.
            return await Task.Run(() =>
            {
                var wanted = eventIds.ToHashSet();
                var queued = GetQueue().Where(q => wanted.Contains(q.EventId)).ToList();
                var created = new List<string>(); // events to remove from this PC with their changes
                var outboxOnly = new List<string>(); // events that stay, with their changes removed
                var download = new HashSet<string>(); // calendars that are downloaded again at the next sync
                foreach (var q in queued)
                {
                    switch (q.Kind)
                    {
                        case QueueKind.New:
                            created.Add(q.EventId);
                            break;

                        case QueueKind.Moved:
                            created.Add(q.EventId); // the copy at the destination, and both queued changes
                            download.Add(q.FromCalendarId!);
                            break;

                        case QueueKind.Deleted:
                            outboxOnly.Add(q.EventId);
                            download.Add(q.CalendarId);
                            break;

                        default:
                            // The oldest change still waiting remembers the event as Google has it (what was sent before is in it).
                            var before = q.Rows.Where(r => r.Operation == OutboxOperation.Update).Select(CalendarStore.ReadBase).FirstOrDefault(e => e is not null);
                            if (before is not null) store.UpsertEvent(before);
                            else download.Add(q.CalendarId); // changes queued by an older version: Google's version comes back with the next download
                            outboxOnly.Add(q.EventId);
                            break;
                    }
                }

                store.DiscardCreatedEvents(created);
                store.DiscardOutboxOf(outboxOnly);
                foreach (var calendar in download) store.SetSyncToken(calendar, null);
                return new QueueCancelResult(queued.Count, download.Count > 0);
            }, CancellationToken.None);
        }
        finally
        {
            Gate.Release();
        }
    }

    public IReadOnlyList<string> AccountEmails => accounts.Load().Select(a => a.Email).ToList();

    /// <param name="onProgress">Called while changes are sent to Google with (sent so far, waiting at the start of the sending).</param>
    // ---------- Creating and deleting calendars (online only: nothing is queued) ----------

    /// <summary>The longest name the app sends for a calendar.</summary>
    public const int MaxCalendarName = 255;

    private static string TimeZoneForGoogle()
    {
        var id = TimeZoneInfo.Local.Id;
        if (id.Contains('/')) return id; // already an IANA name
        return TimeZoneInfo.TryConvertWindowsIdToIanaId(id, out var iana) ? iana : "UTC";
    }

    private GoogleCalendarClient ClientOf(string email)
    {
        var account = accounts.Load().FirstOrDefault(a => a.Email.Equals(email, StringComparison.OrdinalIgnoreCase))
            ?? throw new GoogleCalendarException(GoogleCalendarProblem.NoAccount);
        return new GoogleCalendarClient(http, _config, GoogleAccountStore.Unprotect(account.ProtectedRefreshToken));
    }

    private static GoogleCalendarException Explain(Exception ex) => ex switch
    {
        GoogleCalendarException known => known,
        GoogleApiException { IsTemporary: true, Status: HttpStatusCode.TooManyRequests or HttpStatusCode.Forbidden } => new GoogleCalendarException(GoogleCalendarProblem.RateLimited, ex),
        GoogleApiException { IsTemporary: true } => new GoogleCalendarException(GoogleCalendarProblem.Server, ex),
        GoogleApiException { Status: HttpStatusCode.Unauthorized } => new GoogleCalendarException(GoogleCalendarProblem.AuthExpired, ex),
        GoogleApiException { Status: HttpStatusCode.Forbidden } => new GoogleCalendarException(GoogleCalendarProblem.NotAllowed, ex),
        GoogleAuthException => new GoogleCalendarException(GoogleCalendarProblem.AuthExpired, ex),
        HttpRequestException or TaskCanceledException => new GoogleCalendarException(GoogleCalendarProblem.NoNetwork, ex),
        _ => new GoogleCalendarException(GoogleCalendarProblem.Other, ex),
    };

    /// <summary>
    /// Creates a calendar on Google for the account and adds it here (owned by the person). Goes to Google at once: without a network, or when
    /// Google refuses, nothing is created and <see cref="GoogleCalendarException"/> tells why. The events of the new calendar come with the next sync.
    /// </summary>
    public async Task<CalendarInfo> CreateCalendarAsync(string email, string name, string colorHex, CancellationToken ct)
    {
        name = name.Trim();
        if (name.Length == 0) throw new GoogleCalendarException(GoogleCalendarProblem.EmptyName);
        if (name.Length > MaxCalendarName) name = name[..MaxCalendarName];

        await Gate.WaitAsync(ct);
        try
        {
            var client = ClientOf(email);
            string remoteId;
            try
            {
                remoteId = await client.CreateCalendarAsync(name, TimeZoneForGoogle(), ct);
            }
            catch (Exception ex) when (ex is HttpRequestException or GoogleApiException or GoogleAuthException || (ex is TaskCanceledException && !ct.IsCancellationRequested))
            {
                throw Explain(ex);
            }

            try
            {
                await client.SetCalendarColorAsync(remoteId, colorHex, ct);
            }
            catch (Exception ex) when (ex is HttpRequestException or GoogleApiException or GoogleAuthException || (ex is TaskCanceledException && !ct.IsCancellationRequested))
            {
                // The calendar exists: it is kept with the colour asked for here, and the next sync takes whatever colour Google has.
            }

            var info = new CalendarInfo(LocalCalendarId(email, remoteId), email, name, colorHex, IsOwner: true);
            store.UpsertCalendar(info);
            return info;
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>
    /// Deletes a calendar of the person on Google, with all its events (one request, for everybody it is shared with; the guests are not told), and
    /// then here, together with the changes still waiting for it. Only a calendar the person owns that is not the primary one: anything else is
    /// refused without asking Google. Without a network, or when Google refuses, nothing is deleted.
    /// </summary>
    public async Task DeleteCalendarAsync(string localId, CancellationToken ct)
    {
        if (!IsGoogleCalendar(localId)) throw new GoogleCalendarException(GoogleCalendarProblem.Other);
        if (IsPrimaryCalendar(localId)) throw new GoogleCalendarException(GoogleCalendarProblem.Primary);

        await Gate.WaitAsync(ct);
        try
        {
            var info = store.GetCalendars().FirstOrDefault(c => c.Id == localId);
            if (info is null) return; // already gone
            if (info.IsOwner != true) throw new GoogleCalendarException(GoogleCalendarProblem.NotOwner);

            try
            {
                await ClientOf(AccountOf(localId)).DeleteCalendarAsync(RemoteCalendarId(localId), ct);
            }
            catch (Exception ex) when (ex is HttpRequestException or GoogleApiException or GoogleAuthException || (ex is TaskCanceledException && !ct.IsCancellationRequested))
            {
                throw Explain(ex);
            }

            store.DeleteCalendar(localId);
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>What an edit of a Google calendar did: the calendar as it is now, and why the second step (colour, personal name) did not go through, if it did not.</summary>
    public sealed record GoogleCalendarEdit(CalendarInfo Calendar, GoogleCalendarProblem? NotCompleted = null);

    /// <summary>
    /// Changes the name and/or the colour of a calendar of Google (online only; the sync would put back Google's own values otherwise). A new name
    /// goes to the calendar itself, so only its owner can give one, never for the primary calendar; the colour is the person's own and can be changed on
    /// any calendar. Renaming also removes the personal name the person may have set on Google, which would hide the new one. When the name is changed
    /// but the colour is refused, the name stays changed (here as on Google) and the result says why the rest did not go through.
    /// </summary>
    public async Task<GoogleCalendarEdit> UpdateCalendarAsync(string localId, string? newName, string? newColor, CancellationToken ct)
    {
        if (!IsGoogleCalendar(localId)) throw new GoogleCalendarException(GoogleCalendarProblem.Other);
        var color = newColor is null ? null : CalendarColors.Normalize(newColor) ?? throw new GoogleCalendarException(GoogleCalendarProblem.Other);

        await Gate.WaitAsync(ct);
        try
        {
            var info = store.GetCalendars().FirstOrDefault(c => c.Id == localId) ?? throw new GoogleCalendarException(GoogleCalendarProblem.Other);
            var name = newName?.Trim();
            var rename = name is not null && name != info.Name;
            var recolor = color is not null && !string.Equals(color, info.ColorHex, StringComparison.OrdinalIgnoreCase);
            if (!rename && !recolor) return new GoogleCalendarEdit(info);

            if (rename)
            {
                if (name!.Length == 0) throw new GoogleCalendarException(GoogleCalendarProblem.EmptyName);
                if (IsPrimaryCalendar(localId)) throw new GoogleCalendarException(GoogleCalendarProblem.Primary);
                if (info.IsOwner != true) throw new GoogleCalendarException(GoogleCalendarProblem.NotOwner);
                if (name.Length > MaxCalendarName) name = name[..MaxCalendarName];
            }

            var client = ClientOf(AccountOf(localId));
            var remoteId = RemoteCalendarId(localId);
            if (rename)
            {
                try
                {
                    await client.RenameCalendarAsync(remoteId, name!, ct);
                }
                catch (Exception ex) when (ex is HttpRequestException or GoogleApiException or GoogleAuthException || (ex is TaskCanceledException && !ct.IsCancellationRequested))
                {
                    throw Explain(ex);
                }
            }

            GoogleCalendarProblem? notCompleted = null;
            try
            {
                await client.UpdateCalendarListEntryAsync(remoteId, recolor ? color : null, clearPersonalName: rename, ct);
            }
            catch (Exception ex) when (ex is HttpRequestException or GoogleApiException or GoogleAuthException || (ex is TaskCanceledException && !ct.IsCancellationRequested))
            {
                if (!rename) throw Explain(ex); // nothing was changed
                notCompleted = Explain(ex).Problem;
                recolor = false;
            }

            var updated = info with { Name = rename ? name! : info.Name, ColorHex = recolor ? color! : info.ColorHex };
            store.UpsertCalendar(updated);
            return new GoogleCalendarEdit(updated, notCompleted);
        }
        finally
        {
            Gate.Release();
        }
    }

    public async Task<SyncResult> SyncAllAsync(CancellationToken ct, Action<int, int>? onProgress = null)
    {
        var errors = new List<Msg>();
        var conflicts = new List<Msg>();
        int pulled = 0, pushed = 0;
        var changed = false;
        var cancelled = false;
        foreach (var account in accounts.Load())
        {
            if (ct.IsCancellationRequested) { cancelled = true; break; }
            try
            {
                var client = new GoogleCalendarClient(http, _config, GoogleAccountStore.Unprotect(account.ProtectedRefreshToken));
                var calendars = await client.ListCalendarsAsync(ct);
                var conflictsBefore = conflicts.Count;
                pushed += await PushAsync(client, account.Email, calendars, errors, conflicts, ct, onProgress);
                var (p, c) = await PullAsync(client, account.Email, calendars, ct);
                pulled += p;
                changed |= c;
                // Conflict copies are queued during the push: send them now rather than on the next sync.
                if (conflicts.Count > conflictsBefore) pushed += await PushAsync(client, account.Email, calendars, errors, conflicts, ct, onProgress);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // The user stopped it: not an error. The changes not sent yet stay in the queue.
                cancelled = true;
                break;
            }
            catch (GoogleApiException ex) when (ex.IsTemporary)
            {
                // A limit of requests (per minute, per day) or a problem on Google's side: nothing is lost, the sync is tried again later.
                SlowDown(account.Email, ex.RetryAfter);
                errors.Add(Msg.Of("sync.account_limited", account.Email));
                changed = true; // part of the download (or the removal of calendars gone from Google) may have been stored: the window reads again
            }
            catch (Exception ex) when (ex is HttpRequestException or GoogleApiException or GoogleAuthException or TaskCanceledException)
            {
                errors.Add(Msg.Of("sync.account_error", account.Email, ex.Message));
                changed = true; // idem
            }
        }

        var now = Clock();
        var retry = _slowedDown.Values.Where(v => v.Until > now).Select(v => (DateTimeOffset?)v.Until).Min();
        var pending = store.GetPending().Count(p => IsGoogleCalendar(p.CalendarId));
        return new SyncResult(pulled, pushed, errors, conflicts, changed || pushed > 0 || conflicts.Count > 0, pending, retry, cancelled);
    }

    private async Task<(int Pulled, bool Changed)> PullAsync(GoogleCalendarClient client, string email, IReadOnlyList<GoogleCalendarEntry> remote, CancellationToken ct)
    {
        var existing = store.GetCalendars().ToDictionary(c => c.Id);
        var changed = false;
        var pulled = 0;

        // Calendars removed or unsubscribed on Google go first: if Google refuses while the events are downloaded (a limit of requests,
        // which a full download of a big account easily reaches), they must not stay on this PC until a download ends well.
        var remoteIds = remote.Select(c => LocalCalendarId(email, c.Id)).ToHashSet();
        foreach (var gone in existing.Keys.Where(id => IsGoogleCalendar(id) && AccountOf(id) == email && !remoteIds.Contains(id)).ToList())
        {
            store.DeleteCalendar(gone);
            existing.Remove(gone);
            changed = true;
        }

        foreach (var cal in remote)
        {
            var localId = LocalCalendarId(email, cal.Id);
            if (existing.TryGetValue(localId, out var known))
            {
                // Keep the user's visibility choice; names/colors follow Google.
                if (known.Name != cal.Name || known.ColorHex != cal.ColorHex || known.IsOwner != cal.IsOwner)
                {
                    store.UpsertCalendar(known with { Name = cal.Name, ColorHex = cal.ColorHex, IsOwner = cal.IsOwner });
                    changed = true;
                }
            }
            else
            {
                store.UpsertCalendar(new CalendarInfo(localId, email, cal.Name, cal.ColorHex, IsOwner: cal.IsOwner));
                changed = true;
            }

            var (n, c) = await PullCalendarAsync(client, localId, cal, ct);
            pulled += n;
            changed |= c;
        }

        return (pulled, changed);
    }

    private async Task<(int Pulled, bool Changed)> PullCalendarAsync(GoogleCalendarClient client, string localCalendarId, GoogleCalendarEntry cal, CancellationToken ct)
    {
        var token = store.GetSyncToken(localCalendarId);
        var synced = store.GetSyncedEvents(localCalendarId);
        // A token without any local event means the events were lost (e.g. the calendar was removed and re-added by an
        // older version that kept the token): an incremental sync would never bring them back. Listing an empty calendar is cheap.
        if (token is not null && synced.Count == 0) token = null;
        GoogleSyncPage page;
        try
        {
            page = await client.ListEventsAsync(cal.Id, token, ct);
        }
        catch (GoogleApiException ex) when (ex.Status == HttpStatusCode.Gone && token is not null)
        {
            store.SetSyncToken(localCalendarId, null);
            token = null;
            page = await client.ListEventsAsync(cal.Id, null, ct);
        }

        var pending = store.GetPending().Select(p => p.EventId).ToHashSet();
        var localIds = new Dictionary<string, string>();
        foreach (var (id, remoteId) in synced) localIds[remoteId] = id;
        var seen = new HashSet<string>();
        var upserts = new List<RemoteEvent>();
        var deletes = new List<string>();

        // Masters first, so the occurrences of a series can be linked to their master within the same page.
        var ordered = page.Items.Where(i => !i.TryGetProperty("recurringEventId", out _))
            .Concat(page.Items.Where(i => i.TryGetProperty("recurringEventId", out _)));
        foreach (var item in ordered)
        {
            var remoteId = item.GetProperty("id").GetString()!;
            seen.Add(remoteId);
            var localId = localIds.GetValueOrDefault(remoteId);
            if (localId is not null && pending.Contains(localId)) continue;

            string? masterLocalId = null;
            if (item.TryGetProperty("recurringEventId", out var rid) && rid.GetString() is { } masterRemoteId)
                masterLocalId = localIds.GetValueOrDefault(masterRemoteId);

            var id = localId ?? Guid.NewGuid().ToString("N");
            var mapped = GoogleEventMapper.FromGoogle(item, id, localCalendarId, cal.DefaultReminders, masterLocalId);
            if (mapped is null)
            {
                if (localId is not null) deletes.Add(localId);
                continue;
            }

            if (localId is not null && store.GetEvent(localId) is { } local)
                mapped = mapped with { Reminders = KeepLocalReminderOptions(mapped, local.Reminders) };

            var etag = item.TryGetProperty("etag", out var et) ? et.GetString() : null;
            upserts.Add(new RemoteEvent(mapped, remoteId, etag));
            localIds[remoteId] = id;
        }

        // After a full sync, anything we hold that Google no longer lists was deleted remotely.
        if (token is null)
            deletes.AddRange(synced.Where(s => !seen.Contains(s.RemoteId) && !pending.Contains(s.Id)).Select(s => s.Id));

        store.ApplyRemoteChanges(localCalendarId, upserts, deletes, page.NextSyncToken);
        return (upserts.Count, upserts.Count > 0 || deletes.Count > 0);
    }

    /// <summary>How many creations are sent to Google with one request (1 = one request each). At most <see cref="GoogleCalendarClient.MaxBatchSize"/>.</summary>
    public int BatchSize { get; set; } = GoogleCalendarClient.MaxBatchSize;

    /// <summary>Google asked to slow down: leave the account alone for a while, longer each time in a row (1 minute, 2, 4... up to 15).</summary>
    private void SlowDown(string email, TimeSpan? retryAfter)
    {
        var strikes = (_slowedDown.TryGetValue(email, out var before) ? before.Strikes : 0) + 1;
        var wait = TimeSpan.FromSeconds(Math.Min(900, 60 * Math.Pow(2, strikes - 1)));
        if (retryAfter is { } asked && asked > wait) wait = asked;
        _slowedDown[email] = (Clock() + wait, strikes);
    }

    private sealed record BatchEntry(OutboxItem Item, CalendarEvent Snapshot);

    /// <summary>The creations at <paramref name="position"/> and after it that can go in one batch: consecutive, same calendar, not yet on Google, not a modified occurrence.</summary>
    private List<BatchEntry> CollectBatch(List<OutboxItem> queue, int position)
    {
        var group = new List<BatchEntry>();
        for (var i = position; i < queue.Count && group.Count < Math.Min(BatchSize, GoogleCalendarClient.MaxBatchSize); i++)
        {
            var item = queue[i];
            if (item.Operation != OutboxOperation.Create || !IsGoogleCalendar(item.CalendarId) || item.CalendarId != queue[position].CalendarId) break;
            if (CalendarStore.ReadSnapshot(item) is not { SeriesMasterId: null } snapshot) break;
            if (store.GetRemoteInfo(item.EventId) is not { RemoteId: null }) break; // deleted meanwhile, or already on Google
            group.Add(new BatchEntry(item, snapshot));
        }

        return group;
    }

    private sealed record BatchOutcome(int Done, bool Temporary, TimeSpan? RetryAfter);

    /// <summary>Sends the creations with one request and records what each call answered. The ones Google did not accept for now stay in the queue.</summary>
    private async Task<BatchOutcome> PushBatchAsync(GoogleCalendarClient client, string email, List<BatchEntry> group, List<Msg> errors, CancellationToken ct)
    {
        var remoteCal = RemoteCalendarId(group[0].Item.CalendarId);
        var answers = await client.InsertBatchAsync(remoteCal, group.Select(g => g.Snapshot).ToList(), ct);
        var done = 0;
        var temporary = false;
        TimeSpan? retryAfter = null;
        for (var i = 0; i < group.Count; i++)
        {
            var (item, snapshot) = (group[i].Item, group[i].Snapshot);
            var answer = answers[i];
            JsonElement result = default;
            if (answer.IsSuccess) result = answer.Body;
            else if (answer.Status == (int)HttpStatusCode.Conflict)
            {
                // The same creation was sent before and Google has it (the answer was lost): take what is there, not a second copy.
                try
                {
                    var existing = await client.GetEventAsync(remoteCal, snapshot.Id, ct);
                    result = existing.TryGetProperty("status", out var status) && status.GetString() == "cancelled"
                        ? await client.InsertAsync(remoteCal, snapshot, ct, withClientId: false)
                        : existing;
                }
                catch (GoogleApiException ex) when (ex.IsTemporary)
                {
                    temporary = true;
                    retryAfter = ex.RetryAfter;
                    continue;
                }
            }
            else if (answer.IsTemporary)
            {
                temporary = true;
                continue;
            }
            else
            {
                // A refusal for good must not block the rest of the queue.
                store.Complete(item.Seq);
                errors.Add(Msg.Of("sync.rejected", email, answer.Status, snapshot.Title));
                continue;
            }

            var remoteId = result.GetProperty("id").GetString()!;
            var etag = result.TryGetProperty("etag", out var et) ? et.GetString() : null;
            var meet = result.TryGetProperty("hangoutLink", out var hl) ? hl.GetString() : null;
            if (store.HasPendingAfter(item.EventId, item.Seq)) store.SetRemote(item.EventId, remoteId, etag);
            else store.UpsertEvent(meet is null ? snapshot : snapshot with { MeetLink = meet }, remoteId, etag);
            store.Complete(item.Seq);
            done++;
        }

        return new BatchOutcome(done, temporary, retryAfter);
    }

    private async Task<int> PushAsync(GoogleCalendarClient client, string email, IReadOnlyList<GoogleCalendarEntry> calendars,
        List<Msg> errors, List<Msg> conflicts, CancellationToken ct, Action<int, int>? onProgress = null)
    {
        var done = 0;
        var conflicted = new HashSet<string>();
        var queue = store.GetPending().Where(p => !IsGoogleCalendar(p.CalendarId) || AccountOf(p.CalendarId) == email).ToList();
        // Google asked to slow down: leave the account alone until the time it asked for (the changes stay in the queue).
        if (_slowedDown.TryGetValue(email, out var slowed) && Clock() < slowed.Until) return 0;
        var spaced = queue.Count > ManyWaiting;
        var lastWrite = DateTimeOffset.MinValue;
        var handled = 0;
        var batchFailed = false;
        for (var position = 0; position < queue.Count; position++)
        {
            var item = queue[position];
            onProgress?.Invoke(handled++, queue.Count);
            ct.ThrowIfCancellationRequested();
            if (!IsGoogleCalendar(item.CalendarId))
            {
                store.Complete(item.Seq);
                continue;
            }

            if (conflicted.Contains(item.EventId)) continue; // its queue was dropped when the conflict was resolved

            if (spaced)
            {
                var wait = PushPace - (Clock() - lastWrite);
                if (wait > TimeSpan.Zero) await Task.Delay(wait, ct);
                lastWrite = Clock();
            }

            // Many creations in a row (an import) go to Google up to 50 at a time: far faster than one request each.
            if (!batchFailed && BatchSize > 1 && CollectBatch(queue, position) is { Count: >= 2 } group)
            {
                try
                {
                    var outcome = await PushBatchAsync(client, email, group, errors, ct);
                    done += outcome.Done;
                    handled += group.Count - 1;
                    position += group.Count - 1;
                    if (outcome.Temporary)
                    {
                        SlowDown(email, outcome.RetryAfter);
                        break;
                    }

                    _slowedDown.Remove(email);
                    continue;
                }
                catch (GoogleApiException ex) when (ex.IsTemporary)
                {
                    SlowDown(email, ex.RetryAfter);
                    break;
                }
                catch (GoogleApiException)
                {
                    batchFailed = true; // the batch itself is refused: send this one and the next ones singly
                }
            }

            try
            {
                var remoteCal = RemoteCalendarId(item.CalendarId);
                switch (item.Operation)
                {
                    case OutboxOperation.Delete:
                        if (item.Payload is { } remoteToDelete) await client.DeleteAsync(remoteCal, remoteToDelete, ct);
                        break;

                    default:
                        var snapshot = CalendarStore.ReadSnapshot(item);
                        if (snapshot is null || store.GetRemoteInfo(item.EventId) is not { } current) break; // deleted meanwhile
                        var target = current.RemoteId;
                        if (target is null && snapshot.SeriesMasterId is not null && snapshot.OriginalStart is { } originalStart)
                        {
                            // A modified occurrence is an existing Google "instance" of the series: patch it instead of inserting.
                            var masterInfo = snapshot.SeriesMasterId is null ? null : store.GetRemoteInfo(snapshot.SeriesMasterId);
                            if (masterInfo is null) break; // the series was deleted meanwhile
                            var masterRemote = masterInfo.Value.RemoteId;
                            if (masterRemote is null) continue; // the series has not been pushed yet; retry on the next sync
                            target = GoogleEventMapper.InstanceId(masterRemote, originalStart, snapshot.IsAllDay);
                        }

                        JsonElement result;
                        if (target is null)
                        {
                            try
                            {
                                result = await client.InsertAsync(remoteCal, snapshot, ct);
                            }
                            catch (GoogleApiException ex) when (ex.Status == HttpStatusCode.Conflict)
                            {
                                // The same creation was sent before and Google has it (the answer was lost): take what is there, not a second copy.
                                var existing = await client.GetEventAsync(remoteCal, snapshot.Id, ct);
                                result = existing.TryGetProperty("status", out var status) && status.GetString() == "cancelled"
                                    ? await client.InsertAsync(remoteCal, snapshot, ct, withClientId: false) // the id was used by an event deleted since
                                    : existing;
                            }
                        }
                        else
                        {
                            // Patch only the version we last saw; a modified occurrence's instance id has no known version.
                            var baseEtag = target == current.RemoteId ? current.Etag : null;
                            JsonElement? patched = null;
                            try
                            {
                                patched = await client.PatchAsync(remoteCal, target, snapshot, ct, baseEtag);
                            }
                            catch (GoogleApiException ex) when (ex.Status == HttpStatusCode.PreconditionFailed)
                            {
                                var (remote, remoteEtag) = await GetRemoteAsync(client, item, target, snapshot.SeriesMasterId, calendars, ct);
                                // Google also changes the version on its own (creating a Meet conference, recording a guest's
                                // reply): if what the user sees is still what the edit started from, it is not a conflict.
                                if (remote is not null && CalendarStore.ReadBase(item) is { } baseVersion
                                    && EventContent.Same(remote, baseVersion, ignoreGuest: AccountOf(item.CalendarId)))
                                {
                                    try
                                    {
                                        patched = await client.PatchAsync(remoteCal, target, snapshot, ct, remoteEtag);
                                    }
                                    catch (GoogleApiException again) when (again.Status == HttpStatusCode.PreconditionFailed)
                                    {
                                        // Changed again meanwhile: treat it as a real conflict.
                                    }
                                }

                                if (patched is null)
                                {
                                    conflicts.Add(ResolveConflict(item, target, remote, remoteEtag));
                                    conflicted.Add(item.EventId);
                                    continue;
                                }
                            }

                            result = patched.Value;
                        }

                        var remoteId = result.GetProperty("id").GetString()!;
                        var etag = result.TryGetProperty("etag", out var et) ? et.GetString() : null;
                        var meet = result.TryGetProperty("hangoutLink", out var hl) ? hl.GetString() : null;
                        if (store.HasPendingAfter(item.EventId, item.Seq))
                            store.SetRemote(item.EventId, remoteId, etag);
                        else
                            store.UpsertEvent(meet is null ? snapshot : snapshot with { MeetLink = meet }, remoteId, etag);
                        break;
                }

                store.Complete(item.Seq);
                done++;
                _slowedDown.Remove(email);
            }
            catch (GoogleApiException ex) when (ex.IsTemporary)
            {
                // Not a refusal: Google asks to slow down (or has a problem). The change stays in the queue and the sending resumes later.
                SlowDown(email, ex.RetryAfter);
                break;
            }
            catch (GoogleApiException ex) when (ex.Status is HttpStatusCode.BadRequest or HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
            {
                // A permanent rejection must not block the rest of the queue.
                store.Complete(item.Seq);
                errors.Add(Msg.Of("sync.rejected", email, (int)ex.Status, ex.Message));
            }
        }

        return done;
    }

    /// <summary>The current Google version of an event and its etag; (null, null) if it was deleted on Google.</summary>
    private async Task<(CalendarEvent? Event, string? Etag)> GetRemoteAsync(GoogleCalendarClient client, OutboxItem item, string remoteId,
        string? seriesMasterId, IReadOnlyList<GoogleCalendarEntry> calendars, CancellationToken ct)
    {
        var remoteCal = RemoteCalendarId(item.CalendarId);
        var defaults = calendars.FirstOrDefault(c => c.Id == remoteCal)?.DefaultReminders ?? [];
        try
        {
            var e = await client.GetEventAsync(remoteCal, remoteId, ct);
            return (GoogleEventMapper.FromGoogle(e, item.EventId, item.CalendarId, defaults, seriesMasterId),
                e.TryGetProperty("etag", out var et) ? et.GetString() : null);
        }
        catch (GoogleApiException ex) when (ex.Status is HttpStatusCode.NotFound or HttpStatusCode.Gone)
        {
            return (null, null);
        }
    }

    /// <summary>
    /// The event changed on Google after the version the local edit was based on. Google's version
    /// (<paramref name="remote"/>, null if deleted there) is applied and the local edit is kept as a separate "(conflitto)"
    /// event in the same calendar, so neither is lost. Returns the message for the user.
    /// </summary>
    private Msg ResolveConflict(OutboxItem item, string remoteId, CalendarEvent? remote, string? etag)
    {
        // The last queued snapshot carries every local edit made since the last sync.
        var local = store.GetPending().Where(p => p.EventId == item.EventId && p.Operation != OutboxOperation.Delete)
            .Select(CalendarStore.ReadSnapshot).LastOrDefault(s => s is not null) ?? CalendarStore.ReadSnapshot(item)!;
        store.DiscardPending(item.EventId);

        if (remote is null) store.DeleteSeries(item.EventId);
        else store.UpsertEvent(remote with { Reminders = KeepLocalReminderOptions(remote, local.Reminders) }, remoteId, etag);

        var title = string.IsNullOrWhiteSpace(local.Title) ? L.T("common.untitled") : local.Title;
        if (local.IsCancelled) return Msg.Of("sync.conflict_cancelled", title);

        var copy = local with
        {
            Id = Guid.NewGuid().ToString("N"),
            Title = title + L.T("sync.conflict_suffix"),
            Attendees = [],
            SendInvites = false,
            AddMeet = false,
            MeetLink = null,
            IcsUid = null,
            SeriesMasterId = null,
            OriginalStart = null,
            OccurrenceIndex = null,
        };
        store.UpsertEvent(copy);
        store.Enqueue(copy.Id, copy.CalendarId, OutboxOperation.Create, copy);
        return Msg.Of("sync.conflict_both", title, copy.Title);
    }
}
