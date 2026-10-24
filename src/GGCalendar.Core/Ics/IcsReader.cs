using System.Text;

namespace GGCalendar.Core.Ics;

/// <summary>One content line: the name and the parameter names are upper case, the parameter values have no quotes, the value is not unescaped.</summary>
public sealed record IcsProperty(string Name, IReadOnlyDictionary<string, string> Params, string Value);

/// <summary>A BEGIN/END block with its properties and nested blocks.</summary>
public sealed class IcsComponent(string name, IReadOnlyList<IcsProperty> properties, IReadOnlyList<IcsComponent> children)
{
    public string Name { get; } = name;
    public IReadOnlyList<IcsProperty> Properties { get; } = properties;
    public IReadOnlyList<IcsComponent> Children { get; } = children;

    public IcsProperty? Property(string propertyName) => Properties.FirstOrDefault(p => p.Name == propertyName);

    public IEnumerable<IcsProperty> All(string propertyName) => Properties.Where(p => p.Name == propertyName);

    public IEnumerable<IcsComponent> ChildrenNamed(string componentName) => Children.Where(c => c.Name == componentName);
}

/// <summary>The content of the calendar(s) of a file: the properties of the first VCALENDAR and the blocks of all of them.</summary>
public sealed record IcsDocument(
    IReadOnlyList<IcsProperty> Properties,
    IReadOnlyList<IcsComponent> Components,
    IReadOnlyList<IcsIssue> Issues,
    /// <summary>Set when nothing can be read at all (not a calendar).</summary>
    IcsIssueCode? Fatal = null);

/// <summary>Counts the issues found while reading, in the order they were first seen.</summary>
internal sealed class IcsIssueList
{
    private readonly List<IcsIssueCode> _order = [];
    private readonly Dictionary<IcsIssueCode, int> _counts = [];
    private readonly Dictionary<IcsIssueCode, string> _samples = [];

    public void Add(IcsIssueCode code, string? sample = null, int count = 1)
    {
        if (!_counts.ContainsKey(code)) _order.Add(code);
        _counts[code] = _counts.GetValueOrDefault(code) + count;
        if (sample is not null && !_samples.ContainsKey(code)) _samples[code] = sample.Length > 80 ? sample[..80] : sample;
    }

    public void Merge(IcsIssue issue) => Add(issue.Code, issue.Sample, issue.Count);

    public IReadOnlyList<IcsIssue> List() => _order.Select(c => new IcsIssue(c, _counts[c], _samples.GetValueOrDefault(c))).ToList();
}

/// <summary>
/// Reader for the iCalendar text format (RFC 5545): folded lines, quoted parameters, nested blocks. It treats the text as
/// untrusted data: it has limits and never throws. A damaged part is skipped and counted in <see cref="IcsDocument.Issues"/>; a
/// file that stops in the middle keeps the blocks that were completed.
/// </summary>
public static class IcsReader
{
    public const int MaxLine = 64 * 1024;
    public const int MaxComponents = 100_000;
    public const int MaxDepth = 6;

    private sealed class Builder(string name)
    {
        public string Name { get; } = name;
        public List<IcsProperty> Properties { get; } = [];
        public List<IcsComponent> Children { get; } = [];
        public IcsComponent Build() => new(Name, Properties, Children);
    }

