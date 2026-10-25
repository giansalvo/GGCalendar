using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using GGCalendar.Core.Localization;

namespace GGCalendar.App.Views;

/// <summary>Small modal dialog asking for a single line of text; closes with the entered text or null.</summary>
public sealed class PromptWindow : Window
{
    private readonly TextBox _input;

    public PromptWindow(string title, string label, string initial = "")
    {
        Title = title;
        Width = 400;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _input = new TextBox { Text = initial };
        var ok = new Button { Content = L.T("common.ok"), Classes = { "accent" } };
        var cancel = new Button { Content = L.T("common.cancel"), IsCancel = true };
        Avalonia.Automation.AutomationProperties.SetAutomationId(ok, "Dialog.Ok");
        Avalonia.Automation.AutomationProperties.SetAutomationId(cancel, "Dialog.Cancel");
        ok.Click += (_, _) => Accept();
        cancel.Click += (_, _) => Close((string?)null);
        _input.KeyDown += (_, e) => { if (e.Key == Key.Enter) Accept(); };

        Content = new StackPanel
        {
            Margin = new Thickness(24),
            Spacing = 12,
            Children =
            {
                new TextBlock { Text = label, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                _input,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { cancel, ok },
                },
            },
        };
        Opened += (_, _) => { _input.Focus(); _input.SelectAll(); };
    }

    private void Accept()
    {
        var text = _input.Text?.Trim();
        if (!string.IsNullOrEmpty(text)) Close(text);
    }
}
