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

/// <summary>Hour grid with the timed events of one week, drawn directly for speed and drag &amp; drop control.</summary>
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

    public static readonly StyledProperty<int> FirstHourProperty =
        AvaloniaProperty.Register<WeekGrid, int>(nameof(FirstHour), 0);

    public static readonly StyledProperty<int> LastHourProperty =
        AvaloniaProperty.Register<WeekGrid, int>(nameof(LastHour), 24);

    public static readonly StyledProperty<bool> Use24HourProperty =
        AvaloniaProperty.Register<WeekGrid, bool>(nameof(Use24Hour), true);

    public static readonly StyledProperty<bool> DimPastProperty =
        AvaloniaProperty.Register<WeekGrid, bool>(nameof(DimPast), true);

    private enum DragMode { None, Pending, Move, Resize, Create }

    private readonly List<(Rect Rect, CalendarEvent Event)> _hits = [];
    private DragMode _mode;
    private CalendarEvent? _active;
    private CalendarEvent? _preview;
    private Point _pressPoint;
    private TimeSpan _grabOffset;
    private DateTime _createAnchor;
    private string? _selectedId;
    private DateTime _selectedStart;

    static WeekGrid()
    {
        AffectsRender<WeekGrid>(WeekStartProperty, EventsProperty, CalendarColorsProperty, DayCountProperty,
            FirstHourProperty, LastHourProperty, Use24HourProperty, DimPastProperty);
    }

    public WeekGrid()
    {
        UpdateHeight();
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
    public int FirstHour { get => GetValue(FirstHourProperty); set => SetValue(FirstHourProperty, value); }
    public int LastHour { get => GetValue(LastHourProperty); set => SetValue(LastHourProperty, value); }
    public bool DimPast { get => GetValue(DimPastProperty); set => SetValue(DimPastProperty, value); }
    public bool Use24Hour { get => GetValue(Use24HourProperty); set => SetValue(Use24HourProperty, value); }

    public event Action<CalendarEvent, DateTime, DateTime>? EventChanged;
    public event Action<DateTime, DateTime>? EventCreated;
    public event Action<CalendarEvent>? EventDeleted;
    public event Action<CalendarEvent>? EventOpened;

    /// <summary>Raised when an event is dropped to the left of the grid (the calendar list); carries the window position.</summary>
    public event Action<CalendarEvent, Point>? EventDroppedOutside;

    private double ColumnWidth => (Bounds.Width - Gutter) / Math.Max(1, DayCount);
    private double Y(double hours) => (hours - FirstHour) * HourHeight;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == FirstHourProperty || change.Property == LastHourProperty)
            UpdateHeight();
    }

    private void UpdateHeight() => Height = Math.Max(1, LastHour - FirstHour) * HourHeight;

    private string FormatTime(DateTime t) => t.ToString(Use24Hour ? "HH:mm" : "h:mm tt", CultureInfo.CurrentCulture);

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
            _selectedStart = h.Event.Start;
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
        var outside = e.GetPosition(this).X < 0;
        var windowPoint = TopLevel.GetTopLevel(this) is { } top ? e.GetPosition(top) : default;
        Reset();
        e.Pointer.Capture(null);

        if (preview is not null)
        {
            if (mode == DragMode.Create)
                EventCreated?.Invoke(preview.Start, preview.End);
            else if (mode == DragMode.Move && outside && active is not null)
                EventDroppedOutside?.Invoke(active, windowPoint);
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
            var ev = Events?.FirstOrDefault(x => x.Id == _selectedId && x.Start == _selectedStart);
            if (ev is not null)
            {
                _selectedId = null;
                EventDeleted?.Invoke(ev);
                e.Handled = true;
            }
        }
        else if (e.Key == Key.Enter && _selectedId is not null)
        {
            var ev = Events?.FirstOrDefault(x => x.Id == _selectedId && x.Start == _selectedStart);
            if (ev is not null)
            {
                EventOpened?.Invoke(ev);
                e.Handled = true;
            }
        }
    }

    protected override void OnDoubleTapped(TappedEventArgs e)
    {
        base.OnDoubleTapped(e);
        var p = e.GetPosition(this);
        if (p.X < Gutter) return;

        if (HitTest(p) is { } hit)
        {
            EventOpened?.Invoke(hit.Event);
            return;
        }

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
        var minutes = Math.Clamp(p.Y / HourHeight * 60 + FirstHour * 60, 0, 24 * 60);
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

        using var clip = context.PushClip(new Rect(0, 0, w, h));
        context.FillRectangle(new SolidColorBrush(bg), new Rect(0, 0, w, h));

        for (var hour = FirstHour + 1; hour < LastHour; hour++)
        {
            var y = Math.Round(Y(hour)) + 0.5;
            context.DrawLine(linePen, new Point(Gutter - 8, y), new Point(w, y));
            var text = Text(DateTime.Today.AddHours(hour).ToString(Use24Hour ? "HH:mm" : "h tt", CultureInfo.CurrentCulture), 11, labelBrush);
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

        // Layout always uses the original positions so other events stay still while dragging.
        var source = (Events ?? []).Where(e => !e.IsAllDay).ToList();
        var hideId = _preview is not null && _mode is DragMode.Move or DragMode.Resize ? _active?.Id : null;

        void DrawBlock(CalendarEvent ev, Rect rect, bool translucent, bool selected)
        {
            var hex = ev.ColorHex ?? (colors is not null && colors.TryGetValue(ev.CalendarId, out var c) ? c : "#1A73E8");
            var color = Color.Parse(hex);
            var fillColor = translucent ? Color.FromArgb(215, color.R, color.G, color.B) : color;
            var pen = selected
                ? new Pen(new SolidColorBrush(Color.Parse("#1A73E8")), 2)
                : new Pen(new SolidColorBrush(background), 1);
            using var fade = context.PushOpacity(DimPast && ev.End <= DateTime.Now ? 0.5 : 1.0);
            context.DrawRectangle(new SolidColorBrush(fillColor), pen, rect, 6, 6);

            using (context.PushClip(rect.Deflate(new Thickness(6, 3))))
            {
                var title = Text(string.IsNullOrEmpty(ev.Title) ? "(Senza titolo)" : ev.Title, 12, Brushes.White, FontWeight.SemiBold);
                context.DrawText(title, new Point(rect.X + 6, rect.Y + 3));
                if (rect.Height >= 38)
                {
                    var time = Text($"{FormatTime(ev.Start)} - {FormatTime(ev.End)}", 11, new SolidColorBrush(Color.FromArgb(220, 255, 255, 255)));
                    context.DrawText(time, new Point(rect.X + 6, rect.Y + 3 + title.Height));
                }
            }
        }

        for (var d = 0; d < days; d++)
        {
            var day = WeekStart.Date.AddDays(d);
            var next = day.AddDays(1);
            var items = source
                .Where(e => e.Start < next && e.End > day)
                .Select(e => (Ev: e, S: e.Start < day ? day : e.Start, E: e.End > next ? next : e.End))
                .ToList();

            foreach (var p in OverlapLayout.Place(items, i => i.S, i => i.E))
            {
                var (ev, s, e) = p.Item;
                var slotW = (colW - 8) / p.ColumnCount;
                var x = Gutter + d * colW + 2 + p.Column * slotW;
                var y = Y((s - day).TotalHours) + 1;
                var height = Math.Max((e - s).TotalHours * HourHeight - 2, 20);
                var rect = new Rect(x, y, slotW - (p.ColumnCount > 1 ? 1 : 0) + 2, height);

                _hits.Add((rect, ev));
                if (ev.Id == hideId && ev.Start == _active?.Start) continue;
                DrawBlock(ev, rect, false, ev.Id == _selectedId && ev.Start == _selectedStart);
            }

            if (_preview is { } pv && pv.Start < next && pv.End > day)
            {
                var s = pv.Start < day ? day : pv.Start;
                var e = pv.End > next ? next : pv.End;
                var y = Y((s - day).TotalHours) + 1;
                var height = Math.Max((e - s).TotalHours * HourHeight - 2, 20);
                DrawBlock(pv, new Rect(Gutter + d * colW + 2, y, colW - 6, height), true, false);
            }
        }
    }

    private void DrawNowLine(DrawingContext context, double colW, int days)
    {
        var now = DateTime.Now;
        var index = (int)(now.Date - WeekStart.Date).TotalDays;
        if (index < 0 || index >= days) return;
        var y = Y(now.TimeOfDay.TotalHours);
        if (y < 0 || y > Bounds.Height) return;
        var brush = new SolidColorBrush(Color.Parse("#EA4335"));
        var x0 = Gutter + index * colW;
        context.DrawLine(new Pen(brush, 2), new Point(x0, y), new Point(x0 + colW, y));
        context.DrawEllipse(brush, null, new Point(x0, y), 5, 5);
    }

    private static FormattedText Text(string s, double size, IBrush brush, FontWeight weight = FontWeight.Normal) =>
        new(s, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(FontFamily.Default, FontStyle.Normal, weight), size, brush);
}
