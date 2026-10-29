namespace GGCalendar.Core.Sharing;

/// <summary>The addresses that open WhatsApp, Telegram and the mail program with the text of an event already written (issue #45).</summary>
public static class ShareLinks
{
    /// <summary>Windows hands a mail address to the program of mail with a limit of about 2000 characters.</summary>
    public const int MailtoLimit = 1800;

    /// <summary>Opens the app of WhatsApp (Windows) directly, without the browser; it exists only where the app is installed.</summary>
    public static Uri WhatsAppApp(string text) => new($"whatsapp://send?text={Uri.EscapeDataString(text)}");

    /// <summary>Opens Telegram Desktop directly, without the browser; it exists only where the app is installed.</summary>
    public static Uri TelegramApp(string text) => new($"tg://msg?text={Uri.EscapeDataString(text)}");

    /// <summary>The address on the web, for when the app is not installed.</summary>
    public static Uri WhatsApp(string text) => new($"https://wa.me/?text={Uri.EscapeDataString(text)}");

    /// <summary>
    /// "Write to the guests": a "mailto:" to their addresses with the title as subject; null when no address is usable. It is a string and not a
    /// <see cref="Uri"/> because <see cref="Uri"/> does not accept an address with "%40" for the "@" nor two addresses separated by a comma:
    /// the string goes to the system as it is. The "@" stays as it is and an address with a space, a comma or another sign of the address line is left out.
    /// </summary>
    public static string? MailTo(IEnumerable<string> addresses, string subject) => MailTo(addresses, subject, "", out _);

    /// <summary>
    /// The same with a body. When the address would be too long for Windows the body is cut and ends with "…": <paramref name="cut"/> says so, and the
    /// caller can put the whole text in the clipboard. With no addresses at all the message has no recipient; with addresses that are all unusable it is null.
    /// </summary>
    public static string? MailTo(IEnumerable<string> addresses, string subject, string body, out bool cut)
    {
        var given = addresses.ToList();
        var valid = given.Select(a => a.Trim()).Where(IsAddress).Select(a => Uri.EscapeDataString(a).Replace("%40", "@")).ToList();
        cut = false;
        if (given.Count > 0 && valid.Count == 0) return null;

        var head = $"mailto:{string.Join(",", valid)}?subject={Uri.EscapeDataString(subject)}";
        var text = body.Replace("\r\n", "\n").Replace("\n", "\r\n");
        if (text.Length == 0) return head;

        head += "&body=";
        while (head.Length + Uri.EscapeDataString(text).Length > MailtoLimit && text.Length > 0)
        {
            cut = true;
            var keep = Math.Max(0, text.Length - Math.Max(20, text.Length / 10));
            // Never cut between the two halves of a character outside the basic plane.
            if (keep > 0 && char.IsHighSurrogate(text[keep - 1])) keep--;
            text = text[..keep].TrimEnd();
        }

        if (cut) text += "…";
        return head + Uri.EscapeDataString(text);
    }

    private static bool IsAddress(string address) =>
        System.Text.RegularExpressions.Regex.IsMatch(address, @"^[^\s@,;:?&=<>%#""']+@[^\s@,;:?&=<>%#""']+$");

    public static Uri Telegram(string text) => new($"https://t.me/share/url?url=%20&text={Uri.EscapeDataString(text)}");

    /// <summary>A "mailto:" with the title as subject and the text as body, no recipient (see <see cref="MailTo(IEnumerable{string}, string, string, out bool)"/>).</summary>
    public static Uri Mail(string subject, string body, out bool cut) => new(MailTo([], subject, body, out cut)!);
}
