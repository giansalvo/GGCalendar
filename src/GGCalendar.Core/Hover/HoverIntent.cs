using System;

namespace GGCalendar.Core.Hover;

/// <summary>Starts the timers of <see cref="HoverIntent{T}"/>; the app uses the dispatcher, a test a clock it moves by hand.</summary>
public interface IHoverClock
{
    /// <summary>Runs <paramref name="action"/> once after <paramref name="delay"/>; disposing the result before then cancels it.</summary>
    IDisposable After(TimeSpan delay, Action action);
}

/// <summary>
/// When the card with the details of an event is shown for the mouse that rests on it and when it goes away (issue #91).
/// The card opens after the mouse has stayed on an event for the opening delay; it stays while the mouse is on the event or on the card
/// (so its buttons can be reached) and closes after the closing delay once the mouse is on neither. A card opened by a click is "pinned":
/// it stays until it is closed from outside (a click elsewhere, Esc, one of its buttons).
/// </summary>
public sealed class HoverIntent<T>
{
    private readonly Func<TimeSpan> _openDelay;
    private readonly Func<TimeSpan> _closeDelay;
    private readonly IHoverClock _clock;
    private readonly Action<T> _show;
    private readonly Action _hide;
    private IDisposable? _openTimer;
    private IDisposable? _closeTimer;

    public HoverIntent(Func<TimeSpan> openDelay, Func<TimeSpan> closeDelay, IHoverClock clock, Action<T> show, Action hide)
    {
        _openDelay = openDelay;
        _closeDelay = closeDelay;
        _clock = clock;
        _show = show;
        _hide = hide;
    }

    /// <summary>True while a card is shown by this object.</summary>
    public bool IsShown { get; private set; }

    /// <summary>True when the shown card was opened by a click: it does not close by itself.</summary>
    public bool IsPinned { get; private set; }

    /// <summary>What the shown card is about (the key given to <see cref="Enter"/> or <see cref="Pin"/>).</summary>
    public object? ShownKey { get; private set; }

    /// <summary>An opening delay of zero or less switches the hover off: the card opens only with a click.</summary>
    public bool Enabled => _openDelay() > TimeSpan.Zero;

    /// <summary>The mouse is on the event <paramref name="key"/>.</summary>
    public void Enter(object key, T payload)
    {
        if (!Enabled) return;
        CancelClose();
        if (IsPinned) return;
        if (IsShown && Equals(ShownKey, key))
        {
            CancelOpen();
            return;
        }

        CancelOpen();
        _openTimer = _clock.After(_openDelay(), () =>
        {
            _openTimer = null;
            _show(payload);
            IsShown = true;
            IsPinned = false;
            ShownKey = key;
        });
    }

    /// <summary>The mouse left the event.</summary>
    public void Leave()
    {
        CancelOpen();
        StartClose();
    }

    /// <summary>The mouse is on the card.</summary>
    public void EnterCard() => CancelClose();

    /// <summary>The mouse left the card.</summary>
    public void LeaveCard() => StartClose();

    /// <summary>A click opened the card of <paramref name="key"/> (already shown by the caller): it stays.</summary>
    public void Pin(object key)
    {
        CancelOpen();
        CancelClose();
        IsShown = true;
        IsPinned = true;
        ShownKey = key;
    }

    /// <summary>The card was closed (a click elsewhere, Esc, a button of the card, another view): nothing is pending any more.</summary>
    public void Dismissed()
    {
        CancelOpen();
        CancelClose();
        IsShown = false;
        IsPinned = false;
        ShownKey = null;
    }

    private void StartClose()
    {
        if (!IsShown || IsPinned) return;
        CancelClose();
        _closeTimer = _clock.After(_closeDelay(), () =>
        {
            _closeTimer = null;
            IsShown = false;
            ShownKey = null;
            _hide();
        });
    }

    private void CancelOpen()
    {
        _openTimer?.Dispose();
        _openTimer = null;
    }

    private void CancelClose()
    {
        _closeTimer?.Dispose();
        _closeTimer = null;
    }
}
