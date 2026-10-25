using GGCalendar.Core.Localization;

namespace GGCalendar.Core.Ics;

/// <summary>The sentences the user reads after an import: one for each kind of thing the file had that the app could not take as it was.</summary>
public static class IcsMessages
{
    /// <summary>The sentences as text, in the language shown now.</summary>
    public static IReadOnlyList<string> Describe(IcsReport report) => Messages(report).Select(m => m.Text).ToList();

    /// <summary>The sentences as messages (key and numbers): they can be shown again when the language changes.</summary>
    public static IReadOnlyList<Msg> Messages(IcsReport report)
    {
        var messages = new List<Msg>();
        if (report.Fatal == IcsIssueCode.NotACalendar) return [Msg.Of("ics.not_a_calendar")];
        if (report.Fatal == IcsIssueCode.TooLarge) return [Msg.Of("ics.too_large", IcsSources.MaxBytes / (1024 * 1024))];

        if (string.Equals(report.Method, "CANCEL", StringComparison.Ordinal)) messages.Add(Msg.Of("ics.method_cancel"));
        if (report.CancelledEvents > 0) messages.Add(Msg.Count("ics.cancelled_events", report.CancelledEvents));
        if (report.CancelledOccurrences > 0) messages.Add(Msg.Count("ics.cancelled_occurrences", report.CancelledOccurrences));
        if (report.OrphanOccurrences > 0) messages.Add(Msg.Count("ics.orphan_occurrences", report.OrphanOccurrences));
        if (report.IgnoredComponents.Count > 0)
            messages.Add(Msg.Of("ics.ignored_components", Msg.Join(Separated(report.IgnoredComponents.Select(kv => Msg.Join(Component(kv.Key), Msg.Literal($" ({kv.Value})")))))));

        foreach (var issue in report.Issues)
        {
            if (Describe(issue) is { } message) messages.Add(message);
        }

        return messages;
    }

    private static Msg[] Separated(IEnumerable<Msg> items) =>
        items.SelectMany((m, i) => i == 0 ? new[] { m } : new[] { Msg.Literal(", "), m }).ToArray();

    private static Msg Component(string component) => component is "VTODO" or "VJOURNAL" or "VFREEBUSY"
        ? Msg.Of("ics.component." + component)
        : Msg.Literal(component);

    private static Msg? Describe(IcsIssue issue)
    {
        var n = issue.Count;
        var key = "ics.issue." + issue.Code;
        return issue.Code switch
        {
            IcsIssueCode.Truncated or IcsIssueCode.TooManyComponents or IcsIssueCode.MismatchedEnd
                or IcsIssueCode.AttachIgnored or IcsIssueCode.CategoriesIgnored => Msg.Of(key),
            IcsIssueCode.UnknownTimeZone => Msg.Of(key, n, issue.Sample),
            IcsIssueCode.MalformedLine or IcsIssueCode.LineTooLong or IcsIssueCode.TooDeep or IcsIssueCode.MissingStart
                or IcsIssueCode.InvalidDate or IcsIssueCode.EndBeforeStart or IcsIssueCode.InvalidRule or IcsIssueCode.RdateIgnored
                or IcsIssueCode.ThisAndFuture or IcsIssueCode.AbsoluteAlarm or IcsIssueCode.InvalidAlarm or IcsIssueCode.AlarmAfterStart
                or IcsIssueCode.AlarmTooEarly or IcsIssueCode.TooManyAlarms or IcsIssueCode.FieldTruncated or IcsIssueCode.MissingUid
                or IcsIssueCode.DuplicateUid => Msg.Count(key, n),
            _ => null,
        };
    }
}
