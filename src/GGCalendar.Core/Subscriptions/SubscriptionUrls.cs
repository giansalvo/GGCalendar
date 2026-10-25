namespace GGCalendar.Core.Subscriptions;

public enum UrlProblem { Empty, Invalid, NotHttps, HasCredentials }

/// <summary>The address of a subscription, checked: <see cref="Uri"/> is set when it can be used, <see cref="Problem"/> otherwise.</summary>
public sealed record UrlCheck(Uri? Uri, UrlProblem? Problem)
{
    public bool IsValid => Uri is not null;
}

/// <summary>
/// What an address of a public calendar may look like: https only (webcal:// is read as https://), without a user name and password.
/// An address often contains a secret key, so it is shown only masked (<see cref="Mask(Uri)"/>).
/// </summary>
public static class SubscriptionUrls
{
    public static UrlCheck Check(string? text)
    {
        var s = text?.Trim();
        if (string.IsNullOrEmpty(s)) return new UrlCheck(null, UrlProblem.Empty);

        if (s.StartsWith("webcals://", StringComparison.OrdinalIgnoreCase)) s = "https://" + s["webcals://".Length..];
        else if (s.StartsWith("webcal://", StringComparison.OrdinalIgnoreCase)) s = "https://" + s["webcal://".Length..];

        if (!Uri.TryCreate(s, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host)) return new UrlCheck(null, UrlProblem.Invalid);
        if (uri.Scheme != Uri.UriSchemeHttps) return new UrlCheck(null, uri.Scheme == Uri.UriSchemeHttp ? UrlProblem.NotHttps : UrlProblem.Invalid);
        if (uri.UserInfo.Length > 0) return new UrlCheck(null, UrlProblem.HasCredentials);
        return new UrlCheck(uri, null);
    }

    /// <summary>"calendar.google.com/…": the site only, never the path or the query where the key is.</summary>
    public static string Mask(Uri uri) => uri.Host + "/…";

    public static string Mask(string text)
    {
        var check = Check(text);
        return check.Uri is { } uri ? Mask(uri) : "…";
    }

    /// <summary>The text with every occurrence of the address replaced by its mask (a message from the network layer may repeat it).</summary>
    public static string Redact(string text, Uri uri)
    {
        var masked = Mask(uri);
        foreach (var form in new[] { uri.AbsoluteUri, uri.OriginalString, uri.PathAndQuery })
            if (form.Length > 1) text = text.Replace(form, masked, StringComparison.OrdinalIgnoreCase);
        return text;
    }
}
