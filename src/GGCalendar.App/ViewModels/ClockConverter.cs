using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace GGCalendar.App.ViewModels;

/// <summary>Maps the 24-hour setting to the identifier expected by TimePicker.</summary>
public sealed class ClockConverter : IValueConverter
{
    public static ClockConverter Instance { get; } = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is false ? "12HourClock" : "24HourClock";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
