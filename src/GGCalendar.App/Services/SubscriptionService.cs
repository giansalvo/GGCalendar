using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GGCalendar.Core.Models;
using GGCalendar.Core.Subscriptions;
using GGCalendar.Data;
using GGCalendar.Providers.Google;

namespace GGCalendar.App.Services;

/// <summary>A downloaded public calendar that is not saved yet: what the window shows before "Add".</summary>
public sealed record SubscriptionPreview(
    Uri Uri, byte[] Bytes, string? ETag, string? LastModified, string SuggestedName, SubscriptionSnapshot Snapshot)
{
    /// <summary>The same file counted again for another range of days (the number in the window follows the choice).</summary>
    public SubscriptionPreview Recount(SubscriptionRange range) =>
        this with { Snapshot = SubscriptionContent.Process(Bytes, "preview", range) };
}

/// <summary>Either a preview or the reason there is none.</summary>
public sealed record PreviewResult(SubscriptionPreview? Preview, SubscriptionProblem? Problem)
{
    public bool IsOk => Preview is not null;
}

public enum RefreshOutcome { Updated, NotModified, Failed }

public sealed record RefreshResult(string CalendarId, RefreshOutcome Outcome, SubscriptionChanges? Changes = null, SubscriptionProblem? Problem = null);

/// <summary>
/// Subscriptions to public calendars by address: read-only calendars whose events are downloaded again from time to time.
/// The address is kept encrypted for the current Windows user, is never logged and never shown in a message.
/// </summary>
public sealed class SubscriptionService
{
    /// <summary>The account of every subscription (the window shows its localised name).</summary>
    public const string AccountName = "Public";

    /// <summary>How the fetcher is made when none is given: the tests put a fake site here.</summary>
    public static Func<IcsFetcher> FetcherFactory { get; set; } = () => new IcsFetcher();

    private readonly CalendarStore _store;
    private readonly IcsFetcher _fetcher;
    private readonly Func<DateTime> _utcNow;
    private readonly Func<string, string> _protect;
    private readonly Func<string, string> _unprotect;

    public SubscriptionService(CalendarStore store, IcsFetcher? fetcher = null, Func<DateTime>? utcNow = null,
        Func<string, string>? protect = null, Func<string, string>? unprotect = null)
    {
        _store = store;
        _fetcher = fetcher ?? FetcherFactory();
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
        _protect = protect ?? GoogleAccountStore.Protect;
        _unprotect = unprotect ?? GoogleAccountStore.Unprotect;
    }

    /// <summary>Raised after a refresh or a change that touched the events or the calendars.</summary>
    public event Action? Changed;

    // ---- add ----

    /// <summary>Checks the address and downloads the calendar, without saving anything.</summary>
    public async Task<PreviewResult> PreviewAsync(string text, CancellationToken ct = default)
    {
        var check = SubscriptionUrls.Check(text);
        if (check.Uri is not { } uri) return new PreviewResult(null, SubscriptionContent.From(check.Problem ?? UrlProblem.Invalid));

        var existing = _store.GetSubscriptions();
        if (existing.Count >= SubscriptionLimits.MaxSubscriptions) return new PreviewResult(null, SubscriptionProblem.TooManySubscriptions);
        if (existing.Any(s => SafeUnprotect(s.EncryptedUrl) == uri.AbsoluteUri)) return new PreviewResult(null, SubscriptionProblem.Duplicate);

        var fetched = await _fetcher.FetchAsync(uri, ct: ct);
        if (fetched.Status != FetchStatus.Ok || fetched.Bytes is null)
            return new PreviewResult(null, SubscriptionContent.From(fetched.Error ?? FetchError.HttpError));

        var snapshot = SubscriptionContent.Process(fetched.Bytes, "preview", SubscriptionRange.All);
        if (snapshot.Problem is { } problem && problem != SubscriptionProblem.TooManyEvents) return new PreviewResult(null, problem);

        var name = !string.IsNullOrWhiteSpace(snapshot.Report.CalendarName) ? snapshot.Report.CalendarName!.Trim() : uri.Host;
        return new PreviewResult(new SubscriptionPreview(uri, fetched.Bytes, fetched.ETag, fetched.LastModified, name, snapshot), null);
    }

    /// <summary>Saves the previewed calendar with the chosen name, colour and range of days. Nothing is saved when the range leaves too many events.</summary>
    public (CalendarInfo? Calendar, SubscriptionProblem? Problem) Add(SubscriptionPreview preview, string name, string colorHex, SubscriptionRange range)
    {
        var existing = _store.GetSubscriptions();
        if (existing.Count >= SubscriptionLimits.MaxSubscriptions) return (null, SubscriptionProblem.TooManySubscriptions);
        if (existing.Any(s => SafeUnprotect(s.EncryptedUrl) == preview.Uri.AbsoluteUri)) return (null, SubscriptionProblem.Duplicate);

        var id = SubscriptionRecord.IdPrefix + Guid.NewGuid().ToString("N");
        var snapshot = SubscriptionContent.Process(preview.Bytes, id, range);
        if (snapshot.Problem is { } problem) return (null, problem);

        var now = _utcNow();
        var calendar = new CalendarInfo(id, AccountName, string.IsNullOrWhiteSpace(name) ? preview.SuggestedName : name.Trim(), colorHex);
        var record = new SubscriptionRecord(id, _protect(preview.Uri.AbsoluteUri), preview.ETag, preview.LastModified, snapshot.Hash,
            now, now, null, range.From, range.To);
        _store.AddSubscription(calendar, record, snapshot.Events);
        Changed?.Invoke();
        return (calendar, null);
    }

