using System.Runtime.CompilerServices;
using GGCalendar.Core;
using GGCalendar.Core.Models;
using GGCalendar.Data;
using GGCalendar.Providers.Google;

namespace GGCalendar.Core.Tests.Support;

/// <summary>
/// The folder where the tests keep the app's data, so that they never touch %AppData%\GGCalendar (the installed app's
/// calendars, settings, reminders and the four connected Google accounts). It lives outside the repository because it holds
/// an encrypted Google token.
/// </summary>
public static class TestData
{
    public static string Folder { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GGCalendarTests");

    /// <summary>
    /// Runs once when the test assembly is loaded, before any store is created: points the app to the test folder, starts it
    /// clean (events, settings and reminder state of the previous session are dropped) and makes sure the test account is
    /// there. The account comes from the installed app's own account file: only its row is copied, still encrypted for this
    /// Windows user, and the other accounts are not read into anything.
    /// </summary>
    [ModuleInitializer]
    public static void Prepare()
    {
        Directory.CreateDirectory(Folder);
        Environment.SetEnvironmentVariable(AppPaths.DataDirectoryVariable, Folder);

        foreach (var file in Directory.GetFiles(Folder, "calendar.db*").Concat(Directory.GetFiles(Folder, "reminders.json")))
            File.Delete(file);

        // The row is copied again every session: after a logout and a new login in the installed app the token is another one,
        // and a logout there revokes the old one on Google, so an older copy would stop working.
        var accounts = new GoogleAccountStore(Path.Combine(Folder, "google-accounts.json"));
        var installed = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GGCalendar", "google-accounts.json");
        if (new GoogleAccountStore(installed).Load().FirstOrDefault(a => a.Email.Equals(AppDriver.TestAccount, StringComparison.OrdinalIgnoreCase)) is { } testAccount)
            accounts.Save([testAccount]);

        // The tests do not depend on the language (they find controls by AutomationId and check messages by key), and run in the one
        // of GGCALENDAR_TEST_LANGUAGE (Italian by default) so that a run always shows the same texts.
        var language = Environment.GetEnvironmentVariable("GGCALENDAR_TEST_LANGUAGE") is { Length: > 0 } wanted ? wanted : "it";
        new SettingsStore().Save(new AppSettings { Language = language });
        GGCalendar.Core.Localization.L.Apply(language);
    }
}
