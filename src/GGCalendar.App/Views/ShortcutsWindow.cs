using System.Collections.Generic;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using GGCalendar.Core.Localization;

namespace GGCalendar.App.Views;

/// <summary>Modal list of the keyboard shortcuts (issue #102), opened with F1 or "?".</summary>
public sealed class ShortcutsWindow : Window
{
    /// <summary>What each key does, section by section, in the language shown now.</summary>
    public static IReadOnlyList<(string Title, IReadOnlyList<(string Keys, string Action)> Rows)> Sections()
    {
        var ctrl = L.T("key.ctrl") + "+";
        var shift = L.T("key.shift") + "+";
        var alt = L.T("key.alt") + "+";
        var enter = L.T("key.enter");
        var escape = L.T("key.escape");
        return
        [
            (L.T("shortcuts.section_general"),
            [
                ("T", L.T("shortcuts.today")),
                ("←  /  K  /  P", L.T("shortcuts.previous")),
                ("→  /  J  /  N", L.T("shortcuts.next")),
                (shift + "← →", L.T("shortcuts.day")),
                (ctrl + "← →", L.T("shortcuts.jump")),
                ("G  /  " + ctrl + "G", L.T("goto.title")),
                ("↑  /  ↓", L.T("shortcuts.scroll")),
                (L.T("key.pageup") + "  /  " + L.T("key.pagedown"), L.T("shortcuts.page")),
                (L.T("key.home") + "  /  " + L.T("key.end"), L.T("shortcuts.start_end")),
                (ctrl + L.T("key.home") + "  /  " + L.T("key.end"), L.T("shortcuts.top_bottom")),
                ("C  /  " + ctrl + "N", L.T("shortcuts.new_event")),
                ("/  /  " + ctrl + "F", L.T("shortcuts.search")),
                ("R  /  F5", L.T("main.sync_now")),
                ("S  /  " + ctrl + ",", L.T("main.settings")),
                (ctrl + shift + "A", L.T("alarms.tip")),
                (shift + "W", L.T("main.show_weekends")),
                ("F6", L.T("shortcuts.next_area")),
                (shift + "F6", L.T("shortcuts.previous_area")),
                ("F1  /  ?", L.T("shortcuts.help")),
                ("I", L.T("main.about")),
                (escape, L.T("shortcuts.dismiss")),
            ]),
            (L.T("shortcuts.section_views"),
            [
                ("D  /  1", L.T("main.view_day")),
                ("W  /  2", L.T("main.view_week")),
                ("M  /  3", L.T("main.view_month")),
                ("X  /  4", L.T("main.view_6days")),
                ("A  /  5", L.T("main.view_agenda")),
                ("Y  /  6", L.T("main.view_year")),
            ]),
            (L.T("shortcuts.section_events"),
            [
                (L.T("key.tab") + "  /  " + shift + L.T("key.tab"), L.T("shortcuts.next_event")),
                (L.T("key.space"), L.T("shortcuts.card")),
                (enter + "  /  E", L.T("shortcuts.open_event")),
                (L.T("key.delete") + "  /  " + L.T("key.backspace"), L.T("shortcuts.delete_event")),
                (alt + "↑ ↓", L.T("shortcuts.move_time")),
                (alt + "← →", L.T("shortcuts.move_day")),
                (alt + shift + "↑ ↓", L.T("shortcuts.resize")),
                (ctrl + "S  /  " + ctrl + enter, L.T("shortcuts.save_event")),
                (escape, L.T("shortcuts.cancel_window")),
                (enter + "  /  " + L.T("key.space"), L.T("shortcuts.mini_choose")),
                ("↓  /  ↑", L.T("shortcuts.search_results")),
                (enter, L.T("shortcuts.open_result")),
            ]),
        ];
    }

    public ShortcutsWindow()
    {
        Title = L.T("shortcuts.title");
        Width = 560;
        Height = 620;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var list = new StackPanel { Spacing = 6, Margin = new Thickness(0, 0, 16, 0) };
        foreach (var (title, rows) in Sections())
        {
            list.Children.Add(new TextBlock { Text = title, FontWeight = FontWeight.SemiBold, FontSize = 15, Margin = new Thickness(0, list.Children.Count == 0 ? 0 : 12, 0, 2) });
            foreach (var (keys, action) in rows)
            {
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("190,*") };
                row.Children.Add(new TextBlock { Text = keys, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
                var what = new TextBlock { Text = action, TextWrapping = TextWrapping.Wrap };
                Grid.SetColumn(what, 1);
                row.Children.Add(what);
                list.Children.Add(row);
            }
        }

        var close = new Button { Content = L.T("common.close"), Classes = { "accent" }, IsDefault = true, IsCancel = true, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        AutomationProperties.SetAutomationId(close, "Shortcuts.Close");
        close.Click += (_, _) => Close();
        DockPanel.SetDock(close, Dock.Bottom);

        var panel = new DockPanel { Margin = new Thickness(24) };
        panel.Children.Add(close);
        panel.Children.Add(new ScrollViewer { Content = list });
        Content = panel;
        Opened += (_, _) => close.Focus();
    }
}
