using System.Collections.Concurrent;
using System.ComponentModel;
using GGCalendar.Core.Localization;

namespace GGCalendar.App.Localization;

/// <summary>
/// The text for one key as a binding source: <see cref="Value"/> is the text in the language shown now and raises a change (so every
/// binding to it is refreshed) when the language changes. One object is shared by all the bindings to the same key.
/// </summary>
public sealed class LocText : INotifyPropertyChanged
{
    private static readonly ConcurrentDictionary<string, LocText> Cache = new();

    private readonly string _key;

    private LocText(string key)
    {
        _key = key;
        L.Changed += () => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
    }

    public static LocText For(string key) => Cache.GetOrAdd(key, k => new LocText(k));

    public string Value => L.T(_key);

    public event PropertyChangedEventHandler? PropertyChanged;
}
