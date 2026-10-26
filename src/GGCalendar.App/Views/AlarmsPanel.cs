using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using GGCalendar.App.ViewModels;
using GGCalendar.Core.Localization;
using GGCalendar.Core.Models;

namespace GGCalendar.App.Views;

/// <summary>
/// The window of the alarms (issue #18), opened by the button after the lens: the alarms that ring now ("Stanno suonando") and
/// the ones waiting to ring ("In arrivo"), with the time left. A row opens the event; the cross cancels the alarm (after a question).
/// </summary>
public sealed class AlarmsPanel : UserControl
{
    private static readonly IBrush Red = Brush.Parse("#D93025");
    private static readonly FontFamily Icons = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    private readonly AlarmsViewModel _model;
    private readonly StackPanel _rows = new();
    private readonly TextBlock _title = new() { FontSize = 18, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _stopAll = new() { Padding = new Thickness(12, 4) };
    private readonly TextBlock _note = new() { FontSize = 12, Opacity = 0.65, Margin = new Thickness(0, 10, 0, 0), TextWrapping = TextWrapping.Wrap };
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(15) };
    private readonly HashSet<string> _confirming = [];
    private bool _shown;

    /// <summary>A row was pressed: show the event (the window is closed by the caller).</summary>
    public event Action<CalendarEvent>? EventRequested;

