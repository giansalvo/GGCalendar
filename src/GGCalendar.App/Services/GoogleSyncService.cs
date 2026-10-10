using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using GGCalendar.Core.Models;
using GGCalendar.Data;
using GGCalendar.Providers.Google;

namespace GGCalendar.App.Services;

public sealed record SyncResult(int Pulled, int Pushed, IReadOnlyList<string> Errors, bool Changed);

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

    public IReadOnlyList<string> AccountEmails => accounts.Load().Select(a => a.Email).ToList();

    public async Task<SyncResult> SyncAllAsync(CancellationToken ct)
    {
        var errors = new List<string>();
        int pulled = 0, pushed = 0;
        var changed = false;
        foreach (var account in accounts.Load())
        {
            try
            {
                var client = new GoogleCalendarClient(http, _config, GoogleAccountStore.Unprotect(account.ProtectedRefreshToken));
                pushed += await PushAsync(client, account.Email, errors, ct);
                var (p, c) = await PullAsync(client, account.Email, ct);
                pulled += p;
                changed |= c;
            }
            catch (Exception ex) when (ex is HttpRequestException or GoogleApiException or GoogleAuthException or TaskCanceledException)
            {
                errors.Add($"{account.Email}: {ex.Message}");
            }
        }

        return new SyncResult(pulled, pushed, errors, changed || pushed > 0);
    }

    private async Task<(int Pulled, bool Changed)> PullAsync(GoogleCalendarClient client, string email, CancellationToken ct)
    {
        var remote = await client.ListCalendarsAsync(ct);
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
        var seen = new HashSet<string>();
        var changed = false;
        var count = 0;

        // Masters first, so the occurrences of a series can be linked to their master within the same page.
        var ordered = page.Items.Where(i => !i.TryGetProperty("recurringEventId", out _))
            .Concat(page.Items.Where(i => i.TryGetProperty("recurringEventId", out _)));
        foreach (var item in ordered)
        {
            var remoteId = item.GetProperty("id").GetString()!;
            seen.Add(remoteId);
            var localId = store.FindLocalId(localCalendarId, remoteId);
            if (localId is not null && pending.Contains(localId)) continue;

            string? masterLocalId = null;
            if (item.TryGetProperty("recurringEventId", out var rid) && rid.GetString() is { } masterRemoteId)
                masterLocalId = store.FindLocalId(localCalendarId, masterRemoteId);

            var mapped = GoogleEventMapper.FromGoogle(item, localId ?? Guid.NewGuid().ToString("N"), localCalendarId, cal.DefaultReminders, masterLocalId);
            if (mapped is null)
            {
                if (localId is not null) { DeleteWithOccurrences(localId); changed = true; }
                continue;
            }

            var etag = item.TryGetProperty("etag", out var et) ? et.GetString() : null;
            store.UpsertEvent(mapped, remoteId, etag);
            changed = true;
            count++;
        }

        // After a full sync, anything we hold that Google no longer lists was deleted remotely.
        if (token is null)
        {
            foreach (var (id, remoteId) in store.GetSyncedEvents(localCalendarId))
            {
                if (seen.Contains(remoteId) || pending.Contains(id)) continue;
                store.DeleteEvent(id);
                changed = true;
            }
        }

        store.SetSyncToken(localCalendarId, page.NextSyncToken);
        return (count, changed);
    }

    private void DeleteWithOccurrences(string localId)
    {
        foreach (var child in store.GetAllEvents().Where(e => e.SeriesMasterId == localId)) store.DeleteEvent(child.Id);
        store.DeleteEvent(localId);
    }

    private async Task<int> PushAsync(GoogleCalendarClient client, string email, List<string> errors, CancellationToken ct)
    {
        var done = 0;
        foreach (var item in store.GetPending().Where(p => !IsGoogleCalendar(p.CalendarId) || AccountOf(p.CalendarId) == email))
        {
            if (!IsGoogleCalendar(item.CalendarId))
            {
                store.Complete(item.Seq);
                continue;
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
                        if (snapshot is null || store.GetRemoteInfo(item.EventId) is null) break; // deleted meanwhile
                        var current = store.GetRemoteInfo(item.EventId)!.Value;
                        var target = current.RemoteId;
                        if (target is null && snapshot.SeriesMasterId is not null && snapshot.OriginalStart is { } originalStart)
                        {
                            // A modified occurrence is an existing Google "instance" of the series: patch it instead of inserting.
                            var masterRemote = store.GetRemoteInfo(snapshot.SeriesMasterId)?.RemoteId;
                            if (masterRemote is null) continue; // the series has not been pushed yet; retry on the next sync
                            target = GoogleEventMapper.InstanceId(masterRemote, originalStart, snapshot.IsAllDay);
                        }

                        var result = target is null
                            ? await client.InsertAsync(remoteCal, snapshot, ct)
                            : await client.PatchAsync(remoteCal, target, snapshot, ct);
                        var remoteId = result.GetProperty("id").GetString()!;
                        var etag = result.TryGetProperty("etag", out var et) ? et.GetString() : null;
                        var meet = result.TryGetProperty("hangoutLink", out var hl) ? hl.GetString() : null;
                        if (store.GetPending().Any(p => p.EventId == item.EventId && p.Seq != item.Seq))
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
                errors.Add($"{email}: modifica non accettata da Google ({(int)ex.Status}).");
            }
        }

        return done;
    }
}
