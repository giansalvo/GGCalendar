using Avalonia.Input;
using GGCalendar.App.Views;
using static GGCalendar.App.Views.ShortcutCommand;

namespace GGCalendar.Core.Tests;

/// <summary>The table of the keyboard shortcuts (issue #102, first phase), without a window.</summary>
public class ShortcutTests
{
    [Theory]
    [Trait("TestCase", "T426")]
    [InlineData(Key.T, KeyModifiers.None, Today)]
    [InlineData(Key.Left, KeyModifiers.None, Previous)]
    [InlineData(Key.K, KeyModifiers.None, Previous)]
    [InlineData(Key.P, KeyModifiers.None, Previous)]
    [InlineData(Key.Right, KeyModifiers.None, Next)]
    [InlineData(Key.J, KeyModifiers.None, Next)]
    [InlineData(Key.N, KeyModifiers.None, Next)]
    [InlineData(Key.C, KeyModifiers.None, NewEvent)]
    [InlineData(Key.N, KeyModifiers.Control, NewEvent)]
    [InlineData(Key.R, KeyModifiers.None, Sync)]
    [InlineData(Key.F5, KeyModifiers.None, Sync)]
    [InlineData(Key.S, KeyModifiers.None, Settings)]
    [InlineData(Key.OemComma, KeyModifiers.Control, Settings)]
    [InlineData(Key.F, KeyModifiers.Control, ShortcutCommand.Search)]
    [InlineData(Key.W, KeyModifiers.Shift, Weekends)]
    [InlineData(Key.A, KeyModifiers.Control | KeyModifiers.Shift, Alarms)]
    [InlineData(Key.F1, KeyModifiers.None, Help)]
    [InlineData(Key.I, KeyModifiers.None, About)]
    [InlineData(Key.F6, KeyModifiers.None, NextArea)]
    [InlineData(Key.F6, KeyModifiers.Shift, PreviousArea)]
    public void EachKeyHasItsCommand(Key key, KeyModifiers modifiers, ShortcutCommand command) =>
        Assert.Equal(new Shortcut(command), Shortcuts.Map(key, modifiers, null));

    [Theory]
    [Trait("TestCase", "T426")]
    [InlineData(Key.D, Key.D1, Key.NumPad1, 0)] // day
    [InlineData(Key.W, Key.D2, Key.NumPad2, 1)] // week
    [InlineData(Key.M, Key.D3, Key.NumPad3, 2)] // month
    [InlineData(Key.X, Key.D4, Key.NumPad4, 5)] // 6 days
    [InlineData(Key.A, Key.D5, Key.NumPad5, 4)] // schedule
    [InlineData(Key.Y, Key.D6, Key.NumPad6, 3)] // year
    public void ALetterAndANumberChooseEachView(Key letter, Key digit, Key pad, int view)
    {
        foreach (var key in new[] { letter, digit, pad })
            Assert.Equal(new Shortcut(ShortcutCommand.View, view), Shortcuts.Map(key, KeyModifiers.None, null));
    }

    [Theory]
    [Trait("TestCase", "T427")]
    [InlineData(Key.D7, KeyModifiers.Shift)]  // "/" on an Italian keyboard
    [InlineData(Key.OemQuestion, KeyModifiers.None)] // "/" on a US keyboard
    [InlineData(Key.Divide, KeyModifiers.None)]
    public void TheSlashIsKnownByWhatItWrites(Key key, KeyModifiers modifiers) =>
        Assert.Equal(new Shortcut(ShortcutCommand.Search), Shortcuts.Map(key, modifiers, "/"));

    [Theory]
    [Trait("TestCase", "T427")]
    [InlineData(Key.D7, KeyModifiers.None, "7")]
    [InlineData(Key.D7, KeyModifiers.Shift, "&")]
    [InlineData(Key.OemQuestion, KeyModifiers.None, "ù")]
    [InlineData(Key.D7, KeyModifiers.Control | KeyModifiers.Alt, "/")]
    [InlineData(Key.T, KeyModifiers.Control, null)]
    [InlineData(Key.C, KeyModifiers.Control, null)]
    [InlineData(Key.C, KeyModifiers.Shift, null)]
    [InlineData(Key.Up, KeyModifiers.Control, null)] // the small calendar uses Ctrl and the arrows up and down
    [InlineData(Key.Down, KeyModifiers.Shift, null)]
    [InlineData(Key.Left, KeyModifiers.Alt, null)]
    [InlineData(Key.D1, KeyModifiers.Shift, null)]
    [InlineData(Key.N, KeyModifiers.Control | KeyModifiers.Alt, null)] // AltGr writes characters
    [InlineData(Key.Space, KeyModifiers.None, " ")]
    [InlineData(Key.Enter, KeyModifiers.None, null)]
    [InlineData(Key.Back, KeyModifiers.None, null)]
    public void OtherKeysDoNothing(Key key, KeyModifiers modifiers, string? symbol) => Assert.Null(Shortcuts.Map(key, modifiers, symbol));

