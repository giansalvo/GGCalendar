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
using GGCalendar.Core.Localization;
using GGCalendar.App.Services;

namespace GGCalendar.App.Controls;

/// <summary>Month view: 6 weeks of day cells with event chips.</summary>
public sealed class MonthGrid : Control
{
    private const int Weeks = 6;
    private const double HeaderHeight = 28;
    private const double ChipHeight = 18;
    private const double DayNumberHeight = 26;

    public static readonly StyledProperty<DateTime> GridStartProperty =
        AvaloniaProperty.Register<MonthGrid, DateTime>(nameof(GridStart), DateTime.Today);

    public static readonly StyledProperty<int> MonthProperty =
        AvaloniaProperty.Register<MonthGrid, int>(nameof(Month), DateTime.Today.Month);

    public static readonly StyledProperty<IReadOnlyList<CalendarEvent>?> EventsProperty =
        AvaloniaProperty.Register<MonthGrid, IReadOnlyList<CalendarEvent>?>(nameof(Events));

    public static readonly StyledProperty<IReadOnlyDictionary<string, string>?> CalendarColorsProperty =
        AvaloniaProperty.Register<MonthGrid, IReadOnlyDictionary<string, string>?>(nameof(CalendarColors));

    public static readonly StyledProperty<bool> DimPastProperty =
        AvaloniaProperty.Register<MonthGrid, bool>(nameof(DimPast), true);

    public bool DimPast { get => GetValue(DimPastProperty); set => SetValue(DimPastProperty, value); }

    public static readonly StyledProperty<bool> ShowWeekendsProperty =
        AvaloniaProperty.Register<MonthGrid, bool>(nameof(ShowWeekends), true);

    private readonly List<(Rect Rect, CalendarEvent Event)> _hits = [];

    static MonthGrid()
    {
        AffectsRender<MonthGrid>(DimPastProperty, ShowWeekendsProperty, GridStartProperty, MonthProperty, EventsProperty, CalendarColorsProperty);
    }

    public DateTime GridStart { get => GetValue(GridStartProperty); set => SetValue(GridStartProperty, value); }
    public int Month { get => GetValue(MonthProperty); set => SetValue(MonthProperty, value); }
    public IReadOnlyList<CalendarEvent>? Events { get => GetValue(EventsProperty); set => SetValue(EventsProperty, value); }
    public IReadOnlyDictionary<string, string>? CalendarColors { get => GetValue(CalendarColorsProperty); set => SetValue(CalendarColorsProperty, value); }

    public bool ShowWeekends { get => GetValue(ShowWeekendsProperty); set => SetValue(ShowWeekendsProperty, value); }

    public event Action<CalendarEvent>? EventOpened;

    /// <summary>Raised on a single click on an event, with its block (shows its details beside it).</summary>
    public event Action<CalendarEvent, Rect>? EventClicked;
    public event Action<DateTime>? DayDoubleClicked;
    public event Action<DateTime>? DayNumberClicked;

