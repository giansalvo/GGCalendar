using System;
using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using GGCalendar.Core.Localization;

namespace GGCalendar.App.Views;

/// <summary>"Go to a date" (issue #102): the date is written or chosen in the calendar; closes with it, or with null.</summary>
public sealed class GoToDateWindow : Window
{
    private const int FirstYear = 1900, LastYear = 2200;

    private readonly TextBox _date = new() { Name = "DateBox" };
    private readonly TextBlock _error = new() { Foreground = new SolidColorBrush(Color.Parse("#D93025")), TextWrapping = TextWrapping.Wrap, IsVisible = false };

    public GoToDateWindow() : this(DateTime.Today) { }

    public GoToDateWindow(DateTime initial)
    {
        Title = L.T("goto.title");
        Width = 360;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _date.Text = initial.ToString("d", L.Culture);
        var calendar = new Avalonia.Controls.Calendar { SelectedDate = initial.Date, DisplayDate = initial.Date, HorizontalAlignment = HorizontalAlignment.Center };
        calendar.SelectedDatesChanged += (_, _) =>
        {
            if (calendar.SelectedDate is not { } day) return;
            _date.Text = day.ToString("d", L.Culture);
            _error.IsVisible = false;
        };

        var ok = new Button { Content = L.T("common.ok"), Classes = { "accent" }, IsDefault = true };
        var cancel = new Button { Content = L.T("common.cancel"), IsCancel = true };
        AutomationProperties.SetAutomationId(ok, "Dialog.Ok");
        AutomationProperties.SetAutomationId(cancel, "Dialog.Cancel");
        ok.Click += (_, _) => Accept();
        cancel.Click += (_, _) => Close((DateTime?)null);

        var panel = new StackPanel { Margin = new Thickness(24), Spacing = 12 };
        panel.Children.Add(_date);
        panel.Children.Add(_error);
        panel.Children.Add(calendar);
        panel.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { cancel, ok } });
        Content = panel;
        Opened += (_, _) =>
        {
            _date.Focus();
            _date.SelectAll();
        };
    }

    /// <summary>A date written as in the language shown ("15/3" is in the current year), between 1900 and 2200.</summary>
    public static DateTime? Parse(string? text, CultureInfo culture) =>
        DateTime.TryParse(text, culture, DateTimeStyles.AllowWhiteSpaces, out var date) && date.Year is >= FirstYear and <= LastYear ? date.Date : null;

    private void Accept()
    {
        if (Parse(_date.Text, L.Culture) is { } day)
        {
            Close((DateTime?)day);
            return;
        }

        _error.Text = L.T("goto.error", new DateTime(DateTime.Today.Year, 12, 31).ToString("d", L.Culture));
        _error.IsVisible = true;
    }
}
