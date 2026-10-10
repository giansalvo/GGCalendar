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

    public static string Summary => $"GGCalendar versione {Version}\nAutore: {Author}\nData di build: {BuildDate}";
}
