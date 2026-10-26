using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GGCalendar.App.ViewModels;
using GGCalendar.App.Views;
using GGCalendar.Core.Localization;
using GGCalendar.Core.Models;
using GGCalendar.Core.Tests.Support;
using GGCalendar.Data;
using static GGCalendar.Core.Tests.Support.AppDriver;
using static GGCalendar.Core.Tests.GoogleCalendarUiTests;

namespace GGCalendar.Core.Tests;

/// <summary>"Edit calendar…" on the real window (issue #7): the item of the menu, the window with the name and the 8 quick colours, the 24 of Google, and what the status bar says.</summary>
[Collection(CollectionName)]
public class CalendarEditUiTests
{
    private static async Task<CalendarEditWindow> OpenEditAsync(MainWindow window, CalendarItemViewModel calendar)
    {
        ClickItem(Item(OpenCalendarMenu(window, calendar), "Menu.EditCalendar"));
        return await WaitForAsync(() => window.OwnedWindows.OfType<CalendarEditWindow>().FirstOrDefault(), "la finestra di modifica");
    }

    private static TextBox NameOf(Window dialog) => (TextBox)FindById(dialog, "Cal.Name");

    private static void Write(TextBox box, string text)
    {
        box.Text = text;
        Dispatcher.UIThread.RunJobs();
    }

    private static List<Button> Swatches(Window dialog) =>
        dialog.GetVisualDescendants().OfType<Button>().Where(b => (Avalonia.Automation.AutomationProperties.GetAutomationId(b) ?? "").StartsWith("Cal.Color.")).ToList();

    private static string Back(Button swatch) => (string)swatch.Tag!;

    // ---- the item of the menu ----

    [Fact]
    [Trait("TestCase", "T196")]
    public Task T196_TheMenuOfACalendarHasEditAndTheNameOfASubscriptionIsTheSame() => InFakeGoogleAppAsync(new FakeGoogle(), async window =>
    {
        var vm = Vm(window);
        var local = vm.Calendars.First(c => c.IsLocal);
        var menu = OpenCalendarMenu(window, local);
        var item = Item(menu, "Menu.EditCalendar");
        Assert.True(item.IsVisible);
        Assert.True(item.IsEnabled);
        Assert.Equal(L.T("main.edit_calendar"), item.Header);
        menu.Close();

        var primary = Calendar(window, FakeGoogle.LocalCalendarId); // a Google calendar too, even the primary one: the colour can be changed
        Assert.True(Item(OpenCalendarMenu(window, primary), "Menu.EditCalendar").IsEnabled);
        await Task.CompletedTask;
    });

    // ---- a calendar of this PC ----

    [Fact]
    [Trait("TestCase", "T196")]
    public Task T196_ALocalCalendarIsEditedHere() => InFakeGoogleAppAsync(new FakeGoogle(), async window =>
    {
        var vm = Vm(window);
        var id = vm.CreateLocalCalendar(TestPrefix + " da modificare");
        await PauseAsync(TimeSpan.FromMilliseconds(100));
        var dialog = await OpenEditAsync(window, Calendar(window, id));
        Assert.Equal(TestPrefix + " da modificare", NameOf(dialog).Text);
        Assert.True(NameOf(dialog).IsEnabled);
        Assert.False(FindButton(dialog, "Cal.Save").IsEnabled); // nothing changed yet

        Write(NameOf(dialog), TestPrefix + " cambiato");
        Assert.True(FindButton(dialog, "Cal.Save").IsEnabled);
        Click(FindButton(dialog, "Cal.Color.2"));
        Click(FindButton(dialog, "Cal.Save"));
        await WaitForAsync(() => window.OwnedWindows.Count == 0 ? window : null, "la chiusura");
        await WaitForAsync(() => vm.Calendars.FirstOrDefault(c => c.Id == id && c.Name == TestPrefix + " cambiato"), "il nome nuovo");
        Assert.Equal(CalendarColors.Quick[2], Calendar(window, id).ColorHex);
        Assert.Equal("status.calendar_edited", vm.Status!.Key);
        vm.DeleteLocalCalendar(id);
    });

