using GGCalendar.Core.Ics;
using GGCalendar.Core.Localization;

namespace GGCalendar.Core.Subscriptions;

/// <summary>The messages of the subscriptions, as keys: {0} is the most subscriptions, {1} the most events, {2} the largest file in MB.</summary>
public static class SubscriptionMessages
{
    public static Msg Error(SubscriptionProblem problem) =>
        Msg.Of("subscription.error." + problem, SubscriptionLimits.MaxSubscriptions, SubscriptionLimits.MaxEvents, IcsSources.MaxBytes / (1024 * 1024));

    /// <summary>The message of a problem kept as text in the database (the last error of a subscription).</summary>
    public static Msg? Error(string? storedName) =>
        Enum.TryParse<SubscriptionProblem>(storedName, out var problem) ? Error(problem) : null;
}
