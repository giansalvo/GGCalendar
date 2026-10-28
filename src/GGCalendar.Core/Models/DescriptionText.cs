using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace GGCalendar.Core.Models;

[Flags]
public enum DescriptionStyle
{
    None = 0,
    Bold = 1,
    Italic = 2,
    Underline = 4,

    /// <summary>The text of a title (h1, h2, h3, always with Bold): the biggest first.</summary>
    Heading1 = 8,
    Heading2 = 16,
    Heading3 = 32,
}

/// <summary>A piece of the description with one style and, if it is a link, the address it opens. A line break is a "\n" in the text.</summary>
public sealed record DescriptionRun(string Text, DescriptionStyle Style = DescriptionStyle.None, Uri? Link = null);

/// <summary>
/// The description of an event as Google Calendar shows it. These tags are read, in any case and with any attribute:
/// b, strong, i, em, u, a (with href), br, p, div, span, h1, h2, h3, ul, ol and li; html, body and head (with all that is in it),
/// the DOCTYPE and the HTML comments are removed. Any other tag is plain text, tag included ("&lt;table&gt;" is shown as "&lt;table&gt;" and
/// found by the search). The text is kept as it is written (spaces, tabs, line breaks; CRLF and CR become a line break),
/// the HTML 4 entities, "&amp;apos;" and the numeric ones are shown as characters, and the addresses of web sites and emails are links.
/// The stored text is never changed: this is only how it is read.
/// <list type="bullet">
/// <item>A tag that is not closed lasts until the end of the text; a closing tag with nothing to close is not shown.</item>
/// <item>p starts a new line after an empty one and ends the same way; div and the titles (h1, h2, h3: bold and bigger, h1 the biggest)
/// start and end a line; span only shows its text; strong is b and em is i.</item>
/// <item>Lists: "• " for ul (and for li outside a list), "1. 2. …" for ol with a count for each list, two spaces for each nested level.</item>
/// <item>A text of only spaces and line breaks between two tags of a list, br, p, div, a title, html, head, body or the DOCTYPE (or the
/// start or the end of the text) is ignored, and, as in HTML, a line of a list or a block does not start or end with spaces, also across
/// b, i, u and a ("&lt;li&gt; uno &lt;/li&gt;" is "uno"); the spaces next to b, i, u and a anywhere else are shown.</item>
/// <item>A link opens only http, https or mailto; an a with another address (or none) is plain text, tag and closing tag included.</item>
/// </list>
/// </summary>
public static class DescriptionText
{
    /// <summary>The longest description the app accepts when it is written or imported.</summary>
    public const int MaxLength = 8192;

    private const int MaxIndent = 6;

    /// <summary>A tag longer than this is not a tag: it keeps a text with many "&lt;" and no "&gt;" from being read again and again.</summary>
    private const int MaxTag = 4000;