    public static IcsDocument Read(string raw)
    {
        var issues = new IcsIssueList();
        var text = raw.TrimStart('﻿');
        if (string.IsNullOrWhiteSpace(text) || text[..Math.Min(1024, text.Length)].Contains('\0')
            || !text.TrimStart().StartsWith("BEGIN:VCALENDAR", StringComparison.OrdinalIgnoreCase))
        {
            issues.Add(IcsIssueCode.NotACalendar);
            return new IcsDocument([], [], issues.List(), IcsIssueCode.NotACalendar);
        }

        var root = new Builder("ROOT");
        var stack = new List<Builder> { root };
        var skipping = 0;
        var total = 0;
        foreach (var line in LogicalLines(text))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (line.Length > MaxLine)
            {
                issues.Add(IcsIssueCode.LineTooLong);
                continue;
            }

            if (ParseLine(line) is not { } p)
            {
                issues.Add(IcsIssueCode.MalformedLine);
                continue;
            }

            switch (p.Name)
            {
                case "BEGIN":
                    if (skipping > 0 || stack.Count > MaxDepth)
                    {
                        if (skipping == 0) issues.Add(IcsIssueCode.TooDeep);
                        skipping++;
                        continue;
                    }

                    if (++total > MaxComponents)
                    {
                        issues.Add(IcsIssueCode.TooManyComponents);
                        goto Finish;
                    }

                    stack.Add(new Builder(p.Value.Trim().ToUpperInvariant()));
                    break;
                case "END":
                    if (skipping > 0)
                    {
                        skipping--;
                        continue;
                    }

                    var name = p.Value.Trim().ToUpperInvariant();
                    var at = stack.FindLastIndex(b => b.Name == name);
                    if (at <= 0)
                    {
                        issues.Add(IcsIssueCode.MismatchedEnd, name);
                        continue;
                    }

                    if (at != stack.Count - 1) issues.Add(IcsIssueCode.MismatchedEnd, name);
                    while (stack.Count > at)
                    {
                        var done = stack[^1];
                        stack.RemoveAt(stack.Count - 1);
                        stack[^1].Children.Add(done.Build());
                    }

                    break;
                default:
                    if (skipping == 0) stack[^1].Properties.Add(p);
                    break;
            }
        }

    Finish:
        if (stack.Count > 1)
        {
            // The file stops inside a block: the unfinished blocks inside the calendar are dropped, the calendar itself is kept.
            issues.Add(IcsIssueCode.Truncated);
            while (stack.Count > 2) stack.RemoveAt(stack.Count - 1);
            if (stack.Count == 2)
            {
                var open = stack[1];
                stack.RemoveAt(1);
                if (open.Name == "VCALENDAR") root.Children.Add(open.Build());
            }
        }

        var calendars = root.Children.Where(c => c.Name == "VCALENDAR").ToList();
        if (calendars.Count == 0)
        {
            issues.Add(IcsIssueCode.NotACalendar);
            return new IcsDocument([], [], issues.List(), IcsIssueCode.NotACalendar);
        }

        return new IcsDocument(calendars[0].Properties, calendars.SelectMany(c => c.Children).ToList(), issues.List());
    }

    /// <summary>Joins the folded lines (a line that starts with a space or a tab continues the previous one).</summary>
    private static IEnumerable<string> LogicalLines(string text)
    {
        StringBuilder? current = null;
        foreach (var physical in SplitLines(text))
        {
            if (physical.Length > 0 && (physical[0] == ' ' || physical[0] == '\t'))
            {
                // Past the limit the line is rejected anyway: stop growing it.
                if (current is not null && current.Length <= MaxLine) current.Append(physical, 1, physical.Length - 1);
                continue;
            }

            if (current is not null) yield return current.ToString();
            current = new StringBuilder(physical);
        }

        if (current is not null) yield return current.ToString();
    }

    /// <summary>The lines of the text, ended by CRLF, LF or CR.</summary>
    private static IEnumerable<string> SplitLines(string text)
    {
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '\r' && text[i] != '\n') continue;
            yield return text[start..i];
            if (text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
            start = i + 1;
        }

        if (start < text.Length) yield return text[start..];
    }

    /// <summary>NAME;PARAM=a;PARAM2="b:c":value; null when the line has no name or no colon.</summary>
    internal static IcsProperty? ParseLine(string line)
    {
        var n = line.Length;
        var i = 0;
        while (i < n && line[i] != ':' && line[i] != ';') i++;
        if (i == 0 || i >= n) return null;
        var name = line[..i].Trim().ToUpperInvariant();
        var parameters = new Dictionary<string, string>();
        while (i < n && line[i] == ';')
        {
            i++;
            var keyStart = i;
            while (i < n && line[i] != '=' && line[i] != ':' && line[i] != ';') i++;
            if (i >= n) return null;
            var key = line[keyStart..i].Trim().ToUpperInvariant();
            var value = new StringBuilder();
            if (line[i] == '=')
            {
                i++;
                var quoted = false;
                while (i < n && (quoted || (line[i] != ';' && line[i] != ':')))
                {
                    if (line[i] == '"') quoted = !quoted;
                    else value.Append(line[i]);
                    i++;
                }
            }

            if (key.Length > 0) parameters[key] = value.ToString();
        }

        if (i >= n || line[i] != ':') return null;
        return new IcsProperty(name, parameters, line[(i + 1)..]);
    }
}
