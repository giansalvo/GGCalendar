using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using GGCalendar.Core.Models;
using GGCalendar.Core.Localization;
using GGCalendar.App.Services;

namespace GGCalendar.App.Controls;

/// <summary>Year view: twelve mini months; days with events get a dot, a click opens the day view.</summary>
public sealed class YearGrid : Control
{
    private const double MonthTitleHeight = 28;
    private const double Pad = 14;

    public static readonly StyledProperty<int> YearProperty =
        AvaloniaProperty.Register<YearGrid, int>(nameof(Year), DateTime.Today.Year);

    public static readonly StyledProperty<DayOfWeek> FirstDayOfWeekProperty =
        AvaloniaProperty.Register<YearGrid, DayOfWeek>(nameof(FirstDayOfWeek), DayOfWeek.Monday);

    public static readonly StyledProperty<IReadOnlyList<CalendarEvent>?> EventsProperty =
        AvaloniaProperty.Register<YearGrid, IReadOnlyList<CalendarEvent>?>(nameof(Events));

    static YearGrid()
    {
        AffectsRender<YearGrid>(YearProperty, FirstDayOfWeekProperty, EventsProperty);
    }

    public int Year { get => GetValue(YearProperty); set => SetValue(YearProperty, value); }
    public DayOfWeek FirstDayOfWeek { get => GetValue(FirstDayOfWeekProperty); set => SetValue(FirstDayOfWeekProperty, value); }
    public IReadOnlyList<CalendarEvent>? Events { get => GetValue(EventsProperty); set => SetValue(EventsProperty, value); }

    public event Action<DateTime>? DayClicked;

    private const double MinCellH = 150;