    private static readonly HashSet<string> Tags = ["b", "strong", "i", "em", "u", "a", "br", "p", "div", "span", "h1", "h2", "h3", "ul", "ol", "li", "html", "body", "head"];
    private static readonly Regex Href = new(@"(?:^|\s)href\s*=\s*(?:""([^""]*)""|'([^']*)'|([^\s""'>]+))", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>Web addresses (http, https, www) and emails; the first to start wins, so an address with an email inside is one link.</summary>
    private static readonly Regex Address = new(
        @"(?<web>(?:https?://|\bwww\.)[^\s<>""]+)|(?<mail>[A-Za-z0-9._%+\-]+@[A-Za-z0-9\-]+(?:\.[A-Za-z0-9\-]+)+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    private static readonly Dictionary<string, int> Entities = LoadEntities();

    /// <summary>The description as styled runs, for showing it.</summary>
    public static IReadOnlyList<DescriptionRun> Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        var normalized = Lines(text);
        return NeedsReading(normalized) ? new Reader(normalized).Read() : [new DescriptionRun(normalized)];
    }

    /// <summary>What the user reads, without the tags that are read as formatting: for the search and its snippets.</summary>
    public static string ToPlainText(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var normalized = Lines(text);
        return NeedsReading(normalized) ? string.Concat(new Reader(normalized).Read().Select(r => r.Text)) : normalized;
    }

    /// <summary>The link under the character at <paramref name="offset"/> of the text made by the runs, or null.</summary>
    public static Uri? LinkAt(IReadOnlyList<DescriptionRun> runs, int offset)
    {
        var start = 0;
        foreach (var run in runs)
        {
            if (offset >= start && offset < start + run.Text.Length) return run.Link;
            start += run.Text.Length;
        }

        return null;
    }

    /// <summary>The address if it is one the app opens: absolute http or https with a host, or mailto with an address.</summary>
    public static Uri? SafeLink(string? href)
    {
        if (string.IsNullOrWhiteSpace(href) || !Uri.TryCreate(href.Trim(), UriKind.Absolute, out var uri)) return null;
        return uri.Scheme switch
        {
            "http" or "https" => uri.Host.Length > 0 ? uri : null,
            "mailto" => uri.OriginalString.Length > "mailto:".Length ? uri : null,
            _ => null,
        };
    }

    private static string Lines(string text) => text.Contains('\r') ? text.Replace("\r\n", "\n").Replace('\r', '\n') : text;

    /// <summary>False for a text where nothing changes when it is read (the search runs this on every event).</summary>
    private static bool NeedsReading(string text) =>
        text.AsSpan().IndexOfAny('<', '&', '@') >= 0
        || text.Contains("http", StringComparison.OrdinalIgnoreCase) || text.Contains("www.", StringComparison.OrdinalIgnoreCase);

    /// <summary>Entities of HTML 4 and the numeric ones, shown as characters. The others (and the ones without ";") stay as they are.</summary>
    internal static string DecodeEntities(string text)
    {
        if (!text.Contains('&')) return text;
        var sb = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var semicolon = text[i] == '&' ? text.IndexOf(';', i + 1, Math.Min(10, text.Length - i - 1)) : -1;
            if (semicolon < 0 || Entity(text.AsSpan(i + 1, semicolon - i - 1)) is not { } decoded)
            {
                sb.Append(text[i]);
                continue;
            }

            sb.Append(decoded);
            i = semicolon;
        }

        return sb.ToString();
    }

    private static string? Entity(ReadOnlySpan<char> name)
    {
        if (name.Length == 0) return null;
        if (name[0] != '#') return Entities.GetValueOrDefault(name.ToString()) is var named and > 0 ? char.ConvertFromUtf32(named) : null;
        var hex = name.Length > 1 && name[1] is 'x' or 'X';
        var digits = name[(hex ? 2 : 1)..];
        if (digits.Length == 0 || !int.TryParse(digits, hex ? NumberStyles.HexNumber : NumberStyles.None, CultureInfo.InvariantCulture, out var code)) return null;
        if (code is < 1 or > 0x10FFFF or (>= 0xD800 and <= 0xDFFF)) return null;
        if (code < 32 && code is not (9 or 10)) return null;
        return char.ConvertFromUtf32(code);
    }

