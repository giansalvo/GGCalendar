using System.Diagnostics;
using GGCalendar.Core.Ics;
using GGCalendar.Core.Models;
using GGCalendar.Core.Search;
using GGCalendar.Core.Tests.Support;

namespace GGCalendar.Core.Tests;

/// <summary>
/// The description of an event as Google Calendar shows it: only b, i, u, br, ul, ol, li and a are read (in any case, with any
/// attribute); everything else is text, found by the search; spaces and line breaks are kept; entities are characters.
/// </summary>
public class DescriptionTextTests
{
    private static string Plain(string text) => DescriptionText.ToPlainText(text);

    private static IReadOnlyList<DescriptionRun> Runs(string text) => DescriptionText.Parse(text);

    private static DescriptionStyle StyleOf(string text, string word) => Runs(text).First(r => r.Text.Contains(word, StringComparison.Ordinal)).Style;

    // ---- styles ----

    [Fact]
    [Trait("TestCase", "T258")]
    public void BoldItalicUnderlineAreRuns()
    {
        var runs = Runs("uno <b>due</b> <i>tre</i> <u>quattro</u>");
        Assert.Equal(["uno ", "due", " ", "tre", " ", "quattro"], runs.Select(r => r.Text).ToArray());
        Assert.Equal([DescriptionStyle.None, DescriptionStyle.Bold, DescriptionStyle.None, DescriptionStyle.Italic, DescriptionStyle.None, DescriptionStyle.Underline],
            runs.Select(r => r.Style).ToArray());
    }

    [Fact]
    [Trait("TestCase", "T258")]
    public void NestedStylesAreAdded()
    {
        var runs = Runs("<b>a<i>b</i>c</b>");
        Assert.Equal(["a", "b", "c"], runs.Select(r => r.Text).ToArray());
        Assert.Equal([DescriptionStyle.Bold, DescriptionStyle.Bold | DescriptionStyle.Italic, DescriptionStyle.Bold], runs.Select(r => r.Style).ToArray());
    }

    [Fact]
    [Trait("TestCase", "T258")]
    public void TagsInAnyCaseAndWithAnyAttribute()
    {
        Assert.Equal(DescriptionStyle.Bold, StyleOf("<B>x</B>", "x"));
        Assert.Equal(DescriptionStyle.Bold, StyleOf("<b class=\"x\" id='y'>x</b>", "x"));
        Assert.Equal(DescriptionStyle.Italic, StyleOf("<I style='a>b'>z</I>", "z"));
        Assert.Equal(DescriptionStyle.Underline, StyleOf("<u\tonclick=alert(1)>x</u>", "x"));
        Assert.Equal("x", Plain("<b class=\"x\" id='y'>x</b>"));
    }

    [Fact]
    [Trait("TestCase", "T258")]
    public void LineBreaksOfBrAndOfTheText()
    {
        Assert.Equal("a\nb\nc\nd", Plain("a<BR>b<br/>c<br />d"));
        Assert.Equal("a\nb\nc\nd", Plain("a\r\nb\rc\nd"));
        Assert.Equal("a\nb\nc", Plain("a<br>b\r\nc"));
    }

    [Fact]
    [Trait("TestCase", "T258")]
    public void SpacesAndTabsAreKept()
    {
        Assert.Equal("a  b\t\tc\n\n   d", Plain("a  b\t\tc\n\n   d"));
        Assert.Equal("<<>> a  b\n\n  c", Plain("&lt;&lt;&gt;&gt; <b>a  b</b>\n\n  c"));
        Assert.Equal("  uno   due  ", Plain("  uno <b>  </b>due  "));
    }

    [Fact]
    [Trait("TestCase", "T258")]
    public void EmptyAndWhitespaceOnlyDescriptions()
    {
        Assert.Empty(Runs(""));
        Assert.Empty(Runs(null));
        Assert.Empty(Runs("  \r\n\t "));
        Assert.Equal("", Plain(null));
    }

    [Fact]
    [Trait("TestCase", "T258")]
    public void TextWithoutAnythingToReadIsTheSameText()
    {
        const string text = "riga uno\nriga due, senza tag ne' indirizzi.";
        Assert.Equal(text, Plain(text));
        var run = Assert.Single(Runs(text));
        Assert.Equal(DescriptionStyle.None, run.Style);
        Assert.Null(run.Link);
    }

    // ---- tags with errors ----

    [Fact]
    [Trait("TestCase", "T263")]
    public void TagNotClosedLastsUntilTheEnd()
    {
        Assert.Equal(DescriptionStyle.Bold, StyleOf("<b>aperto", "aperto"));
        Assert.Equal(DescriptionStyle.Bold | DescriptionStyle.Italic, StyleOf("<b><i>x", "x"));
        var runs = Runs("<u>a<b>b</b>c\nd");
        Assert.All(runs, r => Assert.True(r.Style.HasFlag(DescriptionStyle.Underline)));
        Assert.Equal("abc\nd", string.Concat(runs.Select(r => r.Text)));
    }

    [Fact]
    [Trait("TestCase", "T263")]
    public void ClosingTagWithNothingToCloseIsNotShown()
    {
        Assert.Equal("abc", Plain("a</b>b</i></u></a></ul></ol></li></br>c"));
        Assert.Equal(DescriptionStyle.None, StyleOf("</b>x", "x"));
        Assert.Equal("x\n• y", Plain("x<ul>\n<li>y</li></ul></ul></li>"));
    }

