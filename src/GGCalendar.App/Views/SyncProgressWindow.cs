using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using GGCalendar.App.Localization;
using GGCalendar.Core.Localization;

namespace GGCalendar.App.Views;

/// <summary>
/// Shown while many changes are sent to Google: how far it is, how long it will take, and two buttons to stop (one keeps what is not
/// sent yet in the queue, the other throws it away). It does not block the main window (the sending can last long); closing it does not
/// stop the sending, the buttons do. Its texts are shown again when the language changes.
/// </summary>
public sealed class SyncProgressWindow : Window
{
    /// <summary>What is assumed before there is a measure: the speed of the batch requests on the test account (about 12 events a second), a little less to be safe.</summary>
    public const double DefaultEventsPerSecond = 10;

    private enum Mode { Sending, Stopping, Discarding }

    private readonly TextBlock _text = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _estimate = new() { TextWrapping = TextWrapping.Wrap, Opacity = 0.8 };
    private readonly ProgressBar _bar = new() { Minimum = 0, Maximum = 1 };
    private readonly Button _stop = new() { Name = "SyncStopButton" };
    private readonly Button _discard = new() { Name = "SyncDiscardButton" };
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private int? _firstDone;
    private TimeSpan _firstAt;
    private Mode _mode = Mode.Sending;
    private int _done;
    private int _total = 1;
    private TimeSpan _remaining;

    /// <param name="stop">Stop sending; what is not sent yet stays in the queue.</param>
    /// <param name="discard">Stop sending and throw away what is not sent yet. Asks for confirmation first: returns whether it went ahead.</param>
    public SyncProgressWindow(Action stop, Func<Task<bool>> discard)
    {
        Width = 460;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Avalonia.Automation.AutomationProperties.SetAutomationId(_stop, "Sync.Stop");
        Avalonia.Automation.AutomationProperties.SetAutomationId(_discard, "Sync.Discard");
        _stop.Click += (_, _) =>
        {
            Stopping(Mode.Stopping);
            stop();
        };
        _discard.Click += async (_, _) =>
        {
            if (!await discard()) return;
            Stopping(Mode.Discarding);
        };
        var help = new TextBlock { TextWrapping = TextWrapping.Wrap, Opacity = 0.7 };
        Content = new StackPanel
        {
            Margin = new Thickness(24),
            Spacing = 12,
            Children =
            {
                _text,
                _bar,
                _estimate,
                help,
                new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { _discard, _stop } },
            },
        };
        this.Live(w => w.Title = L.T("sync.title"));
        help.Live(h => h.Text = L.T("sync.help"));
        _text.Live(_ => Render());
        Render();
    }

    private void Stopping(Mode mode)
    {
        _mode = mode;
        _stop.IsEnabled = false;
        _discard.IsEnabled = false;
        Render();
    }

    /// <summary>Sets the texts of the window for what it is doing now, in the language shown now.</summary>
    private void Render()
    {
        _discard.Content = L.T("sync.discard_button");
        _stop.Content = L.T(_mode == Mode.Sending ? "sync.stop_button" : "sync.stopping_button");
        switch (_mode)
        {
            case Mode.Stopping:
                _text.Text = L.T("sync.stopping");
                _estimate.Text = "";
                break;
            case Mode.Discarding:
                _text.Text = L.T("sync.stopping_discard");
                _estimate.Text = "";
                break;
            default:
                _bar.Value = _total <= 0 ? 0 : (double)_done / _total;
                _text.Text = L.T("sync.progress", _done, _total);
                _estimate.Text = L.T("sync.estimate", FormatRemaining(_remaining));
                break;
        }
    }

    public bool StopRequested => !_stop.IsEnabled;

    public void Update(int done, int total)
    {
        if (StopRequested) return;
        _done = done;
        _total = total;
        _firstDone ??= done;
        _firstAt = _firstAt == default && done == _firstDone ? _clock.Elapsed : _firstAt;
        var measured = _clock.Elapsed - _firstAt;
        _remaining = EstimateRemaining(done - _firstDone.Value, total - done, measured);
        Render();
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
        if (time < TimeSpan.FromSeconds(60)) return L.T("time.less_than_a_minute");
        var minutes = (int)Math.Round(time.TotalMinutes);
        if (minutes < 60) return L.T("time.about_minutes", minutes);
        return minutes % 60 == 0 ? L.T("time.about_hours", minutes / 60) : L.T("time.about_hours_minutes", minutes / 60, minutes % 60);
    }
}
