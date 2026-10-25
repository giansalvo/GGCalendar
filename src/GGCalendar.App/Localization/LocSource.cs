using System.ComponentModel;
using GGCalendar.Core.Localization;

namespace GGCalendar.App.Localization;

/// <summary>
/// The texts as a binding source: <c>[key]</c> is the text for the key and changes (every binding is refreshed) when the language changes.
/// </summary>
public sealed class LocSource : INotifyPropertyChanged
{
    public static LocSource Instance { get; } = new();

    private LocSource() => L.Changed += () => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));

    public string this[string key] => L.T(key);

    public event PropertyChangedEventHandler? PropertyChanged;
}
