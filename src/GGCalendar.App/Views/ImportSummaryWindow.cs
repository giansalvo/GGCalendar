using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using GGCalendar.App.ViewModels;
using GGCalendar.Core.Localization;

namespace GGCalendar.App.Views;

/// <summary>Import summary: new vs already present events, what to do with the latter, and unsupported parts of the file.</summary>
public sealed class ImportSummaryWindow : Window
{
    private readonly RadioButton _skip = new() { Content = L.T("import.skip"), IsChecked = true, GroupName = "dup" };
    private readonly RadioButton _replace = new() { Content = L.T("import.replace"), GroupName = "dup" };
    private readonly RadioButton _copy = new() { Content = L.T("import.copy"), GroupName = "dup" };

    public ImportSummaryWindow(string calendarName, int total, int existing, IReadOnlyList<string> warnings, string? notice = null)
    {
        Title = L.T("import.title");
        Width = 520;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var panel = new StackPanel { Margin = new Thickness(24), Spacing = 12 };
        panel.Children.Add(new TextBlock
        {
            Text = L.N("import.summary", total, calendarName, total - existing, existing),
            TextWrapping = TextWrapping.Wrap,
        });

        if (existing > 0)
        {
            panel.Children.Add(new TextBlock { Text = L.T("import.existing_label"), FontWeight = FontWeight.SemiBold });
            panel.Children.Add(_skip);
            panel.Children.Add(_replace);
            panel.Children.Add(_copy);
        }

        if (notice is not null)
            panel.Children.Add(new TextBlock { Text = notice, TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.Parse("#E37400")) });

        if (warnings.Count > 0)
        {
            panel.Children.Add(new TextBlock { Text = L.T("import.warnings_label"), FontWeight = FontWeight.SemiBold });
            foreach (var w in warnings)
                panel.Children.Add(new TextBlock { Text = "• " + w, TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.Parse("#E37400")) });
        }

        var ok = new Button { Content = L.T("import.button"), Classes = { "accent" } };
        var cancel = new Button { Content = L.T("common.cancel"), IsCancel = true };
        Avalonia.Automation.AutomationProperties.SetAutomationId(ok, "Dialog.Ok");
        Avalonia.Automation.AutomationProperties.SetAutomationId(cancel, "Dialog.Cancel");
        ok.Click += (_, _) => Close(_replace.IsChecked == true ? ImportDuplicates.Replace : _copy.IsChecked == true ? ImportDuplicates.Copy : ImportDuplicates.Skip);
        cancel.Click += (_, _) => Close((ImportDuplicates?)null);
        panel.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Children = { cancel, ok },
        });
        Content = panel;
    }
}
