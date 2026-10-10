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

    private async void OnResyncAll(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel vm) return;
        const string message =
            "GGCalendar scaricherà di nuovo tutti gli eventi di tutti i calendari Google collegati.\n\n" +
            "• Può richiedere del tempo (anche minuti con molti eventi) e usa la connessione internet.\n" +
            "• Gli eventi locali verranno riallineati a Google: le modifiche già inviate ma modificate altrove vengono sovrascritte con la versione di Google.\n" +
            "• Gli eventi che non esistono più su Google verranno rimossi da GGCalendar.\n" +
            "• Le modifiche locali non ancora inviate vengono inviate prima e non vanno perse; se sei offline restano in coda.\n" +
            "• La risincronizzazione parte alla chiusura delle impostazioni.";
        var ok = await new ConfirmWindow("Risincronizzare tutto?", message, "Risincronizza").ShowDialog<bool>(this);
        if (ok) vm.ResyncAll();
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
