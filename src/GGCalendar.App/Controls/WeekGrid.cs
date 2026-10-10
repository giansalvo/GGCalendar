using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using GGCalendar.Core.Layout;
using GGCalendar.Core.Models;

namespace GGCalendar.App.Controls;

/// <summary>Hour grid with the events of one week, drawn directly for speed and drag &amp; drop control.</summary>
public sealed class WeekGrid : Control
{
    public const double Gutter = 56;
    public const double HourHeight = 48;
    private const double ResizeEdge = 7;
    private const double DragThreshold = 4;

    public static readonly StyledProperty<DateTime> WeekStartProperty =
        AvaloniaProperty.Register<WeekGrid, DateTime>(nameof(WeekStart), DateTime.Today);

    public static readonly StyledProperty<IReadOnlyList<CalendarEvent>?> EventsProperty =
        AvaloniaProperty.Register<WeekGrid, IReadOnlyList<CalendarEvent>?>(nameof(Events));

    public static readonly StyledProperty<IReadOnlyDictionary<string, string>?> CalendarColorsProperty =
        AvaloniaProperty.Register<WeekGrid, IReadOnlyDictionary<string, string>?>(nameof(CalendarColors));

    public static readonly StyledProperty<int> DayCountProperty =
        AvaloniaProperty.Register<WeekGrid, int>(nameof(DayCount), 7);

    public static readonly StyledProperty<int> SnapMinutesProperty =
        AvaloniaProperty.Register<WeekGrid, int>(nameof(SnapMinutes), 15);

    public static readonly StyledProperty<int> DefaultMinutesProperty =
        AvaloniaProperty.Register<WeekGrid, int>(nameof(DefaultMinutes), 60);

    private enum DragMode { None, Pending, Move, Resize, Create }

    private readonly List<(Rect Rect, CalendarEvent Event)> _hits = [];
    private DragMode _mode;
    private CalendarEvent? _active;
    private CalendarEvent? _preview;
    private Point _pressPoint;
    private TimeSpan _grabOffset;
    private DateTime _createAnchor;
    private string? _selectedId;

    static WeekGrid()
    {
        AffectsRender<WeekGrid>(WeekStartProperty, EventsProperty, CalendarColorsProperty, DayCountProperty);
    }

    public WeekGrid()
    {
        Height = 24 * HourHeight;
        Focusable = true;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        timer.Tick += (_, _) => InvalidateVisual();
        AttachedToVisualTree += (_, _) => timer.Start();
        DetachedFromVisualTree += (_, _) => timer.Stop();
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    public DateTime WeekStart { get => GetValue(WeekStartProperty); set => SetValue(WeekStartProperty, value); }
    public IReadOnlyList<CalendarEvent>? Events { get => GetValue(EventsProperty); set => SetValue(EventsProperty, value); }
    public IReadOnlyDictionary<string, string>? CalendarColors { get => GetValue(CalendarColorsProperty); set => SetValue(CalendarColorsProperty, value); }
    public int DayCount { get => GetValue(DayCountProperty); set => SetValue(DayCountProperty, value); }
    public int SnapMinutes { get => GetValue(SnapMinutesProperty); set => SetValue(SnapMinutesProperty, value); }
    public int DefaultMinutes { get => GetValue(DefaultMinutesProperty); set => SetValue(DefaultMinutesProperty, value); }

    public event Action<CalendarEvent, DateTime, DateTime>? EventChanged;
    public event Action<DateTime, DateTime>? EventCreated;
    public event Action<CalendarEvent>? EventDeleted;

    private double ColumnWidth => (Bounds.Width - Gutter) / Math.Max(1, DayCount);

    // ---------- Pointer handling ----------

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var props = e.GetCurrentPoint(this).Properties;
        if (!props.IsLeftButtonPressed) return;
        Focus();

        var p = e.GetPosition(this);
        if (p.X < Gutter) return;
        _pressPoint = p;

        var hit = HitTest(p);
        if (hit is { } h)
        {
            _active = h.Event;
            _selectedId = h.Event.Id;
            var nearBottom = p.Y >= h.Rect.Bottom - ResizeEdge;
            _grabOffset = PointToTime(p, snap: false) - h.Event.Start;
            _mode = nearBottom ? DragMode.Resize : DragMode.Pending;
        }
        else
        {
            _selectedId = null;
            _createAnchor = PointToTime(p, snap: true, floor: true);
            _mode = DragMode.Create;
        }

        e.Pointer.Capture(this);
        InvalidateVisual();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var p = e.GetPosition(this);

        if (_mode == DragMode.None)
        {
            UpdateCursor(p);
            return;
        }

        var snap = Math.Max(1, SnapMinutes);
        if (_mode == DragMode.Pending)
        {
            if (Math.Abs(p.Y - _pressPoint.Y) + Math.Abs(p.X - _pressPoint.X) < DragThreshold) return;
            _mode = DragMode.Move;
        }

        switch (_mode)
        {
            case DragMode.Move when _active is not null:
            {
                var duration = _active.End - _active.Start;
                var start = Snap(PointToTime(p, snap: false) - _grabOffset, snap);
                _preview = _active with { Start = start, End = start + duration };
                break;
            }

            case DragMode.Resize when _active is not null:
            {
                var end = Snap(PointToTime(p, snap: false), snap);
                var minEnd = _active.Start.AddMinutes(snap);
                if (end < minEnd) end = minEnd;
                var dayEnd = _active.Start.Date.AddDays(1);
                if (end > dayEnd) end = dayEnd;
                _preview = _active with { End = end };
                break;
            }

            case DragMode.Create:
            {
                if (_preview is null && Math.Abs(p.Y - _pressPoint.Y) < DragThreshold) return;
                var current = PointToTime(p, snap: true);
                var start = current < _createAnchor ? current : _createAnchor;
                var end = current < _createAnchor ? _createAnchor : current;
                if (end - start < TimeSpan.FromMinutes(snap)) end = start.AddMinutes(snap);
                _preview = new CalendarEvent("", "", "", start, end);
                break;
            }
        }

        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_mode == DragMode.None) return;