    [Fact]
    [Trait("TestCase", "T263")]
    public void StylesThatCrossAreClosedOneByOne()
    {
        var runs = Runs("<b><i>x</b>y</i>z");
        Assert.Equal(["x", "y", "z"], runs.Select(r => r.Text).ToArray());
        Assert.Equal([DescriptionStyle.Bold | DescriptionStyle.Italic, DescriptionStyle.Italic, DescriptionStyle.None], runs.Select(r => r.Style).ToArray());
    }

    [Fact]
    [Trait("TestCase", "T263")]
    public void SelfClosingStyleTagDoesNothing() => Assert.Equal(DescriptionStyle.None, Assert.Single(Runs("a<b/>b")).Style);

    [Theory]
    [Trait("TestCase", "T263")]
    [InlineData("<b class=\"x")]
    [InlineData("<b")]
    [InlineData("<")]
    [InlineData("a <b")]
    [InlineData("<a href=\"https://e.org")]
    public void TagCutOrNotClosedIsText(string text)
    {
        Assert.Equal(text, Plain(text));
        Assert.All(Runs(text), r => Assert.Equal(DescriptionStyle.None, r.Style & ~DescriptionStyle.Underline));
    }

    [Fact]
    [Trait("TestCase", "T263")]
    public void BrokenTagBeforeAGoodOne_TheGoodOneStillWorks()
    {
        var runs = Runs("<b class=\"x e <i>dopo</i>");
        Assert.Equal("<b class=\"x e dopo", string.Concat(runs.Select(r => r.Text)));
        Assert.Equal(DescriptionStyle.Italic, runs.Single(r => r.Text == "dopo").Style);
    }

    [Fact]
    [Trait("TestCase", "T263")]
    public void GreaterThanInsideAQuotedAttributeDoesNotEndTheTag()
    {
        var run = Assert.Single(Runs("<a title=\"1 > 0\" href=\"https://e.org\">x</a>"));
        Assert.Equal("x", run.Text);
        Assert.NotNull(run.Link);
    }

    // ---- other tags are text ----

    [Theory]
    [Trait("TestCase", "T262")]
    [InlineData("<h4>uno</h4> <table><tr><td>due</td></tr></table> <ul2>tre</ul2>")]
    [InlineData("<section class=\"x\">a</section><article>b</article><h5>c</h5><h6>d</h6>")]
    [InlineData("<bold>x</bold>")]
    [InlineData("<br2>x")]
    [InlineData("<abbr title=\"a\">x</abbr><ins>y</ins><header>z</header><link rel=x>")]
    [InlineData("<my-tag>x</my-tag>")]
    [InlineData("<script>var a = 1;</script><style>b { x }</style>")]
    [InlineData("<title>T</title><meta charset=\"UTF-8\"><?xml version=\"1.0\"?><!ELEMENT a>")]
    [InlineData("<img src=\"https://e.org/x.png\" alt=\"x\">")]
    public void OtherTagsAreShownAsTheyAre(string text)
    {
        Assert.Equal(text, Plain(text));
        Assert.All(Runs(text), r => Assert.Equal(DescriptionStyle.None, r.Style & ~DescriptionStyle.Underline));
    }

    [Theory]
    [Trait("TestCase", "T262")]
    [InlineData("a < b e c > d")]
    [InlineData("1 <2 e 3> 2")]
    [InlineData("scrivimi a <mario@e.it>")]
    [InlineData("<<<>>>")]
    public void LessThanWhichIsNotATagIsText(string text) => Assert.Equal(text, Plain(text));

    [Fact]
    [Trait("TestCase", "T262")]
    public void TagInsideAnotherTagOrACommentIsNotRead()
    {
        const string alt = "<img alt=\"<b>non grassetto</b>\" src=\"x\"> e <!-- <i>commento</i> --> fine";
        Assert.Equal("<img alt=\"<b>non grassetto</b>\" src=\"x\"> e  fine", Plain(alt));
        Assert.All(Runs(alt), r => Assert.Equal(DescriptionStyle.None, r.Style));
    }

    [Fact]
    [Trait("TestCase", "T262")]
    public void ReadTagsInsideScriptAndStyleStillWork()
    {
        // The text of a script is text like any other: a tag that is read there is read.
        var runs = Runs("<script>x = \"<b>\";</script>fine");
        Assert.Equal("<script>x = \"\";</script>fine", string.Concat(runs.Select(r => r.Text)));
        Assert.Equal(DescriptionStyle.Bold, runs.Last().Style);
    }

    // ---- p, strong, em, div, span, titles ----

    [Fact]
    [Trait("TestCase", "T262")]
    public void StrongIsBoldAndEmIsItalic()
    {
        Assert.Equal(DescriptionStyle.Bold, StyleOf("a <strong>forte</strong>", "forte"));
        Assert.Equal(DescriptionStyle.Italic, StyleOf("a <EM class=\"x\">enfasi</EM>", "enfasi"));
        Assert.Equal(DescriptionStyle.Bold | DescriptionStyle.Italic, StyleOf("<b><em>x</em></b>", "x"));
        Assert.Equal("forte enfasi", Plain("<strong>forte</strong> <em>enfasi</em>"));
        // strong and b count together: one closes the other.
        Assert.Equal(DescriptionStyle.None, StyleOf("<b>a</strong>b", "b"));
        Assert.Equal(DescriptionStyle.Bold, StyleOf("<strong>aperto", "aperto"));
    }

