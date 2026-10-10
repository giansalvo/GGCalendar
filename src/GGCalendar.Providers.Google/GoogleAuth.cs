using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace GGCalendar.Providers.Google;

public sealed record GoogleTokens(string AccessToken, string? RefreshToken, DateTimeOffset ExpiresAt);

public sealed class GoogleAuthException(string message) : Exception(message);

/// <summary>OAuth 2.0 for installed apps: system browser, loopback redirect and PKCE.</summary>
public static class GoogleAuth
{
    public const string CalendarScope = "https://www.googleapis.com/auth/calendar";
    private const string AuthEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";
    private const string TokenEndpoint = "https://oauth2.googleapis.com/token";

    private sealed class TokenResponse
    {
        [JsonPropertyName("access_token")] public string? AccessToken { get; set; }
        [JsonPropertyName("refresh_token")] public string? RefreshToken { get; set; }
        [JsonPropertyName("expires_in")] public int ExpiresIn { get; set; }
        [JsonPropertyName("error_description")] public string? ErrorDescription { get; set; }
        [JsonPropertyName("error")] public string? Error { get; set; }
    }

    public static async Task<GoogleTokens> SignInAsync(HttpClient http, string clientId, string clientSecret, CancellationToken ct)
    {
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var state = Base64Url(RandomNumberGenerator.GetBytes(16));

        var port = FreePort();
        var redirect = $"http://127.0.0.1:{port}/";
        using var listener = new HttpListener();
        listener.Prefixes.Add(redirect);
        listener.Start();

        var url = AuthEndpoint + "?" + string.Join("&", new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["redirect_uri"] = redirect,
            ["response_type"] = "code",
            ["scope"] = CalendarScope,
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256",
            ["state"] = state,
            ["access_type"] = "offline",
            ["prompt"] = "consent select_account",
        }.Select(kv => $"{kv.Key}={Uri.EscapeDataString(kv.Value)}"));
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

        using var reg = ct.Register(listener.Close);
        string? code;
        try
        {
            var ctx = await listener.GetContextAsync();
            var query = ctx.Request.QueryString;
            code = query["state"] == state ? query["code"] : null;
            var error = query["error"];
            var html = code is not null
                ? "<html><body style='font-family:Segoe UI;text-align:center;margin-top:80px'><h2>Account collegato</h2><p>Puoi chiudere questa scheda e tornare a GGCalendar.</p></body></html>"
                : $"<html><body style='font-family:Segoe UI;text-align:center;margin-top:80px'><h2>Accesso non riuscito</h2><p>{WebUtility.HtmlEncode(error ?? "risposta non valida")}</p></body></html>";
            var bytes = Encoding.UTF8.GetBytes(html);
            ctx.Response.ContentType = "text/html; charset=utf-8";
            ctx.Response.ContentLength64 = bytes.Length;
            await ctx.Response.OutputStream.WriteAsync(bytes, ct);
            ctx.Response.Close();
            if (code is null) throw new GoogleAuthException("Accesso rifiutato o annullato" + (error is null ? "." : $" ({error})."));
        }
        catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException)
        {
            ct.ThrowIfCancellationRequested();
            throw new GoogleAuthException("Accesso interrotto.");
        }

        var tokens = await PostTokenAsync(http, new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
            ["code"] = code,
            ["code_verifier"] = verifier,
            ["grant_type"] = "authorization_code",
            ["redirect_uri"] = redirect,
        }, ct);
        return tokens;
    }

    public static Task<GoogleTokens> RefreshAsync(HttpClient http, string clientId, string clientSecret, string refreshToken, CancellationToken ct) =>
        PostTokenAsync(http, new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
            ["refresh_token"] = refreshToken,
            ["grant_type"] = "refresh_token",
        }, ct);

    private static async Task<GoogleTokens> PostTokenAsync(HttpClient http, Dictionary<string, string> form, CancellationToken ct)
    {
        using var response = await http.PostAsync(TokenEndpoint, new FormUrlEncodedContent(form), ct);
        var body = await response.Content.ReadFromJsonAsync<TokenResponse>(ct);
        if (!response.IsSuccessStatusCode || body?.AccessToken is null)
            throw new GoogleAuthException($"Google ha rifiutato le credenziali: {body?.ErrorDescription ?? body?.Error ?? response.ReasonPhrase}");
        return new GoogleTokens(body.AccessToken, body.RefreshToken, DateTimeOffset.UtcNow.AddSeconds(body.ExpiresIn - 60));
    }

    private static int FreePort()
    {
        var l = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    private static string Base64Url(byte[] data) => Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
