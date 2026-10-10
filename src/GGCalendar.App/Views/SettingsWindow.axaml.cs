using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using GGCalendar.App.ViewModels;
using GGCalendar.Core.Models;

namespace GGCalendar.App.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
    }

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        if ((DataContext as SettingsViewModel)?.TryBuild() is { } settings)
            Close(settings);
    }

    private async void OnBrowseSound(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel vm) return;
        var files = await StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
        {
            Title = "Scegli un suono di notifica",
            AllowMultiple = false,
            FileTypeFilter = [new Avalonia.Platform.Storage.FilePickerFileType("File audio") { Patterns = ["*.mp3", "*.wav", "*.wma", "*.m4a"] }],
        });
        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path)
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