    [Fact]
    [Trait("TestCase", "T262")]
    public void ParagraphIsOnItsOwnLinesWithAnEmptyLineAround()
    {
        Assert.Equal("Prima\n\nSecondo\n\nDopo", Plain("Prima<p>Secondo</p>Dopo"));
        Assert.Equal("Uno\n\nDue", Plain("<p>Uno</p><p>Due</p>"));
        Assert.Equal("Uno\n\nDue\n\nTre", Plain("<p>Uno</p>\n<p>Due</p>\n<p>Tre</p>"));
        Assert.Equal("Uno", Plain("<p>Uno</p>"));
        Assert.Equal("Uno\n\nDue", Plain("<p>  Uno  </p><p>\tDue</p>"));
        Assert.Equal("a\n\nb", Plain("a<br><p>b</p>"));
    }

    [Fact]
    [Trait("TestCase", "T262")]
    public void ParagraphNotClosedAndClosingOrphan()
    {
        Assert.Equal("a\n\nb\n\nc", Plain("a<p>b<p>c"));
        Assert.Equal("ab", Plain("a</p>b"));
        Assert.Equal("ab", Plain("a</div>b"));
        Assert.Equal("a\nb", Plain("a <div> b </div>"));
    }

    [Fact]
    [Trait("TestCase", "T262")]
    public void DivIsOnItsOwnLine()
    {
        Assert.Equal("a\nb\nc", Plain("a<div>b</div>c"));
        Assert.Equal("a\nb", Plain("<div>a</div><div class=\"x\">b</div>"));
        Assert.Equal("a\nb", Plain("<div>a</div>\n<div>b</div>"));
        Assert.Equal("a\nb", Plain("a\n<div>b</div>"));
    }

    [Fact]
    [Trait("TestCase", "T262")]
    public void SpanOnlyShowsItsText()
    {
        Assert.Equal("a b c", Plain("a <span style=\"color:red\">b</span> c"));
        Assert.All(Runs("a <span>b</span> c"), r => Assert.Equal(DescriptionStyle.None, r.Style));
        Assert.Equal("ab", Plain("a</span>b"));
    }

    [Fact]
    [Trait("TestCase", "T262")]
    public void TitlesAreBoldAndBigger_TheBiggestFirst()
    {
        var runs = Runs("<h1>Uno</h1><h2>Due</h2><h3>Tre</h3>normale");
        Assert.Equal("Uno\nDue\nTre\nnormale", string.Concat(runs.Select(r => r.Text)));
        Assert.Equal(DescriptionStyle.Heading1 | DescriptionStyle.Bold, runs.Single(r => r.Text == "Uno").Style);
        Assert.Equal(DescriptionStyle.Heading2 | DescriptionStyle.Bold, runs.Single(r => r.Text == "Due").Style);
        Assert.Equal(DescriptionStyle.Heading3 | DescriptionStyle.Bold, runs.Single(r => r.Text == "Tre").Style);
        Assert.Equal(DescriptionStyle.None, runs.Single(r => r.Text.EndsWith("normale", StringComparison.Ordinal)).Style);
    }

    [Fact]
    [Trait("TestCase", "T262")]
    public void TitleKeepsOtherStylesAndLinks()
    {
        var runs = Runs("<h2>un <i>bel</i> <a href=\"https://e.org\">titolo</a></h2>");
        Assert.All(runs, r => Assert.True(r.Style.HasFlag(DescriptionStyle.Heading2) && r.Style.HasFlag(DescriptionStyle.Bold)));
        Assert.Contains(runs, r => r.Text == "bel" && r.Style.HasFlag(DescriptionStyle.Italic));
        Assert.Contains(runs, r => r.Text == "titolo" && r.Link is not null);
    }

    [Fact]
    [Trait("TestCase", "T262")]
    public void TitleNotClosedLastsUntilTheEnd_AndOtherTitlesAreText()
    {
        Assert.True(StyleOf("<h1>aperto", "aperto").HasFlag(DescriptionStyle.Heading1));
        Assert.Equal("a<h4>b</h4>", Plain("a<h4>b</h4>"));
        Assert.Equal("<h5>b</h5>", Plain("<h5>b</h5>"));
        Assert.Equal("<h6>b</h6>", Plain("<h6>b</h6>"));
        Assert.Equal("ab", Plain("a</h1>b"));
    }

    [Fact]
    [Trait("TestCase", "T262")]
    public void ListsAndBlocksTogether()
    {
        Assert.Equal("Spesa\n\n• pane\n• latte\n\nFine", Plain("<p>Spesa</p><ul><li>pane</li><li>latte</li></ul><p>Fine</p>"));
        // A block right after the bullet stays on its line.
        Assert.Equal("• uno\n\n• due", Plain("<ul><li><p>uno</p></li><li><div>due</div></li></ul>"));
    }

    // ---- comments, DOCTYPE, html, head and body ----

    [Fact]
    [Trait("TestCase", "T262")]
    public void CommentsAreRemovedWithTheirContent()
    {
        Assert.Equal("prima  dopo", Plain("prima <!-- nascosto <b>x</b> <p>y</p> --> dopo"));
        Assert.Equal("ab", Plain("a<!-- x -->b"));
        Assert.Equal("a\nb", Plain("a<!--\nmolte\nrighe\n-->\nb"));
        Assert.Equal("ab", Plain("a<!---->b"));
        Assert.Equal("a<!-- mai chiuso", Plain("a<!-- mai chiuso"));
        Assert.Equal(DescriptionStyle.None, StyleOf("<!-- <b> -->x", "x"));
    }

