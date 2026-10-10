using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using GGCalendar.App.ViewModels;
using GGCalendar.Core.Recurrence;

namespace GGCalendar.App.Views;

public partial class RecurrenceWindow : Window
{
    private readonly DateTime _startDay;

    public RecurrenceWindow() : this(null, DateTime.Today) { }

    public RecurrenceWindow(RecurrenceRule? rule, DateTime startDay)
    {
        InitializeComponent();
        _startDay = startDay;
        DataContext = new RecurrenceViewModel(rule, startDay);
    }

    private void OnOk(object? sender, RoutedEventArgs e) =>
        Close((DataContext as RecurrenceViewModel)?.Build(_startDay));

    private void OnCancel(object? sender, RoutedEventArgs e) => Close((RecurrenceRule?)null);
}
