using System.Text.Json;
using System.Text.Json.Nodes;
using GGCalendar.Core.Models;
using GGCalendar.Providers.Google;

namespace GGCalendar.Core.Tests;

/// <summary>Alarms travel on Google as popups plus the private property "ggAlarm", the same convention as GGCalendar Mobile.</summary>
public class AlarmPropertyTests
{
    private static readonly DateTime Start = new(2026, 10, 14, 9, 0, 0);

    private static CalendarEvent Event(IReadOnlyList<EventReminder> reminders) =>
        new("e1", "c1", "Riunione", Start, Start.AddHours(1)) { Reminders = reminders };

    private static string AlarmValueOf(CalendarEvent ev) =>
        GoogleEventMapper.ToGoogle(ev, includeMeetRequest: false)["extendedProperties"]!["private"]![EventReminder.AlarmProperty]!.GetValue<string>();

    private static CalendarEvent FromJson(string json, IReadOnlyList<EventReminder>? defaults = null) =>
        GoogleEventMapper.FromGoogle(JsonDocument.Parse(json).RootElement, "e1", "c1", defaults ?? [])!;

    private static string Json(string? alarms, string overrides = """[{"method":"popup","minutes":10},{"method":"popup","minutes":30},{"method":"email","minutes":60}]""")
    {
        var extended = alarms is null ? "" : ",\"extendedProperties\":{\"private\":{\"ggAlarm\":\"" + alarms + "\"}}";
        return "{\"id\":\"r1\",\"summary\":\"ttt\",\"start\":{\"dateTime\":\"2026-10-14T07:00:00Z\"},\"end\":{\"dateTime\":\"2026-10-14T08:00:00Z\"},"
            + "\"reminders\":{\"useDefault\":false,\"overrides\":" + overrides + "}" + extended + "}";
    }

    [Fact]
    [Trait("TestCase", "T10")]
    public void FormatIsAscendingWithoutRepeatsAndEmptyWithoutAlarms()
    {
        Assert.Equal("10,30", EventReminder.FormatAlarmMinutes([new(ReminderMethod.Alarm, 30), new(ReminderMethod.Alarm, 10), new(ReminderMethod.Alarm, 10)]));
        Assert.Equal("", EventReminder.FormatAlarmMinutes([new(ReminderMethod.Popup, 10), new(ReminderMethod.Email, 60)]));
        Assert.Equal("", EventReminder.FormatAlarmMinutes([]));
    }

    [Fact]
    [Trait("TestCase", "T10")]
    public void ParseToleratesSpacesAndIgnoresWhatIsNotANumber()
    {
        Assert.Equal(new HashSet<int> { 10, 30 }, EventReminder.ParseAlarmMinutes("10,30"));
        Assert.Equal(new HashSet<int> { 10, 30 }, EventReminder.ParseAlarmMinutes("10, 30"));
        Assert.Equal(new HashSet<int> { 10, 30 }, EventReminder.ParseAlarmMinutes("10,abc,30,-5"));
        Assert.Empty(EventReminder.ParseAlarmMinutes(""));
        Assert.Empty(EventReminder.ParseAlarmMinutes(null));
    }

    [Fact]
    public void ApplyTurnsPopupsIntoAlarmsAndLeavesEmailsAlone()
    {
        var result = EventReminder.ApplyAlarmMinutes([new(ReminderMethod.Popup, 10), new(ReminderMethod.Popup, 30), new(ReminderMethod.Email, 10)], new HashSet<int> { 10 });
        Assert.Equal([new(ReminderMethod.Alarm, 10), new(ReminderMethod.Popup, 30), new(ReminderMethod.Email, 10)], result);
    }

    [Fact]
    public void ApplyMakesOnlyOneOfTwoPopupsWithTheSameMinutesAnAlarm()
    {
        var result = EventReminder.ApplyAlarmMinutes([new(ReminderMethod.Popup, 10), new(ReminderMethod.Popup, 10)], new HashSet<int> { 10 });
        Assert.Equal([new(ReminderMethod.Alarm, 10), new(ReminderMethod.Popup, 10)], result);
    }

    [Fact]
    [Trait("TestCase", "T10")]
    public void ToGoogleAlwaysSendsThePropertyAsAStringNeverNull()
    {
        Assert.Equal("", AlarmValueOf(Event([new(ReminderMethod.Popup, 10)])));
        Assert.Equal("", AlarmValueOf(Event([])));
        Assert.Equal("10,30", AlarmValueOf(Event([new(ReminderMethod.Alarm, 30), new(ReminderMethod.Alarm, 10)])));
    }