    [Fact]
    [Trait("TestCase", "T262")]
    public void DoctypeIsRemoved_OtherDeclarationsAreText()
    {
        Assert.Equal("x", Plain("<!DOCTYPE html>x"));
        Assert.Equal("x", Plain("<!doctype HTML PUBLIC \"-//W3C//DTD HTML 4.01//EN\">\n<html>x</html>"));
        Assert.Equal("<?xml version=\"1.0\"?>x", Plain("<?xml version=\"1.0\"?>x"));
        Assert.Equal("<!ELEMENT a>x", Plain("<!ELEMENT a>x"));
    }

    [Fact]
    [Trait("TestCase", "T262")]
    public void HtmlBodyAndHeadAreRemoved_TheHeadWithAllThatIsInIt()
    {
        Assert.Equal("Ciao", Plain("<html lang=\"it\"><body class=\"x\">Ciao</body></html>"));
        Assert.Equal("Ciao", Plain("<html><head><title>T</title><meta charset=\"UTF-8\"><style>b { x }</style><script>var a = '<b>';</script></head><body>Ciao</body></html>"));
        Assert.Equal("Ciao", Plain("<HEAD>\n<TITLE>T</TITLE>\n</HEAD>Ciao"));
        Assert.Equal("x", Plain("</head>x"));
        // The header is not the head.
        Assert.Equal("</header><header>x</header>", Plain("<head></header><header>x</header>"));
    }

    [Fact]
    [Trait("TestCase", "T262")]
    public void HeadNotClosedIsOnlyTheTag()
    {
        Assert.Equal("a b", Plain("<head>a <b>b</b>"));
        Assert.Equal("ab", Plain("a<head>b"));
    }

    [Fact]
    [Trait("TestCase", "T262")]
    public void HtmlPageIsShownAsGoogleShowsIt()
    {
        const string page = "<!DOCTYPE html>\n<html>\n<head><title>T</title></head>\n<body>\n<p>Ciao <b>mondo</b></p>\n</body>\n</html>";
        Assert.Equal("Ciao mondo", Plain(page));
        Assert.Equal(DescriptionStyle.Bold, StyleOf(page, "mondo"));
        // The whole file of the test T273 (every tag of HTML5 in it): only the text of the page is left.
        const string pretty = "<html>\n<head>\n  <title>T</title>\n</head>\n<body>\n  <h1>Titolo</h1>\n  <p>Uno</p>\n  <div>Due</div>\n</body>\n</html>";
        Assert.Equal("Titolo\n\nUno\n\nDue", Plain(pretty));
    }

    [Fact]
    [Trait("TestCase", "T262")]
    public void SpacesBetweenBlockTagsAreIgnored_ButNotNextToInlineTags()
    {
        Assert.Equal("a\n\nb", Plain("<p>a</p>\n  \n<p>b</p>"));
        Assert.Equal("a\nb", Plain("<div>a</div> \n <div>b</div>"));
        Assert.Equal("a\nb", Plain("<h1>a</h1>\n<h2>b</h2>"));
        Assert.Equal("ab", Plain("<span>a</span><span>b</span>"));
        Assert.Equal("a b", Plain("<span>a</span> <span>b</span>"));
        Assert.Equal("x\n\ny z", Plain("<p>x</p> <strong>y</strong> <em>z</em>"));
    }

    // ---- links ----

    [Fact]
    [Trait("TestCase", "T259")]
    public void LinkKeepsTheTextAndTheAddress()
    {
        var run = Assert.Single(Runs("<a href=\"https://example.org/x?a=1&amp;b=2\">il sito</a>"));
        Assert.Equal("il sito", run.Text);
        Assert.Equal("https://example.org/x?a=1&b=2", run.Link!.AbsoluteUri);
        Assert.True(run.Style.HasFlag(DescriptionStyle.Underline));
    }

    [Theory]
    [Trait("TestCase", "T259")]
    [InlineData("<a href='https://e.org'>x</a>")]
    [InlineData("<a href=https://e.org>x</a>")]
    [InlineData("<A target=\"_blank\" HREF=\"https://e.org\">x</A>")]
    [InlineData("<a class='x' href = \"https://e.org\" rel=nofollow>x</a>")]
    public void LinkAttributesInAnyForm(string html) => Assert.Equal("https://e.org/", Runs(html)[0].Link!.AbsoluteUri);

    [Theory]
    [Trait("TestCase", "T259")]
    [InlineData("javascript:alert(1)")]
    [InlineData("JaVaScRiPt:alert(1)")]
    [InlineData("file:///c:/windows/system32/calc.exe")]
    [InlineData("data:text/html,<b>x</b>")]
    [InlineData("ftp://e.org/x")]
    [InlineData("vbscript:x")]
    [InlineData("/relativo/percorso")]
    [InlineData("https://")]
    [InlineData("mailto:")]
    [InlineData("")]
    public void LinkWithAnotherSchemeOrNoAddress_IsShownAsText(string href)
    {
        var html = $"<a href=\"{href}\">testo</a>";
        Assert.Equal(html, Plain(html));
        Assert.Null(DescriptionText.LinkAt(Runs(html), html.IndexOf("testo", StringComparison.Ordinal)));
    }

