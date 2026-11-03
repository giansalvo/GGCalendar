using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using Avalonia.Threading;
using GGCalendar.App.Views;
using GGCalendar.Core.Localization;
using GGCalendar.Core.Models;
using GGCalendar.Data;

namespace GGCalendar.App.Services;

/// <summary>
/// The icon in the notification area (issue #30): closing the main window hides it and GGCalendar goes on running, so reminders and alarms keep ringing;
/// the icon (a click) or a second start of the app shows the window again, "Esci" really quits. The first time, a message tells it.
/// </summary>
public sealed class TrayController : IDisposable
{
    private readonly MainWindow _window;
    private readonly SettingsStore _store;
    private readonly Action _shutdown;
    private TrayIcon? _icon;
    private NativeMenuItem? _open;
    private NativeMenuItem? _exit;
    private bool _exiting;

    /// <summary>
    /// Shows the first-time message over the window and returns when the person has read it.
    /// Replaceable: the tests answer without a window to click.
    /// </summary>
    public Func<Window, Task> ShowNotice { get; set; } = window => new WarningWindow(L.T("tray.notice_title"), L.T("tray.notice_text")).ShowDialog(window);

    /// <param name="shutdown">Quits the application (the lifetime's Shutdown; the tests put their own).</param>
    /// <param name="withIcon">False: no icon in the notification area (headless tests have no shell to put it in).</param>
    public TrayController(MainWindow window, SettingsStore store, Action shutdown, bool withIcon = true)
    {
        _window = window;
        _store = store;
        _shutdown = shutdown;
        window.Closing += OnClosing;
        window.PropertyChanged += OnWindowPropertyChanged;
        if (withIcon) CreateIcon();
    }

    /// <summary>True once "Esci" (or the end of Windows) has been asked: the window then closes for real.</summary>
    public bool IsExiting => _exiting;

    /// <summary>True while the window is hidden in the tray.</summary>
    public bool IsHidden => !_window.IsVisible;

    private void CreateIcon()
    {
        _open = new NativeMenuItem(L.T("tray.open"));
        _open.Click += (_, _) => Show();
        _exit = new NativeMenuItem(L.T("tray.exit"));
        _exit.Click += (_, _) => Exit();
        var menu = new NativeMenu();
        menu.Items.Add(_open);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(_exit);
        _icon = new TrayIcon
        {
            Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://GGCalendar.App/Assets/GGCalendar.ico"))),
            ToolTipText = L.T("tray.tooltip"),
            Menu = menu,
            IsVisible = true,
        };
        // A click opens the window, so a double click does too.
        _icon.Clicked += (_, _) => Show();
        // An icon that is not in the icons of the application is never made in the notification area.
        if (Application.Current is { } app)
        {
            var icons = TrayIcon.GetIcons(app) ?? [];
            icons.Add(_icon);
            TrayIcon.SetIcons(app, icons);
        }

        L.Changed += OnLanguageChanged;
    }

    private void OnLanguageChanged()
    {
        if (_open is not null) _open.Header = L.T("tray.open");
        if (_exit is not null) _exit.Header = L.T("tray.exit");
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        // Only the close button (or Alt+F4) hides the window; "Esci", the end of the app and the end of Windows close it.
        if (_exiting || e.CloseReason != WindowCloseReason.WindowClosing) return;
        e.Cancel = true;
        _ = HideAsync();
    }

    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        // Minimizing goes to the tray too, not to the taskbar. The window is put back to normal first, so that the message of the first time
        // is shown over a window that can be seen, and so that it opens normal (not minimized) when it is shown again.
        if (e.Property != Window.WindowStateProperty || _exiting || _hiding || _window.WindowState != WindowState.Minimized) return;
        _window.WindowState = WindowState.Normal;
        _ = HideAsync();
    }

    private bool _hiding;

    private async Task HideAsync()
    {
        if (_hiding) return;
        _hiding = true;
        try
        {
            await HideCoreAsync();
        }
        finally
        {
            _hiding = false;
        }
    }

    private async Task HideCoreAsync()
    {
        var settings = _window.DataContext is ViewModels.MainViewModel vm ? vm.Settings : null;
        if (settings is { TrayNoticeShown: false })
        {
            // Saved before the message is shown, so that it does not come back if the app is ended while it is open.
            settings.TrayNoticeShown = true;
            try { _store.Save(settings); }
            catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException) { }
            await ShowNotice(_window);
        }

        _window.Hide();
    }

    /// <summary>Shows the window again, in front of the others, also when it was minimized.</summary>
    public void Show()
    {
        if (_exiting) return;
        if (!_window.IsVisible) _window.Show();
        if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    /// <summary>Quits GGCalendar for real: the window closes and its timers stop as when the app was closed from the window.</summary>
    public void Exit()
    {
        _exiting = true;
        Dispose();
        _shutdown();
    }

    public void Dispose()
    {
        L.Changed -= OnLanguageChanged;
        if (_icon is not null)
        {
            // Without this the icon stays in the notification area until the pointer passes over it.
            if (Application.Current is { } app && TrayIcon.GetIcons(app) is { } icons) icons.Remove(_icon);
            _icon.IsVisible = false;
            _icon.Dispose();
            _icon = null;
        }
    }
}