    [Fact]
    [Trait("TestCase", "T205")]
    public Task T205_SaveIsOffWithoutChangesOrWithAnEmptyNameAndCancelChangesNothing() => InFakeGoogleAppAsync(new FakeGoogle(), async window =>
    {
        var vm = Vm(window);
        var id = vm.CreateLocalCalendar(TestPrefix + " finestra");
        await PauseAsync(TimeSpan.FromMilliseconds(100));
        var calendar = Calendar(window, id);
        var dialog = await OpenEditAsync(window, calendar);
        var save = FindButton(dialog, "Cal.Save");
        Assert.False(save.IsEnabled);

        Write(NameOf(dialog), "   ");
        Assert.False(save.IsEnabled); // empty name
        Write(NameOf(dialog), TestPrefix + " finestra");
        Assert.False(save.IsEnabled); // as it was
        Write(NameOf(dialog), "  " + TestPrefix + " finestra ");
        Assert.False(save.IsEnabled); // only spaces around: the same name
        Write(NameOf(dialog), TestPrefix + " nuovo");
        Assert.True(save.IsEnabled);
        Click(FindButton(dialog, "Dialog.Cancel"));
        await WaitForAsync(() => window.OwnedWindows.Count == 0 ? window : null, "la chiusura");
        Assert.Equal(TestPrefix + " finestra", Calendar(window, id).Name);
        vm.DeleteLocalCalendar(id);
    });

    // ---- a calendar of Google that the person owns ----

    [Fact]
    [Trait("TestCase", "T197")]
    public Task T197_ColourOnlyOfAnOwnedCalendarGoesToGoogleWithOneRequest()
    {
        var fake = new FakeGoogle();
        var mine = fake.AddCalendar("Lavoro");
        return InFakeGoogleAppAsync(fake, async window =>
        {
            await SyncNowAsync(window);
            var vm = Vm(window);
            var dialog = await OpenEditAsync(window, Calendar(window, Local(mine)));
            Assert.True(NameOf(dialog).IsEnabled);
            Click(FindButton(dialog, "Cal.Color.3"));
            Click(FindButton(dialog, "Cal.Save"));
            await WaitForAsync(() => vm.Calendars.FirstOrDefault(c => c.Id == Local(mine) && c.ColorHex == CalendarColors.Quick[3]), "il colore nuovo");
            Assert.Equal(CalendarColors.Quick[3], fake.CalendarColor(mine));
            Assert.Empty(fake.Renames);
            Assert.Single(fake.ColorPatches);
            Assert.Equal("status.calendar_edited", vm.Status!.Key);
        });
    }

    [Fact]
    [Trait("TestCase", "T198")]
    public Task T198_RenamingAnOwnedCalendarSaysThatTheOthersSeeTheNewNameToo()
    {
        var fake = new FakeGoogle();
        var mine = fake.AddCalendar("Lavoro");
        fake.SetPersonalName(mine, "Mio lavoro");
        return InFakeGoogleAppAsync(fake, async window =>
        {
            await SyncNowAsync(window);
            var vm = Vm(window);
            var dialog = await OpenEditAsync(window, Calendar(window, Local(mine)));
            Assert.Equal("Mio lavoro", NameOf(dialog).Text);
            Assert.Equal(L.T("gcal.rename_note_shared"), ((TextBlock)FindById(dialog, "Cal.NameNote")).Text);
            Write(NameOf(dialog), "Studio");
            Click(FindButton(dialog, "Cal.Save"));
            await WaitForAsync(() => vm.Calendars.FirstOrDefault(c => c.Id == Local(mine) && c.Name == "Studio"), "il nome nuovo");
            Assert.Equal([(mine, "Studio")], fake.Renames);
            Assert.Null(fake.PersonalNameOf(mine)); // so that the new name is the one that is seen
            Assert.Equal("status.calendar_edited", vm.Status!.Key);
            Assert.False(vm.SyncIsError);
        });
    }

