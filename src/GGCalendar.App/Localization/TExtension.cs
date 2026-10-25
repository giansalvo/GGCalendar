using System;
using Avalonia.Data;
using Avalonia.Markup.Xaml;

namespace GGCalendar.App.Localization;

/// <summary>
/// <c>{loc:T main.today}</c>: the text for a key, shown again in the new language when the user changes it.
/// </summary>
public sealed class TExtension(string key) : MarkupExtension
{
    public string Key { get; set; } = key;

    public override object ProvideValue(IServiceProvider serviceProvider) =>
        new Binding($"[{Key}]") { Source = LocSource.Instance, Mode = BindingMode.OneWay };
}
