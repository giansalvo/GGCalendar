namespace GGCalendar.Providers.Google;

/// <summary>
/// OAuth client of the GGCalendar Google Cloud project. For desktop apps these values are not confidential:
/// they identify the application, never the user. Users may override them in the advanced settings.
/// </summary>
public sealed record GoogleClientConfig(string ClientId, string ClientSecret)
{
    public const string PlaceholderId = "INSERISCI_CLIENT_ID.apps.googleusercontent.com";

    /// <summary>Replace with the values of the project created in the Google Cloud console.</summary>
    public static GoogleClientConfig Embedded { get; } = new(PlaceholderId, "INSERISCI_CLIENT_SECRET");

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ClientId) && ClientId != PlaceholderId;

    public static GoogleClientConfig Resolve(string? overrideId, string? overrideSecret) =>
        !string.IsNullOrWhiteSpace(overrideId) ? new(overrideId.Trim(), overrideSecret?.Trim() ?? "") : Embedded;
}
