namespace GGCalendar.Core.Localization;

/// <summary>
/// A message for the user that is kept as its key and arguments, not as text: <see cref="Text"/> is made when it is shown and again
/// when the language changes, and the key (<see cref="Key"/>) is what a test checks. An argument can be another <see cref="Msg"/>.
/// </summary>
public sealed class Msg
{
    private readonly object?[] _args;
    private readonly Msg[]? _parts;

    private readonly string? _literal;

    private Msg(string key, object?[] args, Msg[]? parts, string? literal = null)
    {
        Key = key;
        _args = args;
        _parts = parts;
        _literal = literal;
    }

    /// <summary>The key of the message; for a <see cref="Join"/> the key of its first part.</summary>
    public string Key { get; }

    /// <summary>The arguments the message was made with ({0}, {1}...).</summary>
    public IReadOnlyList<object?> Args => _args;

    /// <summary>The parts of a message made with <see cref="Join"/>, else empty.</summary>
    public IReadOnlyList<Msg> Parts => _parts ?? [];

    public static Msg Of(string key, params object?[] args) => new(key, args, null);

    /// <summary>A message with a count: the key plus ".one" or ".other" is looked up; {0} is the count.</summary>
    public static Msg Count(string key, int count, params object?[] args) => new(key + (count == 1 ? ".one" : ".other"), [count, .. args], null);

    /// <summary>A piece of text that is the same in every language (a name, a number, a separator).</summary>
    public static Msg Literal(string text) => new("", [], null, text);

    /// <summary>Messages with a separator (literal text) between them.</summary>
    public static Msg JoinWith(string separator, IEnumerable<Msg> items) =>
        Join(items.SelectMany((m, i) => i == 0 ? new[] { m } : new[] { Literal(separator), m }).ToArray());

    /// <summary>Messages one after the other (no text in between).</summary>
    public static Msg Join(params Msg[] parts) => new(parts.Length > 0 ? parts[0].Key : "", [], parts);

    /// <summary>The text in the language shown now.</summary>
    public string Text => _literal is not null ? _literal : _parts is not null
        ? string.Concat(_parts.Select(p => p.Text))
        : L.T(Key, _args.Select(a => a is Msg m ? m.Text : a).ToArray());

    /// <summary>True when the message, or one of its parts, has this key.</summary>
    public bool Is(string key) => Key == key || (_parts?.Any(p => p.Is(key)) ?? false);

    public override string ToString() => Text;
}