    [Fact]
    [Trait("TestCase", "T259")]
    public void AnchorWithoutHref_IsShownAsText()
    {
        const string html = "<a name=\"x\">testo</a>";
        Assert.Equal(html, Plain(html));
        Assert.All(Runs(html), r => Assert.Null(r.Link));
    }

    [Fact]
    [Trait("TestCase", "T259")]
    public void MailtoIsALink() => Assert.Equal("mailto:mario@e.it", Runs("<a href=\"mailto:mario@e.it\">scrivi</a>")[0].Link!.OriginalString);

    [Fact]
    [Trait("TestCase", "T259")]
    public void LinkNotClosedLastsUntilTheEnd()
    {
        var runs = Runs("<a href=\"https://e.org\">fino alla fine\nanche qui");
        Assert.All(runs, r => Assert.NotNull(r.Link));
        Assert.Equal("fino alla fine\nanche qui", string.Concat(runs.Select(r => r.Text)));
    }

    [Fact]
    [Trait("TestCase", "T259")]
    public void AddressInsideALinkIsNotAnotherLink()
    {
        var run = Assert.Single(Runs("<a href=\"https://a.org\">https://b.org</a>"));
        Assert.Equal("https://a.org/", run.Link!.AbsoluteUri);
        Assert.Equal("https://b.org", run.Text);
    }

    [Fact]
    [Trait("TestCase", "T259")]
    public void LinkAtFindsTheLinkOfACharacter()
    {
        var runs = Runs("ab <a href=\"https://e.org\">cd</a> ef");
        Assert.Null(DescriptionText.LinkAt(runs, 0));
        Assert.Null(DescriptionText.LinkAt(runs, 2));
        Assert.NotNull(DescriptionText.LinkAt(runs, 3));
        Assert.NotNull(DescriptionText.LinkAt(runs, 4));
        Assert.Null(DescriptionText.LinkAt(runs, 5));
        Assert.Null(DescriptionText.LinkAt(runs, 100));
        Assert.Null(DescriptionText.LinkAt(runs, -1));
    }

    [Fact]
    [Trait("TestCase", "T259")]
    public void BareAddressesAreLinks_WithoutThePunctuationOfTheSentence()
    {
        var runs = Runs("Vedi https://e.org/a, poi (https://e.org/b). Scrivi a mario@e.it o vai su www.e.org.");
        Assert.Equal(["Vedi ", "https://e.org/a", ", poi (", "https://e.org/b", "). Scrivi a ", "mario@e.it", " o vai su ", "www.e.org", "."],
            runs.Select(r => r.Text).ToArray());
        Assert.Equal("https://e.org/a", runs[1].Link!.AbsoluteUri);
        Assert.Equal("mailto:mario@e.it", runs[5].Link!.OriginalString);
        Assert.Equal("https://www.e.org/", runs[7].Link!.AbsoluteUri);
        Assert.Null(runs[0].Link);
        Assert.Null(runs[8].Link);
    }

    [Fact]
    [Trait("TestCase", "T259")]
    public void BareDomainsAndFileNamesAreNotLinks()
    {
        const string text = "scarica file.txt e v1.0 da esempio.it o 3.5@x";
        Assert.All(Runs(text), r => Assert.Null(r.Link));
    }

    [Fact]
    [Trait("TestCase", "T259")]
    public void EmailInsideAnAddressIsOneLink()
    {
        var run = Assert.Single(Runs("https://utente@e.org/x"), r => r.Link is not null);
        Assert.Equal("https://utente@e.org/x", run.Text);
    }

    [Fact]
    [Trait("TestCase", "T259")]
    public void AddressInsideATextTagIsALink()
    {
        const string html = "<img src=\"https://e.org/x.png\">";
        Assert.Equal(html, Plain(html));
        Assert.NotNull(DescriptionText.LinkAt(Runs(html), html.IndexOf("https", StringComparison.Ordinal) + 2));
    }

    // ---- lists ----

    [Fact]
    [Trait("TestCase", "T260")]
    public void BulletedList() => Assert.Equal("Elenco\n• uno\n• due", Plain("Elenco<ul><li>uno</li><li>due</li></ul>"));

    [Fact]
    [Trait("TestCase", "T260")]
    public void NumberedListCountsFromOne() => Assert.Equal("1. uno\n2. due\n3. tre", Plain("<ol><li>uno</li><li>due</li><li>tre</li></ol>"));

    [Fact]
    [Trait("TestCase", "T260")]
    public void EachNumberedListHasItsOwnCount() => Assert.Equal("1. a\nx\n1. b", Plain("<ol><li>a</li></ol>x<ol><li>b</li></ol>"));

    [Fact]
    [Trait("TestCase", "T260")]
    public void NestedListsAreIndented_AndEachHasItsOwnCount()
    {
        Assert.Equal("1. a\n  • x\n  • y\n2. b", Plain("<ol><li>a<ul><li>x</li><li>y</li></ul></li><li>b</li></ol>"));
        Assert.Equal("• A\n  • A1\n  • A2\n    • A2a\n• B", Plain("<ul><li>A<ul><li>A1</li><li>A2<ul><li>A2a</li></ul></li></ul></li><li>B</li></ul>"));
    }

    [Fact]
    [Trait("TestCase", "T260")]
    public void ListWrittenOnManyLines_HasNoEmptyLines()
    {
        Assert.Equal("Elenco:\n• uno\n• due\nFine", Plain("Elenco:\n<ul>\n  <li>uno</li>\n  <li>due</li>\n</ul>\nFine"));
        Assert.Equal("• uno\n• due", Plain("\n<ul>\r\n\t<li>uno</li>\r\n\t<li>due</li>\r\n</ul>\n"));
    }

