using Avalonia.Controls;
using Avalonia.Threading;
using GGCalendar.Core.Models;
using GGCalendar.Core.Tests.Support;
using static GGCalendar.Core.Tests.Support.AppDriver;

namespace GGCalendar.Core.Tests;

/// <summary>T153: the "no events" message of the schedule view (issue #2 asked for the second time zone there too).</summary>
[Collection(CollectionName)]
public class AgendaViewTests
{
    [Fact]
    [Trait("TestCase", "T153")]
    public Task T153_TheNoEventsMessageOfTheScheduleIsShownOnlyWhenThereAreNoEvents() => HeadlessApp.RunAsync(async () =>
    {
        var window = OpenApp();
        try
        {
            await Task.Yield();
            var vm = Vm(window);
            var message = window.FindControl<TextBlock>("AgendaEmpty")!;
            var calendar = vm.Calendars.First(c => c.IsLocal).Id;

            vm.ViewModeIndex = 4;
            vm.SelectedDate = new DateTime(2041, 3, 4); // far away: nothing in the next 30 days
            Dispatcher.UIThread.RunJobs();
            Assert.True(vm.ShowAgendaEmpty);
            Assert.True(message.IsVisible);

            var ev = new CalendarEvent(Guid.NewGuid().ToString("N"), calendar, NewTitle(), new DateTime(2041, 3, 5, 10, 0, 0), new DateTime(2041, 3, 5, 11, 0, 0));
            vm.AddEvent(ev);
            Dispatcher.UIThread.RunJobs();
            Assert.NotEmpty(vm.AgendaDays);
            Assert.False(vm.ShowAgendaEmpty);
            Assert.False(message.IsVisible); // with events the message must not stay behind the list

            vm.DeleteEvent(ev);
            Dispatcher.UIThread.RunJobs();
            Assert.True(message.IsVisible); // and it comes back when the list is empty again

            vm.ViewModeIndex = 1; // other views never show it
            Dispatcher.UIThread.RunJobs();
            Assert.False(message.IsVisible);
        }
        finally
        {
            window.Close();
        }
    });
}
