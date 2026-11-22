using GGCalendar.App.ViewModels;
using GGCalendar.Core.Models;
using GGCalendar.Core.Ics;
using GGCalendar.Core.Tests.Support;
using GGCalendar.Data;

namespace GGCalendar.Core.Tests;

/// <summary>
/// T402-T411: visibility and status given to the events created in the app, chosen in the settings (issue #89). Without a choice (a settings file
/// written before the option existed) a new event is private and busy; "default" (the visibility of the calendar on Google) is not a choice.
/// </summary>
public class EventDefaultsTests
{
    private static readonly DateTime Start = new(2026, 11, 22, 10, 0, 0);

    private static CalendarItemViewModel Calendar() => new(new CalendarInfo("c1", "Locale", "Personale", "#1A73E8"), () => { });

    private static CalendarEvent Event(EventVisibility visibility, EventAvailability availability = EventAvailability.Busy) =>
        new("e1", "c1", "Prova", Start, Start.AddHours(1)) { Visibility = visibility, Availability = availability, Reminders = [] };

    private static string TempFile(string extension) => Path.Combine(Path.GetTempPath(), "ggcal-defaults-" + Guid.NewGuid().ToString("N") + extension);

    private static void WithStore(Action<CalendarStore, string> test)
    {
        var path = TempFile(".db");
        try { test(new CalendarStore(path), path); }
        finally { try { File.Delete(path); } catch { } }
    }

    // ---- T402-T403: what is stored ----

    [Fact]
    [Trait("TestCase", "T402")]
    public void WithoutAChoiceANewEventIsPrivateAndBusy()
    {
        var settings = new AppSettings();
        Assert.Equal(EventVisibility.Private, settings.DefaultVisibility);
        Assert.Equal(EventAvailability.Busy, settings.DefaultAvailability);

        // a settings file written before the options existed gives the same values
        var file = TempFile(".json");
        try
        {
            File.WriteAllText(file, "{\"Theme\":1}");
            var old = new SettingsStore(file).Load();
            Assert.Equal(EventVisibility.Private, old.DefaultVisibility);
            Assert.Equal(EventAvailability.Busy, old.DefaultAvailability);
        }
        finally { File.Delete(file); }
    }

    [Fact]
    [Trait("TestCase", "T403")]
    public void TheChoicesAreKeptBetweenStartsAsText()
    {
        var file = TempFile(".json");
        try
        {
            var store = new SettingsStore(file);
            var settings = store.Load();
            settings.DefaultVisibility = EventVisibility.Public;
            settings.DefaultAvailability = EventAvailability.Free;
            store.Save(settings);

            var again = new SettingsStore(file).Load();
            Assert.Equal(EventVisibility.Public, again.DefaultVisibility);
            Assert.Equal(EventAvailability.Free, again.DefaultAvailability);
            var text = File.ReadAllText(file);
            Assert.Contains("\"DefaultVisibility\": \"Public\"", text);
            Assert.Contains("\"DefaultAvailability\": \"Free\"", text);
        }
        finally { File.Delete(file); }
    }

    // ---- T404-T406: the new event ----

    private static CalendarEvent NewEventWith(AppSettings settings)
    {
        CalendarEvent? created = null;
        WithStore((store, _) =>
        {
            store.UpsertCalendar(new CalendarInfo("c1", "Locale", "Personale", "#1A73E8"));
            var vm = new MainViewModel(store) { Settings = settings };
            created = vm.NewEvent(Start, Start.AddHours(1));
        });
        return created!;
    }

    [Fact]
    [Trait("TestCase", "T404")]
    public Task ANewEventWithTheDefaultSettingsIsPrivateAndBusy() => HeadlessApp.RunAsync(() =>
    {
        var ev = NewEventWith(new AppSettings());
        Assert.Equal(EventVisibility.Private, ev.Visibility);
        Assert.Equal(EventAvailability.Busy, ev.Availability);
        return Task.CompletedTask;
    });

    [Fact]
    [Trait("TestCase", "T405")]
    public Task ANewEventTakesTheVisibilityAndTheStatusOfTheSettings() => HeadlessApp.RunAsync(() =>
    {
        var ev = NewEventWith(new AppSettings { DefaultVisibility = EventVisibility.Public, DefaultAvailability = EventAvailability.Free });
        Assert.Equal(EventVisibility.Public, ev.Visibility);
        Assert.Equal(EventAvailability.Free, ev.Availability);
        return Task.CompletedTask;
    });

    [Fact]
    [Trait("TestCase", "T406")]
    public Task AFileEditedByHandWithTheCalendarVisibilityGivesPrivate() => HeadlessApp.RunAsync(() =>
    {
        var ev = NewEventWith(new AppSettings { DefaultVisibility = EventVisibility.Default });
        Assert.Equal(EventVisibility.Private, ev.Visibility);
        return Task.CompletedTask;
    });

