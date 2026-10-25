using System;
using System.Globalization;
using System.Linq;
using System.Reflection;
using GGCalendar.Core.Localization;

namespace GGCalendar.App;

/// <summary>Version, author and build date of the application (set in GGCalendar.App.csproj).</summary>
public static class AppInfo
{
    private static readonly Assembly Asm = typeof(AppInfo).Assembly;

    public const string Author = "Giansalvo Gusinu";

    public static string Version { get; } =
        Asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "1.0";

    public static string BuildDate => Asm.GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(a => a.Key == "BuildDate")?.Value is { } d && DateTime.TryParseExact(d, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
        ? date.ToString("d", L.Culture)
        : "";

    public const string SourceUrl = "https://github.com/giansalvo/GGCalendar";

    /// <summary>The "appropriate legal notices" that the GPL v3 (section 0) requires an interactive program to show.</summary>
    public static string LegalNotice => L.T("about.legal", SourceUrl);
}
