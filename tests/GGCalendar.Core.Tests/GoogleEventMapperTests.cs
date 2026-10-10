using System.Text.Json.Nodes;
using GGCalendar.Core.Models;
using GGCalendar.Providers.Google;

namespace GGCalendar.Core.Tests;

public class GoogleEventMapperTests
{
    private static JsonObject Start(CalendarEvent ev) => GoogleEventMapper.ToGoogle(ev, includeMeetRequest: false)["start"]!.AsObject();

    [Fact]
    public void TimedEventClearsDateSoAPatchFromAllDayIsAccepted()
    {
        var start = Start(new CalendarEvent("e1", "c1", "Riunione", new DateTime(2026, 5, 4, 9, 0, 0), new DateTime(2026, 5, 4, 10, 0, 0)));
        Assert.True(start.ContainsKey("date"));
        Assert.Null(start["date"]);
        Assert.StartsWith("2026-05-04T09:00:00", start["dateTime"]!.GetValue<string>());
    }

    [Fact]
    public void AllDayEventClearsDateTimeAndTimeZoneSoAPatchFromTimedIsAccepted()
    {
        var start = Start(new CalendarEvent("e1", "c1", "Gita", new DateTime(2026, 5, 4), new DateTime(2026, 5, 5), IsAllDay: true));
        Assert.Equal("2026-05-04", start["date"]!.GetValue<string>());
        Assert.True(start.ContainsKey("dateTime"));
        Assert.Null(start["dateTime"]);
        Assert.True(start.ContainsKey("timeZone"));
        Assert.Null(start["timeZone"]);
    }

    private static CalendarEvent? FromJson(string json) =>
        GoogleEventMapper.FromGoogle(System.Text.Json.JsonDocument.Parse(json).RootElement, "e1", "c1", []);

    [Fact]
    public void PendingConferenceKeepsMeetFlag()
    {
        var ev = FromJson("""
            {"id":"r1","summary":"ttt","start":{"dateTime":"2026-10-08T05:00:00Z"},"end":{"dateTime":"2026-10-08T06:00:00Z"},
             "conferenceData":{"createRequest":{"requestId":"x","status":{"statusCode":"pending"}}}}
            """)!;
        Assert.True(ev.AddMeet);
        Assert.Null(ev.MeetLink);
    }

    [Fact]
    public void MeetLinkReadFromConferenceEntryPoints()
    {
        var ev = FromJson("""
            {"id":"r1","summary":"ttt","start":{"dateTime":"2026-10-08T05:00:00Z"},"end":{"dateTime":"2026-10-08T06:00:00Z"},
             "conferenceData":{"entryPoints":[{"entryPointType":"phone","uri":"tel:+39"},{"entryPointType":"video","uri":"https://meet.google.com/abc-defg-hij"}]}}
            """)!;
        Assert.True(ev.AddMeet);
        Assert.Equal("https://meet.google.com/abc-defg-hij", ev.MeetLink);
    }

    [Fact]
    public void NullsAreSerializedSoGoogleRemovesTheField()
    {
        var json = GoogleEventMapper.ToGoogle(new CalendarEvent("e1", "c1", "Gita", new DateTime(2026, 5, 4), new DateTime(2026, 5, 5), IsAllDay: true), false)
            .ToJsonString();
        Assert.Contains("\"dateTime\":null", json);
    }
}
