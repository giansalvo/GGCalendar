using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace GGCalendar.App.Views;

/// <summary>
/// Shown while many changes are sent to Google: how far it is, how long it will take, and two buttons to stop (one keeps what is not
/// sent yet in the queue, the other throws it away). It does not block the main window (the sending can last long); closing it does not
/// stop the sending, the buttons do.
/// </summary>
public sealed class SyncProgressWindow : Window
{
    /// <summary>What is assumed before there is a measure: the speed of the batch requests on the test account (about 12 events a second), a little less to be safe.</summary>
    public const double DefaultEventsPerSecond = 10;

    private readonly TextBlock _text = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _estimate = new() { TextWrapping = TextWrapping.Wrap, Opacity = 0.8 };
    private readonly ProgressBar _bar = new() { Minimum = 0, Maximum = 1 };
    private readonly Button _stop = new() { Content = "Interrompi" };
    private readonly Button _discard = new() { Content = "Interrompi e scarta la coda" };
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private int? _firstDone;
    private TimeSpan _firstAt;

    /// <param name="stop">Stop sending; what is not sent yet stays in the queue.</param>
    /// <param name="discard">Stop sending and throw away what is not sent yet. Asks for confirmation first: returns whether it went ahead.</param>
    public SyncProgressWindow(Action stop, Func<Task<bool>> discard)
    {
        Title = "Invio a Google";
        Width = 460;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _stop.Click += (_, _) =>
        {
            Stopping("Interruzione dell'invio...");
            stop();
        };
        _discard.Click += async (_, _) =>
        {
            if (!await discard()) return;
            Stopping("Interruzione dell'invio e scarto della coda...");
        };
        Content = new StackPanel
        {
            Margin = new Thickness(24),
            Spacing = 12,
            Children =
            {
                _text,
                _bar,
                _estimate,
                new TextBlock
                {
                    Text = "\"Interrompi\" lascia in coda le modifiche non ancora inviate: partiranno alla prossima sincronizzazione. " +
                           "\"Interrompi e scarta la coda\" elimina gli eventi non ancora inviati anche da questo PC.",
                    TextWrapping = TextWrapping.Wrap,
                    Opacity = 0.7,
                },
                new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { _discard, _stop } },
            },
        };
        Update(0, 1);
    }

    private void Stopping(string message)
    {
        _stop.IsEnabled = false;
        _discard.IsEnabled = false;
        _stop.Content = "Interruzione...";
        _text.Text = message;
        _estimate.Text = "";
    }

    public bool StopRequested => !_stop.IsEnabled;

    public void Update(int done, int total)
    {
        if (StopRequested) return;
        _bar.Value = total <= 0 ? 0 : (double)done / total;
        _text.Text = $"Invio a Google: {done} di {total} eventi.";
        _firstDone ??= done;
        _firstAt = _firstAt == default && done == _firstDone ? _clock.Elapsed : _firstAt;
        var measured = _clock.Elapsed - _firstAt;
        _estimate.Text = "Tempo stimato per inviare tutti gli eventi: " + FormatRemaining(EstimateRemaining(done - _firstDone.Value, total - done, measured));
    }

    /// <summary>
    /// How long the events still to send will take. The speed measured since the sending began is used when there is enough of it
    /// (at least 3 seconds and 10 events); before that, <see cref="DefaultEventsPerSecond"/>.
    /// </summary>
    public static TimeSpan EstimateRemaining(int sentSoFar, int remaining, TimeSpan elapsed)
    {
        var perSecond = elapsed >= TimeSpan.FromSeconds(3) && sentSoFar >= 10 ? sentSoFar / elapsed.TotalSeconds : DefaultEventsPerSecond;
        return TimeSpan.FromSeconds(Math.Max(0, remaining) / perSecond);
    }

    public static string FormatRemaining(TimeSpan time)
    {
        if (time < TimeSpan.FromSeconds(60)) return "meno di un minuto";
        var minutes = (int)Math.Round(time.TotalMinutes);
        if (minutes < 60) return $"circa {minutes} minuti";
        return minutes % 60 == 0 ? $"circa {minutes / 60} h" : $"circa {minutes / 60} h {minutes % 60} min";
    }
}
