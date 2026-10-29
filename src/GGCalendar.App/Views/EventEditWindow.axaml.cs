using System;
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
using GGCalendar.Core.Localization;

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

    private async void OnOpenMeet(object? sender, RoutedEventArgs e)
    {
        // Only web links are opened: the value comes from Google, but it is still data from outside the app.
        if (Uri.TryCreate(Vm?.MeetLink, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)
            await Launcher.LaunchUriAsync(uri);
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

    private void OnAttachmentKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        AddAttachment();
        e.Handled = true;
    }

    private void OnAddAttachment(object? sender, RoutedEventArgs e) => AddAttachment();

    /// <summary>Adds the attachment of the boxes and, if it was added, leaves the cursor in the box of the next one, in view.</summary>
    private void AddAttachment()
    {
        Vm?.AddAttachmentCommand.Execute(null);
        if (Vm is { AttachmentError.Length: 0 })
        {
            var box = this.FindControl<TextBox>("AttachmentUrlBox");
            Avalonia.Threading.Dispatcher.UIThread.Post(() => { box?.BringIntoView(); box?.Focus(); });
        }
    }

    private async void OnOpenAttachment(object? sender, RoutedEventArgs e)
    {
        // An address that comes from outside the app (Google, a file): the person sees the real one and decides.
        if ((sender as Control)?.DataContext is not EventAttachment { CanOpen: true } attachment) return;
        var uri = new Uri(attachment.Url);
        var sure = await new ConfirmWindow(L.T("confirm.open_link_title"), L.T("confirm.open_link_text", uri.AbsoluteUri), L.T("confirm.open_link_button")).ShowDialog<bool>(this);
        if (sure) await Launcher.LaunchUriAsync(uri);
    }

    private void OnRemoveAttachment(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is EventAttachment a) Vm?.RemoveAttachmentCommand.Execute(a);
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
        if (await SoundPicker.PickAsync(this) is { } path)
        {
            Vm.UseCustomSound(r, path);
            Vm.PreviewSoundCommand.Execute(r);
        }
    }
}