    // ---- the primary calendar, the calendars of others, a role not known ----

    [Fact]
    [Trait("TestCase", "T200")]
    public Task T200_ThePrimaryCalendarHasAGreyNameWithTheReasonAndTakesAColour()
    {
        var fake = new FakeGoogle();
        return InFakeGoogleAppAsync(fake, async window =>
        {
            var vm = Vm(window);
            var dialog = await OpenEditAsync(window, Calendar(window, FakeGoogle.LocalCalendarId));
            Assert.False(NameOf(dialog).IsEnabled);
            Assert.Equal(L.T("gcal.rename_hint_primary"), ((TextBlock)FindById(dialog, "Cal.NameNote")).Text);
            Assert.False(FindButton(dialog, "Cal.Save").IsEnabled);
            Click(FindButton(dialog, "Cal.Color.1"));
            Assert.True(FindButton(dialog, "Cal.Save").IsEnabled); // the colour alone is enough
            Click(FindButton(dialog, "Cal.Save"));
            await WaitForAsync(() => vm.Calendars.FirstOrDefault(c => c.Id == FakeGoogle.LocalCalendarId && c.ColorHex == CalendarColors.Quick[1]), "il colore nuovo");
            Assert.Empty(fake.Renames);
            Assert.Equal(CalendarColors.Quick[1], fake.CalendarColor(FakeGoogle.RemoteCalendar));
        });
    }

    [Fact]
    [Trait("TestCase", "T199")]
    public Task T199_ACalendarOfSomeoneElseHasAGreyNameAndTakesAColour()
    {
        var fake = new FakeGoogle();
        var holidays = fake.AddCalendar("Festività in Italia", role: "reader");
        return InFakeGoogleAppAsync(fake, async window =>
        {
            await SyncNowAsync(window);
            var vm = Vm(window);
            var dialog = await OpenEditAsync(window, Calendar(window, Local(holidays)));
            Assert.False(NameOf(dialog).IsEnabled);
            Assert.Equal(L.T("gcal.rename_hint_not_owner"), ((TextBlock)FindById(dialog, "Cal.NameNote")).Text);
            Click(FindButton(dialog, "Cal.Color.0"));
            Click(FindButton(dialog, "Cal.Save"));
            await WaitForAsync(() => vm.Calendars.FirstOrDefault(c => c.Id == Local(holidays) && c.ColorHex == CalendarColors.Quick[0]), "il colore nuovo");
            Assert.Empty(fake.Renames);
            Assert.Equal("Festività in Italia", fake.CalendarSummary(holidays));
        });
    }

    [Fact]
    [Trait("TestCase", "T201")]
    public Task T201_WhenTheRoleIsNotKnownYetTheNameIsGreyWithTheReason() => InFakeGoogleAppAsync(new FakeGoogle(), async window =>
    {
        var store = new CalendarStore();
        store.UpsertCalendar(new CalendarInfo(Local("nuovo@group.calendar.google.com"), FakeGoogle.Account, "Nuovo", "#9FE1E7"));
        Vm(window).Reload();
        await PauseAsync(TimeSpan.FromMilliseconds(100));
        var dialog = await OpenEditAsync(window, Calendar(window, Local("nuovo@group.calendar.google.com")));
        Assert.False(NameOf(dialog).IsEnabled);
        Assert.Equal(L.T("gcal.rename_hint_unknown"), ((TextBlock)FindById(dialog, "Cal.NameNote")).Text);
    });

    // ---- errors and a result in part ----

