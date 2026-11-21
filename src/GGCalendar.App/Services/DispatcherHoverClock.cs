using System;
using Avalonia.Threading;
using GGCalendar.Core.Hover;

namespace GGCalendar.App.Services;

/// <summary>The timers of the hover card (issue #91), run by the dispatcher of the window.</summary>
public sealed class DispatcherHoverClock : IHoverClock
{
    public IDisposable After(TimeSpan delay, Action action)
    {
        if (delay <= TimeSpan.Zero)
        {
            action();
            return new Noop();
        }

        var timer = new DispatcherTimer { Interval = delay };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            action();
        };
        timer.Start();
        return new Stopper(timer);
    }

    private sealed class Noop : IDisposable
    {
        public void Dispose() { }
    }

    private sealed class Stopper(DispatcherTimer timer) : IDisposable
    {
        public void Dispose() => timer.Stop();
    }
}
