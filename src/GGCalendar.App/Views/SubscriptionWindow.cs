using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using GGCalendar.App.Services;
using GGCalendar.App.ViewModels;
using GGCalendar.Core.Localization;
using GGCalendar.Core.Models;
using GGCalendar.Core.Subscriptions;

namespace GGCalendar.App.Views;

/// <summary>What the window of a subscription gives back: the choices, and the calendar when it was added.</summary>
public sealed record SubscriptionEdit(string Name, string ColorHex, SubscriptionRange Range, CalendarInfo? Added = null);

/// <summary>
/// Adds a public calendar by its address (paste, check, preview, name, colour, days to keep) or, with <c>editing</c>, changes the name,
/// the colour and the days to keep of one already followed. Nothing is saved until "Add".
/// </summary>
public sealed class SubscriptionWindow : Window
{
    private readonly SubscriptionService _service;
    private readonly bool _editing;

    private readonly TextBox _url = new() { Watermark = "https://…", Name = "UrlBox" };
    private readonly Button _paste = new() { Content = L.T("sub.paste") };
    private readonly Button _check = new() { Content = L.T("sub.check") };
    private readonly TextBlock _progress = new() { Text = L.T("sub.reading"), IsVisible = false, Opacity = 0.75 };
    private readonly SelectableTextBlock _error = new() { Foreground = new SolidColorBrush(Color.Parse("#D93025")), TextWrapping = TextWrapping.Wrap, IsVisible = false };
    private readonly SelectableTextBlock _previewText = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBox _name = new();
    private readonly CalendarDatePicker _from = new();
    private readonly CalendarDatePicker _to = new();
    private readonly Button _add;
    private readonly Button _cancel = new() { Content = L.T("common.cancel"), IsCancel = true };
    private readonly StackPanel _details = new() { Spacing = 12, IsVisible = false };
    private readonly Button[] _swatches;

    private CancellationTokenSource? _cts;
    private SubscriptionPreview? _preview;
    private string? _previewFor;
    private string _color;

    public SubscriptionWindow(SubscriptionService service, string defaultColor, CalendarInfo? editing = null, SubscriptionRecord? record = null)
    {
        _service = service;
        _editing = editing is not null;
        _color = editing?.ColorHex ?? defaultColor;
        Title = L.T(_editing ? "sub.edit_title" : "sub.dialog_title");
        Width = 560;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        AutomationProperties.SetAutomationId(_url, "Sub.Url");
        AutomationProperties.SetAutomationId(_paste, "Sub.Paste");
        AutomationProperties.SetAutomationId(_check, "Sub.Check");
        AutomationProperties.SetAutomationId(_progress, "Sub.Progress");
        AutomationProperties.SetAutomationId(_error, "Sub.Error");
        AutomationProperties.SetAutomationId(_previewText, "Sub.Preview");
        AutomationProperties.SetAutomationId(_name, "Sub.Name");
        AutomationProperties.SetAutomationId(_from, "Sub.From");
        AutomationProperties.SetAutomationId(_to, "Sub.To");
        AutomationProperties.SetAutomationId(_cancel, "Dialog.Cancel");

        _add = new Button { Content = L.T(_editing ? "common.save" : "sub.add"), IsEnabled = _editing, Classes = { "accent" } };
        AutomationProperties.SetAutomationId(_add, _editing ? "Sub.Save" : "Sub.Add");

        _swatches = MainViewModel.CalendarPalette.Select((hex, i) =>
        {
            var b = new Button
            {
                Width = 28, Height = 28, CornerRadius = new CornerRadius(14), Padding = new Thickness(0),
                Background = new SolidColorBrush(Color.Parse(hex)), Tag = hex,
            };
            AutomationProperties.SetAutomationId(b, "Sub.Color." + i);
            b.Click += (_, _) => { _color = hex; ShowColor(); };
            return b;
        }).ToArray();
        ShowColor();

        var palette = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var s in _swatches) { s.Margin = new Thickness(0, 0, 6, 6); palette.Children.Add(s); }

