namespace GGCalendar.Core.Ics;

/// <summary>What went wrong, or was left out, while a .ics file was read. The summary of an import counts them.</summary>
public enum IcsIssueCode
{
    NotACalendar, Truncated, MalformedLine, LineTooLong, TooManyComponents, TooDeep, MismatchedEnd,
    MissingStart, InvalidDate, EndBeforeStart, UnknownTimeZone, InvalidRule, RdateIgnored, ThisAndFuture,
    AbsoluteAlarm, InvalidAlarm, AlarmAfterStart, AlarmTooEarly, TooManyAlarms, FieldTruncated,
    MissingUid, DuplicateUid, AttachIgnored, CategoriesIgnored, TooLarge,
}

/// <summary><see cref="Count"/> occurrences of one kind of issue; <see cref="Sample"/> is the first detail seen (a time zone name, a rule), never a long text.</summary>
public sealed record IcsIssue(IcsIssueCode Code, int Count, string? Sample = null);

/// <summary>What the reader and the mapper found in a file besides the events: nothing is dropped without being counted here.</summary>
public sealed record IcsReport(
    string? Method,
    string? CalendarName,
    int CancelledEvents,
    int CancelledOccurrences,
    /// <summary>Occurrences of a series that is not in the file (the public feeds of Google): imported as single events.</summary>
    int OrphanOccurrences,
    IReadOnlyDictionary<string, int> IgnoredComponents,
    IReadOnlyList<IcsIssue> Issues,
    /// <summary>Set when nothing can be read at all (not a calendar, too large).</summary>
    IcsIssueCode? Fatal)
{
    public static IcsReport Empty { get; } = new(null, null, 0, 0, 0, new Dictionary<string, int>(), [], null);

    public int CountOf(IcsIssueCode code) => Issues.Where(i => i.Code == code).Sum(i => i.Count);

    public bool Has(IcsIssueCode code) => CountOf(code) > 0;
}

/// <summary>How a file is read. Tests use <see cref="LocalZone"/> to read with another PC time zone.</summary>
public sealed record IcsImportOptions
{
    /// <summary>The zone of the PC: times in UTC or in other zones are converted to it, floating times are taken as it. Null = this PC's.</summary>
    public TimeZoneInfo? LocalZone { get; init; }

    /// <summary>Read the relative reminders (VALARM) of the events.</summary>
    public bool ImportAlarms { get; init; } = true;
}
