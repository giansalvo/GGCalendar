using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using GGCalendar.App.Services;
using GGCalendar.App.ViewModels;
using GGCalendar.Core.Localization;
using GGCalendar.Data;

namespace GGCalendar.App.Views;

/// <summary>
/// The window of the queue (issue #19): the events with changes still waiting for Google, grouped by calendar, with a cross to cancel the
/// changes of one event, a button for a whole calendar and one for all. Cancelling puts the event back as Google has it.
/// </summary>
public sealed class SyncQueueWindow : Window
{
    private static readonly FontFamily Icons = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    private readonly GoogleSyncService _google;
    private readonly MainViewModel _main;
    private readonly Func<IReadOnlyList<QueuedEvent>, Task> _cancel;
    private readonly StackPanel _rows = new();
    private readonly TextBlock _info = new() { Opacity = 0.75, Margin = new Thickness(0, 0, 0, 8), TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _title = new() { FontSize = 18, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _cancelAll = new() { Padding = new Thickness(12, 5) };
    private readonly Button _retry = new() { Padding = new Thickness(14, 6) };
    private readonly Button _close = new() { Padding = new Thickness(14, 6) };
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(15) };

    public SyncQueueWindow(GoogleSyncService google, MainViewModel main, Func<IReadOnlyList<QueuedEvent>, Task> cancel, Func<Task> retry)
    {
        _google = google;
        _main = main;
        _cancel = cancel;
        Width = 660;
        Height = 600;
        MinWidth = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        AutomationProperties.SetAutomationId(_cancelAll, "Queue.CancelAll");
        AutomationProperties.SetAutomationId(_retry, "Queue.Retry");
        _cancelAll.Click += async (_, _) => await AskAsync([], null, all: true);
        _retry.Click += async (_, _) =>
        {
            IsEnabled = false;
            try
            {
                await retry();
            }
            finally
            {
                IsEnabled = true;
                Refresh();
            }
        };
        _close.Click += (_, _) => Close();

        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 0, 0, 6) };
        header.Children.Add(_title);
        Grid.SetColumn(_cancelAll, 1);
        header.Children.Add(_cancelAll);