    private static Dictionary<string, int> LoadEntities()
    {
        const string list =
        "quot:34 amp:38 apos:39 lt:60 gt:62 nbsp:160 iexcl:161 cent:162 pound:163 curren:164 yen:165 brvbar:166 sect:167 " +
        "uml:168 copy:169 ordf:170 laquo:171 not:172 shy:173 reg:174 macr:175 deg:176 plusmn:177 sup2:178 sup3:179 " +
        "acute:180 micro:181 para:182 middot:183 cedil:184 sup1:185 ordm:186 raquo:187 frac14:188 frac12:189 frac34:190 " +
        "iquest:191 Agrave:192 Aacute:193 Acirc:194 Atilde:195 Auml:196 Aring:197 AElig:198 Ccedil:199 Egrave:200 " +
        "Eacute:201 Ecirc:202 Euml:203 Igrave:204 Iacute:205 Icirc:206 Iuml:207 ETH:208 Ntilde:209 Ograve:210 " +
        "Oacute:211 Ocirc:212 Otilde:213 Ouml:214 times:215 Oslash:216 Ugrave:217 Uacute:218 Ucirc:219 Uuml:220 " +
        "Yacute:221 THORN:222 szlig:223 agrave:224 aacute:225 acirc:226 atilde:227 auml:228 aring:229 aelig:230 " +
        "ccedil:231 egrave:232 eacute:233 ecirc:234 euml:235 igrave:236 iacute:237 icirc:238 iuml:239 eth:240 " +
        "ntilde:241 ograve:242 oacute:243 ocirc:244 otilde:245 ouml:246 divide:247 oslash:248 ugrave:249 uacute:250 " +
        "ucirc:251 uuml:252 yacute:253 thorn:254 yuml:255 OElig:338 oelig:339 Scaron:352 scaron:353 Yuml:376 fnof:402 " +
        "circ:710 tilde:732 Alpha:913 Beta:914 Gamma:915 Delta:916 Epsilon:917 Zeta:918 Eta:919 Theta:920 Iota:921 " +
        "Kappa:922 Lambda:923 Mu:924 Nu:925 Xi:926 Omicron:927 Pi:928 Rho:929 Sigma:931 Tau:932 Upsilon:933 Phi:934 " +
        "Chi:935 Psi:936 Omega:937 alpha:945 beta:946 gamma:947 delta:948 epsilon:949 zeta:950 eta:951 theta:952 " +
        "iota:953 kappa:954 lambda:955 mu:956 nu:957 xi:958 omicron:959 pi:960 rho:961 sigmaf:962 sigma:963 tau:964 " +
        "upsilon:965 phi:966 chi:967 psi:968 omega:969 thetasym:977 upsih:978 piv:982 ensp:8194 emsp:8195 thinsp:8201 " +
        "zwnj:8204 zwj:8205 lrm:8206 rlm:8207 ndash:8211 mdash:8212 lsquo:8216 rsquo:8217 sbquo:8218 ldquo:8220 " +
        "rdquo:8221 bdquo:8222 dagger:8224 Dagger:8225 bull:8226 hellip:8230 permil:8240 prime:8242 Prime:8243 " +
        "lsaquo:8249 rsaquo:8250 oline:8254 frasl:8260 euro:8364 image:8465 weierp:8472 real:8476 trade:8482 " +
        "alefsym:8501 larr:8592 uarr:8593 rarr:8594 darr:8595 harr:8596 crarr:8629 lArr:8656 uArr:8657 rArr:8658 " +
        "dArr:8659 hArr:8660 forall:8704 part:8706 exist:8707 empty:8709 nabla:8711 isin:8712 notin:8713 ni:8715 " +
        "prod:8719 sum:8721 minus:8722 lowast:8727 radic:8730 prop:8733 infin:8734 ang:8736 and:8743 or:8744 cap:8745 " +
        "cup:8746 int:8747 there4:8756 sim:8764 cong:8773 asymp:8776 ne:8800 equiv:8801 le:8804 ge:8805 sub:8834 " +
        "sup:8835 nsub:8836 sube:8838 supe:8839 oplus:8853 otimes:8855 perp:8869 sdot:8901 lceil:8968 rceil:8969 " +
        "lfloor:8970 rfloor:8971 lang:9001 rang:9002 loz:9674 spades:9824 clubs:9827 hearts:9829 diams:9830 ";
        var map = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var pair in list.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var colon = pair.IndexOf(':');
            map[pair[..colon]] = int.Parse(pair[(colon + 1)..], CultureInfo.InvariantCulture);
        }

