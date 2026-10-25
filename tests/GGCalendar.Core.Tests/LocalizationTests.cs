using System.Globalization;
using System.Text.RegularExpressions;
using GGCalendar.Core.Localization;
using GGCalendar.Core.Models;

namespace GGCalendar.Core.Tests;

/// <summary>
/// T129-T133: the texts of the app in the languages it supports (issue #3): which language is chosen, that every language has all
/// the texts, that the code only asks for texts that exist, and that a message is made again in another language.
/// These change the language of the process, so they run one at a time with the tests of the app and put it back as it was.
/// </summary>
[Collection(Support.AppDriver.CollectionName)]
public class LocalizationTests
{
    /// <summary>Runs <paramref name="body"/> and puts the language back as it was, whatever happens.</summary>
    private static void KeepingLanguage(Action body)
    {
        var language = L.Language;
        try
        {
            body();
        }
        finally
        {
            L.Apply(language);
        }
    }

    [Theory]
    [Trait("TestCase", "T129")]
    [InlineData("system", "it-IT", "it")]
    [InlineData("system", "en-GB", "en")]
    [InlineData("system", "de-DE", "de")]
    [InlineData("system", "es-MX", "es")]
    [InlineData("system", "fr-CA", "fr")]
    [InlineData("system", "pt-BR", "en")] // Windows in a language the app does not have: English
    [InlineData("system", "ja-JP", "en")]
    [InlineData(null, "de-DE", "de")] // nothing saved means "system"
    [InlineData("", "fr-FR", "fr")]
    [InlineData("de", "it-IT", "de")] // a chosen language wins over the one of Windows
    [InlineData("FR", "it-IT", "fr")]
    [InlineData("xx", "it-IT", "en")] // a code the app does not know (an old or edited file)
    public void T129_TheLanguageIsTheOneChosenElseTheOneOfWindowsElseEnglish(string? setting, string windows, string expected)
    {
        Assert.Equal(expected, L.Resolve(setting, CultureInfo.GetCultureInfo(windows)));
        Assert.Equal("system", new AppSettings().Language); // what a new installation has
    }

    [Fact]
    [Trait("TestCase", "T130")]
    public void T130_ChangingTheLanguageIsAnnouncedOnceAndNotWhenItStaysTheSame() => KeepingLanguage(() =>
    {
        L.Apply("it");
        var announced = 0;
        void Count() => announced++;
        L.Changed += Count;
        try
        {
            L.Apply("it");
            Assert.Equal(0, announced);

            L.Apply("en");
            Assert.Equal((1, "en", "en-US"), (announced, L.Language, L.Culture.Name));

            L.Apply("system", CultureInfo.GetCultureInfo("fr-FR")); // "system" is read from the culture given
            Assert.Equal((2, "fr", "fr-FR"), (announced, L.Language, L.Culture.Name));
            Assert.Equal("it-IT", CultureInfo.GetCultureInfo("it-IT").Name);
        }
        finally
        {
            L.Changed -= Count;
        }
    });

    [Theory]
    [Trait("TestCase", "T131")]
    [InlineData("en")]
    public void T131_EveryLanguageHasAllTheTextsOfItalianWithTheSamePlaceholders(string language)
    {
        var italian = L.Texts("it");
        var texts = L.Texts(language);

        Assert.Empty(italian.Keys.Except(texts.Keys).OrderBy(k => k));
        Assert.Empty(texts.Keys.Except(italian.Keys).OrderBy(k => k));
        Assert.Empty(texts.Where(t => string.IsNullOrWhiteSpace(t.Value)).Select(t => t.Key));

        static string Placeholders(string text) =>
            string.Join(",", Regex.Matches(text, @"\{(\d+)(?::[^}]*)?\}").Select(m => m.Groups[1].Value).Distinct().OrderBy(x => x));
        var different = italian.Where(t => texts.TryGetValue(t.Key, out var other) && Placeholders(t.Value) != Placeholders(other)).Select(t => t.Key).ToList();
        Assert.Empty(different);

        // Texts with a count come in pairs: one and other.
        foreach (var language2 in new[] { "it", language })
        {
            var keys = L.Texts(language2).Keys;
            Assert.False(keys.Where(k => k.EndsWith(".one", StringComparison.Ordinal)).Any(k => !keys.Contains(k[..^4] + ".other")));
            Assert.False(keys.Where(k => k.EndsWith(".other", StringComparison.Ordinal)).Any(k => !keys.Contains(k[..^6] + ".one")));
        }
    }