    [Fact]
    [Trait("TestCase", "T260")]
    public void SpacesBetweenBrAreIgnored_ButNotTheTextAroundThem()
    {
        Assert.Equal("a\n\nb", Plain("a<br>\n<br>b"));
        Assert.Equal("a\n\nb", Plain("a<br>\nb"));
        Assert.Equal("a\n\n\nb", Plain("a<br> \n  <br> <br>b"));
    }

    [Fact]
    [Trait("TestCase", "T260")]
    public void SpacesNextToTheTagsOfAListAreNotShown()
    {
        Assert.Equal("• uno\n• due", Plain("<ul> <li> uno </li> <li>\tdue\t</li> </ul>"));
        Assert.Equal("• uno  due", Plain("<li> uno  due </li>"));
        Assert.Equal("Elenco :\n• uno", Plain("Elenco : <ul> <li> uno </li> </ul>"));
        // As in HTML, also across b, i, u and a; the spaces inside a line are kept.
        Assert.Equal("• x", Plain("<li><b> x </b></li>"));
        Assert.Equal("• a b", Plain("<li> <i>a</i> b </li>"));
        Assert.Equal("• a  b", Plain("<li><u>a </u> <a href=\"https://e.org\">b</a> </li>"));
        Assert.Equal("• uno\nDopo", Plain("<ul><li>uno</li></ul>   Dopo"));
        Assert.Equal("a b c", Plain("a <b>b</b> c"));
        Assert.Equal(" a ", Plain(" <i>a</i> "));
    }

    [Fact]
    [Trait("TestCase", "T260")]
    public void ItemOutsideAListHasABullet() => Assert.Equal("• solo\n• due", Plain("<li>solo</li><li>due</li>"));

    [Fact]
    [Trait("TestCase", "T260")]
    public void ItemNotClosedEndsWhenTheNextOneStarts() => Assert.Equal("• a\n• b", Plain("<ul><li>a<li>b</ul>"));

    [Fact]
    [Trait("TestCase", "T260")]
    public void EmptyItemsStillHaveTheirLine() => Assert.Equal("•\n•\n• x", Plain("<ul><li></li><li></li><li>x</li></ul>"));

    [Fact]
    [Trait("TestCase", "T260")]
    public void StylesAndLinksWorkInsideItems()
    {
        var runs = Runs("<ul><li><b>forte</b> e <a href=\"https://e.org\">link</a></li></ul>");
        Assert.Equal("• forte e link", string.Concat(runs.Select(r => r.Text)));
        Assert.Contains(runs, r => r.Text == "forte" && r.Style == DescriptionStyle.Bold);
        Assert.Contains(runs, r => r.Text == "link" && r.Link is not null);
        Assert.Equal(DescriptionStyle.None, runs[0].Style);
    }

    [Fact]
    [Trait("TestCase", "T260")]
    public void ListAttributesAreIgnored() => Assert.Equal("1. a\n2. b", Plain("<ol start=\"5\" class=\"x\"><li id=\"1\">a</li><li>b</li></ol>"));

    // ---- entities ----

    [Theory]
    [Trait("TestCase", "T261")]
    [InlineData("a &amp; b", "a & b")]
    [InlineData("&lt;b&gt;x&lt;/b&gt;", "<b>x</b>")]
    [InlineData("&quot;x&quot;", "\"x\"")]
    [InlineData("&nbsp;|&nbsp;", "\u00A0|\u00A0")]
    [InlineData("&copy; &euro; &agrave; &hellip; &rarr; &Omega;", "© € à … → Ω")]
    [InlineData("l&apos;uno &apos;x&apos;", "l'uno 'x'")]
    [InlineData("l&#8217;uno", "l\u2019uno")]
    [InlineData("&#x1F600; &#X41;", "\U0001F600 A")]
    [InlineData("AT&amp;T", "AT&T")]
    [InlineData("<b>&amp;</b>", "&")]
    public void EntitiesAreCharacters(string text, string expected) => Assert.Equal(expected, Plain(text));

    [Theory]
    [Trait("TestCase", "T261")]
    [InlineData("&unknown;")]
    [InlineData("&AMP;")]
    [InlineData("&#xZZ;")]
    [InlineData("&#0;")]
    [InlineData("&#13;")]
    [InlineData("&#55357;")]
    [InlineData("&#1114112;")]
    [InlineData("a & b")]
    [InlineData("&amp")]
    [InlineData("&copy 2026")]
    [InlineData("&;")]
    [InlineData("&#;")]
    public void WrongEntitiesStayAsTheyAre(string text) => Assert.Equal(text, Plain(text));

    [Fact]
    [Trait("TestCase", "T261")]
    public void EntityThatLooksLikeATagIsNotATag()
    {
        var runs = Runs("&lt;b&gt;non grassetto&lt;/b&gt;");
        Assert.Equal("<b>non grassetto</b>", Assert.Single(runs).Text);
        Assert.Equal(DescriptionStyle.None, runs[0].Style);
    }

    [Fact]
    [Trait("TestCase", "T261")]
    public void EntitiesAreCharactersInAnAddress()
    {
        var run = Assert.Single(Runs("<a href=\"https://e.org/?a=1&amp;b=2\">x</a>"));
        Assert.Equal("https://e.org/?a=1&b=2", run.Link!.AbsoluteUri);
    }

