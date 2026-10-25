using System;
using Avalonia.Controls;
using Avalonia.Threading;
using GGCalendar.Core.Localization;

namespace GGCalendar.App.Localization;

/// <summary>For controls built in code: their texts are set again when the user changes the language, as long as the control is on screen.</summary>
public static class LocBind
{
    /// <summary>Runs <paramref name="apply"/> now and every time the language changes while the control is loaded.</summary>
    public static T Live<T>(this T control, Action<T> apply) where T : Control
    {
        void Run() => apply(control);
        Action handler = () => Dispatcher.UIThread.Post(Run);
        control.Loaded += (_, _) =>
        {
            L.Changed -= handler;
            L.Changed += handler;
            Run();
        };
        control.Unloaded += (_, _) => L.Changed -= handler;
        Run();
        return control;
    }

    /// <summary>A text block whose text is the key's text (with its arguments), shown again after a language change.</summary>
    public static TextBlock Text(this TextBlock block, string key, params object?[] args) => block.Live(b => b.Text = L.T(key, args));
}