        var mode = _mode;
        var active = _active;
        var preview = _preview;
        Reset();
        e.Pointer.Capture(null);

        if (preview is not null)
        {
            if (mode == DragMode.Create)
                EventCreated?.Invoke(preview.Start, preview.End);
            else if (active is not null && (preview.Start != active.Start || preview.End != active.End))
                EventChanged?.Invoke(active, preview.Start, preview.End);
        }

        InvalidateVisual();
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        if (_mode != DragMode.None)
        {
            Reset();
            InvalidateVisual();
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape && _mode != DragMode.None)
        {
            Reset();
            InvalidateVisual();
            e.Handled = true;
        }
        else if (e.Key == Key.Delete && _selectedId is not null)
        {
            var ev = Events?.FirstOrDefault(x => x.Id == _selectedId);
            if (ev is not null)
            {
                _selectedId = null;
                EventDeleted?.Invoke(ev);
                e.Handled = true;
            }
        }
    }

    protected override void OnDoubleTapped(TappedEventArgs e)
    {
        base.OnDoubleTapped(e);
        var p = e.GetPosition(this);
        if (p.X < Gutter || HitTest(p) is not null) return;
        var start = PointToTime(p, snap: true, floor: true);
        EventCreated?.Invoke(start, start.AddMinutes(DefaultMinutes));
    }

    private void Reset()
    {
        _mode = DragMode.None;
        _active = null;
        _preview = null;
    }

    private void UpdateCursor(Point p)
    {
        var hit = p.X < Gutter ? null : HitTest(p);
        Cursor = hit is null
            ? Cursor.Default
            : new Cursor(p.Y >= hit.Value.Rect.Bottom - ResizeEdge ? StandardCursorType.SizeNorthSouth : StandardCursorType.SizeAll);
    }

    private (Rect Rect, CalendarEvent Event)? HitTest(Point p)
    {
        for (var i = _hits.Count - 1; i >= 0; i--)
            if (_hits[i].Rect.Contains(p))
                return _hits[i];
        return null;
    }

    private DateTime PointToTime(Point p, bool snap, bool floor = false)
    {
        var days = Math.Max(1, DayCount);
        var day = (int)Math.Clamp(Math.Floor((p.X - Gutter) / ColumnWidth), 0, days - 1);
        var minutes = Math.Clamp(p.Y / HourHeight * 60, 0, 24 * 60);
        var t = WeekStart.Date.AddDays(day).AddMinutes(minutes);
        return snap ? Snap(t, Math.Max(1, SnapMinutes), floor) : t;
    }

    private static DateTime Snap(DateTime t, int minutes, bool floor = false)
    {
        var ticks = TimeSpan.FromMinutes(minutes).Ticks;
        var rounded = floor ? t.Ticks / ticks * ticks : (long)Math.Round((double)t.Ticks / ticks) * ticks;
        return new DateTime(rounded, t.Kind);
    }

    // ---------- Rendering ----------

    public override void Render(DrawingContext context)
    {
        var dark = ActualThemeVariant == ThemeVariant.Dark;
        var bg = dark ? Color.Parse("#202124") : Colors.White;
        var line = dark ? Color.Parse("#3C4043") : Color.Parse("#DADCE0");
        var label = dark ? Color.Parse("#9AA0A6") : Color.Parse("#70757A");
        var linePen = new Pen(new SolidColorBrush(line), 1);
        var labelBrush = new SolidColorBrush(label);

        var w = Bounds.Width;
        var h = Bounds.Height;
        var days = Math.Max(1, DayCount);
        var colW = ColumnWidth;

        context.FillRectangle(new SolidColorBrush(bg), new Rect(0, 0, w, h));

        for (var hour = 1; hour < 24; hour++)
        {
            var y = Math.Round(hour * HourHeight) + 0.5;
            context.DrawLine(linePen, new Point(Gutter - 8, y), new Point(w, y));
            var text = Text($"{hour:00}:00", 11, labelBrush);
            context.DrawText(text, new Point(Gutter - 12 - text.Width, y - text.Height / 2));
        }

        for (var d = 0; d <= days; d++)
        {
            var x = Math.Round(Gutter + d * colW) + 0.5;
            context.DrawLine(linePen, new Point(x, 0), new Point(x, h));
        }

        DrawEvents(context, colW, days, bg);
        DrawNowLine(context, colW, days);
    }

    private void DrawEvents(DrawingContext context, double colW, int days, Color background)
    {
        _hits.Clear();
        if (Events is null && _preview is null) return;
        var colors = CalendarColors;

        var source = (Events ?? []).Select(e => _active is not null && e.Id == _active.Id && _preview is not null && _mode != DragMode.Create ? _preview : e).ToList();
        var isCreating = _mode == DragMode.Create && _preview is not null;
        if (isCreating) source.Add(_preview!);

        for (var d = 0; d < days; d++)
        {
            var day = WeekStart.Date.AddDays(d);
            var next = day.AddDays(1);
            var items = source
                .Where(e => !e.IsAllDay && e.Start < next && e.End > day)
                .Select(e => (Ev: e, S: e.Start < day ? day : e.Start, E: e.End > next ? next : e.End))
                .ToList();

            foreach (var p in OverlapLayout.Place(items, i => i.S, i => i.E))
            {
                var (ev, s, e) = p.Item;
                var slotW = (colW - 8) / p.ColumnCount;
                var x = Gutter + d * colW + 2 + p.Column * slotW;
                var y = (s - day).TotalHours * HourHeight + 1;
                var height = Math.Max((e - s).TotalHours * HourHeight - 2, 20);
                var rect = new Rect(x, y, slotW - (p.ColumnCount > 1 ? 1 : 0) + 2, height);

                var isDragged = _active is not null && ev.Id == _active.Id && _mode != DragMode.None && _mode != DragMode.Pending;
                if (ev.Id != "") _hits.Add((rect, ev));

                var hex = colors is not null && colors.TryGetValue(ev.CalendarId, out var c) ? c : "#1A73E8";
                var color = Color.Parse(hex);
                var fillColor = isDragged || ev.Id == "" ? Color.FromArgb(215, color.R, color.G, color.B) : color;
                var selected = ev.Id == _selectedId && ev.Id != "";
                var pen = selected
                    ? new Pen(new SolidColorBrush(Color.Parse("#1A73E8")), 2)
                    : new Pen(new SolidColorBrush(background), 1);
                context.DrawRectangle(new SolidColorBrush(fillColor), pen, rect, 6, 6);

                using (context.PushClip(rect.Deflate(new Thickness(6, 3))))
                {
                    var title = Text(string.IsNullOrEmpty(ev.Title) ? "(Senza titolo)" : ev.Title, 12, Brushes.White, FontWeight.SemiBold);
                    context.DrawText(title, new Point(rect.X + 6, rect.Y + 3));
                    if (height >= 38)
                    {
                        var time = Text($"{ev.Start:HH:mm} - {ev.End:HH:mm}", 11, new SolidColorBrush(Color.FromArgb(220, 255, 255, 255)));
                        context.DrawText(time, new Point(rect.X + 6, rect.Y + 3 + title.Height));
                    }
                }
            }
        }
    }

    private void DrawNowLine(DrawingContext context, double colW, int days)
    {
        var now = DateTime.Now;
        var index = (int)(now.Date - WeekStart.Date).TotalDays;
        if (index < 0 || index >= days) return;
        var y = now.TimeOfDay.TotalHours * HourHeight;
        var brush = new SolidColorBrush(Color.Parse("#EA4335"));
        var x0 = Gutter + index * colW;
        context.DrawLine(new Pen(brush, 2), new Point(x0, y), new Point(x0 + colW, y));
        context.DrawEllipse(brush, null, new Point(x0, y), 5, 5);
    }

    private static FormattedText Text(string s, double size, IBrush brush, FontWeight weight = FontWeight.Normal) =>
        new(s, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(FontFamily.Default, FontStyle.Normal, weight), size, brush);
}