    public AlarmsPanel(AlarmsViewModel model)
    {
        _model = model;
        Width = 440;
        AutomationProperties.SetAutomationId(_stopAll, "Alarms.StopAll");
        _stopAll.Click += (_, _) => _model.StopAllRinging();

        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 0, 0, 8) };
        header.Children.Add(_title);
        Grid.SetColumn(_stopAll, 1);
        header.Children.Add(_stopAll);

        var scroll = new ScrollViewer
        {
            MaxHeight = 480,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = _rows,
        };
        AutomationProperties.SetAutomationId(scroll, "Alarms.List");

        var panel = new StackPanel { Margin = new Thickness(4) };
        panel.Children.Add(header);
        panel.Children.Add(scroll);
        panel.Children.Add(new Separator { Margin = new Thickness(0, 8, 0, 0) });
        panel.Children.Add(_note);
        Content = panel;

        _timer.Tick += (_, _) => Rebuild();
        _model.Changed += OnChanged;
        AttachedToVisualTree += (_, _) => { _shown = true; Rebuild(); _timer.Start(); };
        DetachedFromVisualTree += (_, _) => { _shown = false; _timer.Stop(); _confirming.Clear(); };
        Rebuild();
    }

    private void OnChanged()
    {
        if (_shown) Rebuild();
    }

    /// <summary>Draws the list again (the list changed, the language changed, or the time left has to follow the clock).</summary>
    public void Rebuild()
    {
        _title.Text = L.T("alarms.title");
        _stopAll.Content = L.T("alarms.stop_all");
        _note.Text = "ⓘ " + L.T("alarms.only_open");
        var now = _model.Now;
        var rows = _model.Rows();
        var ringing = rows.Where(r => r.IsRinging).ToList();
        var coming = rows.Where(r => !r.IsRinging).ToList();
        _stopAll.IsVisible = ringing.Count > 0;
        _confirming.IntersectWith(coming.Select(c => c.Key));

        _rows.Children.Clear();
        if (ringing.Count > 0)
        {
            _rows.Children.Add(Section(L.T("alarms.section_ringing")));
            foreach (var r in ringing) _rows.Children.Add(Row(r, now));
        }

        if (coming.Count > 0)
        {
            _rows.Children.Add(Section(L.T("alarms.section_coming")));
            foreach (var r in coming) _rows.Children.Add(_confirming.Contains(r.Key) ? Confirm(r) : Row(r, now));
        }

        if (rows.Count == 0)
        {
            _rows.Children.Add(new TextBlock
            {
                Text = L.T("alarms.empty"),
                Opacity = 0.7,
                Margin = new Thickness(0, 24),
                HorizontalAlignment = HorizontalAlignment.Center,
            });
        }
    }

    private static TextBlock Section(string text) =>
        new() { Text = text, FontSize = 11, FontWeight = FontWeight.SemiBold, Opacity = 0.6, Margin = new Thickness(4, 10, 0, 4) };

    private Control Row(AlarmRow row, DateTime now)
    {
        var ringing = row.IsRinging;
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"), ColumnSpacing = 10 };

        var icon = new TextBlock
        {
            Text = ringing ? "" : "",
            FontFamily = Icons,
            FontSize = 18,
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (ringing) icon.Foreground = Red; // a null brush would make the glyph invisible
        grid.Children.Add(icon);

        var text = new StackPanel { Spacing = 1 };
        text.Children.Add(new TextBlock
        {
            Text = _model.WhenText(row),
            FontSize = 12,
            FontWeight = FontWeight.SemiBold,
            Foreground = ringing ? Red : Brush.Parse("#1A73E8"),
        });
        text.Children.Add(new TextBlock { Text = row.Title, FontWeight = FontWeight.Medium, TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis });
        text.Children.Add(new TextBlock { Text = row.Detail, FontSize = 12, Opacity = 0.7, TextWrapping = TextWrapping.Wrap });
        var calendar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        calendar.Children.Add(new Ellipse { Width = 9, Height = 9, Fill = Brush.Parse(row.ColorHex), VerticalAlignment = VerticalAlignment.Center });
        calendar.Children.Add(new TextBlock { Text = row.CalendarName, FontSize = 12, Opacity = 0.7, MaxWidth = 240, TextTrimming = TextTrimming.CharacterEllipsis });
        text.Children.Add(calendar);
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        if (!ringing)
        {
            var left = new TextBlock { Text = AlarmsViewModel.Remaining(row.FireAt - now), FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            AutomationProperties.SetAutomationId(left, "Alarms.Remaining");
            Grid.SetColumn(left, 2);
            grid.Children.Add(left);
        }

        var action = new Button
        {
            VerticalAlignment = VerticalAlignment.Center,
            Padding = ringing ? new Thickness(12, 4) : new Thickness(0),
            Width = ringing ? double.NaN : 32,
            Height = ringing ? double.NaN : 32,
            Background = ringing ? null : Brushes.Transparent,
            CornerRadius = new CornerRadius(16),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        if (ringing)
        {
            action.Content = L.T("reminder.turn_off");
            ToolTip.SetTip(action, L.T("alarms.stop_tip"));
            action.Click += (_, _) => _model.StopRinging(row);
        }
        else
        {
            action.Content = new TextBlock { Text = "", FontFamily = Icons, FontSize = 12 };
            ToolTip.SetTip(action, L.T("alarms.cancel_tip"));
            action.Click += (_, _) =>
            {
                _confirming.Add(row.Key);
                Rebuild();
            };
        }

        AutomationProperties.SetAutomationId(action, ringing ? "Alarms.Stop" : "Alarms.Cancel");
        Grid.SetColumn(action, 3);
        grid.Children.Add(action);

        var border = new Border
        {
            Padding = new Thickness(8),
            CornerRadius = new CornerRadius(8),
            Background = ringing ? Brush.Parse("#14D93025") : Brushes.Transparent,
            Child = grid,
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        ToolTip.SetTip(border, row.Title);
        border.PointerEntered += (_, _) => { if (!ringing) border.Background = Brush.Parse("#14888888"); };
        border.PointerExited += (_, _) => { if (!ringing) border.Background = Brushes.Transparent; };
        border.PointerReleased += (_, e) =>
        {
            if (e.Handled || e.InitialPressMouseButton != MouseButton.Left) return;
            EventRequested?.Invoke(row.Event);
        };
        AutomationProperties.SetAutomationId(border, "Alarms.Row");
        return border;
    }

    private Control Confirm(AlarmRow row)
    {
        var keep = new Button { Content = L.T("alarms.keep") };
        var cancel = new Button { Content = L.T("alarms.cancel") };
        cancel.Classes.Add("accent");
        AutomationProperties.SetAutomationId(cancel, "Alarms.CancelConfirm");
        AutomationProperties.SetAutomationId(keep, "Alarms.Keep");
        keep.Click += (_, _) =>
        {
            _confirming.Remove(row.Key);
            Rebuild();
        };
        cancel.Click += (_, _) =>
        {
            _confirming.Remove(row.Key);
            _model.Cancel(row);
        };

        var box = new StackPanel { Spacing = 8, Margin = new Thickness(8) };
        box.Children.Add(new TextBlock { Text = L.T("alarms.confirm_title"), FontWeight = FontWeight.SemiBold });
        box.Children.Add(new TextBlock { Text = L.T("alarms.confirm_text", row.Title), TextWrapping = TextWrapping.Wrap });
        box.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { keep, cancel } });
        var border = new Border { Background = Brush.Parse("#14888888"), CornerRadius = new CornerRadius(8), Child = box };
        AutomationProperties.SetAutomationId(border, "Alarms.Confirm");
        return border;
    }
}