    // ---- the samples of the semi-automatic test ----

    [Fact]
    [Trait("TestCase", "T273")]
    public void SamplesShowTheTextThatIsExpected()
    {
        Assert.NotEmpty(DescriptionSamples.All);
        foreach (var sample in DescriptionSamples.All)
            Assert.True(sample.Expected == Plain(sample.Source), $"{sample.Name}: attesa «{sample.Expected}», letta «{Plain(sample.Source)}»");
    }

    // ---- robustness and limit ----

    [Fact]
    [Trait("TestCase", "T264")]
    public void BigAndNestedInput_IsReadFastAndWithoutErrors()
    {
        var deep = string.Concat(Enumerable.Repeat("<b><i><u><ul><li>", 20_000)) + "fine";
        var big = string.Concat(Enumerable.Repeat("<b>ciao</b> <a href=\"https://e.org\">x</a><br>", 20_000));
        var manyAngles = string.Concat(Enumerable.Repeat("<", 50_000)) + string.Concat(Enumerable.Repeat("<a href=\"", 20_000));
        var manyComments = string.Concat(Enumerable.Repeat("<!-- ", 20_000));
        var manyAddresses = string.Concat(Enumerable.Repeat("a", 8_000)) + "@" + string.Concat(Enumerable.Repeat("http://", 5_000));
        var longName = "a." + string.Concat(Enumerable.Repeat("a.", 4_000)) + "@";
        var manyHeads = string.Concat(Enumerable.Repeat("<head>x", 20_000));
        var manyParagraphs = string.Concat(Enumerable.Repeat("<p><div><h1>a</h1></div></p>", 20_000));
        var watch = Stopwatch.StartNew();
        Assert.Contains("fine", Plain(deep));
        Assert.NotEmpty(Plain(big));
        Assert.NotNull(Runs(manyAngles));
        Assert.NotNull(Runs(manyComments));
        Assert.NotNull(Runs(manyAddresses));
        Assert.NotNull(Runs(longName));
        Assert.NotNull(Runs(manyHeads));
        Assert.NotEmpty(Runs(manyParagraphs));
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), $"troppo lento: {watch.Elapsed}");
    }

    [Fact]
    [Trait("TestCase", "T264")]
    public void TextCutInTheMiddleOfATagOrAnEntity_DoesNotFail()
    {
        // The import cuts a description at 8192 characters.
        foreach (var cut in new[] { "<b>ciao</", "<b>ciao</b", "<a href=\"https://e.org", "<a href=\"https://e.org\"", "<ul><li>uno</li", "<b>ciao&am", "<b>ciao&#", "<!--", "<!-", "<!DOCTYPE" })
            Assert.NotNull(Runs(cut));
    }

    [Fact]
    [Trait("TestCase", "T264")]
    public void LimitIs8192Characters()
    {
        Assert.Equal(8192, DescriptionText.MaxLength);
        Assert.Equal(DescriptionText.MaxLength, IcsMapper.MaxDescription);
    }

    // ---- the search ----

    private static CalendarEvent Ev(string id, string description) =>
        new(id, "c1", "Evento " + id, new DateTime(2026, 10, 28, 10, 0, 0), new DateTime(2026, 10, 28, 11, 0, 0), Description: description);

    private static readonly DateTime Today = new(2026, 10, 28);

    private static string[] Found(string query, params CalendarEvent[] events) =>
        EventSearch.Search(events, new SearchQuery { Text = query }, Today).Hits.Select(h => h.Event.Id).Distinct().ToArray();

    [Theory]
    [Trait("TestCase", "T265")]
    [InlineData("<b>")]
    [InlineData("</b>")]
    [InlineData("<br>")]
    [InlineData("</ul>")]
    [InlineData("<li>")]
    [InlineData("href")]
    [InlineData("segreto")]
    public void SearchDoesNotFindTheTagsThatAreRead(string word)
    {
        var ev = Ev("a", "Ordine: <b>bilancio</b><br><a href=\"https://example.org/segreto\">documento</a><ul><li>uno</li></ul>");
        Assert.Empty(Found(word, ev));
    }

    [Theory]
    [Trait("TestCase", "T265")]
    [InlineData("<h4>")]
    [InlineData("</h4>")]
    [InlineData("table")]
    [InlineData("script")]
    [InlineData("<section class")]
    [InlineData("<?xml")]
    [InlineData("<!element")]
    [InlineData("<my-tag>")]
    public void SearchFindsTheTagsThatAreShownAsText(string word)
    {
        var ev = Ev("a", "<h4>riunione</h4> <table>forte</table> <section class=\"x\">y</section><script>z</script><?xml v?><!ELEMENT a><my-tag>w</my-tag>");
        Assert.Equal(["a"], Found(word, ev));
    }

    [Theory]
    [Trait("TestCase", "T265")]
    [InlineData("<p>")]
    [InlineData("</p>")]
    [InlineData("strong")]
    [InlineData("<em")]
    [InlineData("<div")]
    [InlineData("<span")]
    [InlineData("<h1>")]
    [InlineData("<h3")]
    [InlineData("nascosto")]
    [InlineData("doctype")]
    [InlineData("<html")]
    [InlineData("<body")]
    [InlineData("<title>")]
    [InlineData("charset")]
    public void SearchDoesNotFindTheNewTagsThatAreRead(string word)
    {
        var ev = Ev("a", "<!DOCTYPE html><html><head><title>titolo</title><meta charset=\"UTF-8\"></head><body><p>uno</p><strong>due</strong><em>tre</em><div class=\"x\">q</div>" +
            "<span>s</span><h1>h</h1><h3>k</h3><!-- nascosto --></body></html>");
        Assert.Empty(Found(word, ev));
    }

    [Theory]
    [Trait("TestCase", "T265")]
    [InlineData("uno")]
    [InlineData("due tre")]
    [InlineData("riunione")]
    public void SearchFindsTheTextInsideTheNewTags(string word)
    {
        var ev = Ev("a", "<html><head><title>titolo</title></head><body><p>uno</p><strong>due</strong> <em>tre</em><div>riunione</div></body></html>");
        Assert.Equal(["a"], Found(word, ev));
    }

    [Fact]
    [Trait("TestCase", "T265")]
    public void SearchDoesNotFindTheTitleOfTheHead() => Assert.Empty(Found("titolo", Ev("a", "<html><head><title>titolo</title></head><body>x</body></html>")));

    [Theory]
    [Trait("TestCase", "T265")]
    [InlineData("bilancio")]
    [InlineData("documento")]
    [InlineData("uno")]
    [InlineData("at&t")]
    [InlineData("example.org")]
    [InlineData("uno €")]
    public void SearchFindsTheTextTheUserSees(string word)
    {
        var ev = Ev("a", "<b>bilancio</b><br><a href=\"https://x.org\">documento</a><ul><li>uno</li></ul> AT&amp;T example.org &euro;");
        Assert.Equal(["a"], Found(word, ev));
    }

    [Fact]
    [Trait("TestCase", "T265")]
    public void SearchInPlainTextIsUnchanged()
    {
        Assert.Equal(["a"], Found("href", Ev("a", "scrivi href nel testo")));
        Assert.Equal(["a"], Found("https", Ev("a", "vedi https://e.org")));
        Assert.Equal(["a"], Found("due", Ev("a", "uno\r\ndue")));
    }

    [Fact]
    [Trait("TestCase", "T265")]
    public void SnippetOfADescriptionHasNoTagsThatAreRead()
    {
        var result = EventSearch.Search([Ev("a", "<b>Riunione</b> sul <i>tesoro</i> nascosto<br>domani")], new SearchQuery { Text = "tesoro" }, Today);
        var snippet = Assert.Single(result.Hits).Snippet;
        Assert.DoesNotContain('<', snippet);
        Assert.Contains("tesoro", snippet);
    }

    [Fact]
    [Trait("TestCase", "T265")]
    public void NotWordUsesTheVisibleText()
    {
        var ev = Ev("a", "<b>riunione</b> <a href=\"https://example.org/segreto\">sito</a> <h4>x</h4>");
        var query = new SearchQuery { Text = "riunione", Not = "segreto" };
        Assert.Equal(["a"], EventSearch.Search([ev], query, Today).Hits.Select(h => h.Event.Id).Distinct().ToArray());
        Assert.Empty(EventSearch.Search([ev], query with { Not = "sito" }, Today).Hits);
        Assert.Empty(EventSearch.Search([ev], query with { Not = "<h4>" }, Today).Hits);
    }

    // ---- the .ics file keeps the text as it is ----

    private static string Ics(string description, string? meet = null) =>
        IcsWriter.Write([Ev("a", description) with { MeetLink = meet }]);

    /// <summary>The lines of the file are folded at 75 bytes: they are joined again to look for a text.</summary>
    private static string Unfold(string ics) => ics.Replace("\r\n ", "").Replace("\n ", "");

    [Fact]
    [Trait("TestCase", "T266")]
    public void IcsKeepsTheDescriptionAsItIs()
    {
        const string html = "<b>Ordine</b> del giorno<br><a href=\"https://e.org/doc\">documento</a><ul><li>uno</li></ul> & <p>x</p>";
        Assert.Contains("DESCRIPTION:" + html, Unfold(Ics(html)));
    }

    [Fact]
    [Trait("TestCase", "T266")]
    public void IcsRoundTripGivesTheSameText()
    {
        const string html = "<b>Ordine</b><br><a href=\"https://e.org/doc\">documento</a><ol><li>uno</li><li>due</li></ol>";
        var back = Assert.Single(IcsParser.Analyze(Ics(html), "dest").Events);
        Assert.Equal(html, back.Description);
    }

    [Fact]
    [Trait("TestCase", "T266")]
    public void IcsAddsTheMeetLinkOnceToAnHtmlDescription()
    {
        var once = Unfold(Ics("<b>Riunione</b>", "https://meet.google.com/abc-defg-hij"));
        Assert.Contains("DESCRIPTION:<b>Riunione</b>\\nGoogle Meet: https://meet.google.com/abc-defg-hij", once);
        var already = Unfold(Ics("<a href=\"https://meet.google.com/abc-defg-hij\">x</a>", "https://meet.google.com/abc-defg-hij"));
        Assert.DoesNotContain("Google Meet:", already);
    }

    [Fact]
    [Trait("TestCase", "T266")]
    public void IcsImportCutsADescriptionAt8192Characters()
    {
        var back = Assert.Single(IcsParser.Analyze(Ics(new string('a', 9_000)), "dest").Events);
        Assert.Equal(DescriptionText.MaxLength, back.Description.Length);
    }
}
