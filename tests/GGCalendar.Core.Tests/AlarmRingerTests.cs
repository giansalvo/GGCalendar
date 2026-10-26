using GGCalendar.Core.Models;
using GGCalendar.Core.Reminders;

namespace GGCalendar.Core.Tests;

/// <summary>The alarms that ring now (issue #18): one at a time, the others wait in line.</summary>
public class AlarmRingerTests
{
    private static readonly DateTime Start = new(2026, 10, 26, 10, 0, 0);

    private static DueReminder Due(string id) =>
        new(new CalendarEvent(id, "c", "Evento " + id, Start, Start.AddHours(1)), new EventReminder(ReminderMethod.Alarm, 0), Start);

    private sealed class Spy
    {
        public readonly List<string> Log = [];
        public readonly AlarmRinger Ringer = new();

        public Spy()
        {
            Ringer.SoundRequested += a => Log.Add("suona " + a.Key.Split('|')[0]);
            Ringer.SoundStopRequested += () => Log.Add("stop");
        }

        public string[] States => Ringer.Items.Select(a => a.Key.Split('|')[0] + ":" + a.State).ToArray();
    }

    [Fact]
    [Trait("TestCase", "T218")]
    public void T218_TheFirstAlarmRingsAndTheOthersWaitInLine()
    {
        var spy = new Spy();

        spy.Ringer.Add(Due("a"), "a.wav");
        spy.Ringer.Add(Due("b"), "b.wav");
        spy.Ringer.Add(Due("c"), null);

        Assert.Equal(["suona a"], spy.Log);
        Assert.Equal(["a:Ringing", "b:Queued", "c:Queued"], spy.States);
        Assert.Equal("a.wav", spy.Ringer.Current!.Sound);
    }

    [Fact]
    [Trait("TestCase", "T218")]
    public void T218_WhenTheRingingAlarmIsTurnedOffTheNextOneInLineStarts()
    {
        var spy = new Spy();
        spy.Ringer.Add(Due("a"), null);
        spy.Ringer.Add(Due("b"), null);
        spy.Ringer.Add(Due("c"), null);

        spy.Ringer.Remove(Due("a").Key);

        Assert.Equal(["suona a", "stop", "suona b"], spy.Log);
        Assert.Equal(["b:Ringing", "c:Queued"], spy.States);
    }

    [Fact]
    [Trait("TestCase", "T219")]
    public void T219_AnAlarmInLineTurnedOffDoesNotStopTheOneThatRings()
    {
        var spy = new Spy();
        spy.Ringer.Add(Due("a"), null);
        spy.Ringer.Add(Due("b"), null);

        spy.Ringer.Remove(Due("b").Key);

        Assert.Equal(["suona a"], spy.Log);
        Assert.Equal(["a:Ringing"], spy.States);
    }

    [Fact]
    [Trait("TestCase", "T219")]
    public void T219_TurnOffAllStopsTheSoundAndEmptiesTheList()
    {
        var spy = new Spy();
        spy.Ringer.Add(Due("a"), null);
        spy.Ringer.Add(Due("b"), null);

        spy.Ringer.RemoveAll();

        Assert.Equal(["suona a", "stop"], spy.Log);
        Assert.Empty(spy.Ringer.Items);
        Assert.Null(spy.Ringer.Current);
    }

    [Fact]
    [Trait("TestCase", "T219")]
    public void T219_TurningOffWhatIsNotThereDoesNothing()
    {
        var spy = new Spy();
        spy.Ringer.Remove("x");
        spy.Ringer.RemoveAll();
        Assert.Empty(spy.Log);
    }

    [Fact]
    [Trait("TestCase", "T220")]
    public void T220_WhenTheTimeLimitPassesTheAlarmStaysButIsQuietAndTheNextOneStarts()
    {
        var spy = new Spy();
        spy.Ringer.Add(Due("a"), null);
        spy.Ringer.Add(Due("b"), null);

        spy.Ringer.Silence(Due("a").Key);

        Assert.Equal(["suona a", "stop", "suona b"], spy.Log);
        Assert.Equal(["a:Silenced", "b:Ringing"], spy.States);
    }

    [Fact]
    [Trait("TestCase", "T220")]
    public void T220_ASilencedAlarmIsStillThereUntilItIsTurnedOffAndTurningItOffDoesNotStopTheOneThatRings()
    {
        var spy = new Spy();
        spy.Ringer.Add(Due("a"), null);
        spy.Ringer.Add(Due("b"), null);
        spy.Ringer.Silence(Due("a").Key);
        spy.Log.Clear();

        spy.Ringer.Remove(Due("a").Key);

        Assert.Empty(spy.Log);
        Assert.Equal(["b:Ringing"], spy.States);
    }

    [Fact]
    [Trait("TestCase", "T220")]
    public void T220_AnAlarmThatArrivesAfterTheOthersWereSilencedRingsAtOnce()
    {
        var spy = new Spy();
        spy.Ringer.Add(Due("a"), null);
        spy.Ringer.Silence(Due("a").Key);

        spy.Ringer.Add(Due("b"), null);

        Assert.Equal(["a:Silenced", "b:Ringing"], spy.States);
    }

    [Fact]
    [Trait("TestCase", "T218")]
    public void T218_TheSameAlarmTwiceCountsOnce()
    {
        var spy = new Spy();
        spy.Ringer.Add(Due("a"), null);
        spy.Ringer.Add(Due("a"), null);
        Assert.Single(spy.Ringer.Items);
    }

    [Fact]
    [Trait("TestCase", "T218")]
    public void T218_TheListIsToldWhenSomethingChanges()
    {
        var spy = new Spy();
        var changes = 0;
        spy.Ringer.Changed += () => changes++;

        spy.Ringer.Add(Due("a"), null);
        spy.Ringer.Silence(Due("a").Key);
        spy.Ringer.Remove(Due("a").Key);

        Assert.Equal(3, changes);
    }
}