    // Day offsets (0-6 from GridStart) of the visible columns.
    private int[] Columns() => Enumerable.Range(0, 7)
        .Where(c => ShowWeekends || GridStart.AddDays(c).DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
        .ToArray();

    private double CellWidth => Bounds.Width / Columns().Length;
    private double CellHeight => Math.Max(40, (Bounds.Height - HeaderHeight) / Weeks);

    private DateTime? DayAt(Point p)
    {
        if (p.Y < HeaderHeight) return null;
        var cols = Columns();
        var col = cols[(int)Math.Clamp(p.X / CellWidth, 0, cols.Length - 1)];
        var row = (int)Math.Clamp((p.Y - HeaderHeight) / CellHeight, 0, Weeks - 1);
        return GridStart.Date.AddDays(row * 7 + col);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (e.ClickCount != 1 || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        var p = e.GetPosition(this);
        for (var i = _hits.Count - 1; i >= 0; i--)
        {
            if (!_hits[i].Rect.Contains(p)) continue;
            EventClicked?.Invoke(_hits[i].Event, _hits[i].Rect);
            return;
        }

        if (DayAt(p) is not { } day) return;
        var row = (int)((p.Y - HeaderHeight) / CellHeight);
        var localY = p.Y - HeaderHeight - row * CellHeight;
        if (localY <= DayNumberHeight && !_hits.Any(h => h.Rect.Contains(p))) DayNumberClicked?.Invoke(day);
    }

    protected override void OnDoubleTapped(TappedEventArgs e)
    {
        base.OnDoubleTapped(e);
        var p = e.GetPosition(this);
        for (var i = _hits.Count - 1; i >= 0; i--)
        {
            if (_hits[i].Rect.Contains(p))
            {
                EventOpened?.Invoke(_hits[i].Event);
                return;
            }
        }

        if (DayAt(p) is { } day) DayDoubleClicked?.Invoke(day);
    }

    public override void Render(DrawingContext context)
    {
        var dark = ActualThemeVariant == ThemeVariant.Dark;
        var linePen = new Pen(new SolidColorBrush(Color.Parse(dark ? "#3C4043" : "#DADCE0")), 1);
        var fg = dark ? Brushes.White : Brushes.Black;
        var muted = new SolidColorBrush(Color.Parse(dark ? "#80868B" : "#70757A"));
        var outside = new SolidColorBrush(dark ? Color.Parse("#14FFFFFF") : Color.Parse("#0A000000"));
        var culture = L.Culture;
        var cw = CellWidth;
        var ch = CellHeight;
        var typeface = new Typeface(AppFonts.Current);

        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));
        _hits.Clear();

        var columns = Columns();
        for (var c = 0; c < columns.Length; c++)
        {
            var name = culture.DateTimeFormat.GetAbbreviatedDayName(GridStart.AddDays(columns[c]).DayOfWeek).ToUpperInvariant();
            var t = new FormattedText(name, culture, FlowDirection.LeftToRight, typeface, 11, muted);
            context.DrawText(t, new Point(c * cw + (cw - t.Width) / 2, (HeaderHeight - t.Height) / 2));
        }

        var events = (Events ?? []).OrderBy(e => !e.IsAllDay).ThenBy(e => e.Start).ToList();
        for (var row = 0; row < Weeks; row++)
        {
            for (var ci = 0; ci < columns.Length; ci++)
            {
                var day = GridStart.Date.AddDays(row * 7 + columns[ci]);
                var cell = new Rect(ci * cw, HeaderHeight + row * ch, cw, ch);
                if (day.Month != Month) context.FillRectangle(outside, cell);
                context.DrawRectangle(null, linePen, cell);

                var isToday = day == DateTime.Today;
                var num = new FormattedText(day.Day.ToString(culture), culture, FlowDirection.LeftToRight, typeface, 12,
                    isToday ? Brushes.White : day.Month == Month ? fg : muted);
                var badge = new Rect(cell.X + 6, cell.Y + 3, 22, 22);
                if (isToday) context.DrawRectangle(new SolidColorBrush(Color.Parse("#1A73E8")), null, badge, 11, 11);
                context.DrawText(num, new Point(badge.X + (22 - num.Width) / 2, badge.Y + (22 - num.Height) / 2));

                var dayEvents = events.Where(e => e.Start < day.AddDays(1) && e.End > day).ToList();
                var slots = Math.Max(0, (int)((ch - DayNumberHeight - 2) / (ChipHeight + 2)));
                var shown = dayEvents.Count > slots ? Math.Max(0, slots - 1) : dayEvents.Count;
                for (var i = 0; i < shown; i++)
                {
                    var ev = dayEvents[i];
                    var rect = new Rect(cell.X + 3, cell.Y + DayNumberHeight + i * (ChipHeight + 2), cell.Width - 6, ChipHeight);
                    _hits.Add((rect, ev));
                    var hex = ev.ColorHex ?? (CalendarColors is not null && CalendarColors.TryGetValue(ev.CalendarId, out var cc) ? cc : "#1A73E8");
                    var color = Color.Parse(hex);
                    var title = string.IsNullOrEmpty(ev.Title) ? L.T("common.untitled") : ev.Title;
                    using var fade = context.PushOpacity(DimPast && ev.End <= DateTime.Now ? 0.5 : 1.0);
                    if (ev.IsAllDay)
                    {
                        context.DrawRectangle(new SolidColorBrush(color), null, rect, 4, 4);
                        DrawClipped(context, title, rect, typeface, Brushes.White, 11, 6);
                    }
                    else
                    {
                        context.DrawEllipse(new SolidColorBrush(color), null, new Point(rect.X + 8, rect.Center.Y), 4, 4);
                        DrawClipped(context, $"{ev.Start:HH:mm} {title}", rect, typeface, fg, 11, 16);
                    }
                }

                if (shown < dayEvents.Count)
                {
                    var more = new FormattedText($"+{dayEvents.Count - shown} altri", culture, FlowDirection.LeftToRight, typeface, 11, muted);
                    context.DrawText(more, new Point(cell.X + 8, cell.Y + DayNumberHeight + shown * (ChipHeight + 2) + 2));
                }
            }
        }
    }

    private static void DrawClipped(DrawingContext context, string s, Rect rect, Typeface typeface, IBrush brush, double size, double padLeft)
    {
        using (context.PushClip(rect.Deflate(new Thickness(0, 0, 4, 0))))
        {
            var t = new FormattedText(s, L.Culture, FlowDirection.LeftToRight, typeface, size, brush);
            context.DrawText(t, new Point(rect.X + padLeft, rect.Y + (rect.Height - t.Height) / 2));
        }
    }
}
