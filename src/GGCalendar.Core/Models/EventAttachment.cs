namespace GGCalendar.Core.Models;

/// <summary>
/// An attachment of an event: a link, never a copy of the file. <see cref="FileId"/> and <see cref="IconUrl"/> are what Google adds to
/// a file of Drive (or to a known type); they are kept as Google sent them so a save writes them back unchanged.
/// </summary>
public sealed record EventAttachment(string Url, string Title = "", string? MimeType = null, string? FileId = null, string? IconUrl = null)
{
    /// <summary>The name of the file in the address (empty for a page of Drive, where the title is the only name).</summary>
    public string FileName => AttachmentLink.FileName(Url);

    /// <summary>What the person reads: the title, or the name of the file when there is no title.</summary>
    public string DisplayName => Title.Length > 0 ? Title : FileName.Length > 0 ? FileName : Url;

    /// <summary>True when the link may be opened from the app (https only).</summary>
    public bool CanOpen => AttachmentLink.IsValid(Url);
}

/// <summary>The rules of the links of the attachments: https only, the limits, the name and the type of the file taken from the address.</summary>
public static class AttachmentLink
{
    /// <summary>Google keeps 25 and drops the others without telling.</summary>
    public const int MaxCount = 25;

    public const int MaxUrlLength = 2048;
    public const int MaxTitleLength = 256;

    private static readonly Dictionary<string, string> Types = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = "image/jpeg", [".jpeg"] = "image/jpeg", [".png"] = "image/png", [".gif"] = "image/gif", [".webp"] = "image/webp",
        [".pdf"] = "application/pdf", [".txt"] = "text/plain", [".ics"] = "text/calendar", [".csv"] = "text/csv", [".html"] = "text/html",
        [".htm"] = "text/html", [".doc"] = "application/msword",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".xls"] = "application/vnd.ms-excel",
        [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        [".ppt"] = "application/vnd.ms-powerpoint",
        [".pptx"] = "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        [".zip"] = "application/zip",
    };

    /// <summary>True for an absolute https address without credentials, spaces or control characters, and not longer than <see cref="MaxUrlLength"/>.</summary>
    public static bool IsValid(string? url)
    {
        if (string.IsNullOrEmpty(url) || url.Length > MaxUrlLength) return false;
        if (url.Any(c => c <= ' ' || c == '\u007f' || char.IsWhiteSpace(c))) return false;
        return Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttps
            && uri.Host.Length > 0
            && uri.UserInfo.Length == 0;
    }

    /// <summary>The name of the file: the last part of the path, without the query, decoded. Empty for the pages of Drive and Docs, which have no name in the address.</summary>
    public static string FileName(string? url)
    {
        if (!IsValid(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri)) return "";
        if (uri.Host.Equals("drive.google.com", StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith(".google.com", StringComparison.OrdinalIgnoreCase)) return "";
        var last = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "";
        try { last = Uri.UnescapeDataString(last); }
        catch (UriFormatException) { }
        return last.Length > MaxTitleLength ? last[..MaxTitleLength] : last;
    }

    /// <summary>The type of the file from the extension of its name; null when it is not one of the known ones.</summary>
    public static string? MimeType(string? url)
    {
        var name = FileName(url);
        var dot = name.LastIndexOf('.');
        return dot >= 0 && Types.TryGetValue(name[dot..], out var type) ? type : null;
    }

    /// <summary>
    /// The attachment a person adds by pasting a link: the address as it was written (only the spaces around it are removed), the title as typed
    /// or, when empty, the name of the file; null when the address is not valid.
    /// </summary>
    public static EventAttachment? Create(string? url, string? title = null)
    {
        var address = url?.Trim();
        if (!IsValid(address)) return null;
        var name = title?.Trim() ?? "";
        if (name.Length == 0) name = FileName(address);
        if (name.Length > MaxTitleLength) name = name[..MaxTitleLength];
        return new EventAttachment(address!, name, MimeType(address));
    }

    /// <summary>The words the search looks in: the title and the name of the file (not the address).</summary>
    public static string SearchText(EventAttachment attachment) =>
        attachment.FileName.Length > 0 && !attachment.FileName.Equals(attachment.Title, StringComparison.OrdinalIgnoreCase)
            ? attachment.Title + " " + attachment.FileName
            : attachment.Title;
}
