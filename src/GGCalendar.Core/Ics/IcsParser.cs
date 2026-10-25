using GGCalendar.Core.Models;

using GGCalendar.Core.Localization;

namespace GGCalendar.Core.Ics;

public sealed record IcsImport(IReadOnlyList<CalendarEvent> Events, IReadOnlyList<string> Warnings)
{
    /// <summary>What was found besides the events: issues with codes and counts.</summary>
    public IcsReport Report { get; init; } = IcsReport.Empty;

    /// <summary>The warnings as messages (key and numbers), to show them again in another language.</summary>
    public IReadOnlyList<Msg> Messages => IcsMessages.Messages(Report);
}

/// <summary>Reads the VEVENTs of an RFC 5545 .ics file. Times are converted to the local time zone of the PC.</summary>
public static class IcsParser
{
    public static IReadOnlyList<CalendarEvent> Parse(string text, string calendarId) => Analyze(text, calendarId).Events;

    /// <summary>Reads the text and reports what this app cannot represent (so nothing is lost silently).</summary>
    public static IcsImport Analyze(string text, string calendarId, IcsImportOptions? options = null) =>
        IcsMapper.Map(IcsReader.Read(text), calendarId, options);

    /// <summary>Reads a file from its bytes: the size limit, the encoding and the unfolding of the lines are decided here.</summary>
    public static IcsImport AnalyzeFile(byte[] bytes, string calendarId, IcsImportOptions? options = null)
    {
        if (bytes.Length > IcsSources.MaxBytes)
        {
            var tooLarge = IcsReport.Empty with { Fatal = IcsIssueCode.TooLarge, Issues = [new IcsIssue(IcsIssueCode.TooLarge, 1)] };
            return new IcsImport([], IcsMessages.Describe(tooLarge)) { Report = tooLarge };
        }

        return Analyze(IcsSources.Decode(bytes), calendarId, options);
    }
}
