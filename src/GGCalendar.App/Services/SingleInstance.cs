using System;
using System.Threading;

namespace GGCalendar.App.Services;

/// <summary>
/// One GGCalendar per person and per Windows session (issue #30): with the window hidden in the tray a second start is easy,
/// and two copies would ring every alarm twice and write the same database. The second start does not open anything: it asks the first to show its window and ends.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    public const string DefaultName = "GGCalendar.SingleInstance.6F1D2C0B";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _signal;
    private readonly CancellationTokenSource _stop = new();
    private Thread? _listener;

    private SingleInstance(Mutex mutex, EventWaitHandle signal)
    {
        _mutex = mutex;
        _signal = signal;
    }

    /// <summary>Raised, on another thread, when another start asked this copy to show its window.</summary>
    public event Action? Activated;

    /// <summary>
    /// Takes the place of the one copy, or, if another copy is already running, asks it to show its window and returns null.
    /// <paramref name="name"/> is the same for every copy of the app; the tests use their own.
    /// </summary>
    public static SingleInstance? TryAcquire(string name = DefaultName)
    {
        var mutex = new Mutex(true, @"Local\" + name, out var first);
        var signal = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\" + name + ".Show");
        if (!first)
        {
            // The signal stays set until the first copy takes it, even if that copy is still starting.
            signal.Set();
            signal.Dispose();
            mutex.Dispose();
            return null;
        }

        return new SingleInstance(mutex, signal);
    }

    /// <summary>Starts to listen for the other starts; call it when the window can be shown.</summary>
    public void StartListening()
    {
        if (_listener is not null) return;
        _listener = new Thread(Listen) { IsBackground = true, Name = "GGCalendar single instance" };
        _listener.Start();
    }

    private void Listen()
    {
        var handles = new[] { _signal, _stop.Token.WaitHandle };
        while (WaitHandle.WaitAny(handles) == 0) Activated?.Invoke();
    }

    public void Dispose()
    {
        _stop.Cancel();
        _listener?.Join(1000);
        _signal.Dispose();
        _mutex.Dispose();
        _stop.Dispose();
    }
}