    // ---- refresh ----

    /// <summary>Downloads the calendar again. <paramref name="force"/> ignores the validators and the hash (used when the range changes).</summary>
    public async Task<RefreshResult> RefreshAsync(string calendarId, bool force = false, CancellationToken ct = default)
    {
        var record = _store.GetSubscription(calendarId);
        if (record is null) return new RefreshResult(calendarId, RefreshOutcome.Failed, Problem: SubscriptionProblem.Unreadable);

        var now = _utcNow();
        var check = SubscriptionUrls.Check(SafeUnprotect(record.EncryptedUrl));
        if (check.Uri is not { } uri) return Fail(record, now, SubscriptionProblem.InvalidUrl);

        var fetched = await _fetcher.FetchAsync(uri, force ? null : record.ETag, force ? null : record.LastModified, ct);
        if (fetched.Status == FetchStatus.Failed) return Fail(record, now, SubscriptionContent.From(fetched.Error ?? FetchError.HttpError) ?? SubscriptionProblem.HttpError);

        if (fetched.Status == FetchStatus.NotModified)
        {
            _store.UpdateSubscription(record with { LastFetchedUtc = now, LastSyncedUtc = now, LastError = null });
            return new RefreshResult(calendarId, RefreshOutcome.NotModified);
        }

        var snapshot = SubscriptionContent.Process(fetched.Bytes!, calendarId, record.Range);
        if (snapshot.Problem is { } problem) return Fail(record, now, problem);

        var next = record with
        {
            ETag = fetched.ETag, LastModified = fetched.LastModified, Hash = snapshot.Hash,
            LastFetchedUtc = now, LastSyncedUtc = now, LastError = null,
        };
        if (!force && record.Hash == snapshot.Hash)
        {
            _store.UpdateSubscription(next);
            return new RefreshResult(calendarId, RefreshOutcome.NotModified);
        }

        var changes = _store.ReplaceSubscriptionEvents(calendarId, snapshot.Events, next);
        if (changes.Any) Changed?.Invoke();
        return new RefreshResult(calendarId, changes.Any ? RefreshOutcome.Updated : RefreshOutcome.NotModified, changes);
    }

    private RefreshResult Fail(SubscriptionRecord record, DateTime now, SubscriptionProblem problem)
    {
        _store.UpdateSubscription(record with { LastFetchedUtc = now, LastError = problem.ToString() });
        Changed?.Invoke();
        return new RefreshResult(record.CalendarId, RefreshOutcome.Failed, Problem: problem);
    }

    /// <summary>Refreshes the subscriptions whose last check is older than the interval (less a tolerance of a tenth of it, at most 2 minutes).</summary>
    public async Task<IReadOnlyList<RefreshResult>> RefreshDueAsync(TimeSpan interval, CancellationToken ct = default)
    {
        var results = new List<RefreshResult>();
        var tolerance = TimeSpan.FromTicks(Math.Min(interval.Ticks / 10, TimeSpan.FromMinutes(2).Ticks));
        foreach (var s in _store.GetSubscriptions())
        {
            ct.ThrowIfCancellationRequested();
            if (s.LastFetchedUtc is { } last && _utcNow() - last < interval - tolerance) continue;
            var result = await RefreshAsync(s.CalendarId, ct: ct);
            results.Add(result);
            // Without a network the other addresses would fail the same way: no more attempts until the next time.
            if (result.Problem is SubscriptionProblem.NoNetwork) break;
        }

        return results;
    }

    public async Task<IReadOnlyList<RefreshResult>> RefreshAllAsync(CancellationToken ct = default)
    {
        var results = new List<RefreshResult>();
        foreach (var s in _store.GetSubscriptions())
        {
            ct.ThrowIfCancellationRequested();
            results.Add(await RefreshAsync(s.CalendarId, force: true, ct));
        }

        return results;
    }

    // ---- change and remove ----

    /// <summary>Renames, recolours and changes the range of days; a different range downloads the calendar again.</summary>
    public async Task<RefreshResult?> UpdateAsync(string calendarId, string name, string colorHex, SubscriptionRange range, CancellationToken ct = default)
    {
        var record = _store.GetSubscription(calendarId);
        var calendar = _store.GetCalendars().FirstOrDefault(c => c.Id == calendarId);
        if (record is null || calendar is null) return null;

        _store.UpsertCalendar(calendar with { Name = string.IsNullOrWhiteSpace(name) ? calendar.Name : name.Trim(), ColorHex = colorHex });
        var rangeChanged = record.RangeFrom != range.From || record.RangeTo != range.To;
        if (rangeChanged) _store.UpdateSubscription(record with { RangeFrom = range.From, RangeTo = range.To });
        Changed?.Invoke();
        return rangeChanged ? await RefreshAsync(calendarId, force: true, ct) : null;
    }

    /// <summary>How many events (not counting modified occurrences) a subscription holds.</summary>
    public int EventCount(string calendarId) => _store.GetAllEvents().Count(e => e.CalendarId == calendarId && e.SeriesMasterId is null);

    /// <summary>The address in the clear, only for the person who asks to see it.</summary>
    public string? GetUrl(string calendarId) => _store.GetSubscription(calendarId) is { } r ? SafeUnprotect(r.EncryptedUrl) : null;

    public void Remove(string calendarId)
    {
        _store.DeleteCalendar(calendarId);
        Changed?.Invoke();
    }

    private string SafeUnprotect(string value)
    {
        try { return _unprotect(value); }
        catch (Exception e) when (e is FormatException or System.Security.Cryptography.CryptographicException) { return ""; }
    }
}
