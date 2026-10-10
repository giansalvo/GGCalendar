using GGCalendar.Core.Models;

namespace GGCalendar.Core.Tests;

public class EventReminderTests
{
    [Fact]
    public void AlarmAndSoundSurviveAGoogleRoundTrip()
    {
        // Sent to Google as popups; Google sends them back as plain popups.
        IReadOnlyList<EventReminder> local = [new(ReminderMethod.Alarm, 10, @"C:\suoni\sveglia.mp3"), new(ReminderMethod.Popup, 30, @"C:\suoni\ding.wav")];
        IReadOnlyList<EventReminder> remote = [new(ReminderMethod.Popup, 10), new(ReminderMethod.Popup, 30)];

        var merged = EventReminder.KeepLocalOptions(remote, local);

        Assert.Equal(local, merged);
    }

    [Fact]
    public void RemindersChangedOnGoogleAreTakenFromGoogle()
    {
        IReadOnlyList<EventReminder> local = [new(ReminderMethod.Alarm, 10, @"C:\suoni\sveglia.mp3")];
        IReadOnlyList<EventReminder> remote = [new(ReminderMethod.Popup, 15), new(ReminderMethod.Email, 60)];

        var merged = EventReminder.KeepLocalOptions(remote, local);

        Assert.Equal(remote, merged);
    }

    [Fact]
    public void EachLocalReminderIsUsedOnce()
    {
        IReadOnlyList<EventReminder> local = [new(ReminderMethod.Alarm, 10)];
        IReadOnlyList<EventReminder> remote = [new(ReminderMethod.Popup, 10), new(ReminderMethod.Popup, 10)];

        var merged = EventReminder.KeepLocalOptions(remote, local);

        Assert.Equal([new(ReminderMethod.Alarm, 10), new(ReminderMethod.Popup, 10)], merged);
    }

    [Fact]
    public void EmailRemindersAreNeverMatchedToLocalOnes()
    {
        IReadOnlyList<EventReminder> local = [new(ReminderMethod.Alarm, 10)];
        IReadOnlyList<EventReminder> remote = [new(ReminderMethod.Email, 10)];

        Assert.Equal(remote, EventReminder.KeepLocalOptions(remote, local));
    }
}
