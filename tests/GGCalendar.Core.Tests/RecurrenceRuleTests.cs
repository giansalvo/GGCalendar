using GGCalendar.Core.Recurrence;
using Xunit;

namespace GGCalendar.Core.Tests;

// In the serial collection: the test below changes the language, which is global.
[Collection(GGCalendar.Core.Tests.Support.AppDriver.CollectionName)]
public class RecurrenceRuleTests
{
    [Fact]
    public void ParsesAndRebuildsWeeklyWithCount()
    {
        var rule = RecurrenceRule.Parse("FREQ=WEEKLY;INTERVAL=2;BYDAY=TH,MO;COUNT=13")!;
        Assert.Equal(2, rule.Interval);
        Assert.Equal(13, rule.Count);
        Assert.Equal("FREQ=WEEKLY;INTERVAL=2;BYDAY=MO,TH;COUNT=13", rule.ToRrule(false));

        // The sentence is in the language of the app: checked in two languages, whatever the one of this PC is.
        var language = GGCalendar.Core.Localization.L.Language;
        try
        {
            GGCalendar.Core.Localization.L.Apply("it");
            Assert.Equal("Ogni 2 settimane il lunedì, giovedì, 13 volte", rule.Describe());
            GGCalendar.Core.Localization.L.Apply("en");
            Assert.Equal("Every 2 weeks on Monday, Thursday, 13 times", rule.Describe());
        }
        finally
        {
            GGCalendar.Core.Localization.L.Apply(language);
        }
    }

    [Fact]
    public void UntilIsDateOnlyForAllDayAndUtcForTimed()
    {
        var rule = new RecurrenceRule { Frequency = RecurrenceFrequency.Daily, Until = new DateTime(2027, 1, 7) };
        Assert.Equal("FREQ=DAILY;UNTIL=20270107", rule.ToRrule(true));
        Assert.Equal("FREQ=DAILY;UNTIL=20270107T235959Z", rule.ToRrule(false));
        Assert.Equal(new DateTime(2027, 1, 7), RecurrenceRule.Parse("FREQ=DAILY;UNTIL=20270107T235959Z")!.Until);
    }

    [Fact]
    public void WithCountReplacesCountAndUntil()
    {
        Assert.Equal("FREQ=WEEKLY;BYDAY=MO;COUNT=3", RecurrenceRule.WithCount("FREQ=WEEKLY;BYDAY=MO;COUNT=10", 3));
        Assert.Equal("FREQ=DAILY;COUNT=2", RecurrenceRule.WithCount("FREQ=DAILY;UNTIL=20270107", 2));
    }

    [Theory]
    [InlineData("FREQ=MONTHLY;BYSETPOS=1;BYDAY=MO")]
    [InlineData("FREQ=HOURLY")]
    [InlineData("FREQ=DAILY;COUNT=3;UNTIL=20270107")]
    [InlineData("")]
    public void UnsupportedRulesReturnNull(string rrule) => Assert.Null(RecurrenceRule.Parse(rrule));
}
