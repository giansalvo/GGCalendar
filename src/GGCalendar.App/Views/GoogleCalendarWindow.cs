using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using GGCalendar.App.ViewModels;
using GGCalendar.Core.Localization;

namespace GGCalendar.App.Views;

/// <summary>What the window gives back: the account, the name and the colour of the calendar to create on Google.</summary>
public sealed record GoogleCalendarRequest(string Email, string Name, string ColorHex);

/// <summary>
/// Asks for the name and the colour of a new calendar of a Google account (the account too, when it was not chosen by the menu and there are
/// several). "Create" stays off while the name is empty. Nothing is sent to Google here: the caller does it.
/// </summary>
public sealed class GoogleCalendarWindow : Window
{
    private readonly TextBox _name = new() { MaxLength = 255 };
    private readonly ComboBox _account = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly Button _create = new() { Content = L.T("main.create"), IsEnabled = false, Classes = { "accent" } };
    private readonly Button _cancel = new() { Content = L.T("common.cancel"), IsCancel = true };
    private readonly Button[] _swatches;
    private string _color;

    /// <param name="accounts">The connected accounts.</param>
    /// <param name="account">The account chosen by the menu it was opened from, or null to ask for it (when there are several).</param>
    public GoogleCalendarWindow(IReadOnlyList<string> accounts, string? account, string defaultColor)
    {
        _color = defaultColor;
        Title = L.T("gcal.title");
        Width = 460;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        AutomationProperties.SetAutomationId(_name, "Gcal.Name");
        AutomationProperties.SetAutomationId(_account, "Gcal.Account");
        AutomationProperties.SetAutomationId(_create, "Gcal.Create");
        AutomationProperties.SetAutomationId(_cancel, "Dialog.Cancel");

        _account.ItemsSource = accounts;
        _account.SelectedItem = account is not null && accounts.Contains(account) ? account : accounts.FirstOrDefault();
        var askAccount = account is null && accounts.Count > 1;

        _swatches = MainViewModel.CalendarPalette.Select((hex, i) =>
        {
            var b = new Button
            {
                Width = 28, Height = 28, CornerRadius = new CornerRadius(14), Padding = new Thickness(0),
                Background = new SolidColorBrush(Color.Parse(hex)), Tag = hex,
            };
            AutomationProperties.SetAutomationId(b, "Gcal.Color." + i);
            b.Click += (_, _) => { _color = hex; ShowColor(); };
            return b;
        }).ToArray();
        ShowColor();
        var palette = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var s in _swatches) { s.Margin = new Thickness(0, 0, 6, 6); palette.Children.Add(s); }

        var root = new StackPanel { Margin = new Thickness(24), Spacing = 12 };
        if (askAccount) root.Children.Add(Labeled(L.T("gcal.account_label"), _account));
        root.Children.Add(Labeled(L.T("gcal.name_label"), _name));
        root.Children.Add(Labeled(L.T("sub.color"), palette));
        root.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { _cancel, _create } });
        Content = root;

        _name.TextChanged += (_, _) => _create.IsEnabled = !string.IsNullOrWhiteSpace(_name.Text);
        _name.KeyDown += (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; Accept(); } };
        _create.Click += (_, _) => Accept();
        _cancel.Click += (_, _) => Close(null);
        Opened += (_, _) => _name.Focus();
    }

    private static StackPanel Labeled(string label, Control control) => new()
    {
        Spacing = 4,
        Children = { new TextBlock { Text = label, FontWeight = FontWeight.SemiBold }, control },
    };

    private void ShowColor()
    {
        foreach (var b in _swatches)
        {
            var on = string.Equals((string?)b.Tag, _color, StringComparison.OrdinalIgnoreCase);
            b.BorderThickness = new Thickness(on ? 3 : 1);
            b.BorderBrush = on ? Brushes.White : new SolidColorBrush(Color.Parse("#55808080"));
        }
    }

    private void Accept()
    {
        if (!_create.IsEnabled || _account.SelectedItem is not string email) return;
        Close(new GoogleCalendarRequest(email, _name.Text!.Trim(), _color));
    }
}
