using GGCalendar.Core.Models;

namespace GGCalendar.Data;

/// <summary>Decides at the start if the splash screen is due and remembers that this version has had it.</summary>
public static class SplashGate
{
    /// <summary>
    /// True the first time <paramref name="version"/> runs. The version is saved before the splash is shown, so a crash
    /// does not bring it back at every start; if the file cannot be written the splash is shown anyway.
    /// </summary>
    public static bool Claim(SettingsStore store, AppSettings settings, string version)
    {
        if (!SplashPolicy.ShouldShow(settings, version)) return false;
        settings.SplashVersion = version;
        try { store.Save(settings); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return true;
    }
}
