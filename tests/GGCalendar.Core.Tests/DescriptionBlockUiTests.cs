using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GGCalendar.App.Views;
using GGCalendar.Core.Models;
using GGCalendar.Core.Tests.Support;

namespace GGCalendar.Core.Tests;

/// <summary>The description in the card of an event (read only): the formatting, the links and the click on a link. No Google, no data of the app.</summary>
public class DescriptionBlockUiTests
{
    private static CalendarEvent Ev(string description) =>
        new("e1", "c1", "Evento", new DateTime(2026, 10, 28, 10, 0, 0), new DateTime(2026, 10, 28, 11, 0, 0), Description: description);

    private static Task InCardAsync(string description, Func<Window, EventDetailsCard, DescriptionBlock, Task> test) => HeadlessApp.RunAsync(async () =>
    {
        var card = new EventDetailsCard(Ev(description), null, "#1A73E8", "Calendario", "Account", use24Hour: true, dark: false);
        var window = new Window { Width = 600, Height = 600, Content = card };
        window.Show();
        try
        {
            await Task.Delay(100);
            Dispatcher.UIThread.RunJobs();
            var block = card.GetVisualDescendants().OfType<DescriptionBlock>().Single();
            await test(window, card, block);
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    /// Like <see cref="InCardAsync"/>, for the tests that click on the text: they need the real positions of the letters, which the
    /// headless session has only when it draws for real (GGCALENDAR_TEST_SCREENSHOTS=1, the same as T273); otherwise they do nothing.
    /// </summary>
    private static Task InDrawnCardAsync(string description, Func<Window, EventDetailsCard, DescriptionBlock, Task> test) =>
        HeadlessApp.DrawsForReal ? InCardAsync(description, test) : Task.CompletedTask;

    private static List<Run> Runs(DescriptionBlock block) => block.Inlines!.OfType<Run>().ToList();

    /// <summary>The middle of a character of the text, in the coordinates of the window.</summary>
    private static Point CenterOf(Window window, DescriptionBlock block, int offset)
    {
        var rect = block.TextLayout.HitTestTextRange(offset, 1).First();
        return block.TranslatePoint(rect.Center, window)!.Value;
    }

    /// <summary>What the layout says about a character, to understand a click that finds no link.</summary>
    private static string Probe(DescriptionBlock block, int offset)
    {
        var point = block.TextLayout.HitTestTextRange(offset, 1).First().Center;
        var hit = block.TextLayout.HitTestPoint(point);
        return $"posizione trovata {hit.TextPosition}, dentro {hit.IsInside}, link {block.LinkAt(point)}";
    }

    private static void Click(Window window, Point point)
    {
        window.MouseMove(point);
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    [Fact]
    [Trait("TestCase", "T267")]
    public Task Card_ShowsBoldItalicUnderlineAndLinks() => InCardAsync(
        "Ordine: <b>forte</b> <i>corsivo</i> <u>sottolineato</u> <a href=\"https://example.org/doc\">documento</a>",
        (_, _, block) =>
        {
            var runs = Runs(block);
            Assert.Equal(FontWeight.Bold, runs.Single(r => r.Text == "forte").FontWeight);
            Assert.Equal(FontStyle.Italic, runs.Single(r => r.Text == "corsivo").FontStyle);
            Assert.NotNull(runs.Single(r => r.Text == "sottolineato").TextDecorations);
            var link = runs.Single(r => r.Text == "documento");
            Assert.Equal(Color.Parse("#1A73E8"), Assert.IsAssignableFrom<ISolidColorBrush>(link.Foreground).Color);
            Assert.NotNull(link.TextDecorations);
            Assert.DoesNotContain("<", string.Concat(runs.Select(r => r.Text)));
            return Task.CompletedTask;
        });

    [Fact]
    [Trait("TestCase", "T267")]
    public Task Card_ShowsTitlesBoldAndBigger_TheBiggestFirst() => InCardAsync(
        "<h1>Uno</h1><h2>Due</h2><h3>Tre</h3>normale <strong>forte</strong> <em>enfasi</em>",
        (_, _, block) =>
        {
            var runs = Runs(block);
            var sizes = new[] { "Uno", "Due", "Tre" }.Select(w => runs.Single(r => r.Text == w)).ToList();
            Assert.All(sizes, r => Assert.Equal(FontWeight.Bold, r.FontWeight));
            Assert.True(sizes[0].FontSize > sizes[1].FontSize && sizes[1].FontSize > sizes[2].FontSize, "i titoli devono andare dal più grande al più piccolo");
            Assert.True(sizes[2].FontSize > 14, "il titolo piccolo è più grande del testo");
            Assert.Equal(FontWeight.Bold, runs.Single(r => r.Text == "forte").FontWeight);
            Assert.Equal(FontStyle.Italic, runs.Single(r => r.Text == "enfasi").FontStyle);
            return Task.CompletedTask;
        });

    [Fact]
    [Trait("TestCase", "T267")]
    public Task Card_ShowsListsAndLineBreaks() => InCardAsync(
        "Porta:<br><ul><li>pane</li><li>vino</li></ul>",
        (_, _, block) =>
        {
            Assert.Equal("Porta:\n• pane\n• vino", string.Concat(Runs(block).Select(r => r.Text)));
            return Task.CompletedTask;
        });

    [Fact]
    [Trait("TestCase", "T267")]
    public Task Card_PlainDescription_IsShownAsItIs_WithClickableAddresses() => InCardAsync(
        "Riga uno\nVedi https://example.org/x per i dettagli",
        (_, _, block) =>
        {
            Assert.Equal("Riga uno\nVedi https://example.org/x per i dettagli", string.Concat(Runs(block).Select(r => r.Text)));
            Assert.Single(Runs(block), r => r.Text == "https://example.org/x" && r.TextDecorations is not null);
            return Task.CompletedTask;
        });

    [Fact]
    [Trait("TestCase", "T268")]
    public Task Click_OnALink_AsksToOpenIt_AndNotOnOtherText() => InDrawnCardAsync(
        "Vai al <a href=\"https://example.org/doc\">documento</a> subito",
        (window, card, block) =>
        {
            var asked = new List<Uri>();
            card.LinkRequested += asked.Add;
            var text = string.Concat(Runs(block).Select(r => r.Text));

            Click(window, CenterOf(window, block, text.IndexOf("Vai", StringComparison.Ordinal)));
            Click(window, CenterOf(window, block, text.IndexOf("subito", StringComparison.Ordinal)));
            Assert.Empty(asked);

            Click(window, CenterOf(window, block, text.IndexOf("documento", StringComparison.Ordinal) + 2));
            Assert.Equal(["https://example.org/doc"], asked.Select(u => u.AbsoluteUri).ToArray());
            return Task.CompletedTask;
        });

    [Fact]
    [Trait("TestCase", "T268")]
    public Task Click_OnALinkInAListAfterLineBreaks_AsksToOpenIt() => InDrawnCardAsync(
        "<ul><li><b>grassetto</b> e <i>corsivo</i></li><li><a href=\"https://example.org\">link</a></li></ul>",
        (window, card, block) =>
        {
            var asked = new List<Uri>();
            card.LinkRequested += asked.Add;
            var text = string.Concat(Runs(block).Select(r => r.Text));
            Assert.Equal("• grassetto e corsivo\n• link", text);
            var at = text.IndexOf("link", StringComparison.Ordinal);
            for (var i = 0; i < 4; i++)
            {
                asked.Clear();
                Click(window, CenterOf(window, block, at + i));
                Assert.True(asked.Count == 1, $"nessun clic sul carattere {i} di 'link' (offset {at + i}): {Probe(block, at + i)}");
            }

            return Task.CompletedTask;
        });

    [Fact]
    [Trait("TestCase", "T268")]
    public Task Click_OnEveryBareAddressEvenOnTheLinesThatWrap_AsksToOpenIt() => InDrawnCardAsync(
        "Sito: https://example.org/doc, www.example.org, mail: mario@example.org. Non link: esempio.it e file.txt",
        (window, card, block) =>
        {
            var asked = new List<Uri>();
            card.LinkRequested += asked.Add;
            var text = string.Concat(Runs(block).Select(r => r.Text));
            // The text is wider than the card: the second address and the email are on the lines under the first.
            Assert.True(block.TextLayout.TextLines.Count > 1, "il testo doveva andare a capo");
            foreach (var (word, expected) in new[] { ("https://example.org/doc", "https://example.org/doc"), ("www.example.org", "https://www.example.org/"), ("mario@example.org", "mailto:mario@example.org") })
            {
                var at = text.IndexOf(word, StringComparison.Ordinal);
                for (var i = 0; i < word.Length; i += 3)
                {
                    asked.Clear();
                    Click(window, CenterOf(window, block, at + i));
                    Assert.True(asked.Count == 1, $"nessun clic sul carattere {i} di '{word}': {Probe(block, at + i)}");
                    Assert.Equal(expected, asked[0].Scheme == "mailto" ? asked[0].OriginalString : asked[0].AbsoluteUri);
                }
            }

            foreach (var word in new[] { "Non link", "esempio.it", "file.txt" })
            {
                asked.Clear();
                Click(window, CenterOf(window, block, text.IndexOf(word, StringComparison.Ordinal) + 1));
                Assert.Empty(asked);
            }

            return Task.CompletedTask;
        });

    [Fact]
    [Trait("TestCase", "T268")]
    public Task Click_OnALinkWithAForbiddenScheme_DoesNothing() => InDrawnCardAsync(
        "<a href=\"javascript:alert(1)\">clicca</a> <a href=\"file:///c:/windows/system32/calc.exe\">calcolatrice</a>",
        (window, card, block) =>
        {
            var asked = new List<Uri>();
            card.LinkRequested += asked.Add;
            var text = string.Concat(Runs(block).Select(r => r.Text));
            Click(window, CenterOf(window, block, text.IndexOf("clicca", StringComparison.Ordinal) + 1));
            Click(window, CenterOf(window, block, text.IndexOf("calcolatrice", StringComparison.Ordinal) + 1));
            Assert.Empty(asked);
            Assert.Null(block.LinkAt(new Point(1, 1)));
            return Task.CompletedTask;
        });

    [Fact]
    [Trait("TestCase", "T268")]
    public Task Link_ShowsTheRealAddress_NotTheTextOfTheLink() => InDrawnCardAsync(
        "<a href=\"https://truffa.example/login\">https://banca.example</a>",
        (window, card, block) =>
        {
            Uri? asked = null;
            card.LinkRequested += u => asked = u;
            Click(window, CenterOf(window, block, 3));
            Assert.Equal("https://truffa.example/login", asked?.AbsoluteUri);
            return Task.CompletedTask;
        });

    [Fact]
    [Trait("TestCase", "T267")]
    public Task LongDescription_IsCutAtEightLines() => InCardAsync(
        string.Join("<br>", Enumerable.Range(1, 40).Select(i => $"<b>riga {i}</b>")),
        (_, _, block) =>
        {
            Assert.Equal(8, block.MaxLines);
            Assert.True(block.TextLayout.TextLines.Count <= 8);
            return Task.CompletedTask;
        });
}