    [Fact]
    [Trait("TestCase", "T202")]
    public Task T202_WithoutANetworkNothingChangesAndTheStatusSaysWhy()
    {
        var fake = new FakeGoogle();
        var mine = fake.AddCalendar("Lavoro");
        return InFakeGoogleAppAsync(fake, async window =>
        {
            await SyncNowAsync(window);
            var vm = Vm(window);
            var dialog = await OpenEditAsync(window, Calendar(window, Local(mine)));
            Write(NameOf(dialog), "Studio");
            fake.Offline = true;
            Click(FindButton(dialog, "Cal.Save"));
            await WaitForAsync(() => vm.Status?.Key == "error.gcal_edit" ? vm : null, "il messaggio");
            Assert.True(vm.SyncIsError);
            Assert.Equal(L.T("error.gcal_edit", L.T("gcal.problem.NoNetwork")), vm.SyncStatus);
            Assert.Equal("Lavoro", Calendar(window, Local(mine)).Name);
            Assert.True(window.IsEnabled);
        });
    }

    [Fact]
    [Trait("TestCase", "T203")]
    public Task T203_NameDoneAndColourRefusedIsToldAsDoneInPart()
    {
        var fake = new FakeGoogle();
        var mine = fake.AddCalendar("Lavoro");
        return InFakeGoogleAppAsync(fake, async window =>
        {
            await SyncNowAsync(window);
            var vm = Vm(window);
            var dialog = await OpenEditAsync(window, Calendar(window, Local(mine)));
            Write(NameOf(dialog), "Studio");
            Click(FindButton(dialog, "Cal.Color.4"));
            fake.LimitAfterWrites = fake.Writes + 1;
            Click(FindButton(dialog, "Cal.Save"));
            await WaitForAsync(() => vm.Status?.Key == "status.gcal_edited_partial" ? vm : null, "il messaggio");
            Assert.Equal(L.T("status.gcal_edited_partial", "Studio", L.T("gcal.problem.RateLimited")), vm.SyncStatus);
            Assert.Equal("Studio", Calendar(window, Local(mine)).Name);
        });
    }

    // ---- the colours ----

    [Fact]
    [Trait("TestCase", "T206")]
    public Task T206_TheWindowShowsTheEightQuickColoursAndTheCurrentOneWhenItIsNotAmongThem() => InFakeGoogleAppAsync(new FakeGoogle(), async window =>
    {
        var vm = Vm(window);
        var id = vm.CreateLocalCalendar(TestPrefix + " colori");
        vm.UpdateLocalCalendar(id, TestPrefix + " colori", "#123456"); // not in the palette
        await PauseAsync(TimeSpan.FromMilliseconds(100));
        var dialog = await OpenEditAsync(window, Calendar(window, id));
        var swatches = Swatches(dialog);
        Assert.Equal(9, swatches.Count); // the 8 and the current one
        Assert.Equal(CalendarColors.Quick, swatches.Take(8).Select(Back));
        Assert.Equal("#123456", Back(swatches[8]));
        Assert.Equal(3, swatches[8].BorderThickness.Left); // it is the selected one
        Assert.False(FindButton(dialog, "Cal.Save").IsEnabled);

        Click(FindButton(dialog, "Cal.Color.0"));
        swatches = Swatches(dialog);
        Assert.Equal(9, swatches.Count); // the colour of the calendar stays in the row until it is saved: "cancel" can go back to it
        Assert.True(FindButton(dialog, "Cal.Save").IsEnabled);
        Click(FindButton(dialog, "Dialog.Cancel"));
        await WaitForAsync(() => window.OwnedWindows.Count == 0 ? window : null, "la chiusura");
        vm.DeleteLocalCalendar(id);
    });

