using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using GGCalendar.Core.Models;

namespace GGCalendar.App.Controls;

/// <summary>Strip above the hour grid showing all-day events of the week.</summary>
public sealed class AllDayStrip : Control
{
    private const double RowHeight = 22;
    private const double RowGap = 2;

    public static readonly StyledProperty<DateTime> WeekStartProperty =
        AvaloniaProperty.Register<AllDayStrip, DateTime>(nameof(WeekStart), DateTime.Today);

    public static readonly StyledProperty<IReadOnlyList<CalendarEvent>?> EventsProperty =
        AvaloniaProperty.Register<AllDayStrip, IReadOnlyList<CalendarEvent>?>(nameof(Events));

    public static readonly StyledProperty<IReadOnlyDictionary<string, string>?> CalendarColorsProperty =
        AvaloniaProperty.Register<AllDayStrip, IReadOnlyDictionary<string, string>?>(nameof(CalendarColors));

    public static readonly StyledProperty<int> DayCountProperty =
        AvaloniaProperty.Register<AllDayStrip, int>(nameof(DayCount), 7);

    private readonly List<(Rect Rect, CalendarEvent Event)> _hits = [];
    private List<(CalendarEvent Ev, int FirstDay, int LastDay, int Row)> _layout = [];

    static AllDayStrip()
    {
        AffectsRender<AllDayStrip>(WeekStartProperty, EventsProperty, CalendarColorsProperty, DayCountProperty);
    }

    public DateTime WeekStart { get => GetValue(WeekStartProperty); set => SetValue(WeekStartProperty, value); }
    public IReadOnlyList<CalendarEvent>? Events { get => GetValue(EventsProperty); set => SetValue(EventsProperty, value); }
    public IReadOnlyDictionary<string, string>? CalendarColors { get => GetValue(CalendarColorsProperty); set => SetValue(CalendarColorsProperty, value); }
    public int DayCount { get => GetValue(DayCountProperty); set => SetValue(DayCountProperty, value); }

    public event Action<CalendarEvent>? EventOpened;
    public event Action<DateTime>? DayDoubleClicked;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == WeekStartProperty || change.Property == EventsProperty || change.Property == DayCountProperty)
        {
            BuildLayout();
            var rows = _layout.Count == 0 ? 0 : _layout.Max(l => l.Row) + 1;
            Height = rows == 0 ? 6 : rows * (RowHeight + RowGap) + 6;
        }
    }

    private void BuildLayout()
    {
        var days = Math.Max(1, DayCount);
        var weekEnd = WeekStart.Date.AddDays(days);
        var rowEnds = new List<int>();
        _layout = [];

        foreach (var ev in (Events ?? []).Where(e => e.IsAllDay && e.Start < weekEnd && e.End > WeekStart.Date)
                     .OrderBy(e => e.Start).ThenByDescending(e => e.End))
        {
            var first = Math.Max(0, (int)(ev.Start.Date - WeekStart.Date).TotalDays);
            var last = Math.Min(days - 1, (int)(ev.End.Date - WeekStart.Date).TotalDays - 1);
            if (last < first) last = first;
            var row = rowEnds.FindIndex(end => end < first);
            if (row < 0) { rowEnds.Add(last); row = rowEnds.Count - 1; }
            else rowEnds[row] = last;
            _layout.Add((ev, first, last, row));
        }
    }

    protected override void OnDoubleTapped(TappedEventArgs e)
    {
        base.OnDoubleTapped(e);
        var p = e.GetPosition(this);
        if (p.X < WeekGrid.Gutter) return;

        for (var i = _hits.Count - 1; i >= 0; i--)
        {
            if (_hits[i].Rect.Contains(p))
            {
                EventOpened?.Invoke(_hits[i].Event);
                return;
            }
        }

        var colW = (Bounds.Width - WeekGrid.Gutter) / Math.Max(1, DayCount);
        var day = (int)Math.Clamp(Math.Floor((p.X - WeekGrid.Gutter) / colW), 0, DayCount - 1);
        DayDoubleClicked?.Invoke(WeekStart.Date.AddDays(day));
    }

    public override void Render(DrawingContext context)
    {
        var dark = ActualThemeVariant == ThemeVariant.Dark;
        var line = dark ? Color.Parse("#3C4043") : Color.Parse("#DADCE0");
        var linePen = new Pen(new SolidColorBrush(line), 1);
        var days = Math.Max(1, DayCount);
        var colW = (Bounds.Width - WeekGrid.Gutter) / days;

        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));
        for (var d = 0; d <= days; d++)
        {
            var x = Math.Round(WeekGrid.Gutter + d * colW) + 0.5;
            context.DrawLine(linePen, new Point(x, 0), new Point(x, Bounds.Height));
        }

        context.DrawLine(linePen, new Point(0, Bounds.Height - 0.5), new Point(Bounds.Width, Bounds.Height - 0.5));

        _hits.Clear();
        foreach (var (ev, first, last, row) in _layout)
        {
            var rect = new Rect(WeekGrid.Gutter + first * colW + 2, 2 + row * (RowHeight + RowGap),
                (last - first + 1) * colW - 5, RowHeight);
            _hits.Add((rect, ev));
            var hex = CalendarColors is not null && CalendarColors.TryGetValue(ev.CalendarId, out var c) ? c : "#1A73E8";
            context.DrawRectangle(new SolidColorBrush(Color.Parse(hex)), null, rect, 5, 5);
            using (context.PushClip(rect.Deflate(new Thickness(6, 0))))
            {
                var text = new FormattedText(string.IsNullOrEmpty(ev.Title) ? "(Senza titolo)" : ev.Title,
                    CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                    new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.SemiBold), 12, Brushes.White);
                context.DrawText(text, new Point(rect.X + 6, rect.Y + (RowHeight - text.Height) / 2));
            }
        }
    }
}
