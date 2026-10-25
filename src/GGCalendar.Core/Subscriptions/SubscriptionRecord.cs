namespace GGCalendar.Core.Subscriptions;

/// <summary>
/// What is kept about a subscription besides its calendar and events. <see cref="EncryptedUrl"/> is the address protected for the
/// current Windows user: the store never sees it in the clear. <see cref="Hash"/> recognises an unchanged file when the site sends
/// no ETag (the public feeds of Google).
/// </summary>
public sealed record SubscriptionRecord(
    string CalendarId, string EncryptedUrl, string? ETag = null, string? LastModified = null, string? Hash = null,
    DateTime? LastFetchedUtc = null, DateTime? LastSyncedUtc = null, string? LastError = null,
    DateTime? RangeFrom = null, DateTime? RangeTo = null)
{
    public const string IdPrefix = "sub:";

    public SubscriptionRange Range => new(RangeFrom, RangeTo);

    public static bool IsSubscriptionId(string calendarId) => calendarId.StartsWith(IdPrefix, StringComparison.Ordinal);
}

/// <summary>How many events a refresh added, changed and removed.</summary>
public sealed record SubscriptionChanges(int Added, int Changed, int Removed)
{
    public bool Any => Added + Changed + Removed > 0;
}
