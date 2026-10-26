using System;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using GGCalendar.Core.Localization;
using GGCalendar.Core.Models;

namespace GGCalendar.App.Views;

/// <summary>What the window gives back: the name (as written) and the colour chosen.</summary>
public sealed record CalendarEditRequest(string Name, string ColorHex);

/// <summary>
/// Changes the name and the colour of a calendar. The name is a grey field with the reason under it when it cannot be changed (the primary calendar of
/// Google, one the person does not own); the colour always can. The row of colours has the 8 quick ones; "More colours…" opens the 24 of Google in
/// another window. "Save" is off while nothing has changed or the name is empty. Nothing is saved here: the caller does it.
/// </summary>
public sealed class CalendarEditWindow : Window
{
    private readonly string _name;
    private readonly string _originalColor;
    private readonly bool _canRename;
    private readonly TextBox _nameBox = new() { MaxLength = 255 };
    private readonly Button _save = new() { Content = L.T("common.save"), IsEnabled = false, Classes = { "accent" } };
    private readonly Button _cancel = new() { Content = L.T("common.cancel"), IsCancel = true };
    private readonly Button _more = new() { Content = L.T("cal.more_colors") };
    private readonly WrapPanel _palette = new() { Orientation = Orientation.Horizontal };
    private string _color;

    /// <param name="name">The name as shown now.</param>
    /// <param name="hint">Why the name cannot be changed (empty when it can).</param>
    /// <param name="note">A remark under the name that can be changed (for a calendar of Google: everybody it is shared with sees the new name).</param>
    public CalendarEditWindow(string name, string colorHex, bool canRename, string hint, string note)
    {
        _name = name;
        _canRename = canRename;
        _color = CalendarColors.Normalize(colorHex) ?? colorHex;
        _originalColor = _color;
        Title = L.T("cal.edit_title");
        Width = 460;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        AutomationProperties.SetAutomationId(_nameBox, "Cal.Name");
        AutomationProperties.SetAutomationId(_save, "Cal.Save");
        AutomationProperties.SetAutomationId(_cancel, "Dialog.Cancel");
        AutomationProperties.SetAutomationId(_more, "Cal.MoreColors");
        AutomationProperties.SetAutomationId(_palette, "Cal.Palette");

        _nameBox.Text = name;
        _nameBox.IsEnabled = canRename;
        var nameBlock = new StackPanel { Spacing = 4 };
        nameBlock.Children.Add(new TextBlock { Text = L.T("gcal.name_label"), FontWeight = FontWeight.SemiBold });
        nameBlock.Children.Add(_nameBox);
        var remark = canRename ? note : hint;
        if (remark.Length > 0)
        {
            var text = new TextBlock { Text = remark, TextWrapping = TextWrapping.Wrap, Opacity = 0.7, FontSize = 12 };
            AutomationProperties.SetAutomationId(text, "Cal.NameNote");
            nameBlock.Children.Add(text);
        }

        var colorBlock = new StackPanel { Spacing = 4 };
        colorBlock.Children.Add(new TextBlock { Text = L.T("sub.color"), FontWeight = FontWeight.SemiBold });
        colorBlock.Children.Add(_palette);
        colorBlock.Children.Add(_more);
        ShowPalette();

        var root = new StackPanel { Margin = new Thickness(24), Spacing = 12 };
        root.Children.Add(nameBlock);
        root.Children.Add(colorBlock);
        root.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { _cancel, _save } });
        Content = root;

        _nameBox.TextChanged += (_, _) => Update();
        _nameBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; Accept(); } };
        _more.Click += async (_, _) =>
        {
            if (await new CalendarColorsWindow(_color).ShowDialog<string?>(this) is { } picked)
            {
                _color = picked;
                ShowPalette();
                Update();
            }
        };
        _save.Click += (_, _) => Accept();
        _cancel.Click += (_, _) => Close(null);
        Opened += (_, _) => { if (_canRename) _nameBox.Focus(); else _save.Focus(); };
    }

    /// <summary>The 8 quick colours, the colour the calendar has when it is not one of them, and the one chosen from the 24 when it is another.</summary>
    private void ShowPalette()
    {
        _palette.Children.Clear();
        var shown = CalendarColors.Quick.ToList();
        // The colour the calendar has stays in the row (so that it can be taken back), then the one chosen from the 24, if it is another one.
        foreach (var extra in new[] { _originalColor, _color })
            if (!shown.Contains(extra, StringComparer.OrdinalIgnoreCase)) shown.Add(extra);
        for (var i = 0; i < shown.Count; i++)
        {
            var hex = shown[i];
            var on = string.Equals(hex, _color, StringComparison.OrdinalIgnoreCase);
            var b = new Button
            {
                Width = 28, Height = 28, CornerRadius = new CornerRadius(14), Padding = new Thickness(0), Margin = new Thickness(0, 0, 6, 6),
                Background = new SolidColorBrush(Color.Parse(hex)), Tag = hex,
                BorderThickness = new Thickness(on ? 3 : 1), BorderBrush = on ? Brushes.White : new SolidColorBrush(Color.Parse("#55808080")),
            };
            AutomationProperties.SetAutomationId(b, "Cal.Color." + i);
            b.Click += (_, _) => { _color = hex; ShowPalette(); Update(); };
            _palette.Children.Add(b);
        }
    }

    private bool Changed => (_canRename && _nameBox.Text?.Trim() != _name) || !string.Equals(_color, _originalColor, StringComparison.OrdinalIgnoreCase);

    private void Update() => _save.IsEnabled = Changed && (!_canRename || !string.IsNullOrWhiteSpace(_nameBox.Text));

    private void Accept()
    {
        if (!_save.IsEnabled) return;
        Close(new CalendarEditRequest(_canRename ? _nameBox.Text!.Trim() : _name, _color));
    }
}

/// <summary>The 24 colours of Google, to pick one that the row of quick colours does not have. Gives back the colour (as "#RRGGBB") or null.</summary>
public sealed class CalendarColorsWindow : Window
{
    public CalendarColorsWindow(string current)
    {
        Title = L.T("cal.colors_title");
        SizeToContent = SizeToContent.WidthAndHeight;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var grid = new UniformGrid { Columns = 6 };
        AutomationProperties.SetAutomationId(grid, "Colors.Grid");
        for (var i = 0; i < CalendarColors.All.Count; i++)
        {
            var hex = CalendarColors.All[i];
            var on = string.Equals(hex, current, StringComparison.OrdinalIgnoreCase);
            var b = new Button
            {
                Width = 36, Height = 36, CornerRadius = new CornerRadius(18), Padding = new Thickness(0), Margin = new Thickness(4),
                Background = new SolidColorBrush(Color.Parse(hex)), Tag = hex,
                BorderThickness = new Thickness(on ? 3 : 1), BorderBrush = on ? Brushes.White : new SolidColorBrush(Color.Parse("#55808080")),
            };
            AutomationProperties.SetAutomationId(b, "Colors." + (i + 1));
            b.Click += (_, _) => Close(hex);
            grid.Children.Add(b);
        }

        var cancel = new Button { Content = L.T("common.cancel"), IsCancel = true, HorizontalAlignment = HorizontalAlignment.Right };
        AutomationProperties.SetAutomationId(cancel, "Dialog.Cancel");
        cancel.Click += (_, _) => Close(null);
        Content = new StackPanel { Margin = new Thickness(20), Spacing = 12, Children = { grid, cancel } };
    }
}
