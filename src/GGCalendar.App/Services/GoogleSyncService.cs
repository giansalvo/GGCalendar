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

namespace GGCalendar.App.Services;

public sealed record SyncResult(int Pulled, int Pushed, IReadOnlyList<string> Errors, IReadOnlyList<string> Conflicts, bool Changed);

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

    private GoogleClientConfig _config = GoogleClientConfig.Embedded;

    public void Configure(GoogleClientConfig config) => _config = config;

    public bool IsConfigured => _config.IsConfigured;

    /// <summary>Opens the browser, signs in and stores the account. Returns its email.</summary>
    public async Task<string> AddAccountAsync(CancellationToken ct)
    {
        if (!_config.IsConfigured) throw new GoogleAuthException("Credenziali Google non configurate (Impostazioni → Account Google → Avanzate).");
        var tokens = await GoogleAuth.SignInAsync(http, _config.ClientId, _config.ClientSecret, ct);
        if (tokens.RefreshToken is null) throw new GoogleAuthException("Google non ha restituito il token di accesso persistente. Riprova.");
        var email = await new GoogleCalendarClient(http, _config, tokens.RefreshToken).GetAccountEmailAsync(ct);
        accounts.AddOrReplace(email, tokens.RefreshToken);
        return email;
    }

    public void RemoveAccount(string email)
    {
        accounts.Remove(email);
        foreach (var c in store.GetCalendars().Where(c => IsGoogleCalendar(c.Id) && AccountOf(c.Id) == email))
            store.DeleteCalendar(c.Id);
    }

    /// <summary>Forgets the incremental sync position: the next sync downloads everything again.</summary>
    public void ResetSync() => store.ClearSyncTokens();

    public IReadOnlyList<string> AccountEmails => accounts.Load().Select(a => a.Email).ToList();

    public async Task<SyncResult> SyncAllAsync(CancellationToken ct)
    {
        var errors = new List<string>();
        var conflicts = new List<string>();
        int pulled = 0, pushed = 0;
        var changed = false;
        foreach (var account in accounts.Load())
        {
            try
            {
                var client = new GoogleCalendarClient(http, _config, GoogleAccountStore.Unprotect(account.ProtectedRefreshToken));
                var calendars = await client.ListCalendarsAsync(ct);
                var conflictsBefore = conflicts.Count;
                pushed += await PushAsync(client, account.Email, calendars, errors, conflicts, ct);
                var (p, c) = await PullAsync(client, account.Email, calendars, ct);
                pulled += p;
                changed |= c;
                // Conflict copies are queued during the push: send them now rather than on the next sync.
                if (conflicts.Count > conflictsBefore) pushed += await PushAsync(client, account.Email, calendars, errors, conflicts, ct);
            }
            catch (Exception ex) when (ex is HttpRequestException or GoogleApiException or GoogleAuthException or TaskCanceledException)
            {
                errors.Add($"{account.Email}: {ex.Message}");
            }
        }

        return new SyncResult(pulled, pushed, errors, conflicts, changed || pushed > 0 || conflicts.Count > 0);
    }

    private async Task<(int Pulled, bool Changed)> PullAsync(GoogleCalendarClient client, string email, IReadOnlyList<GoogleCalendarEntry> remote, CancellationToken ct)
    {
        var existing = store.GetCalendars().ToDictionary(c => c.Id);
        var changed = false;
        var pulled = 0;

        foreach (var cal in remote)
        {
            var localId = LocalCalendarId(email, cal.Id);
            if (existing.TryGetValue(localId, out var known))
            {
                // Keep the user's visibility choice; names/colors follow Google.
                if (known.Name != cal.Name || known.ColorHex != cal.ColorHex)
                {
                    store.UpsertCalendar(known with { Name = cal.Name, ColorHex = cal.ColorHex });
                    changed = true;
                }
            }
            else
            {
                store.UpsertCalendar(new CalendarInfo(localId, email, cal.Name, cal.ColorHex));
                changed = true;
            }

            var (n, c) = await PullCalendarAsync(client, localId, cal, ct);
            pulled += n;
            changed |= c;
        }

        // Calendars removed or unsubscribed on Google.
        var remoteIds = remote.Select(c => LocalCalendarId(email, c.Id)).ToHashSet();
        foreach (var gone in existing.Keys.Where(id => IsGoogleCalendar(id) && AccountOf(id) == email && !remoteIds.Contains(id)))
        {
            store.DeleteCalendar(gone);
            changed = true;
        }

        return (pulled, changed);
    }

    private async Task<(int Pulled, bool Changed)> PullCalendarAsync(GoogleCalendarClient client, string localCalendarId, GoogleCalendarEntry cal, CancellationToken ct)
    {
        var token = store.GetSyncToken(localCalendarId);
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
        var synced = store.GetSyncedEvents(localCalendarId);
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

    private async Task<int> PushAsync(GoogleCalendarClient client, string email, IReadOnlyList<GoogleCalendarEntry> calendars,
        List<string> errors, List<string> conflicts, CancellationToken ct)
    {
        var done = 0;
        var conflicted = new HashSet<string>();
        foreach (var item in store.GetPending().Where(p => !IsGoogleCalendar(p.CalendarId) || AccountOf(p.CalendarId) == email))
        {
            if (!IsGoogleCalendar(item.CalendarId))
            {
                store.Complete(item.Seq);
                continue;
            }

            if (conflicted.Contains(item.EventId)) continue; // its queue was dropped when the conflict was resolved

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
                            result = await client.InsertAsync(remoteCal, snapshot, ct);
                        }
                        else
                        {
                            // Patch only the version we last saw; a modified occurrence's instance id has no known version.
                            var baseEtag = target == current.RemoteId ? current.Etag : null;
                            try
                            {
                                result = await client.PatchAsync(remoteCal, target, snapshot, ct, baseEtag);
                            }
                            catch (GoogleApiException ex) when (ex.Status == HttpStatusCode.PreconditionFailed)
                            {
                                conflicts.Add(await ResolveConflictAsync(client, item, target, calendars, ct));
                                conflicted.Add(item.EventId);
                                continue;
                            }
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
            }
            catch (GoogleApiException ex) when (ex.Status is HttpStatusCode.BadRequest or HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
            {
                // A permanent rejection must not block the rest of the queue.
                store.Complete(item.Seq);
                errors.Add($"{email}: modifica non accettata da Google ({(int)ex.Status}): {ex.Message}");
            }
        }

        return done;
    }

    /// <summary>
    /// The event changed on Google after the version the local edit was based on. Google's version is applied and the
    /// local edit is kept as a separate "(conflitto)" event in the same calendar, so neither is lost. Returns the message for the user.
    /// </summary>
    private async Task<string> ResolveConflictAsync(GoogleCalendarClient client, OutboxItem item, string remoteId,
        IReadOnlyList<GoogleCalendarEntry> calendars, CancellationToken ct)
    {
        // The last queued snapshot carries every local edit made since the last sync.
        var local = store.GetPending().Where(p => p.EventId == item.EventId && p.Operation != OutboxOperation.Delete)
            .Select(CalendarStore.ReadSnapshot).LastOrDefault(s => s is not null) ?? CalendarStore.ReadSnapshot(item)!;
        store.DiscardPending(item.EventId);

        var remoteCal = RemoteCalendarId(item.CalendarId);
        var defaults = calendars.FirstOrDefault(c => c.Id == remoteCal)?.DefaultReminders ?? [];
        CalendarEvent? remote = null;
        string? etag = null;
        try
        {
            var e = await client.GetEventAsync(remoteCal, remoteId, ct);
            remote = GoogleEventMapper.FromGoogle(e, item.EventId, item.CalendarId, defaults, local.SeriesMasterId);
            etag = e.TryGetProperty("etag", out var et) ? et.GetString() : null;
        }
        catch (GoogleApiException ex) when (ex.Status is HttpStatusCode.NotFound or HttpStatusCode.Gone)
        {
            // Deleted on Google meanwhile: only the local copy below remains.
        }

        if (remote is null) store.DeleteSeries(item.EventId);
        else store.UpsertEvent(remote, remoteId, etag);

        var title = string.IsNullOrWhiteSpace(local.Title) ? "(Senza titolo)" : local.Title;
        if (local.IsCancelled) return $"\"{title}\" è stato modificato anche su Google: tenuta la versione di Google.";

        var copy = local with
        {
            Id = Guid.NewGuid().ToString("N"),
            Title = title + " (conflitto)",
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
        return $"\"{title}\" è stato modificato sia qui sia su Google: tenuta la versione di Google, la tua è salvata come \"{copy.Title}\".";
    }
}