        var footer = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        footer.Children.Add(_retry);
        footer.Children.Add(_close);

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = _rows };
        AutomationProperties.SetAutomationId(scroll, "Queue.List");
        var dock = new DockPanel { Margin = new Thickness(20) };
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(_info, Dock.Top);
        DockPanel.SetDock(footer, Dock.Bottom);
        dock.Children.Add(header);
        dock.Children.Add(_info);
        dock.Children.Add(footer);
        dock.Children.Add(scroll);
        Content = dock;

        _timer.Tick += (_, _) => Refresh();
        Opened += (_, _) => _timer.Start();
        Closed += (_, _) =>
        {
            _closed = true;
            _timer.Stop();
        };
        L.Changed += Refresh;
        Closed += (_, _) => L.Changed -= Refresh;
        Build(_google.GetQueue()); // the first drawing is at once: the window opens with its list
    }

    /// <summary>More events than this wait: only the first ones are drawn (thousands of lines would freeze the window); the buttons of the calendars and of all cover the rest.</summary>
    public const int MaxRows = 150;

    private bool _closed;
    private bool _refreshing;
    private bool _again;

    /// <summary>Draws the queue again: it changed (a sync sent something, the person cancelled) or the time in the queue has to follow the clock.</summary>
    public async void Refresh()
    {
        if (_closed) return;
        if (_refreshing)
        {
            _again = true; // asked while another drawing is going on: it is done again, so that the list is never left older than the last request
            return;
        }

        _refreshing = true;
        try
        {
            do
            {
                _again = false;
                var queue = await Task.Run(_google.GetQueue); // thousands of events: read off the thread of the window
                if (!_closed) Build(queue);
            }
            while (_again && !_closed);
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void Build(IReadOnlyList<QueuedEvent> queue)
    {
        Title = L.T("queue.title");
        _title.Text = L.T("queue.title");
        _retry.Content = L.T("queue.retry");
        _close.Content = L.T("common.close");
        _rows.Children.Clear();
        _cancelAll.IsVisible = queue.Count > 0;
        _cancelAll.Content = L.T("queue.cancel_all", queue.Count);
        _info.Text = queue.Count == 0
            ? L.T("queue.empty")
            : L.N("queue.info", queue.Count) + (_google.NextRetry() is { } at ? L.T("queue.info_retry", at.ToLocalTime().DateTime) : "") + "\n" + L.T("queue.paused");

        var drawn = 0;
        foreach (var group in queue.GroupBy(q => q.CalendarId))
        {
            var calendar = _main.Calendars.FirstOrDefault(c => c.Id == group.Key);
            var list = group.ToList();
            var cancelGroup = new Button { Content = L.T("queue.cancel_group"), Padding = new Thickness(10, 3), FontSize = 12 };
            AutomationProperties.SetAutomationId(cancelGroup, "Queue.CancelGroup");
            cancelGroup.Click += async (_, _) => await AskAsync(list, calendar?.Name ?? "");

            var head = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 8, Margin = new Thickness(0, 14, 0, 4) };
            head.Children.Add(new Ellipse { Width = 10, Height = 10, Fill = Brush.Parse(calendar?.ColorHex ?? "#1A73E8"), VerticalAlignment = VerticalAlignment.Center });
            var names = new StackPanel();
            names.Children.Add(new TextBlock { Text = (calendar?.Name ?? group.Key) + "  ·  " + (calendar?.AccountName ?? ""), FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            names.Children.Add(new TextBlock { Text = Summary(list), FontSize = 12, Opacity = 0.7 });
            Grid.SetColumn(names, 1);
            head.Children.Add(names);
            Grid.SetColumn(cancelGroup, 2);
            head.Children.Add(cancelGroup);
            _rows.Children.Add(head);

            foreach (var q in list)
            {
                if (drawn++ >= MaxRows) break;
                _rows.Children.Add(Row(q));
            }
        }

        if (queue.Count > MaxRows)
            _rows.Children.Add(new TextBlock { Text = L.N("queue.more", queue.Count - MaxRows), Opacity = 0.7, Margin = new Thickness(18, 10, 0, 6), TextWrapping = TextWrapping.Wrap });

        if (queue.Count == 0)
        {
            _rows.Children.Add(new TextBlock
            {
                Text = "●  " + L.T("queue.tip_ok"),
                Foreground = Brush.Parse("#16A765"),
                FontSize = 16,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 60),
            });
        }
    }

    private static string Summary(IReadOnlyCollection<QueuedEvent> items)
    {
        var parts = new List<string>();
        void Add(QueueKind kind, string key)
        {
            var n = items.Count(i => i.Kind == kind);
            if (n > 0) parts.Add(L.N(key, n));
        }

        Add(QueueKind.New, "queue.sum_new");
        Add(QueueKind.Edited, "queue.sum_edited");
        Add(QueueKind.Deleted, "queue.sum_deleted");
        Add(QueueKind.Moved, "queue.sum_moved");
        return string.Join(" · ", parts);
    }

    private string KindText(QueuedEvent q) => q.Kind switch
    {
        QueueKind.New => L.T("queue.kind_new"),
        QueueKind.Edited => q.Edits > 1 ? L.T("queue.kind_edited_n", q.Edits) : L.T("queue.kind_edited"),
        QueueKind.Deleted => L.T("queue.kind_deleted"),
        _ => L.T("queue.kind_moved", _main.Calendars.FirstOrDefault(c => c.Id == q.FromCalendarId)?.Name ?? q.FromCalendarId ?? ""),
    };

    private Control Row(QueuedEvent q)
    {
        var glyph = q.Kind switch { QueueKind.New => "", QueueKind.Edited => "", QueueKind.Deleted => "", _ => "" };
        var title = string.IsNullOrWhiteSpace(q.Title) ? L.T("common.untitled") : q.Title;
        var detail = q.Since is { } since ? L.T("queue.detail", KindText(q), AlarmsViewModel.Remaining(DateTime.UtcNow - since.ToUniversalTime())) : KindText(q);

        var cross = new Button
        {
            Content = new TextBlock { Text = "", FontFamily = Icons, FontSize = 12 },
            Width = 30,
            Height = 30,
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            CornerRadius = new CornerRadius(15),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetAutomationId(cross, "Queue.Cancel");
        ToolTip.SetTip(cross, L.T("queue.cancel_one"));
        cross.Click += async (_, _) => await AskAsync([q], null);

        var text = new StackPanel { Spacing = 1 };
        text.Children.Add(new TextBlock { Text = title, TextTrimming = TextTrimming.CharacterEllipsis, FontWeight = FontWeight.Medium });
        text.Children.Add(new TextBlock { Text = detail, FontSize = 12, Opacity = 0.7 });
        ToolTip.SetTip(text, title);

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 12, Margin = new Thickness(18, 3, 0, 3) };
        AutomationProperties.SetAutomationId(grid, "Queue.Row");
        grid.Children.Add(new TextBlock { Text = glyph, FontFamily = Icons, FontSize = 16, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.85 });
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        Grid.SetColumn(cross, 2);
        grid.Children.Add(cross);
        return grid;
    }

    /// <summary>What the confirmation tells: what happens to each kind of event.</summary>
    internal static string Explain(IReadOnlyCollection<QueuedEvent> items)
    {
        var lines = new List<string>();
        void Add(QueueKind kind, string key)
        {
            var n = items.Count(i => i.Kind == kind);
            if (n > 0) lines.Add(L.N(key, n));
        }

        Add(QueueKind.New, "queue.explain_new");
        Add(QueueKind.Edited, "queue.explain_edited");
        Add(QueueKind.Deleted, "queue.explain_deleted");
        Add(QueueKind.Moved, "queue.explain_moved");
        return string.Join("\n", lines);
    }

    /// <summary>The question for one event, a calendar (<paramref name="calendarName"/> given) or all of them.</summary>
    internal static (string Title, string Text) Question(IReadOnlyList<QueuedEvent> items, string? calendarName)
    {
        var loss = L.T("queue.confirm_loss");
        if (items.Count == 1 && calendarName is null)
        {
            var title = string.IsNullOrWhiteSpace(items[0].Title) ? L.T("common.untitled") : items[0].Title!;
            return (L.T("queue.confirm_title_one"), $"«{title}»\n\n{Explain(items)}\n\n{loss}");
        }

        return calendarName is null
            ? (L.T("queue.confirm_title_all"), $"{L.N("queue.confirm_all", items.Count)}\n\n{Explain(items)}\n\n{loss}")
            : (L.T("queue.confirm_title_group", calendarName), $"{L.N("queue.confirm_group", items.Count, calendarName)}\n\n{Explain(items)}\n\n{loss}");
    }

    /// <param name="shown">The events of the line, calendar or button that was pressed, as they were on show.</param>
    /// <param name="all">All the events in the queue.</param>
    private async Task AskAsync(IReadOnlyList<QueuedEvent> shown, string? calendarName, bool all = false)
    {
        // The list on show may be older than the queue (a sync can have sent some of the events meanwhile): the question is about what is
        // really waiting now, and the list is drawn again so that it says the same.
        var fresh = await Task.Run(_google.GetQueue);
        if (_closed) return;
        Build(fresh);
        var wanted = shown.Select(q => q.EventId).ToHashSet();
        var items = all ? fresh : fresh.Where(q => wanted.Contains(q.EventId)).ToList();
        if (items.Count == 0)
        {
            await new WarningWindow(L.T("queue.title"), L.T("queue.cancel_none")).ShowDialog(this); // they were sent meanwhile
            return;
        }

        var (title, text) = Question(items, calendarName);
        if (!await new ConfirmWindow(title, text, L.T("queue.confirm_button")).ShowDialog<bool>(this)) return;
        IsEnabled = false; // it waits for a sync that is running
        _info.Text = L.T("queue.busy");
        try
        {
            await _cancel(items);
        }
        finally
        {
            IsEnabled = true;
            Refresh();
        }
    }
}
