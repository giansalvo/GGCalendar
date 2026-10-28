namespace GGCalendar.Core.Models;

/// <summary>The splash screen is shown the first time a version runs: after an install or an update, not at every start.</summary>
public static class SplashPolicy
{
    public static bool ShouldShow(AppSettings settings, string version) =>
        !string.Equals(settings.SplashVersion, version, StringComparison.Ordinal);
}
