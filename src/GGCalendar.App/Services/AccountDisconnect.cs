using GGCalendar.Core.Localization;

namespace GGCalendar.App.Services;

/// <summary>The texts of the question asked before an account is disconnected (from the menu of the account and from the settings, the same).</summary>
public static class AccountDisconnect
{
    public static string Title => L.T("account.disconnect_title");

    public static string Button => L.T("account.disconnect_button");

    public static string Text(string email, GoogleSyncService.AccountDisconnectInfo info)
    {
        var text = L.T("account.disconnect_text", email);
        if (info.Calendars > 0 || info.Events > 0)
            text += "\n\n" + L.T("account.disconnect_removes") + "\n" + L.N("account.disconnect_calendars", info.Calendars) + "\n" + L.N("account.disconnect_events", info.Events);
        // Always said, one way or the other: the person sees that the changes were looked at.
        text += "\n\n" + (info.PendingChanges > 0 ? L.N("account.disconnect_pending", info.PendingChanges) : L.T("account.disconnect_nothing_pending"));
        return text;
    }
}
