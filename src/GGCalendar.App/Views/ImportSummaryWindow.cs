using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using GGCalendar.App.ViewModels;

namespace GGCalendar.App.Views;

/// <summary>Import summary: new vs already present events, what to do with the latter, and unsupported parts of the file.</summary>
public sealed class ImportSummaryWindow : Window
{
    private readonly RadioButton _skip = new() { Content = "Salta quelli già presenti (consigliato)", IsChecked = true, GroupName = "dup" };
    private readonly RadioButton _replace = new() { Content = "Sostituisci quelli già presenti con la versione del file", GroupName = "dup" };
    private readonly RadioButton _copy = new() { Content = "Importa comunque come copie", GroupName = "dup" };

    public ImportSummaryWindow(string calendarName, int total, int existing, IReadOnlyList<string> warnings, string? notice = null)
    {
        Title = "Importa file .ics";
        Width = 520;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var panel = new StackPanel { Margin = new Thickness(24), Spacing = 12 };
        panel.Children.Add(new TextBlock
        {
            Text = $"Calendario di destinazione: \"{calendarName}\"\n{total} eventi nel file: {total - existing} nuovi, {existing} già presenti.",
            TextWrapping = TextWrapping.Wrap,
        });

        if (existing > 0)
        {
            panel.Children.Add(new TextBlock { Text = "Eventi già presenti:", FontWeight = FontWeight.SemiBold });
            panel.Children.Add(_skip);
            panel.Children.Add(_replace);
            panel.Children.Add(_copy);
        }

        if (notice is not null)
            panel.Children.Add(new TextBlock { Text = notice, TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.Parse("#E37400")) });

        if (warnings.Count > 0)
        {
            panel.Children.Add(new TextBlock { Text = "Attenzione, parti del file non supportate:", FontWeight = FontWeight.SemiBold });
            foreach (var w in warnings)
                panel.Children.Add(new TextBlock { Text = "• " + w, TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.Parse("#E37400")) });
        }

        var ok = new Button { Content = "Importa", Classes = { "accent" } };
        var cancel = new Button { Content = "Annulla", IsCancel = true };
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
