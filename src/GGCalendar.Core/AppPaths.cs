namespace GGCalendar.Core;

/// <summary>Where the app keeps its data. Always %AppData%\GGCalendar, unless the variable below names another folder (used by the tests).</summary>
public static class AppPaths
{
    public const string DataDirectoryVariable = "GGCALENDAR_DATA_DIR";

    public static string DataDirectory =>
        Environment.GetEnvironmentVariable(DataDirectoryVariable) is { Length: > 0 } folder
            ? folder
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GGCalendar");

    public static string FileInDataDirectory(string name) => Path.Combine(DataDirectory, name);
}