    // ---- T407-T409: the editor of the event ----

    private static EventEditViewModel Editor(CalendarEvent ev, bool isNew) =>
        new(ev, [Calendar()], isNew, new AppSettings());

    [Fact]
    [Trait("TestCase", "T407")]
    public Task TheEditorOfANewEventOffersPublicAndPrivateAndSelectsTheDefault() => HeadlessApp.RunAsync(() =>
    {
        var vm = Editor(Event(EventVisibility.Private), isNew: true);
        Assert.Equal([EventVisibility.Public, EventVisibility.Private], vm.VisibilityOptions.Select(o => o.Value).ToArray());
        Assert.Equal(EventVisibility.Private, vm.Visibility.Value);

        vm = Editor(Event(EventVisibility.Public, EventAvailability.Free), isNew: true);
        Assert.Equal(EventVisibility.Public, vm.Visibility.Value);
        Assert.Equal(EventAvailability.Free, vm.Availability.Value);
        return Task.CompletedTask;
    });

    [Theory]
    [Trait("TestCase", "T408")]
    [InlineData(EventVisibility.Public)]
    [InlineData(EventVisibility.Private)]
    public Task TheEditorOfAnExistingEventShowsItsOwnVisibility(EventVisibility visibility) => HeadlessApp.RunAsync(() =>
    {
        var vm = Editor(Event(visibility), isNew: false);
        Assert.Equal(visibility, vm.Visibility.Value);
        Assert.DoesNotContain(vm.VisibilityOptions, o => o.Value == EventVisibility.Default);
        return Task.CompletedTask;
    });

    [Fact]
    [Trait("TestCase", "T409")]
    public Task AnEventWithTheVisibilityOfGoogleKeepsItWhenSaved() => HeadlessApp.RunAsync(() =>
    {
        var vm = Editor(Event(EventVisibility.Default), isNew: false);
        Assert.Equal(EventVisibility.Default, vm.Visibility.Value);
        Assert.Contains(vm.VisibilityOptions, o => o.Value == EventVisibility.Default);
        Assert.Contains("Google", vm.Visibility.Label); // it must not look like the default of the app
        return Task.CompletedTask;
    });

    // ---- T410: the events that were not created in the app ----

    [Fact]
    [Trait("TestCase", "T410")]
    public Task TheEventsNotCreatedInTheAppAreNotTouched() => HeadlessApp.RunAsync(() =>
    {
        // an event without a visibility (from an .ics file, from Google) stays "default"
        var imported = IcsParser.Parse("BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nUID:u1\r\nDTSTART:20261122T100000\r\nDTEND:20261122T110000\r\nSUMMARY:Importato\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n", "c1").Single();
        Assert.Equal(EventVisibility.Default, imported.Visibility);

        WithStore((store, path) =>
        {
            store.UpsertCalendar(new CalendarInfo("c1", "Locale", "Personale", "#1A73E8"));
            store.UpsertEvent(Event(EventVisibility.Default, EventAvailability.Free));
            var vm = new MainViewModel(store) { Settings = new AppSettings { DefaultVisibility = EventVisibility.Private, DefaultAvailability = EventAvailability.Busy } };
            vm.NewEvent(Start, Start.AddHours(1));

            var stored = new CalendarStore(path).GetAllEvents().Single();
            Assert.Equal(EventVisibility.Default, stored.Visibility);
            Assert.Equal(EventAvailability.Free, stored.Availability);
        });
        return Task.CompletedTask;
    });

    // ---- T411: the settings window ----

    [Fact]
    [Trait("TestCase", "T411")]
    public Task TheSettingsWindowReadsAndWritesTheTwoChoices() => HeadlessApp.RunAsync(() =>
    {
        var vm = new SettingsViewModel(new AppSettings());
        Assert.Equal(EventVisibility.Private, vm.DefaultVisibility.Value);
        Assert.Equal(EventAvailability.Busy, vm.DefaultAvailability.Value);
        Assert.Equal([EventVisibility.Public, EventVisibility.Private], vm.VisibilityChoices.Select(o => o.Value).ToArray());

        vm.DefaultVisibility = vm.VisibilityChoices.First(o => o.Value == EventVisibility.Public);
        vm.DefaultAvailability = vm.AvailabilityChoices.First(o => o.Value == EventAvailability.Free);
        var built = vm.TryBuild()!;
        Assert.Equal(EventVisibility.Public, built.DefaultVisibility);
        Assert.Equal(EventAvailability.Free, built.DefaultAvailability);

        // a file edited by hand with "default" shows private
        Assert.Equal(EventVisibility.Private, new SettingsViewModel(new AppSettings { DefaultVisibility = EventVisibility.Default }).DefaultVisibility.Value);
        return Task.CompletedTask;
    });
}