    [Theory]
    [Trait("TestCase", "T449")]
    [InlineData(Key.OemQuestion, KeyModifiers.Shift)] // "?" on a US keyboard
    [InlineData(Key.OemOpenBrackets, KeyModifiers.Shift)] // on an Italian keyboard
    public void TheQuestionMarkIsKnownByWhatItWrites(Key key, KeyModifiers modifiers) =>
        Assert.Equal(new Shortcut(Help), Shortcuts.Map(key, modifiers, "?"));

    [Theory]
    [Trait("TestCase", "T450")]
    [InlineData(Key.G, KeyModifiers.None, GoToDate)]
    [InlineData(Key.G, KeyModifiers.Control, GoToDate)]
    [InlineData(Key.Left, KeyModifiers.Shift, PreviousDay)]
    [InlineData(Key.Right, KeyModifiers.Shift, NextDay)]
    [InlineData(Key.Left, KeyModifiers.Control, JumpBack)]
    [InlineData(Key.Right, KeyModifiers.Control, JumpForward)]
    [InlineData(Key.Up, KeyModifiers.None, LineUp)]
    [InlineData(Key.Down, KeyModifiers.None, LineDown)]
    [InlineData(Key.PageUp, KeyModifiers.None, ShortcutCommand.PageUp)]
    [InlineData(Key.PageDown, KeyModifiers.None, ShortcutCommand.PageDown)]
    [InlineData(Key.Home, KeyModifiers.None, Start)]
    [InlineData(Key.End, KeyModifiers.None, End)]
    [InlineData(Key.Home, KeyModifiers.Control, Top)]
    [InlineData(Key.End, KeyModifiers.Control, Bottom)]
    public void TheKeysOfTheSecondPhaseHaveTheirCommands(Key key, KeyModifiers modifiers, ShortcutCommand command)
    {
        Assert.Equal(new Shortcut(command), Shortcuts.Map(key, modifiers, null));
        Assert.Equal(command >= LineUp, Shortcuts.IsScroll(command));
    }

    [Theory]
    [Trait("TestCase", "T451")]
    [InlineData("it", "20/05/2027", "2027-05-20")]
    [InlineData("it", " 3/1/2030 ", "2030-01-03")]
    [InlineData("en-US", "5/20/2027", "2027-05-20")]
    [InlineData("de", "20.05.2027", "2027-05-20")]
    [InlineData("it", "31/02/2027", null)]
    [InlineData("it", "domani", null)]
    [InlineData("it", "", null)]
    [InlineData("it", "01/01/1899", null)]
    [InlineData("it", "01/01/2201", null)]
    public void ADateIsReadAsWrittenInTheLanguage(string culture, string text, string? expected) =>
        Assert.Equal(expected is null ? null : Time(expected + " 00:00"), GoToDateWindow.Parse(text, System.Globalization.CultureInfo.GetCultureInfo(culture)));

    private static DateTime Time(string text) => DateTime.ParseExact(text, "yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture);

    [Theory]
    [Trait("TestCase", "T428")]
    [InlineData("2026-11-26 00:00", "2026-11-26 10:20", 8, "2026-11-26 11:00")] // today: the next full hour
    [InlineData("2026-11-26 15:45", "2026-11-26 10:00", 8, "2026-11-26 11:00")] // the hour of the day chosen does not count
    [InlineData("2026-11-26 00:00", "2026-11-26 05:10", 8, "2026-11-26 08:00")] // today, before the start of the day
    [InlineData("2026-11-26 00:00", "2026-11-26 23:30", 8, "2026-11-26 08:00")] // no next hour today
    [InlineData("2026-12-03 00:00", "2026-11-26 10:20", 8, "2026-12-03 08:00")] // another day: its start
    [InlineData("2026-11-20 00:00", "2026-11-26 10:20", 9, "2026-11-20 09:00")] // also in the past
    public void ANewEventStartsOnTheDayChosen(string day, string now, int dayStart, string expected) =>
        Assert.Equal(Time(expected), Shortcuts.NewEventStart(Time(day), Time(now), dayStart));
}
