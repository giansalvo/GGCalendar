using System.Globalization;

namespace GGCalendar.Core.Search;

/// <summary>One piece of the search line: a word, or a keyword with its value (<c>title:riunione</c>, <c>email:"mario rossi"</c>, <c>-annullata</c>).</summary>
/// <param name="Key">The canonical keyword (see <see cref="SearchSyntax.Keys"/>), or null for a plain word.</param>
/// <param name="Value">The words, without the quotes.</param>
/// <param name="Raw">The piece as it was written, to write it back unchanged.</param>
public sealed record SearchToken(string? Key, string Value, string Raw);

/// <summary>
/// The syntax of the search box, like the <c>site:</c> of a browser. Plain words look in the title, the description, the location and the
/// guests. The keywords (always in English, in any order, in any case) narrow it down:
/// <c>title:</c>, <c>description:</c> (<c>desc:</c>), <c>what:</c> (title or description), <c>location:</c> (<c>where:</c>),
/// <c>email:</c> (<c>who:</c>, <c>guest:</c>), <c>not:</c> (or <c>-word</c>), <c>from:</c> and <c>to:</c> (a day, <c>2026-10-25</c> or <c>25/10/2026</c>).
/// A value of several words goes in quotes: <c>title:"riunione alfa"</c>. All the words must be found, all the keywords apply.
/// A keyword that is not known, or has no value yet, is not a keyword: it is a plain word (or nothing while it is being typed).
/// </summary>
public static class SearchSyntax
{
    public const string Title = "title";
    public const string Description = "description";
    public const string What = "what";
    public const string Location = "location";
    public const string Email = "email";
    public const string Not = "not";
    public const string From = "from";
    public const string To = "to";

    /// <summary>The keywords, canonical.</summary>
    public static IReadOnlyList<string> Keys { get; } = [Title, Description, What, Location, Email, Not, From, To];

    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        [Title] = Title, [Description] = Description, ["desc"] = Description, [What] = What, [Location] = Location, ["where"] = Location,
        [Email] = Email, ["who"] = Email, ["guest"] = Email, [Not] = Not, [From] = From, [To] = To,
    };

    /// <summary>The keywords that the detailed panel of the search edits (the others are left as they are when the panel writes the line).</summary>
    public static IReadOnlySet<string> PanelKeys { get; } = new HashSet<string> { What, Email, Location, Not, From, To };

    private static readonly string[] DayFormats = ["yyyy-MM-dd", "yyyy-M-d", "dd/MM/yyyy", "d/M/yyyy"];

    public static IReadOnlyList<SearchToken> Tokenize(string? text)
    {
        var tokens = new List<SearchToken>();
        if (string.IsNullOrWhiteSpace(text)) return tokens;
        var i = 0;
        while (i < text.Length)
        {
            if (char.IsWhiteSpace(text[i])) { i++; continue; }
            var start = i;

            // A keyword: letters, a colon, then a word or a quoted text.
            var j = i;
            while (j < text.Length && char.IsLetter(text[j])) j++;
            if (j > i && j < text.Length && text[j] == ':' && Aliases.TryGetValue(text[i..j], out var key))
            {
                var (value, end) = ReadValue(text, j + 1);
                if (value.Length > 0) tokens.Add(new SearchToken(key, value, text[start..end]));
                i = value.Length > 0 ? end : j + 1; // a keyword with no value yet is nothing: the person is still typing
                continue;
            }

            if (text[i] == '-' && i + 1 < text.Length && !char.IsWhiteSpace(text[i + 1]))
            {
                var (value, end) = ReadValue(text, i + 1);
                if (value.Length > 0)
                {
                    tokens.Add(new SearchToken(Not, value, text[start..end]));
                    i = end;
                    continue;
                }
            }

            var (word, wordEnd) = ReadValue(text, i);
            if (wordEnd == i) wordEnd = i + 1; // nothing read (a lone quote): go on
            if (word.Length > 0) tokens.Add(new SearchToken(null, word, text[start..wordEnd]));
            i = wordEnd;
        }

        return tokens;
    }

    /// <summary>A word up to the next space, or the text between quotes (an open quote takes the rest of the line).</summary>
    private static (string Value, int End) ReadValue(string text, int from)
    {
        if (from < text.Length && text[from] == '"')
        {
            var close = text.IndexOf('"', from + 1);
            return close < 0 ? (text[(from + 1)..].Trim(), text.Length) : (text[(from + 1)..close].Trim(), close + 1);
        }

        var end = from;
        while (end < text.Length && !char.IsWhiteSpace(text[end])) end++;
        return (text[from..end], end);
    }

    /// <summary>The query that a line of the box stands for (the calendars are not part of the line).</summary>
    public static SearchQuery Parse(string? text)
    {
        var general = new List<string>();
        var what = new List<string>();
        var title = new List<string>();
        var description = new List<string>();
        var who = new List<string>();
        var where = new List<string>();
        var not = new List<string>();
        DateTime? from = null;
        DateTime? to = null;
        foreach (var t in Tokenize(text))
        {
            switch (t.Key)
            {
                case null: general.Add(t.Value); break;
                case What: what.Add(t.Value); break;
                case Title: title.Add(t.Value); break;
                case Description: description.Add(t.Value); break;
                case Email: who.Add(t.Value); break;
                case Location: where.Add(t.Value); break;
                case Not: not.Add(t.Value); break;
                case From: from = ParseDay(t.Value) ?? from; break;
                case To: to = ParseDay(t.Value) ?? to; break;
            }
        }

        string Join(List<string> words) => string.Join(' ', words);
        return new SearchQuery
        {
            Text = Join(general), What = Join(what), Title = Join(title), Description = Join(description),
            Who = Join(who), Where = Join(where), Not = Join(not), From = from, To = to,
        };
    }

    public static DateTime? ParseDay(string? text) =>
        DateTime.TryParseExact(text?.Trim(), DayFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) ? day.Date : null;

    public static string FormatDay(DateTime day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>The keyword with its value, in quotes when the value has more than one word.</summary>
    public static string Keyword(string key, string value)
    {
        var v = value.Trim().Replace("\"", "");
        return v.Any(char.IsWhiteSpace) ? $"{key}:\"{v}\"" : $"{key}:{v}";
    }

    /// <summary>
    /// What the box says after the detailed panel wrote its boxes in it: the plain words and the keywords the panel does not edit are kept,
    /// the ones it edits (<see cref="PanelKeys"/>, and <c>-word</c>) are replaced by what the panel has.
    /// </summary>
    public static string Merge(string? line, string what, string who, string where, string not, DateTime? from, DateTime? to)
    {
        var parts = Tokenize(line).Where(t => t.Key is null || !PanelKeys.Contains(t.Key)).Select(t => t.Raw).ToList();
        void Add(string key, string value)
        {
            if (!string.IsNullOrWhiteSpace(value)) parts.Add(Keyword(key, value));
        }

        Add(What, what);
        Add(Email, who);
        Add(Location, where);
        Add(Not, not);
        if (from is { } f) parts.Add($"{From}:{FormatDay(f)}");
        if (to is { } t) parts.Add($"{To}:{FormatDay(t)}");
        return string.Join(' ', parts);
    }
}
