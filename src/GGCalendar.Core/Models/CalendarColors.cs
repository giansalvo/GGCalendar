namespace GGCalendar.Core.Models;

/// <summary>
/// The colours of calendars: the 24 that Google Calendar offers (the "calendar" palette of its colors API, checked by a test against Google)
/// and the 8 of them that the windows show first. Local calendars use the same ones, so a calendar looks the same everywhere.
/// </summary>
public static class CalendarColors
{
    /// <summary>The 24 colours of Google, in the order of their ids (1 to 24).</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        "#AC725E", "#D06B64", "#F83A22", "#FA573C", "#FF7537", "#FFAD46",
        "#42D692", "#16A765", "#7BD148", "#B3DC6C", "#FBE983", "#FAD165",
        "#92E1C0", "#9FE1E7", "#9FC6E7", "#4986E7", "#9A9CFF", "#B99AFF",
        "#C2C2C2", "#CABDBF", "#CCA6AC", "#F691B2", "#CD74E6", "#A47AE2",
    ];

    /// <summary>The 8 colours shown in the row of the windows (all of them are in <see cref="All"/>): blue, green, orange, purple, yellow, light blue, red, grey.</summary>
    public static IReadOnlyList<string> Quick { get; } =
        ["#4986E7", "#16A765", "#FA573C", "#A47AE2", "#FAD165", "#9FE1E7", "#F83A22", "#C2C2C2"];

    public static bool IsInPalette(string? colorHex) => All.Contains(colorHex ?? "", StringComparer.OrdinalIgnoreCase);

    /// <summary>The same colour written in capitals (the form of the lists above), or null when it is not a colour like "#RRGGBB".</summary>
    public static string? Normalize(string? colorHex) =>
        colorHex is { Length: 7 } c && c[0] == '#' && c[1..].All(Uri.IsHexDigit) ? c.ToUpperInvariant() : null;

    /// <summary>
    /// The text colour for a background: black or white by the brightness. Google, when the colour is sent as RGB, refuses any other text colour
    /// (its own palette pairs #1D1D1D with its colours, but that is refused too: found with the real Google).
    /// </summary>
    public static string TextFor(string colorHex)
    {
        var hex = colorHex.TrimStart('#');
        if (hex.Length != 6 || !int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var rgb)) return "#000000";
        var luminance = 0.299 * ((rgb >> 16) & 255) + 0.587 * ((rgb >> 8) & 255) + 0.114 * (rgb & 255);
        return luminance < 140 ? "#FFFFFF" : "#000000";
    }
}
