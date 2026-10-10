using GGCalendar.Core.Models;

namespace GGCalendar.Core.Tests;

public class EventContentTests
{
    private static readonly CalendarEvent Base = new("e1", "c1", "ttt", new DateTime(2026, 10, 8, 7, 0, 0), new DateTime(2026, 10, 8, 8, 0, 0))
    {
        Attendees = [new EventAttendee("ospite@example.com")],
    };

    [Fact]
    public void ChangesMadeByGoogleAloneAreNotAConflict()
    {
        // Meet created asynchronously, organizer added to the guests, different reminders and etag-related ids.
        var remote = Base with
        {
            Id = "other",
            AddMeet = true,
            MeetLink = "https://meet.google.com/abc-defg-hij",
            Reminders = [],
            Attendees = [new EventAttendee("ospite@example.com"), new EventAttendee("me@gmail.com")],
        };
        Assert.True(EventContent.Same(remote, Base, ignoreGuest: "me@gmail.com"));
    }

    [Theory]
    [InlineData("title")]
    [InlineData("time")]
    [InlineData("guest")]
    [InlineData("description")]
    public void UserVisibleChangesAreAConflict(string change)
    {
        var remote = change switch
        {
            "title" => Base with { Title = "ttt modificato" },
            "time" => Base with { Start = Base.Start.AddHours(1), End = Base.End.AddHours(1) },
            "guest" => Base with { Attendees = [new EventAttendee("altro@example.com")] },
            _ => Base with { Description = "nuova descrizione" },
        };
        Assert.False(EventContent.Same(remote, Base));
    }
}
