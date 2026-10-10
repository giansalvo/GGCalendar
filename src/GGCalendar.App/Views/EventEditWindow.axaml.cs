using System.IO;
using System.Linq;
using System.Text;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using GGCalendar.App.ViewModels;
using GGCalendar.Core.Ics;
using GGCalendar.Core.Models;
using GGCalendar.Core.Recurrence;

namespace GGCalendar.App.Views;

public sealed record EventEditResult(CalendarEvent? Event, bool Delete);

public partial class EventEditWindow : Window
{
    public EventEditWindow()
    {
        InitializeComponent();
        Opened += (_, _) => TitleBox.Focus();
        Closed += (_, _) => Vm?.StopSound();
        DataContextChanged += (_, _) =>
        {
            if (Vm is { } vm) vm.CustomRecurrenceRequested += () => OpenCustomRecurrence(vm);
        };
    }

    private async void OpenCustomRecurrence(EventEditViewModel vm)
    {
        var dialog = new RecurrenceWindow(RecurrenceRule.Parse(vm.CurrentRecurrenceRule), vm.StartDay);
        var rule = await dialog.ShowDialog<RecurrenceRule?>(this);
        vm.ApplyCustomRecurrence(rule);
    }

    private EventEditViewModel? Vm => DataContext as EventEditViewModel;

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        if (Vm?.TryBuild() is { } ev)
            Close(new EventEditResult(ev, false));
    }

    private void OnDelete(object? sender, RoutedEventArgs e) => Close(new EventEditResult(null, true));

    private void OnCancel(object? sender, RoutedEventArgs e) => Close((EventEditResult?)null);

    private async void OnExport(object? sender, RoutedEventArgs e)
    {
        if (Vm?.TryBuild() is not { } ev) return;
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Esporta evento",
            SuggestedFileName = string.IsNullOrWhiteSpace(ev.Title) ? "evento" : ev.Title,
            DefaultExtension = "ics",
            FileTypeChoices = [new FilePickerFileType("iCalendar") { Patterns = ["*.ics"] }],
        });
        if (file is null) return;

        await using var stream = await file.OpenWriteAsync();
        stream.SetLength(0);
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        await writer.WriteAsync(IcsWriter.Write([ev]));
    }

    private async void OnCopyMeet(object? sender, RoutedEventArgs e)
    {
        if (Vm?.MeetLink is { Length: > 0 } link && Clipboard is { } clipboard)
            await clipboard.SetTextAsync(link);
    }

    private void OnAttendeeKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        Vm?.AddAttendeeCommand.Execute(null);
        e.Handled = true;
    }

    private void OnRemoveAttendee(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is AttendeeViewModel a) Vm?.RemoveAttendeeCommand.Execute(a);
    }

    private void OnRemoveReminder(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is ReminderItemViewModel r) Vm?.RemoveReminderCommand.Execute(r);
    }

    private void OnPreviewSound(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is ReminderItemViewModel r) Vm?.PreviewSoundCommand.Execute(r);
    }

    private async void OnBrowseSound(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not ReminderItemViewModel r || Vm is null) return;
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Scegli un suono di notifica",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("File audio") { Patterns = ["*.mp3", "*.wav", "*.wma", "*.m4a"] }],
        });
        if (files.FirstOrDefault()?.TryGetLocalPath() is { } path)
        {
            Vm.UseCustomSound(r, path);
            Vm.PreviewSoundCommand.Execute(r);
        }
    }
}
