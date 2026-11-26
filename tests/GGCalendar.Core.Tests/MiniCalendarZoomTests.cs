using Avalonia.Controls;
using GGCalendar.App.ViewModels;
using GGCalendar.Data;

namespace GGCalendar.Core.Tests;

/// <summary>The rules of the small calendar with the month and year views (issue #103), without a window.</summary>
[Collection(Support.AppDriver.CollectionName)]
public class MiniCalendarZoomTests
{
    private static DateTime Date(string text) => DateTime.ParseExact(text, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    [Theory]
    [Trait("TestCase", "T412")]
    [InlineData(0, CalendarMode.Month)]
    [InlineData(1, CalendarMode.Month)]
    [InlineData(2, CalendarMode.Year)]
    [InlineData(3, CalendarMode.Decade)]
    [InlineData(4, CalendarMode.Month)]
    [InlineData(5, CalendarMode.Month)]
    public void EachViewHasItsRestingMode(int view, CalendarMode mode) => Assert.Equal(mode, MiniCalendarZoom.RestingMode(view));

    [Theory]
    [Trait("TestCase", "T413")]
    [InlineData("2026-05-20", "2027-03-01", CalendarMode.Year, "2027-03-20")]   // the month and its year, the day kept
    [InlineData("2026-01-31", "2026-02-01", CalendarMode.Year, "2026-02-28")]   // a shorter month
    [InlineData("2026-01-31", "2028-02-01", CalendarMode.Year, "2028-02-29")]   // a leap February
    [InlineData("2026-05-31", "2026-05-01", CalendarMode.Year, "2026-05-31")]   // the month already shown
    [InlineData("2026-05-20", "2031-01-01", CalendarMode.Decade, "2031-05-20")] // the year; month and day kept
    [InlineData("2024-02-29", "2027-01-01", CalendarMode.Decade, "2027-02-28")] // 29 February in a common year
    [InlineData("2024-02-29", "2028-01-01", CalendarMode.Decade, "2028-02-29")]
    [InlineData("2026-05-20", "2026-07-04", CalendarMode.Month, "2026-07-04")]  // days: the day chosen
    public void AChoiceKeepsWhatItDoesNotSay(string selected, string shown, CalendarMode resting, string expected) =>
        Assert.Equal(Date(expected), MiniCalendarZoom.Chosen(Date(selected), Date(shown), resting));

    [Theory]
    [Trait("TestCase", "T414")]
    [InlineData(2, "2026-02-01", "2026-02-28")]
    [InlineData(3, "2030-01-01", "2030-01-31")]
    public void AChoiceSelectsTheDayAndShowsItAgainWhenItDoesNotChange(int view, string shown, string expected)
    {
        var vm = new MainViewModel(new CalendarStore(":memory:")) { ViewModeIndex = view, SelectedDate = Date("2026-01-31") };
        var days = new List<DateTime>();
        vm.DayShown += days.Add;

        vm.ChooseInMiniCalendar(Date(shown));
        Assert.Equal(Date(expected), vm.SelectedDate);
        Assert.Equal([Date(expected)], days);

        vm.ChooseInMiniCalendar(Date(shown)); // the same again: nothing changes, but the small calendar must go back
        Assert.Equal(Date(expected), vm.SelectedDate);
        Assert.Equal([Date(expected), Date(expected)], days);
    }

    [Fact]
    [Trait("TestCase", "T415")]
    public void AChangeOfViewAsksToShowTheSelectedDay()
    {
        var vm = new MainViewModel(new CalendarStore(":memory:")) { ViewModeIndex = 1, SelectedDate = Date("2026-05-20") };
        var days = new List<DateTime>();
        vm.DayShown += days.Add;

        vm.ViewModeIndex = 2;
        vm.ViewModeIndex = 3;
        Assert.Equal([Date("2026-05-20"), Date("2026-05-20")], days);
    }
}
