namespace GGCalendar.Providers.Google;

/// <summary>
/// OAuth client of the GGCalendar Google Cloud project. For desktop apps these values are not confidential:
/// they identify the application, never the user. Users may override them in the advanced settings.
/// </summary>
public sealed record GoogleClientConfig(string ClientId, string ClientSecret)
{
    public const string PlaceholderId = "INSERISCI_CLIENT_ID.apps.googleusercontent.com";

    /// <summary>
    /// Loaded from the OAuth\client_secret*.json file (git-ignored) that is embedded in the assembly at build time.
    /// </summary>
    public static GoogleClientConfig Embedded { get; } = LoadEmbedded();

    private static GoogleClientConfig LoadEmbedded()
    {
        try
        {
            using var stream = typeof(GoogleClientConfig).Assembly.GetManifestResourceStream("google-client.json");
            if (stream is null) return new(PlaceholderId, "");
            using var doc = System.Text.Json.JsonDocument.Parse(stream);
            var node = doc.RootElement.GetProperty("installed");
            return new(node.GetProperty("client_id").GetString() ?? PlaceholderId,
                       node.GetProperty("client_secret").GetString() ?? "");
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return new(PlaceholderId, "");
        }
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ClientId) && ClientId != PlaceholderId;

    public static GoogleClientConfig Resolve(string? overrideId, string? overrideSecret) =>
        !string.IsNullOrWhiteSpace(overrideId) ? new(overrideId.Trim(), overrideSecret?.Trim() ?? "") : Embedded;
}
