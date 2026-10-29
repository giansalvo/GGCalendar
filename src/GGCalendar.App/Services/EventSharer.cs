using System;
using System.Linq;
using System.Threading.Tasks;
using GGCalendar.Core.Localization;
using GGCalendar.Core.Models;
using GGCalendar.Core.Sharing;

namespace GGCalendar.App.Services;

/// <summary>
/// What each way of sharing an event does (issue #45). The window gives it the things that need a screen or the system
/// (opening an address, the clipboard, the file picker, the share sheet of Windows), so that what is chosen and what is said can be tested.
/// </summary>
public sealed class EventSharer
{
    /// <summary>Opens an address with the program of the system that handles it; false when there is none.</summary>
    public required Func<Uri, Task<bool>> Launch { get; init; }

    /// <summary>Opens a "mailto:" given as text (it can have several recipients, which a <see cref="Uri"/> cannot).</summary>
    public required Func<string, Task> OpenMailTo { get; init; }
    public required Func<string, Task> Copy { get; init; }

    /// <summary>Asks where to save the .ics file of the event and saves it; false when the user gave up.</summary>
    public required Func<CalendarEvent, Task<bool>> SaveIcs { get; init; }

    /// <summary>Opens the share sheet of Windows with the text and the .ics file of the event.</summary>
    public required Func<CalendarEvent, string, Task> ShowShareSheet { get; init; }

    /// <summary>The app is opened directly; the web only when the app is not there (no program handles its address, or it does not start).</summary>
    private async Task OpenApp(Uri app, Uri web)
    {
        try
        {
            if (await Launch(app)) return;
        }
        catch (Exception)
        {
            // The app is not installed: the browser is the way left.
        }

        await Launch(web);
    }

    /// <summary>"Write to the guests": the mail to the guests with the title as subject and the text of the event as body.</summary>
    public async Task<Msg?> MailGuestsAsync(CalendarEvent ev, string text)
    {
        try
        {
            var subject = string.IsNullOrWhiteSpace(ev.Title) ? L.T("common.untitled") : ev.Title.Trim();
            if (ShareLinks.MailTo(ev.Attendees.Select(a => a.Email), subject, text, out var cut) is not { } mailto) return null;
            if (cut) await Copy(text);
            await OpenMailTo(mailto);
            return Msg.Of(cut ? "status.share_mail_cut" : "status.share_mail");
        }
        catch (Exception ex)
        {
            return Msg.Of("error.share_open", L.T("details.mail"), ex.Message);
        }
    }

    /// <param name="ev">The event to save as a file (the whole series for a repeating event, like "Export as .ics").</param>
    /// <param name="text">The text of the event, see <see cref="EventShareText"/>.</param>
    /// <returns>The message for the status bar: what was done, or why it could not be done; null when there is nothing to say.</returns>
    public async Task<Msg?> ShareAsync(CalendarEvent ev, string text, ShareTarget target)
    {
        try
        {
            switch (target)
            {
                case ShareTarget.WhatsApp:
                    await OpenApp(ShareLinks.WhatsAppApp(text), ShareLinks.WhatsApp(text));
                    return Msg.Of("status.share_whatsapp");
                case ShareTarget.Telegram:
                    await OpenApp(ShareLinks.TelegramApp(text), ShareLinks.Telegram(text));
                    return Msg.Of("status.share_telegram");
                case ShareTarget.Mail:
                    var subject = string.IsNullOrWhiteSpace(ev.Title) ? L.T("common.untitled") : ev.Title.Trim();
                    var uri = ShareLinks.Mail(subject, text, out var cut);
                    // Too long for the mail program: it gets the beginning, the clipboard gets all of it.
                    if (cut) await Copy(text);
                    await Launch(uri);
                    return Msg.Of(cut ? "status.share_mail_cut" : "status.share_mail");
                case ShareTarget.MailGuests:
                    return await MailGuestsAsync(ev, text);
                case ShareTarget.Clipboard:
                    await Copy(text);
                    return Msg.Of("status.share_clipboard");
                case ShareTarget.Disk:
                    return await SaveIcs(ev) ? Msg.Of("status.share_saved") : null;
                case ShareTarget.Apps:
                    await ShowShareSheet(ev, text);
                    return null;
                default:
                    return null;
            }
        }
        catch (Exception ex)
        {
            return target switch
            {
                ShareTarget.Apps => Msg.Of("error.share_apps", ex.Message),
                ShareTarget.Clipboard => Msg.Of("error.share_open", L.T("share.hint.clipboard"), ex.Message),
                ShareTarget.Disk => Msg.Of("error.export", ex.Message),
                _ => Msg.Of("error.share_open", target.ToString(), ex.Message),
            };
        }
    }
}