    private static int ColumnsFor(double width) => width >= 1000 ? 4 : width >= 700 ? 3 : 2;
    private int Columns => ColumnsFor(Bounds.Width);
    private int Rows => 12 / Columns;
    private double CellW => Bounds.Width / Columns;
    private double CellH => Math.Max(MinCellH, Bounds.Height / Rows);

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? 900 : availableSize.Width;
        // Every row of months must fit: a narrow window (or large Windows scaling) means 2 columns and 6 rows.
        var needed = MinCellH * (12 / ColumnsFor(width));
        // Inside a ScrollViewer the height is unbounded: fill the visible area when there is room, otherwise
        // report the full height so the last months can be scrolled to.
        var available = double.IsInfinity(availableSize.Height)
            ? _scroller?.Bounds.Height - Margin.Top - Margin.Bottom ?? 0
            : availableSize.Height;
        return new Size(width, Math.Max(available, needed));
    }

    private ScrollViewer? _scroller;

    // The scroller always offers an unbounded height, so a taller or shorter window would not re-measure the grid on its own.
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _scroller = this.FindAncestorOfType<ScrollViewer>();
        if (_scroller is not null) _scroller.PropertyChanged += OnScrollerPropertyChanged;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_scroller is not null) _scroller.PropertyChanged -= OnScrollerPropertyChanged;
        _scroller = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnScrollerPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == BoundsProperty && e.OldValue is Rect before && e.NewValue is Rect after && before.Height != after.Height)
            InvalidateMeasure();
    }

    private (Rect Rect, DateTime Day)? DayAt(Point p)
    {
        var col = (int)(p.X / CellW);
        var row = (int)(p.Y / CellH);
        if (col < 0 || col >= Columns || row < 0 || row >= Rows) return null;
        var month = row * Columns + col + 1;
        var origin = new Point(col * CellW + Pad, row * CellH + MonthTitleHeight);
        var dw = (CellW - 2 * Pad) / 7;
        var dh = (CellH - MonthTitleHeight - 6) / 7;
        var first = new DateTime(Year, month, 1);
        var offset = (7 + (int)first.DayOfWeek - (int)FirstDayOfWeek) % 7;
        var gx = (int)((p.X - origin.X) / dw);
        var gy = (int)((p.Y - origin.Y) / dh) - 1;
        if (gx < 0 || gx > 6 || gy < 0 || gy > 5) return null;
        var dayNumber = gy * 7 + gx - offset + 1;
        if (dayNumber < 1 || dayNumber > DateTime.DaysInMonth(Year, month)) return null;
        return (new Rect(origin.X + gx * dw, origin.Y + (gy + 1) * dh, dw, dh), new DateTime(Year, month, dayNumber));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (DayAt(e.GetPosition(this)) is { } hit) DayClicked?.Invoke(hit.Day);
    }

    public override void Render(DrawingContext context)
    {
        var dark = ActualThemeVariant == ThemeVariant.Dark;
        var fg = dark ? Brushes.White : Brushes.Black;
        var muted = new SolidColorBrush(Color.Parse(dark ? "#80868B" : "#70757A"));
        var accent = new SolidColorBrush(Color.Parse("#1A73E8"));
        var culture = L.Culture;
        var typeface = new Typeface(AppFonts.Current);
        var withEvents = new HashSet<DateTime>();
        foreach (var ev in Events ?? [])
        {
            var last = ev.IsAllDay || ev.End.TimeOfDay == TimeSpan.Zero ? ev.End.Date.AddDays(-1) : ev.End.Date;
            for (var d = ev.Start.Date; d <= last && (d - ev.Start.Date).TotalDays < 366; d = d.AddDays(1)) withEvents.Add(d);
        }

        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));
        var dw = (CellW - 2 * Pad) / 7;
        var dh = (CellH - MonthTitleHeight - 6) / 7;
        for (var m = 1; m <= 12; m++)
        {
            var col = (m - 1) % Columns;
            var row = (m - 1) / Columns;
            var ox = col * CellW + Pad;
            var oy = row * CellH;
            var title = new FormattedText(culture.TextInfo.ToTitleCase(culture.DateTimeFormat.GetMonthName(m)), culture,
                FlowDirection.LeftToRight, new Typeface(AppFonts.Current, FontStyle.Normal, FontWeight.SemiBold), 14, fg);
            context.DrawText(title, new Point(ox, oy + 4));

            for (var i = 0; i < 7; i++)
            {
                var dow = (DayOfWeek)(((int)FirstDayOfWeek + i) % 7);
                var t = new FormattedText(culture.DateTimeFormat.GetAbbreviatedDayName(dow)[..1].ToUpperInvariant(), culture,
                    FlowDirection.LeftToRight, typeface, 10, muted);
                context.DrawText(t, new Point(ox + i * dw + (dw - t.Width) / 2, oy + MonthTitleHeight + (dh - t.Height) / 2));
            }

            var first = new DateTime(Year, m, 1);
            var offset = (7 + (int)first.DayOfWeek - (int)FirstDayOfWeek) % 7;
            for (var d = 1; d <= DateTime.DaysInMonth(Year, m); d++)
            {
                var idx = offset + d - 1;
                var cx = ox + (idx % 7) * dw + dw / 2;
                var cy = oy + MonthTitleHeight + (idx / 7 + 1) * dh + dh / 2;
                var date = new DateTime(Year, m, d);
                var today = date == DateTime.Today;
                if (today) context.DrawEllipse(accent, null, new Point(cx, cy), Math.Min(dw, dh) / 2 - 1, Math.Min(dw, dh) / 2 - 1);
                var n = new FormattedText(d.ToString(culture), culture, FlowDirection.LeftToRight, typeface, 11, today ? Brushes.White : fg);
                context.DrawText(n, new Point(cx - n.Width / 2, cy - n.Height / 2 - (withEvents.Contains(date) && !today ? 2 : 0)));
                if (withEvents.Contains(date) && !today)
                    context.DrawEllipse(accent, null, new Point(cx, cy + n.Height / 2 - 0.5), 1.8, 1.8);
            }
        }
    }
}
