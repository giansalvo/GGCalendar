using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace GGCalendar.App.Views;

/// <summary>
/// Shown while many changes are sent to Google: how far it is, and a button to stop. It does not block the main window (the sending can
/// last long); closing it does not stop the sending, the button does.
/// </summary>
public sealed class SyncProgressWindow : Window
{
    private readonly TextBlock _text = new() { TextWrapping = TextWrapping.Wrap };
    private readonly ProgressBar _bar = new() { Minimum = 0, Maximum = 1 };
    private readonly Button _stop = new() { Content = "Interrompi" };

    public SyncProgressWindow(Action stop)
    {
        Title = "Invio a Google";
        Width = 420;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _stop.Click += (_, _) =>
        {
            _stop.IsEnabled = false;
            _stop.Content = "Interruzione...";
            _text.Text = "Interruzione dell'invio...";
            stop();
        };
        Content = new StackPanel
        {
            Margin = new Thickness(24),
            Spacing = 12,
            Children =
            {
                _text,
                _bar,
                new TextBlock
                {
                    Text = "Se interrompi, le modifiche non ancora inviate restano in coda e vengono inviate alla prossima sincronizzazione.",
                    TextWrapping = TextWrapping.Wrap,
                    Opacity = 0.7,
                },
                new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { _stop } },
            },
        };
        Update(0, 1);
    }

    public bool StopRequested => !_stop.IsEnabled;

    public void Update(int done, int total)
    {
        if (StopRequested) return;
        _bar.Value = total <= 0 ? 0 : (double)done / total;
        _text.Text = $"Invio a Google: {done} di {total} eventi.";
    }
}
