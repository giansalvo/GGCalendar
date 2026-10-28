using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GGCalendar.App.ViewModels;
using GGCalendar.App.Views;
using GGCalendar.Core.Models;
using GGCalendar.Core.Tests.Support;
using static GGCalendar.Core.Tests.Support.AppDriver;
using static GGCalendar.Core.Tests.GoogleCalendarUiTests;

namespace GGCalendar.Core.Tests;

/// <summary>Changing the name and the colour of a calendar on the real Google of the test account (issue #7). Only calendars the tests made are renamed; the colour of the others is put back.</summary>
[Collection(CollectionName)]
[Trait("Category", "RealGoogle")]
public class CalendarEditRealTests
{
    [Fact]
    [Trait("TestCase", "T298")]
    public async Task T298_TheTwentyFourColoursAreTheOnesOfGoogle()
    {
        using var google = await TestGoogle.ConnectAsync();
        var colors = await google.GetCalendarColorsAsync();
        Assert.Equal(CalendarColors.All, colors.Select(c => c.Background.ToUpperInvariant()).ToList());
    }

    private static async Task SaveEditAsync(MainWindow window, CalendarItemViewModel calendar, string? newName, int? quickColor)
    {
        ClickItem(Item(OpenCalendarMenu(window, calendar), "Menu.EditCalendar"));
        var dialog = await WaitForAsync(() => window.OwnedWindows.OfType<CalendarEditWindow>().FirstOrDefault(), "la finestra di modifica");
        if (newName is not null)
        {
            ((TextBox)FindById(dialog, "Cal.Name")).Text = newName;
            Dispatcher.UIThread.RunJobs();
        }

        if (quickColor is { } i) Click(FindButton(dialog, "Cal.Color." + i));
        Click(FindButton(dialog, "Cal.Save"));
        await WaitForAsync(() => window.OwnedWindows.Count == 0 ? window : null, "la chiusura", SyncTimeout);
    }

    [Fact]
    [Trait("TestCase", "T296")]
    public Task T296_ACalendarMadeByTheTestIsRenamedAndRecolouredOnGoogleAndTheSyncKeepsIt() => InAppAsync(async window =>
    {
        var vm = Vm(window);
        using var google = await TestGoogle.ConnectAsync();
        var name = NewTitle() + " calendario";

        // A calendar to work on, made through the app.
        var group = vm.Accounts.First(a => a.AccountName == TestAccount);
        var header = window.GetVisualDescendants().OfType<Grid>().First(g => g.DataContext == group && g.ContextMenu is not null);
        header.ContextMenu!.DataContext = group;
        header.ContextMenu.Open(header);
        ClickItem(Item(header.ContextMenu, "Menu.NewCalendarInAccount"));
        var create = await WaitForAsync(() => window.OwnedWindows.OfType<GoogleCalendarWindow>().FirstOrDefault(), "la finestra del nuovo calendario");
        ((TextBox)FindById(create, "Gcal.Name")).Text = name;
        Dispatcher.UIThread.RunJobs();
        Click(FindButton(create, "Gcal.Create"));
        var created = await WaitForAsync(() => vm.Calendars.FirstOrDefault(c => c.Name == name), "il calendario creato", SyncTimeout);
        var remoteId = (await google.FindCalendarAsync(name))!;

        // A personal name on Google, which would hide the new name.
        await Task.Delay(0);
        var renamed = NewTitle() + " rinominato";
        await SaveEditAsync(window, vm.Calendars.First(c => c.Id == created.Id), renamed, 6);
        await WaitForAsync(() => vm.Calendars.FirstOrDefault(c => c.Name == renamed), "il nome nuovo", SyncTimeout);

        var calendar = await google.GetCalendarAsync(remoteId);
        Assert.Equal(renamed, calendar.GetProperty("summary").GetString());
        var entry = await google.GetCalendarEntryAsync(remoteId);
        Assert.False(entry.TryGetProperty("summaryOverride", out _)); // nothing hides the real name
        Assert.Equal(CalendarColors.Quick[6], entry.GetProperty("backgroundColor").GetString()!.ToUpperInvariant());

        // The sync gives the same, it does not undo anything.
        await SyncAsync(window);
        var after = vm.Calendars.First(c => c.Id == created.Id);
        Assert.Equal((renamed, CalendarColors.Quick[6]), (after.Name, after.ColorHex.ToUpperInvariant()));
    });

    [Fact]
    [Trait("TestCase", "T297")]
    public Task T297_TheColourOfThePrimaryCalendarAndOfOneOfSomeoneElseIsChangedAndPutBack() => InAppAsync(async window =>
    {
        var vm = Vm(window);
        using var google = await TestGoogle.ConnectAsync();

        async Task ChangeAndRestoreAsync(CalendarItemViewModel calendar)
        {
            var remoteId = calendar.Id[(calendar.Id.IndexOf('|') + 1)..];
            var before = await google.GetCalendarEntryAsync(remoteId);
            var originalBackground = before.GetProperty("backgroundColor").GetString()!;
            var originalForeground = before.GetProperty("foregroundColor").GetString()!;
            var originalName = before.TryGetProperty("summaryOverride", out var so) ? so.GetString() : before.GetProperty("summary").GetString();
            var wanted = CalendarColors.Quick.First(c => !c.Equals(originalBackground, StringComparison.OrdinalIgnoreCase));
            try
            {
                await SaveEditAsync(window, calendar, null, CalendarColors.Quick.ToList().IndexOf(wanted));
                await WaitForAsync(() => vm.Calendars.FirstOrDefault(c => c.Id == calendar.Id && c.ColorHex.Equals(wanted, StringComparison.OrdinalIgnoreCase)), "il colore nuovo", SyncTimeout);
                var entry = await google.GetCalendarEntryAsync(remoteId);
                Assert.Equal(wanted, entry.GetProperty("backgroundColor").GetString()!.ToUpperInvariant());
                var name = entry.TryGetProperty("summaryOverride", out var so2) ? so2.GetString() : entry.GetProperty("summary").GetString();
                Assert.Equal(originalName, name); // the name is the same
            }
            finally
            {
                await google.SetCalendarColorAsync(remoteId, originalBackground, originalForeground);
            }
        }

        var primary = vm.Calendars.First(c => c.Id == TestCalendarId);
        Assert.False(primary.CanRename);
        await ChangeAndRestoreAsync(primary);

        var other = vm.Calendars.FirstOrDefault(c => c.IsGoogle && c.AccountName == TestAccount && c.IsOwner == false);
        if (other is null) return; // the test account has no calendar of someone else
        Assert.False(other.CanRename);
        await ChangeAndRestoreAsync(other);
        await SyncAsync(window);
    });
}