        return map;
    }

    private readonly record struct Tag(string Name, bool Closing, bool SelfClosing, string Attributes, int End, bool Known)
    {
        /// <summary>A tag that starts or ends a line: the spaces and line breaks between two of them are only how the text was laid out.</summary>
        public bool IsBlock => Known && Name is "ul" or "ol" or "li" or "br" or "p" or "div" or "h1" or "h2" or "h3" or "html" or "head" or "body" or "!doctype";
    }

    /// <summary>
    /// Reads the tag that starts at <paramref name="at"/> ("&lt;"). Known is true for the tags that are read; any other tag (or comment,
    /// or declaration) is returned as a piece of text, whole, so that what is inside of it is not read. False when it is not a tag.
    /// </summary>
    private static bool TryTag(string text, int at, out Tag tag)
    {
        tag = default;
        var p = at + 1;
        if (p >= text.Length) return false;
        if (text[p] is '!' or '?')
        {
            var comment = string.CompareOrdinal(text, p, "!--", 0, 3) == 0;
            var close = comment
                ? text.IndexOf("-->", p + 3, Math.Min(MaxLength, text.Length - p - 3), StringComparison.Ordinal) is var c and >= 0 ? c + 3 : -1
                : text.IndexOf('>', p, Math.Min(MaxTag, text.Length - p)) is var g and >= 0 ? g + 1 : -1;
            if (close < 0) return false;
            // A comment and the DOCTYPE are removed ("Known": they are read, and show nothing); any other declaration is text.
            var doctype = !comment && string.Compare(text, p, "!doctype", 0, 8, StringComparison.OrdinalIgnoreCase) == 0;
            tag = new Tag(comment ? "!--" : doctype ? "!doctype" : "", false, false, "", close, comment || doctype);
            return true;
        }

        var closing = text[p] == '/';
        if (closing) p++;
        var nameStart = p;
        while (p < text.Length && (char.IsAsciiLetterOrDigit(text[p]) || text[p] is '-' or ':' or '_')) p++;
        if (p == nameStart || !char.IsAsciiLetter(text[nameStart])) return false;
        if (p >= text.Length || !(char.IsWhiteSpace(text[p]) || text[p] is '/' or '>')) return false;

        // The end of the tag: a ">" outside of quotes.
        var quote = (char)0;
        var end = p;
        var limit = Math.Min(text.Length, at + MaxTag);
        for (; end < limit; end++)
        {
            var c = text[end];
            if (quote != 0) { if (c == quote) quote = (char)0; }
            else if (c is '"' or '\'') quote = c;
            else if (c == '>') break;
        }

        if (end >= limit) return false;
        var name = text[nameStart..p].ToLowerInvariant();
        var attributes = text[p..end];
        tag = new Tag(name, closing, attributes.TrimEnd().EndsWith('/'), attributes, end + 1, Tags.Contains(name));
        return true;
    }

    /// <summary>Reads the text in one pass, without recursion: the cost is linear in its length.</summary>
    private sealed class Reader(string text)
    {
        private readonly List<DescriptionRun> _runs = [];
        private readonly StringBuilder _pending = new();
        private readonly List<(Uri? Link, bool Raw)> _anchors = [];
        private readonly List<(bool Ordered, int Count)> _lists = [];
        private int _bold, _italic, _underline, _openItems, _openParagraphs, _openDivs, _h1, _h2, _h3;
        private int _headCloseNotFoundFrom = int.MaxValue; // no closing head after this position: the search is not repeated
        private Uri? _link; // the link the text is in
        private (DescriptionStyle Style, Uri? Link)? _current;
        private bool _any, _lineStart, _afterMarker;
        private int _breakPending; // line breaks to write before the next text: 1 for the end of a line, 2 for an empty line too
        private int _trailingNewlines; // line breaks at the end of what was written (at most 2)

        public IReadOnlyList<DescriptionRun> Read()
        {
            var i = 0;
            var afterBlock = true; // the start of the text counts as a tag that ends a line
            while (i < text.Length)
            {
                if (text[i] == '<')
                {
                    if (TryTag(text, i, out var tag))
                    {
                        if (tag.Known && tag.Name == "head" && !tag.Closing && HeadEnd(tag.End) is >= 0 and var end)
                        {
                            // The head and all that is in it (title, meta, style, script) is not shown.
                            i = end;
                            afterBlock = true;
                            continue;
                        }

                        if (tag.Known) Apply(tag, text[i..tag.End]);
                        else Write(text[i..tag.End]);
                        if (tag.Name != "!--") afterBlock = tag.IsBlock; // a comment is transparent
                        i = tag.End;
                    }
                    else
                    {
                        Write("<");
                        afterBlock = false;
                        i++;
                    }

                    continue;
                }

                var next = text.IndexOf('<', i);
                if (next < 0) next = text.Length;
                var piece = text[i..next];
                var following = default(Tag);
                var hasFollowing = next < text.Length && TryTag(text, next, out following);
                // Spaces and line breaks between the tags that start or end a line are only how the text was laid out.
                var skip = afterBlock && string.IsNullOrWhiteSpace(piece) && (next >= text.Length || (hasFollowing && following.IsBlock));
                if (!skip)
                {
                    Write(piece);
                    afterBlock = false;
                }

                i = next;
            }

            Flush();
            return _runs;
        }

        /// <summary>Where the head that ends at <paramref name="from"/> ends (after its closing tag), or -1 if it is never closed.</summary>
        private int HeadEnd(int from)
        {
            if (from >= _headCloseNotFoundFrom) return -1;
            for (var at = from; ;)
            {
                var close = text.IndexOf("</head", at, StringComparison.OrdinalIgnoreCase);
                if (close < 0)
                {
                    _headCloseNotFoundFrom = Math.Min(_headCloseNotFoundFrom, from);
                    return -1;
                }

                var after = close + "</head".Length;
                if (after < text.Length && (char.IsWhiteSpace(text[after]) || text[after] is '>' or '/'))
                {
                    var gt = text.IndexOf('>', after);
                    return gt < 0 ? -1 : gt + 1;
                }

                at = after; // "</header>"
            }
        }

        private void Apply(Tag tag, string source)
        {
            switch (tag.Name)
            {
                case "b" or "strong": Toggle(ref _bold, tag); break;
                case "i" or "em": Toggle(ref _italic, tag); break;
                case "u": Toggle(ref _underline, tag); break;
                case "p": Block(ref _openParagraphs, tag, 2); break;
                case "div": Block(ref _openDivs, tag, 1); break;
                case "h1": Block(ref _h1, tag, 1); break;
                case "h2": Block(ref _h2, tag, 1); break;
                case "h3": Block(ref _h3, tag, 1); break;
                case "br" when !tag.Closing:
                    ResolveBreak();
                    Append("\n", DescriptionStyle.None, null);
                    break;
                case "ul" or "ol":
                    if (!tag.Closing && !tag.SelfClosing)
                    {
                        EnsureBreak();
                        _lists.Add((tag.Name == "ol", 0));
                    }
                    else if (tag.Closing && _lists.Count > 0)
                    {
                        _lists.RemoveAt(_lists.Count - 1);
                        EnsureBreak();
                    }

                    break;
                case "li":
                    if (!tag.Closing)
                    {
                        EnsureBreak();
                        _openItems++;
                        Append(Marker(), DescriptionStyle.None, null);
                        _lineStart = true;
                        _afterMarker = true;
                    }
                    else if (_openItems > 0)
                    {
                        _openItems--;
                        _afterMarker = false;
                        EnsureBreak();
                    }

                    break;
                case "a" when !tag.Closing:
                    var m = Href.Match(tag.Attributes);
                    var href = m.Success ? DecodeEntities(m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Value) : null;
                    var uri = SafeLink(href);
                    _anchors.Add((uri, uri is null));
                    Relink();
                    if (uri is null) Write(source); // an address the app does not open: the tag is text
                    break;
                case "a" when _anchors.Count > 0:
                    var (_, raw) = _anchors[^1];
                    _anchors.RemoveAt(_anchors.Count - 1);
                    Relink();
                    if (raw) Write(source);
                    break;
            }
        }

        /// <summary>A tag that starts and ends a line: the closing one is not shown if nothing was opened.</summary>
        private void Block(ref int open, Tag tag, int breaks)
        {
            if (tag.SelfClosing && !tag.Closing) return;
            if (!tag.Closing)
            {
                open++;
                if (!_afterMarker) EnsureBreak(breaks); // right after a bullet the block stays on its line
                _lineStart = true; // a block does not start with spaces
            }
            else if (open > 0)
            {
                open--;
                EnsureBreak(breaks);
            }
        }

        private void Toggle(ref int counter, Tag tag)
        {
            if (tag.SelfClosing && !tag.Closing) return;
            if (!tag.Closing) counter++;
            else if (counter > 0) counter--;
        }

        private void Relink() => _link = _anchors.LastOrDefault(a => a.Link is not null).Link;

        private string Marker()
        {
            if (_lists.Count == 0) return "• ";
            var (ordered, count) = _lists[^1];
            _lists[^1] = (ordered, count + 1);
            return new string(' ', 2 * Math.Min(_lists.Count - 1, MaxIndent)) + (ordered ? $"{count + 1}. " : "• ");
        }

        /// <summary>The end of a line, written only if more text follows (or by a line break in the text, which is the break).</summary>
        private void EnsureBreak(int breaks = 1)
        {
            TrimEndSpaces();
            if (_any) _breakPending = Math.Max(_breakPending, breaks);
        }

        /// <summary>As in HTML, the spaces at the end of a line of a list are not shown: "&lt;li&gt; uno &lt;/li&gt;" is "uno".</summary>
        private void TrimEndSpaces()
        {
            while (true)
            {
                while (_pending.Length > 0 && _pending[^1] is ' ' or '\t')
                    _pending.Length--;
                if (_pending.Length > 0 || _runs.Count == 0) return;
                var last = _runs[^1];
                var trimmed = last.Text.TrimEnd(' ', '\t');
                if (trimmed.Length == last.Text.Length) return;
                if (trimmed.Length == 0) _runs.RemoveAt(_runs.Count - 1);
                else _runs[^1] = last with { Text = trimmed };
                if (trimmed.Length > 0) return;
            }
        }

        private void ResolveBreak()
        {
            var missing = _breakPending - _trailingNewlines;
            if (missing > 0) AddRun(new string('\n', missing), DescriptionStyle.None, null);
            _breakPending = 0;
        }

        /// <summary>Text as it is written: entities as characters, the addresses of sites and emails as links (except inside a link).</summary>
        private void Write(string raw)
        {
            var piece = DecodeEntities(raw);
            if (_lineStart || _breakPending > 0)
            {
                // As in HTML, a line of a list does not start with spaces.
                piece = piece.TrimStart(' ', '\t');
                if (piece.Length == 0) return;
            }

            _lineStart = false;
            _afterMarker = false;
            var heading = _h1 > 0 ? DescriptionStyle.Heading1 : _h2 > 0 ? DescriptionStyle.Heading2 : _h3 > 0 ? DescriptionStyle.Heading3 : 0;
            var style = (_bold > 0 || heading != 0 ? DescriptionStyle.Bold : 0) | (_italic > 0 ? DescriptionStyle.Italic : 0) | (_underline > 0 ? DescriptionStyle.Underline : 0) | heading;
            if (_link is not null || !NeedsAddresses(piece))
            {
                Append(piece, style, _link);
                return;
            }

            var last = 0;
            foreach (Match match in Address.Matches(piece))
            {
                var length = Trimmed(match.Value);
                var address = match.Groups["mail"].Success ? "mailto:" + match.Value[..length] : (match.Value.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? "https://" : "") + match.Value[..length];
                if (SafeLink(address) is not { } uri) continue;
                Append(piece[last..match.Index], style, null);
                Append(piece.Substring(match.Index, length), style | DescriptionStyle.Underline, uri);
                last = match.Index + length;
            }

            Append(piece[last..], style, null);
        }

        private static bool NeedsAddresses(string piece) =>
            piece.Contains('@') || piece.Contains("http", StringComparison.OrdinalIgnoreCase) || piece.Contains("www.", StringComparison.OrdinalIgnoreCase);

        /// <summary>The length of the address without the punctuation of the sentence it is in ("see www.e.org.", "(https://e.org)").</summary>
        private static int Trimmed(string value)
        {
            var end = value.Length;
            while (end > 0)
            {
                var c = value[end - 1];
                var drop = c is '.' or ',' or ';' or ':' or '!' or '?' or '\'' or '"'
                    || (c is ')' && !value.Contains('(')) || (c is ']' && !value.Contains('[')) || (c is '}' && !value.Contains('{'));
                if (!drop) break;
                end--;
            }

            return end;
        }

        private void Append(string piece, DescriptionStyle style, Uri? link)
        {
            if (piece.Length == 0) return;
            if (_breakPending > 0)
            {
                // The line breaks of the text count: a text that starts with one does not need it written again.
                var lead = 0;
                while (lead < piece.Length && lead < _breakPending && piece[lead] == '\n') lead++;
                var missing = _breakPending - _trailingNewlines - lead;
                _breakPending = 0;
                if (missing > 0) AddRun(new string('\n', missing), DescriptionStyle.None, null);
            }

            AddRun(piece, style | (link is not null ? DescriptionStyle.Underline : 0), link);
        }

        private void AddRun(string piece, DescriptionStyle style, Uri? link)
        {
            if (_current is { } c && (c.Style != style || !ReferenceEquals(c.Link, link))) Flush();
            _current = (style, link);
            _pending.Append(piece);
            _any = true;
            var newlines = 0;
            while (newlines < piece.Length && piece[^(newlines + 1)] == '\n') newlines++;
            _trailingNewlines = Math.Min(2, newlines == piece.Length ? _trailingNewlines + newlines : newlines);
        }

        private void Flush()
        {
            if (_current is { } c && _pending.Length > 0) _runs.Add(new DescriptionRun(_pending.ToString(), c.Style, c.Link));
            _pending.Clear();
            _current = null;
        }
    }
}
