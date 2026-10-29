using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Fonts;

namespace GGCalendar.App.Services;

/// <summary>
/// The font of the text of the app (issue #42): three open source fonts come with it (Inter, Roboto, Plus Jakarta Sans, all SIL OFL,
/// the licences are in Assets/FontLicenses) and the person can pick any other font of the system in the settings.
/// </summary>
public static class AppFonts
{
    /// <summary>The font used when nothing is chosen, or when the chosen one is not there any more.</summary>
    public const string DefaultName = "Inter";

    /// <summary>The fonts that come with the app: the name shown in the settings and the key Avalonia knows the collection by.</summary>
    public static readonly IReadOnlyList<(string Name, string Key)> Bundled =
    [
        (DefaultName, "fonts:Inter#Inter"),
        ("Roboto", "fonts:Roboto#Roboto"),
        ("Plus Jakarta Sans", "fonts:PlusJakartaSans#Plus Jakarta Sans"),
    ];

    // Fonts made of symbols or icons: the text of the app would be unreadable in them.
    private static readonly string[] Symbolic =
        ["symbol", "wingdings", "webdings", "marlett", "dingbats", "emoji", "icons", "mdl2", "ornament", "holomdl"];

    /// <summary>The font the text is drawn in now. The grids drawn by hand read it every time they draw.</summary>
    public static FontFamily Current { get; private set; } = new(Bundled[0].Key);

    /// <summary>Registers the fonts that come with the app (and Inter, as before) in the application.</summary>
    public static AppBuilder WithAppFonts(this AppBuilder builder) => builder
        .WithInterFont()
        .ConfigureFonts(manager =>
        {
            manager.AddFontCollection(new EmbeddedFontCollection(
                new Uri("fonts:Roboto", UriKind.Absolute), new Uri("avares://GGCalendar.App/Assets/Fonts/Roboto", UriKind.Absolute)));
            manager.AddFontCollection(new EmbeddedFontCollection(
                new Uri("fonts:PlusJakartaSans", UriKind.Absolute), new Uri("avares://GGCalendar.App/Assets/Fonts/PlusJakartaSans", UriKind.Absolute)));
        });

    private static bool IsBundled(string name) => Bundled.Any(b => b.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    private static bool IsSymbolic(string name) => Symbolic.Any(s => name.Contains(s, StringComparison.OrdinalIgnoreCase));

    /// <summary>The name to use: the chosen one if it is one of ours or is installed, otherwise the default one.</summary>
    public static string Normalize(string? name, IEnumerable<string> systemFonts)
    {
        if (string.IsNullOrWhiteSpace(name)) return DefaultName;
        name = name.Trim();
        if (Bundled.FirstOrDefault(b => b.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).Name is { } ours) return ours;
        return systemFonts.FirstOrDefault(f => f.Equals(name, StringComparison.OrdinalIgnoreCase)) ?? DefaultName;
    }

    /// <summary>What the list of the settings shows: ours first, then the fonts of the system in alphabetical order, without symbol fonts.</summary>
    public static IReadOnlyList<string> Choices(IEnumerable<string> systemFonts) =>
    [
        .. Bundled.Select(b => b.Name),
        .. systemFonts.Where(f => !string.IsNullOrWhiteSpace(f) && !f.StartsWith('@') && !IsBundled(f) && !IsSymbolic(f))
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(f => f, StringComparer.CurrentCultureIgnoreCase),
    ];

    /// <summary>The family to draw with for a name already normalised.</summary>
    public static FontFamily Resolve(string? name, IEnumerable<string> systemFonts)
    {
        var chosen = Normalize(name, systemFonts);
        return new FontFamily(Bundled.FirstOrDefault(b => b.Name == chosen).Key ?? chosen);
    }

    /// <summary>The fonts installed in the system, by name.</summary>
    public static IReadOnlyList<string> SystemFontNames()
    {
        try
        {
            return FontManager.Current.SystemFonts.Select(f => f.Name).ToList();
        }
        catch (Exception)
        {
            return []; // no platform yet (a unit test without the application): only our own fonts are offered
        }
    }

    /// <summary>Puts the chosen font in use: the windows follow the resource at once, the grids drawn by hand at their next drawing.</summary>
    public static void Apply(string? name)
    {
        Current = Resolve(name, SystemFontNames());
        if (Application.Current is { } app) app.Resources["AppFontFamily"] = Current;
    }
}