    [Fact]
    [Trait("TestCase", "T206")]
    public Task T206_MoreColoursOpensTheTwentyFourOfGoogleAndTheChoiceComesBack() => InFakeGoogleAppAsync(new FakeGoogle(), async window =>
    {
        var vm = Vm(window);
        var id = vm.CreateLocalCalendar(TestPrefix + " altri");
        await PauseAsync(TimeSpan.FromMilliseconds(100));
        var dialog = await OpenEditAsync(window, Calendar(window, id));
        Click(FindButton(dialog, "Cal.MoreColors"));
        var colors = await WaitForAsync(() => dialog.OwnedWindows.OfType<CalendarColorsWindow>().FirstOrDefault(), "la finestra dei colori");
        var grid = colors.GetVisualDescendants().OfType<Button>().Where(b => (Avalonia.Automation.AutomationProperties.GetAutomationId(b) ?? "").StartsWith("Colors.")).ToList();
        Assert.Equal(24, grid.Count);
        Assert.Equal(CalendarColors.All, grid.Select(b => (string)b.Tag!));
        Click(FindButton(colors, "Colors.19")); // grey of Google: not one of the quick ones
        await WaitForAsync(() => dialog.OwnedWindows.Count == 0 ? dialog : null, "il ritorno alla finestra");

        var swatches = Swatches(dialog);
        Assert.Equal(CalendarColors.All[18], Back(swatches.Last())); // the choice is in the row, selected (after the colour the calendar had, if that was not a quick one)
        Assert.True(FindButton(dialog, "Cal.Save").IsEnabled);
        Click(FindButton(dialog, "Cal.Save"));
        await WaitForAsync(() => vm.Calendars.FirstOrDefault(c => c.Id == id && c.ColorHex == CalendarColors.All[18]), "il colore nuovo");
        vm.DeleteLocalCalendar(id);
    });

    [Fact]
    [Trait("TestCase", "T206")]
    public Task T206_CancelInTheListOfColoursChangesNothing() => InFakeGoogleAppAsync(new FakeGoogle(), async window =>
    {
        var vm = Vm(window);
        var id = vm.CreateLocalCalendar(TestPrefix + " annulla");
        await PauseAsync(TimeSpan.FromMilliseconds(100));
        var dialog = await OpenEditAsync(window, Calendar(window, id));
        Click(FindButton(dialog, "Cal.MoreColors"));
        var colors = await WaitForAsync(() => dialog.OwnedWindows.OfType<CalendarColorsWindow>().FirstOrDefault(), "la finestra dei colori");
        Click(FindButton(colors, "Dialog.Cancel"));
        await WaitForAsync(() => dialog.OwnedWindows.Count == 0 ? dialog : null, "la chiusura");
        Assert.False(FindButton(dialog, "Cal.Save").IsEnabled);
        Click(FindButton(dialog, "Dialog.Cancel"));
        vm.DeleteLocalCalendar(id);
    });

    // ---- the list of the calendars: wide, and a tooltip with the whole name ----

    [Fact]
    [Trait("TestCase", "T211")]
    public Task T211_TheListOfCalendarsIsWideAndEveryAccountAndCalendarHasATooltipWithItsWholeName() => InFakeGoogleAppAsync(new FakeGoogle(), async window =>
    {
        var vm = Vm(window);
        var longName = TestPrefix + " un nome di calendario molto lungo che non sta nella riga";
        var id = vm.CreateLocalCalendar(longName);
        await PauseAsync(TimeSpan.FromMilliseconds(150));

        var list = window.GetVisualDescendants().OfType<Border>().First(b => b.Width == 340); // the column of the calendars
        Assert.True(list.Bounds.Width >= 340);

        var texts = window.GetVisualDescendants().OfType<TextBlock>().ToList();
        var calendarName = texts.First(t => t.DataContext is CalendarItemViewModel { } c && c.Id == id && t.Text == longName);
        Assert.Equal(longName, ToolTip.GetTip(calendarName)); // the whole name, also when the line shows only the beginning
        Assert.Equal(Avalonia.Media.TextTrimming.CharacterEllipsis, calendarName.TextTrimming);

        var accountName = texts.First(t => t.DataContext is AccountGroup g && g.AccountName == FakeGoogle.Account && t.Text == FakeGoogle.Account);
        Assert.Equal(FakeGoogle.Account, ToolTip.GetTip(accountName));
        vm.DeleteLocalCalendar(id);
    });
}