    /// <summary>The folder of the solution, found from the folder of this file (the tests may run from another folder).</summary>
    private static string RepoRoot([System.Runtime.CompilerServices.CallerFilePath] string thisFile = "")
    {
        var root = Path.GetDirectoryName(thisFile);
        while (root is not null && !File.Exists(Path.Combine(root, "GGCalendar.sln"))) root = Path.GetDirectoryName(root);
        return root ?? throw new InvalidOperationException("GGCalendar.sln non trovato.");
    }

    [Fact]
    [Trait("TestCase", "T132")]
    public void T132_TheCodeAsksOnlyForTextsThatExistAndTheWindowsHaveNoTextWrittenInThem()
    {
        var root = RepoRoot();
        var sources = Directory.GetFiles(Path.Combine(root, "src"), "*.*", SearchOption.AllDirectories)
            .Where(f => (f.EndsWith(".cs") || f.EndsWith(".axaml")) && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .ToList();
        var italian = L.Texts("it");

        var used = new HashSet<string>();
        var hardCoded = new List<string>();
        foreach (var file in sources)
        {
            var text = File.ReadAllText(file);
            foreach (Match m in Regex.Matches(text, @"\{loc:T ([\w.]+)\}")) used.Add(m.Groups[1].Value);
            foreach (Match m in Regex.Matches(text, @"(?:L\.T|Msg\.Of|SetStatus|L\.Has)\(\s*""([\w.]+)""")) used.Add(m.Groups[1].Value);
            foreach (Match m in Regex.Matches(text, @"(?:L\.N|Msg\.Count)\(\s*""([\w.]+)""")) { used.Add(m.Groups[1].Value + ".one"); used.Add(m.Groups[1].Value + ".other"); }

            // A text written in a window: an attribute that shows text and is neither a key, nor a binding, nor an icon.
            if (file.EndsWith(".axaml"))
            {
                foreach (Match m in Regex.Matches(text, @"\s(Content|Text|Header|ToolTip\.Tip|Title|Watermark|PlaceholderText)=""([^""{]*)"""))
                {
                    var value = m.Groups[2].Value;
                    if (Regex.IsMatch(value, "[A-Za-zÀ-ÿ]{2,}") && !value.StartsWith("&#x") && value != "GGCalendar") hardCoded.Add($"{Path.GetFileName(file)}: {m.Groups[1].Value}=\"{value}\"");
                }
            }
        }

        used.Remove("ics.component."); // the name of the component is added by the code
        Assert.Empty(used.Where(k => !italian.ContainsKey(k)).OrderBy(k => k));
        Assert.Empty(hardCoded);
    }

    [Fact]
    [Trait("TestCase", "T133")]
    public void T133_AMessageKeepsItsKeyAndIsMadeAgainInTheNewLanguage() => KeepingLanguage(() =>
    {
        var imported = Msg.Join(Msg.Count("status.imported", 5, "Lavoro"), Msg.Count("status.imported_skipped", 1), Msg.Literal("."));
        var one = Msg.Count("status.exported", 1, "Lavoro");

        // The key and the numbers are what a test (or any code) checks, whatever the language is.
        Assert.Equal("status.imported.other", imported.Key);
        Assert.True(imported.Is("status.imported_skipped.one"));
        Assert.Equal("status.exported.one", one.Key);

        L.Apply("it");
        var italian = imported.Text;
        Assert.Contains("5", italian);
        Assert.Contains("Lavoro", italian);
        L.Apply("en");
        var english = imported.Text;
        Assert.Contains("5", english);
        Assert.Contains("Lavoro", english);
        Assert.NotEqual(italian, english);
        Assert.Equal("Exported 1 event of the calendar \"Lavoro\".", one.Text);

        // A message inside another one is made in the language shown now too.
        var wrapper = Msg.Of("error.sync", Msg.Of("sync.account_error", "me@example.com", "x"));
        Assert.Equal("Sync error: me@example.com: x", wrapper.Text);
        L.Apply("it");
        Assert.Equal("Errore di sincronizzazione: me@example.com: x", wrapper.Text);

        // A key that exists nowhere shows itself.
        Assert.Equal("no.such.key", L.T("no.such.key"));
    });
}
