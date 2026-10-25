using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace GGCalendar.Core.Localization;

/// <summary>A language the app can be shown in: the code ("it"), the name in its own language and the culture used for dates.</summary>
public sealed record LanguageInfo(string Code, string NativeName, string CultureName);

/// <summary>
/// The texts of the app in the language the user chose. Texts are looked up by key (<c>"main.today"</c>) in one JSON file per language
/// embedded in this assembly; a key missing in a language falls back to English, then Italian, then the key itself.
/// The setting is "system" (follow Windows, read when the app starts; English when Windows is in a language not supported) or a language code.
/// </summary>
public static class L
{
    public const string System = "system";
    public const string Fallback = "en";

    public static IReadOnlyList<LanguageInfo> Languages { get; } =
    [
        new("it", "Italiano", "it-IT"),
        new("en", "English", "en-US"),
        new("de", "Deutsch", "de-DE"),
        new("es", "Español", "es-ES"),
        new("fr", "Français", "fr-FR"),
    ];

    private static readonly Dictionary<string, Dictionary<string, string>> Tables = [];
    private static readonly CultureInfo SystemCulture = CultureInfo.CurrentUICulture;

    /// <summary>The language shown now ("it", "en"...), never "system".</summary>
    public static string Language { get; private set; } = Fallback;

    /// <summary>Culture of the language shown: names of months and days, date formats.</summary>
    public static CultureInfo Culture { get; private set; } = CultureInfo.GetCultureInfo("en-US");

    /// <summary>Raised after the language changed: whatever shows a text must show it again.</summary>
    public static event Action? Changed;

    /// <summary>The language code a setting stands for: "system" is the language of Windows when supported, else English.</summary>
    public static string Resolve(string? setting, CultureInfo? system = null)
    {
        var wanted = string.IsNullOrWhiteSpace(setting) || setting == System ? (system ?? SystemCulture).TwoLetterISOLanguageName : setting;
        return Languages.FirstOrDefault(l => l.Code.Equals(wanted, StringComparison.OrdinalIgnoreCase))?.Code ?? Fallback;
    }

    /// <summary>Applies a setting ("system" or a language code). Raises <see cref="Changed"/> when the language shown is different.</summary>
    public static void Apply(string? setting, CultureInfo? system = null)
    {
        var code = Resolve(setting, system);
        var changed = code != Language;
        Language = code;
        Culture = CultureInfo.GetCultureInfo(Languages.First(l => l.Code == code).CultureName);
        // Controls that format by themselves (calendar picker, date and time pickers) follow the language of the app too.
        CultureInfo.DefaultThreadCurrentCulture = Culture;
        CultureInfo.DefaultThreadCurrentUICulture = Culture;
        CultureInfo.CurrentCulture = Culture;
        CultureInfo.CurrentUICulture = Culture;
        if (changed) Changed?.Invoke();
    }

    /// <summary>The text for <paramref name="key"/> with the arguments put in its {0}, {1}...</summary>
    public static string T(string key, params object?[] args)
    {
        var text = Find(key);
        return args.Length == 0 ? text : string.Format(Culture, text, args);
    }

    /// <summary>A text that depends on a count: the key plus ".one" for 1 and ".other" otherwise. {0} is the count.</summary>
    public static string N(string key, int count, params object?[] args) =>
        T(key + (count == 1 ? ".one" : ".other"), [count, .. args]);

    /// <summary>True when the language has a text for the key (no fallback).</summary>
    public static bool Has(string language, string key) => Table(language).ContainsKey(key);

    /// <summary>All the keys of a language, to check that the languages agree.</summary>
    public static IReadOnlyDictionary<string, string> Texts(string language) => Table(language);

    private static string Find(string key)
    {
        if (Table(Language).TryGetValue(key, out var text)) return text;
        if (Table(Fallback).TryGetValue(key, out text)) return text;
        return Table("it").TryGetValue(key, out text) ? text : key;
    }

    private static Dictionary<string, string> Table(string language)
    {
        lock (Tables)
        {
            if (Tables.TryGetValue(language, out var table)) return table;
            var assembly = typeof(L).Assembly;
            var name = assembly.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith($".Strings.{language}.json", StringComparison.Ordinal));
            if (name is null) return Tables[language] = [];
            using var stream = assembly.GetManifestResourceStream(name)!;
            return Tables[language] = JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? [];
        }
    }
}
