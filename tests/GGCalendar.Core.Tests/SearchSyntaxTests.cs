using GGCalendar.Core.Search;

namespace GGCalendar.Core.Tests;

/// <summary>The syntax of the search box (issue #8): plain words and keywords like <c>title:</c> and <c>email:</c>, in English, in any order.</summary>
public class SearchSyntaxTests
{
    private static string[] Keys(string text) => SearchSyntax.Tokenize(text).Select(t => t.Key ?? "-").ToArray();

    [Fact] // T181
    public void PlainWordsAndKeywordsAreSeparated()
    {
        var t = SearchSyntax.Tokenize("alfa title:riunione  email:mario@e.it beta");
        Assert.Equal(["-", "title", "email", "-"], t.Select(x => x.Key ?? "-").ToArray());
        Assert.Equal(["alfa", "riunione", "mario@e.it", "beta"], t.Select(x => x.Value).ToArray());
    }

    [Theory] // T181
    [InlineData("title:x", "title")]
    [InlineData("TITLE:x", "title")]
    [InlineData("Description:x", "description")]
    [InlineData("desc:x", "description")]
    [InlineData("what:x", "what")]
    [InlineData("location:x", "location")]
    [InlineData("where:x", "location")]
    [InlineData("email:x", "email")]
    [InlineData("who:x", "email")]
    [InlineData("guest:x", "email")]
    [InlineData("not:x", "not")]
    [InlineData("-x", "not")]
    [InlineData("from:2026-10-25", "from")]
    [InlineData("to:2026-10-25", "to")]
    public void KeywordsAndTheirAliases(string text, string key) => Assert.Equal([key], Keys(text));

    [Fact] // T181
    public void QuotesGroupTheWordsOfAValue()
    {
        var t = SearchSyntax.Tokenize("title:\"riunione alfa\" location:roma");
        Assert.Equal(["riunione alfa", "roma"], t.Select(x => x.Value).ToArray());
        Assert.Equal("title:\"riunione alfa\"", t[0].Raw);
        Assert.Equal(["riunione", "alfa"], EventSearch.Words(SearchSyntax.Parse("title:\"riunione alfa\"").Title));
    }

    [Fact] // T181
    public void AnOpenQuoteTakesTheRestOfTheLine()
    {
        var t = SearchSyntax.Tokenize("alfa title:\"riunione beta");
        Assert.Equal(["alfa", "riunione beta"], t.Select(x => x.Value).ToArray());
    }

    [Theory] // T181: what is not a keyword is a plain word, or nothing yet
    [InlineData("foo:bar", "foo:bar")]
    [InlineData("http://x", "http://x")]
    [InlineData("title:", "")]
    [InlineData("title: alfa", "alfa")]
    [InlineData("-", "-")]
    [InlineData("a-b", "a-b")]
    public void NotAKeyword(string text, string general) => Assert.Equal(general, SearchSyntax.Parse(text).Text);

    [Fact] // T181
    public void ParseFillsTheQuery()
    {
        var q = SearchSyntax.Parse("alfa beta title:riunione description:budget what:x email:mario location:roma not:annullata -vecchio from:2026-11-02 to:30/11/2026");
        Assert.Equal("alfa beta", q.Text);
        Assert.Equal(("riunione", "budget", "x"), (q.Title, q.Description, q.What));
        Assert.Equal(("mario", "roma", "annullata vecchio"), (q.Who, q.Where, q.Not));
        Assert.Equal((new DateTime(2026, 11, 2), new DateTime(2026, 11, 30)), (q.From, q.To));
        Assert.Null(q.CalendarIds);
    }

    [Fact] // T181: several of the same keyword all apply
    public void SameKeywordTwiceIsBoth()
    {
        var q = SearchSyntax.Parse("title:alfa title:beta email:a email:b");
        Assert.Equal(("alfa beta", "a b"), (q.Title, q.Who));
    }

    [Theory] // T181
    [InlineData("2026-10-25", 2026, 10, 25)]
    [InlineData("2026-1-5", 2026, 1, 5)]
    [InlineData("25/10/2026", 2026, 10, 25)]
    [InlineData("5/1/2026", 2026, 1, 5)]
    public void DaysAreReadInTwoWays(string text, int y, int m, int d) => Assert.Equal(new DateTime(y, m, d), SearchSyntax.ParseDay(text));

    [Theory] // T181
    [InlineData("")]
    [InlineData("domani")]
    [InlineData("2026-13-40")]
    [InlineData("10/25/2026")]
    public void BadDaysAreLeftOut(string text)
    {
        Assert.Null(SearchSyntax.ParseDay(text));
        Assert.Null(SearchSyntax.Parse("from:" + text).From);
    }

    [Fact] // T181
    public void KeywordIsQuotedOnlyWhenNeeded()
    {
        Assert.Equal("what:prova", SearchSyntax.Keyword("what", "prova"));
        Assert.Equal("what:\"prova uno\"", SearchSyntax.Keyword("what", "prova uno"));
        Assert.Equal("what:ab", SearchSyntax.Keyword("what", " \"ab\" "));
    }

    [Fact] // T181: the panel writes its boxes back; what it does not edit stays
    public void MergeReplacesWhatThePanelEditsAndKeepsTheRest()
    {
        var merged = SearchSyntax.Merge("gaza title:alfa what:old email:x -vecchio location:l not:n from:2026-01-01", "prova uno", "mario", "", "", null, null);
        Assert.Equal("gaza title:alfa what:\"prova uno\" email:mario", merged);
        Assert.Equal("what:a from:2026-11-02 to:2026-11-30", SearchSyntax.Merge("", "a", "", "", "", new DateTime(2026, 11, 2), new DateTime(2026, 11, 30)));
        Assert.Equal("", SearchSyntax.Merge("what:a", "", "", "", "", null, null));
    }

    [Fact] // T181: what the panel writes is read back the same
    public void MergeThenParseGivesTheSameBoxes()
    {
        var line = SearchSyntax.Merge("alfa", "uno due", "mario@e.it", "sala 2", "annullata", new DateTime(2026, 11, 2), new DateTime(2026, 11, 30));
        var q = SearchSyntax.Parse(line);
        Assert.Equal(("alfa", "uno due", "mario@e.it", "sala 2", "annullata"), (q.Text, q.What, q.Who, q.Where, q.Not));
        Assert.Equal((new DateTime(2026, 11, 2), new DateTime(2026, 11, 30)), (q.From, q.To));
    }

    // ---- the search itself with title: and description: ----

    private static readonly DateTime Today = new(2026, 10, 25);

    [Fact] // T181
    public void TitleAndDescriptionLookInTheirOwnFieldOnly()
    {
        var events = new[]
        {
            new GGCalendar.Core.Models.CalendarEvent("t", "c", "Alfa", Today, Today.AddHours(1), Description: "x"),
            new GGCalendar.Core.Models.CalendarEvent("d", "c", "x", Today, Today.AddHours(1), Description: "alfa"),
        };
        string[] Ids(SearchQuery q) => EventSearch.Search(events, q, Today).Hits.Select(h => h.Event.Id).ToArray();
        Assert.Equal(["t"], Ids(new SearchQuery { Title = "alfa" }));
        Assert.Equal(["d"], Ids(new SearchQuery { Description = "alfa" }));
        Assert.Equal(["d", "t"], Ids(new SearchQuery { What = "alfa" }).Order().ToArray());

        var hit = EventSearch.Search(events, new SearchQuery { Title = "alfa" }, Today).Hits.Single();
        Assert.Equal([SearchField.Title], hit.Fields); // only the field that was looked in
    }
}
