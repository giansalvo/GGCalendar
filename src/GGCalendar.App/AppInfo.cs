using System;
using System.Globalization;
using System.Linq;
using System.Reflection;

namespace GGCalendar.App;

/// <summary>Version, author and build date of the application (set in GGCalendar.App.csproj).</summary>
public static class AppInfo
{
    private static readonly Assembly Asm = typeof(AppInfo).Assembly;

    public const string Author = "Giansalvo Gusinu";

    public static string Version { get; } =
        Asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "1.0";

    public static string BuildDate { get; } = Asm.GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(a => a.Key == "BuildDate")?.Value is { } d && DateTime.TryParseExact(d, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
        ? date.ToString("dd/MM/yyyy")
        : "";

    public const string SourceUrl = "https://github.com/giansalvo/GGCalendar";

    /// <summary>The "appropriate legal notices" that the GPL v3 (section 0) requires an interactive program to show.</summary>
    public const string LegalNotice =
        "Copyright © 2026 Giansalvo Gusinu.\n" +
        "Software libero distribuito con licenza GNU General Public License versione 3 o (a tua scelta) successiva, SENZA ALCUNA GARANZIA. " +
        "Puoi ridistribuirlo e modificarlo secondo i termini della licenza: https://www.gnu.org/licenses/gpl-3.0.html\n" +
        "Codice sorgente: " + SourceUrl + "\n" +
        "Il nome e il logo GGCalendar non sono concessi in licenza.";

    public static string Summary => $"GGCalendar versione {Version}\nAutore: {Author}\nData di build: {BuildDate}\n\n{LegalNotice}";
}
