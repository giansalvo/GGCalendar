using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
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

    public static readonly StyledProperty<DateTime> WeekStartProperty =
        AvaloniaProperty.Register<WeekGrid, DateTime>(nameof(WeekStart), DateTime.Today);

    public static readonly StyledProperty<IReadOnlyList<CalendarEvent>?> EventsProperty =
        AvaloniaProperty.Register<WeekGrid, IReadOnlyList<CalendarEvent>?>(nameof(Events));

    public static readonly StyledProperty<IReadOnlyDictionary<string, string>?> CalendarColorsProperty =
        AvaloniaProperty.Register<WeekGrid, IReadOnlyDictionary<string, string>?>(nameof(CalendarColors));

    public static readonly StyledProperty<int> DayCountProperty =
        AvaloniaProperty.Register<WeekGrid, int>(nameof(DayCount), 7);

    static WeekGrid()
    {
        AffectsRender<WeekGrid>(WeekStartProperty, EventsProperty, CalendarColorsProperty, DayCountProperty);
    }

    public WeekGrid()
    {
        Height = 24 * HourHeight;
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
        var colW = (w - Gutter) / days;

        context.FillRectangle(new SolidColorBrush(bg), new Rect(0, 0, w, h));

        for (var hour = 0; hour < 24; hour++)
        {
            var y = Math.Round(hour * HourHeight) + 0.5;
            if (hour > 0)
            {
                context.DrawLine(linePen, new Point(Gutter - 8, y), new Point(w, y));
                var text = Text($"{hour:00}:00", 11, labelBrush);
                context.DrawText(text, new Point(Gutter - 12 - text.Width, y - text.Height / 2));
            }
        }

        for (var d = 0; d <= days; d++)
        {
            var x = Math.Round(Gutter + d * colW) + 0.5;
            context.DrawLine(linePen, new Point(x, 0), new Point(x, h));
        }

        DrawEvents(context, colW, days);
        DrawNowLine(context, colW, days);
    }

    private void DrawEvents(DrawingContext context, double colW, int days)
    {
        if (Events is null) return;
        var colors = CalendarColors;

        for (var d = 0; d < days; d++)
        {
            var day = WeekStart.Date.AddDays(d);
            var next = day.AddDays(1);
            var items = Events
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

                var hex = colors is not null && colors.TryGetValue(ev.CalendarId, out var c) ? c : "#039BE5";
                var fill = new SolidColorBrush(Color.Parse(hex));
                context.DrawRectangle(fill, new Pen(new SolidColorBrush(ActualThemeVariant == ThemeVariant.Dark ? Color.Parse("#202124") : Colors.White), 1), rect, 6, 6);

                using (context.PushClip(rect.Deflate(new Thickness(6, 3))))
                {
                    var title = Text(ev.Title, 12, Brushes.White, FontWeight.SemiBold);
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
