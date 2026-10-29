using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using GGCalendar.App.Services;
using GGCalendar.App.ViewModels;
using GGCalendar.Core.Models;
using GGCalendar.Core.Localization;

namespace GGCalendar.App.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
        // The expander is the last thing of the window: opened, its fields are below the edge, so the window scrolls down to show them.
        OauthExpander.PropertyChanged += (_, e) =>
        {
            if (e.Property == Expander.IsExpandedProperty && OauthExpander.IsExpanded)
                Avalonia.Threading.Dispatcher.UIThread.Post(() => SettingsScroll.ScrollToEnd(), Avalonia.Threading.DispatcherPriority.Background);
        };
    }

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        if ((DataContext as SettingsViewModel)?.TryBuild() is { } settings)
            Close(settings);
    }

    /// <summary>"Scollega": the person is told what goes away from this PC and confirms (the same question as in the menu of the account).</summary>
    private async void OnDisconnectAccount(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel vm || (sender as Control)?.Tag is not string email) return;
        var ok = await new ConfirmWindow(AccountDisconnect.Title, AccountDisconnect.Text(email, vm.DescribeAccount(email)), AccountDisconnect.Button).ShowDialog<bool>(this);
        if (ok) await vm.RemoveGoogleAccountCommand.ExecuteAsync(email);
    }

    private async void OnResyncAll(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel vm) return;
        var ok = await new ConfirmWindow(L.T("settings.resync_title"), L.T("settings.resync_confirm"), L.T("settings.resync_button")).ShowDialog<bool>(this);
        if (ok) vm.ResyncAll();
    }

    private async void OnBrowseSound(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel vm) return;
        if (await SoundPicker.PickAsync(this) is not { } path) return;
        if ((sender as Control)?.Tag as string == "alarm")
        {
            vm.AlarmSound = vm.AddCustomSound(path);
            vm.PreviewAlarmSoundCommand.Execute(null);
        }
        else
        {
            vm.DefaultSound = vm.AddCustomSound(path);
            vm.PreviewDefaultSoundCommand.Execute(null);
        }
    }

    protected override void OnClosed(System.EventArgs e)
    {
        (DataContext as SettingsViewModel)?.StopSound();
        base.OnClosed(e);
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close((AppSettings?)null);
}
