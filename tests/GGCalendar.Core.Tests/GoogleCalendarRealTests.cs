using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GGCalendar.App.ViewModels;
using GGCalendar.App.Views;
using GGCalendar.Core.Models;
using GGCalendar.Core.Tests.Support;
using static GGCalendar.Core.Tests.Support.AppDriver;

namespace GGCalendar.Core.Tests;

/// <summary>Creating and deleting a calendar on the real Google of the test account (issue #16). Only calendars with the test prefix are made and deleted.</summary>
[Collection(CollectionName)]
[Trait("Category", "RealGoogle")]
public class GoogleCalendarRealTests
{
    private static MenuItem Item(ContextMenu menu, string automationId) =>
        menu.Items.OfType<MenuItem>().First(i => Avalonia.Automation.AutomationProperties.GetAutomationId(i) == automationId);

    private static void ClickItem(MenuItem item)
    {
        item.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }

    [Fact]
    [Trait("TestCase", "T182")]
    public Task T182_T185_ACalendarIsCreatedOnGoogleUsedAndDeletedWithItsEvents() => InAppAsync(async window =>
    {
        var vm = Vm(window);
        var name = NewTitle() + " calendario";
        using var google = await TestGoogle.ConnectAsync();

        // The menu of the account: a new calendar.
        var group = vm.Accounts.First(a => a.AccountName == TestAccount);
        var header = window.GetVisualDescendants().OfType<Grid>().First(g => g.DataContext == group && g.ContextMenu is not null);
        header.ContextMenu!.DataContext = group;
        header.ContextMenu.Open(header);
        ClickItem(Item(header.ContextMenu, "Menu.NewCalendarInAccount"));
        var dialog = await WaitForAsync(() => window.OwnedWindows.OfType<GoogleCalendarWindow>().FirstOrDefault(), "la finestra del nuovo calendario");
        ((TextBox)FindById(dialog, "Gcal.Name")).Text = name;
        Dispatcher.UIThread.RunJobs();
        Click(FindButton(dialog, "Gcal.Color.4"));
        Click(FindButton(dialog, "Gcal.Create"));
        var created = await WaitForAsync(() => vm.Calendars.FirstOrDefault(c => c.Name == name), "il calendario creato", SyncTimeout);

        // It is on Google, with the colour chosen, and the app owns it.
        var remoteId = await google.FindCalendarAsync(name);
        Assert.NotNull(remoteId);
        Assert.Equal(TestAccount + "|" + remoteId, created.Id);
        Assert.Equal(true, created.IsOwner);
        Assert.True(created.CanDeleteFromGoogle);

        // An event made on Google reaches the app; one made in the app reaches Google.
        var onGoogle = NewTitle() + " da Google";
        await google.InsertEventInAsync(remoteId!, onGoogle, DateTime.UtcNow.AddDays(3));
        var fromApp = NewTitle() + " dall'app";
        vm.AddEvent(new CalendarEvent(Guid.NewGuid().ToString("N"), created.Id, fromApp, DateTime.Today.AddDays(4).AddHours(10), DateTime.Today.AddDays(4).AddHours(11)) { Reminders = [] });
        await SyncAsync(window);
        Assert.Contains(vm.AllEvents, e => e.Title == onGoogle && e.CalendarId == created.Id);
        Assert.Equal(2, await google.CountEventsAsync(remoteId!));

        // The primary calendar of the account is grey, the new one is not.
        var primary = vm.Calendars.First(c => c.Id == TestCalendarId);
        Assert.False(primary.CanDeleteFromGoogle);
        Assert.Equal(true, primary.IsOwner);

        // Delete it: one confirmation, and the calendar and its events are gone from Google and here.
        var current = vm.Calendars.First(c => c.Id == created.Id); // a sync makes the items again
        await PauseAsync(TimeSpan.FromMilliseconds(100));
        var holder = window.GetVisualDescendants().OfType<Border>().First(b => b.DataContext == current && b.ContextMenu is not null);
        holder.ContextMenu!.DataContext = current;
        holder.ContextMenu.Open(holder);
        ClickItem(Item(holder.ContextMenu, "Menu.DeleteGoogleCalendar"));
        var confirm = await WaitForAsync(() => window.OwnedWindows.OfType<ConfirmWindow>().FirstOrDefault(), "la conferma");
        Click(FindButton(confirm, "Dialog.Ok"));
        await WaitForAsync(() => vm.Calendars.All(c => c.Id != created.Id) ? vm : null, "la scomparsa del calendario", SyncTimeout);
        Assert.Null(await google.FindCalendarAsync(name));
        Assert.DoesNotContain(vm.AllEvents, e => e.CalendarId == created.Id);

        // The next sync is quiet: nothing comes back.
        await SyncAsync(window);
        Assert.DoesNotContain(vm.Calendars, c => c.Name == name);
    });

    [Fact]
    [Trait("TestCase", "T182")]
    public async Task NoCalendarOfTheTestsIsLeftOnTheTestAccount()
    {
        using var google = await TestGoogle.ConnectAsync();
        Assert.DoesNotContain(await google.ListCalendarsAsync(), c => c.Name.StartsWith(TestPrefix, StringComparison.Ordinal));
    }
}
