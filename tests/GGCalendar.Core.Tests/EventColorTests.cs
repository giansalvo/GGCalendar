using System.Text.Json;
using GGCalendar.Core.Models;
using GGCalendar.Core.Tests.Support;
using GGCalendar.Providers.Google;

namespace GGCalendar.Core.Tests;

/// <summary>The color of an event travels on Google as colorId 1-11 (Google's classic event palette), as in GGCalendar Mobile.</summary>
public class EventColorTests
{
    private static readonly DateTime Start = new(2026, 10, 14, 9, 0, 0);

    private static CalendarEvent Event(string? colorHex) =>
        new("e1", "c1", "Riunione", Start, Start.AddHours(1)) { ColorHex = colorHex, Reminders = [] };

    private static CalendarEvent FromJson(string? colorId)
    {
        var color = colorId is null ? "" : ",\"colorId\":\"" + colorId + "\"";
        var json = "{\"id\":\"r1\",\"summary\":\"ttt\",\"start\":{\"dateTime\":\"2026-10-14T07:00:00Z\"},\"end\":{\"dateTime\":\"2026-10-14T08:00:00Z\"}" + color + "}";
        return GoogleEventMapper.FromGoogle(JsonDocument.Parse(json).RootElement, "e1", "c1", [])!;
    }

    [Theory]
    [Trait("TestCase", "T01")]
    [InlineData("1", "#7986CB")]
    [InlineData("2", "#33B679")]
    [InlineData("3", "#8E24AA")]
    [InlineData("4", "#E67C73")]
    [InlineData("5", "#F6BF26")]
    [InlineData("6", "#F4511E")]
    [InlineData("7", "#039BE5")]
    [InlineData("8", "#616161")]
    [InlineData("9", "#3F51B5")]
    [InlineData("10", "#0B8043")]
    [InlineData("11", "#D50000")]
    public void T01_ColorIdFromGoogleBecomesTheEventColor(string colorId, string hex)
    {
        Assert.Equal(hex, FromJson(colorId).ColorHex, ignoreCase: true);
    }

    [Fact]
    [Trait("TestCase", "T01")]
    public void T01_AnEventWithoutColorIdUsesTheCalendarColor()
    {
        Assert.Null(FromJson(null).ColorHex);
    }

    [Theory]
    [Trait("TestCase", "T01")]
    [InlineData("#7986CB", "1")]
    [InlineData("#33B679", "2")]
    [InlineData("#8E24AA", "3")]
    [InlineData("#E67C73", "4")]
    [InlineData("#F6BF26", "5")]
    [InlineData("#F4511E", "6")]
    [InlineData("#039BE5", "7")]
    [InlineData("#616161", "8")]
    [InlineData("#3F51B5", "9")]
    [InlineData("#0B8043", "10")]
    [InlineData("#D50000", "11")]
    [InlineData("#d50000", "11")]
    public void T01_TheEventColorIsSentAsColorId(string hex, string colorId)
    {
        var body = GoogleEventMapper.ToGoogle(Event(hex), includeMeetRequest: false);

        Assert.Equal(colorId, body["colorId"]?.GetValue<string>());
    }

    [Fact]
    [Trait("TestCase", "T01")]
    public void T01_WithoutColorTheBodyCarriesAnExplicitNullSoAPatchRemovesIt()
    {
        var body = GoogleEventMapper.ToGoogle(Event(null), includeMeetRequest: false);

        Assert.True(body.ContainsKey("colorId"));
        Assert.Null(body["colorId"]);
    }

    [Fact]
    [Trait("TestCase", "T01")]
    public async Task T01_RemovingTheColorOnTheDesktopRemovesItOnGoogle()
    {
        using var rig = new SyncRig();
        var colored = rig.AddLocal(SyncRig.NewEvent("e1", "Colorato", "#D50000"));
        await rig.SyncOkAsync();
        var remoteId = Assert.Single(rig.Google.Ids);
        Assert.Equal("11", rig.Google.ColorId(remoteId));

        rig.Edit(colored, colored with { ColorHex = null });
        await rig.SyncOkAsync();

        Assert.Null(rig.Google.ColorId(remoteId));
        Assert.Null(rig.Local("Colorato")!.ColorHex);
    }

    [Theory]
    [Trait("TestCase", "T01")]
    [InlineData("#7986CB")]
    [InlineData("#33B679")]
    [InlineData("#8E24AA")]
    [InlineData("#E67C73")]
    [InlineData("#F6BF26")]
    [InlineData("#F4511E")]
    [InlineData("#039BE5")]
    [InlineData("#616161")]
    [InlineData("#3F51B5")]
    [InlineData("#0B8043")]
    [InlineData("#D50000")]
    public void T01_AColorSurvivesTheRoundTripThroughGoogle(string hex)
    {
        var json = GoogleEventMapper.ToGoogle(Event(hex), includeMeetRequest: false).ToJsonString();

        var back = GoogleEventMapper.FromGoogle(JsonDocument.Parse(json).RootElement, "e1", "c1", [])!;

        Assert.Equal(hex, back.ColorHex, ignoreCase: true);
    }
}