    [Fact]
    public void AlarmsAreStillPopupsInTheRemindersOfGoogle()
    {
        var body = GoogleEventMapper.ToGoogle(Event([new(ReminderMethod.Alarm, 10), new(ReminderMethod.Email, 60)]), includeMeetRequest: false);
        var overrides = body["reminders"]!["overrides"]!.AsArray().Select(o => (o!["method"]!.GetValue<string>(), o["minutes"]!.GetValue<int>())).ToList();
        Assert.Equal([("popup", 10), ("email", 60)], overrides);
    }

    [Fact]
    public void FromGoogleWithoutThePropertyKeepsPopupsAndSaysAlarmsAreUnknown()
    {
        var ev = FromJson(Json(null));
        Assert.False(ev.AlarmsFromProvider);
        Assert.Equal([new(ReminderMethod.Popup, 10), new(ReminderMethod.Popup, 30), new(ReminderMethod.Email, 60)], ev.Reminders);
    }

    [Fact]
    public void FromGoogleWithAnEmptyPropertyHasNoAlarmsAndSaysSo()
    {
        var ev = FromJson(Json(""));
        Assert.True(ev.AlarmsFromProvider);
        Assert.DoesNotContain(ev.Reminders, r => r.Method == ReminderMethod.Alarm);
    }

    [Fact]
    [Trait("TestCase", "T10")]
    public void FromGoogleReadsTheFormatWrittenByAndroid()
    {
        var ev = FromJson(Json("10,30"));
        Assert.True(ev.AlarmsFromProvider);
        Assert.Equal([new(ReminderMethod.Alarm, 10), new(ReminderMethod.Alarm, 30), new(ReminderMethod.Email, 60)], ev.Reminders);
    }

    [Fact]
    public void ARoundTripThroughGoogleKeepsTheAlarms()
    {
        var original = Event([new(ReminderMethod.Alarm, 10), new(ReminderMethod.Popup, 30), new(ReminderMethod.Email, 60)]);
        var body = GoogleEventMapper.ToGoogle(original, includeMeetRequest: false);
        body["id"] = "r1";
        var back = FromJson(body.ToJsonString());
        Assert.True(back.AlarmsFromProvider);
        Assert.Equal(original.Reminders, back.Reminders);
    }

    [Fact]
    public void LocalSoundsSurviveWhenGoogleTellsTheAlarms()
    {
        var local = new EventReminder[] { new(ReminderMethod.Alarm, 10, @"C:\suoni\sveglia.mp3"), new(ReminderMethod.Popup, 30, EventReminder.NoSound) };
        var remote = EventReminder.ApplyAlarmMinutes([new(ReminderMethod.Popup, 10), new(ReminderMethod.Popup, 30)], new HashSet<int> { 10 });
        Assert.Equal(local, EventReminder.KeepLocalSounds(remote, local));
    }

    [Fact]
    public void AnAlarmRemovedOnGoogleIsNotBroughtBackByTheLocalCopy()
    {
        // Android took the alarm away: the property is empty and the local alarm must not turn the popup back into one.
        var local = new EventReminder[] { new(ReminderMethod.Alarm, 10) };
        var remote = FromJson(Json("")).Reminders;
        var merged = EventReminder.KeepLocalSounds(remote, local);
        Assert.DoesNotContain(merged, r => r.Method == ReminderMethod.Alarm);
    }

    [Fact]
    public void ASeriesExceptionKeepsItsOwnAlarms()
    {
        var ev = GoogleEventMapper.FromGoogle(JsonDocument.Parse("""
            {"id":"r1_20261014T070000Z","summary":"spostata","recurringEventId":"r1","originalStartTime":{"dateTime":"2026-10-14T07:00:00Z"},
             "start":{"dateTime":"2026-10-14T09:00:00Z"},"end":{"dateTime":"2026-10-14T10:00:00Z"},
             "reminders":{"useDefault":false,"overrides":[{"method":"popup","minutes":5}]},
             "extendedProperties":{"private":{"ggAlarm":"5"}}}
            """).RootElement, "x1", "c1", [], seriesMasterLocalId: "m1")!;
        Assert.Equal([new(ReminderMethod.Alarm, 5)], ev.Reminders);
    }

    [Fact]
    public void ChangingOnlyTheAlarmsIsNotAContentChange()
    {
        var a = Event([new(ReminderMethod.Popup, 10)]);
        var b = Event([new(ReminderMethod.Alarm, 10)]);
        Assert.True(EventContent.Same(a, b));
    }

    [Fact]
    public void AllDayAndDefaultReminderEventsAreMappedToo()
    {
        var ev = GoogleEventMapper.FromGoogle(JsonDocument.Parse("""
            {"id":"r2","summary":"Ferie","start":{"date":"2026-10-14"},"end":{"date":"2026-10-15"},
             "reminders":{"useDefault":true},"extendedProperties":{"private":{"ggAlarm":"30"}}}
            """).RootElement, "e2", "c1", [new(ReminderMethod.Popup, 30)])!;
        Assert.Equal([new(ReminderMethod.Alarm, 30)], ev.Reminders);
    }
}
