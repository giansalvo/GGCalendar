using System;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using GGCalendar.Core.Reminders;

namespace GGCalendar.App.Views;

/// <summary>
/// A time of day written by hand to the minute ("9:03", "903"); the arrows and the Up and Down keys move it by 5 minutes
/// (see <see cref="TimeEntry"/>). A text that is not a time goes back to the last valid one.
/// </summary>
public sealed class TimeBox : UserControl
{
    public static readonly StyledProperty<TimeSpan?> ValueProperty =
        AvaloniaProperty.Register<TimeBox, TimeSpan?>(nameof(Value), defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<bool> Use24HourProperty =
        AvaloniaProperty.Register<TimeBox, bool>(nameof(Use24Hour), true);

    private readonly TextBox _box;

    public TimeSpan? Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public bool Use24Hour { get => GetValue(Use24HourProperty); set => SetValue(Use24HourProperty, value); }

    /// <summary>The text box, for tests and for the automation id of the screen readers.</summary>
    public TextBox Box => _box;

    public TimeBox()
    {
        _box = new TextBox
        {
            Width = 112,
            TextAlignment = Avalonia.Media.TextAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            InnerRightContent = Arrows(),
        };
        _box.KeyDown += OnKeyDown;
        _box.LostFocus += (_, _) => Commit();
        _box.GotFocus += (_, _) => _box.SelectAll();
        Content = _box;
        ShowValue();
    }

    private StackPanel Arrows() => new()
    {
        Margin = new Thickness(0, 0, 2, 0),
        VerticalAlignment = VerticalAlignment.Center,
        Children = { Arrow("▲", 1), Arrow("▼", -1) },
    };

    private RepeatButton Arrow(string glyph, int direction)
    {
        var b = new RepeatButton
        {
            Content = glyph, FontSize = 8, Padding = new Thickness(6, 0), MinHeight = 0, MinWidth = 0, Height = 14,
            Background = Avalonia.Media.Brushes.Transparent, Focusable = false, Interval = 120, Delay = 400,
        };
        AutomationProperties.SetName(b, direction > 0 ? "+5" : "-5");
        b.Click += (_, _) => { Commit(); Step(direction); };
        return b;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ValueProperty || change.Property == Use24HourProperty) ShowValue();
    }

    private void ShowValue() => _box.Text = Value is { } v ? TimeEntry.Format(v, Use24Hour) : "";

    private void Commit()
    {
        if (TimeEntry.Parse(_box.Text, Use24Hour) is { } typed) { if (typed != Value) Value = typed; }
        ShowValue();
    }

    private void Step(int direction)
    {
        Value = TimeEntry.Step(Value ?? TimeSpan.Zero, direction);
        ShowValue();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Up: Commit(); Step(1); e.Handled = true; break;
            case Key.Down: Commit(); Step(-1); e.Handled = true; break;
            case Key.Enter: Commit(); _box.SelectAll(); e.Handled = true; break; // confirms the time only: the editor does not save
        }
    }
}