        var range = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,*"), RowDefinitions = new RowDefinitions("Auto"), ColumnSpacing = 8 };
        range.Children.Add(new TextBlock { Text = L.T("sub.range_from"), VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(_from, 1);
        var toLabel = new TextBlock { Text = L.T("sub.range_to"), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(toLabel, 2);
        Grid.SetColumn(_to, 3);
        _from.HorizontalAlignment = HorizontalAlignment.Stretch;
        _to.HorizontalAlignment = HorizontalAlignment.Stretch;
        range.Children.Add(_from);
        range.Children.Add(toLabel);
        range.Children.Add(_to);

        if (!_editing) _details.Children.Add(_previewText);
        _details.Children.Add(Labeled(L.T("sub.name"), _name));
        _details.Children.Add(Labeled(L.T("sub.color"), palette));
        _details.Children.Add(new StackPanel
        {
            Spacing = 4,
            Children =
            {
                new TextBlock { Text = L.T("sub.range_title"), FontWeight = FontWeight.SemiBold },
                range,
                new TextBlock { Text = L.T("sub.range_hint"), Opacity = 0.65, FontSize = 12 },
            },
        });

        var urlRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = 8, IsVisible = !_editing };
        Grid.SetColumn(_paste, 1);
        Grid.SetColumn(_check, 2);
        urlRow.Children.Add(_url);
        urlRow.Children.Add(_paste);
        urlRow.Children.Add(_check);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { _cancel, _add } };
        var root = new StackPanel { Margin = new Thickness(24), Spacing = 12 };
        if (!_editing) root.Children.Add(new TextBlock { Text = L.T("sub.url_label"), TextWrapping = TextWrapping.Wrap });
        root.Children.Add(urlRow);
        root.Children.Add(_progress);
        root.Children.Add(_error);
        root.Children.Add(_details);
        root.Children.Add(buttons);
        Content = root;

        if (_editing)
        {
            _name.Text = editing!.Name;
            _from.SelectedDate = record?.RangeFrom;
            _to.SelectedDate = record?.RangeTo;
            _details.IsVisible = true;
        }

        _paste.Click += async (_, _) => await PasteAsync();
        _check.Click += async (_, _) => await CheckAsync();
        _url.KeyDown += async (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; await CheckAsync(); } };
        _url.TextChanged += (_, _) => { if (!_editing && _preview is not null && _url.Text != _previewFor) { _preview = null; _details.IsVisible = false; Update(); } };
        _from.SelectedDateChanged += (_, _) => RangeChanged();
        _to.SelectedDateChanged += (_, _) => RangeChanged();
        _name.TextChanged += (_, _) => Update();
        _cancel.Click += (_, _) =>
        {
            if (_cts is not null) _cts.Cancel(); // while the calendar is being read, Cancel stops the request
            else Close(null);
        };
        _add.Click += (_, _) => Accept();
        Opened += (_, _) => (_editing ? _name : _url).Focus();
        Closed += (_, _) => _cts?.Cancel();
    }

    private static StackPanel Labeled(string label, Control control) => new()
    {
        Spacing = 4,
        Children = { new TextBlock { Text = label, FontWeight = FontWeight.SemiBold }, control },
    };

    private SubscriptionRange Range => new(_from.SelectedDate, _to.SelectedDate);

    private bool RangeIsValid => _from.SelectedDate is not { } f || _to.SelectedDate is not { } t || f <= t;

    private void ShowColor()
    {
        foreach (var b in _swatches)
        {
            var on = string.Equals((string?)b.Tag, _color, StringComparison.OrdinalIgnoreCase);
            b.BorderThickness = new Thickness(on ? 3 : 1);
            b.BorderBrush = on ? Brushes.White : new SolidColorBrush(Color.Parse("#55808080"));
        }
    }

    private async Task PasteAsync()
    {
        if (Clipboard is not { } clipboard) return;
        var text = await Avalonia.Input.Platform.ClipboardExtensions.TryGetTextAsync(clipboard);
        if (!string.IsNullOrWhiteSpace(text)) _url.Text = text.Trim();
    }

    private void Fail(Msg message)
    {
        _error.Text = message.Text;
        _error.IsVisible = true;
    }

    private async Task CheckAsync()
    {
        if (_cts is not null) return;
        _error.IsVisible = false;
        _preview = null;
        _details.IsVisible = false;
        _check.IsEnabled = false;
        _progress.IsVisible = true;
        Update();
        _cts = new CancellationTokenSource();
        try
        {
            var result = await _service.PreviewAsync(_url.Text ?? "", _cts.Token);
            if (result.Problem is { } problem)
            {
                Fail(SubscriptionMessages.Error(problem));
                return;
            }

            _preview = result.Preview;
            _previewFor = _url.Text;
            _name.Text = _preview!.SuggestedName;
            _details.IsVisible = true;
            ShowPreview();
        }
        catch (OperationCanceledException)
        {
            // Cancel pressed while reading: the window stays open and nothing is shown.
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            _progress.IsVisible = false;
            _check.IsEnabled = true;
            Update();
        }
    }

    private void RangeChanged()
    {
        if (_preview is not null) ShowPreview();
        else Update();
    }

    /// <summary>Counts the file again for the days chosen; the number in the window follows the choice.</summary>
    private void ShowPreview()
    {
        _error.IsVisible = false;
        if (_preview is null) return;
        if (!RangeIsValid)
        {
            Fail(Msg.Of("sub.range_invalid"));
            Update();
            return;
        }

        var snap = _preview.Recount(Range).Snapshot;
        if (snap.Problem is { } problem)
        {
            _previewText.Text = "";
            Fail(SubscriptionMessages.Error(problem));
            Update();
            return;
        }

        var text = L.N("sub.preview_events", snap.Events.Count(e => e.SeriesMasterId is null));
        if (snap.FirstStart is { } first && snap.LastEnd is { } last)
            text += " · " + L.T("sub.preview_period", first.ToString("d", L.Culture), last.AddSeconds(-1).ToString("d", L.Culture));
        if (snap.Report.OrphanOccurrences > 0) text += "\n" + L.N("sub.preview_orphans", snap.Report.OrphanOccurrences);
        if (snap.Report.IgnoredComponents.Count > 0)
            text += "\n" + L.T("sub.preview_ignored", string.Join(", ", snap.Report.IgnoredComponents.Select(kv => $"{kv.Key} ×{kv.Value}")));
        _previewText.Text = text;
        Update();
    }

    private void Update()
    {
        var ready = _editing ? RangeIsValid : _preview is not null && RangeIsValid && _preview.Recount(Range).Snapshot.Problem is null;
        _add.IsEnabled = ready && !string.IsNullOrWhiteSpace(_name.Text);
    }

    private void Accept()
    {
        if (!_add.IsEnabled) return;
        if (_editing)
        {
            Close(new SubscriptionEdit(_name.Text!.Trim(), _color, Range));
            return;
        }

        var (calendar, problem) = _service.Add(_preview!, _name.Text!, _color, Range);
        if (problem is { } p)
        {
            Fail(SubscriptionMessages.Error(p));
            return;
        }

        Close(new SubscriptionEdit(calendar!.Name, calendar.ColorHex, Range, calendar));
    }
}
