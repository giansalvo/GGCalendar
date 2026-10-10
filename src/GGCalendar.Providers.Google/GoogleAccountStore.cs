using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GGCalendar.Providers.Google;

/// <summary>A connected Google account. The refresh token is stored encrypted with DPAPI (current Windows user).</summary>
public sealed class GoogleAccount
{
    public string Email { get; set; } = "";
    public string ProtectedRefreshToken { get; set; } = "";
}

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class GoogleAccountStore
{
    private readonly string _path;

    public GoogleAccountStore(string? path = null)
    {
        _path = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GGCalendar", "google-accounts.json");
    }

    public List<GoogleAccount> Load()
    {
        try
        {
            return File.Exists(_path) ? JsonSerializer.Deserialize<List<GoogleAccount>>(File.ReadAllText(_path)) ?? [] : [];
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return [];
        }
    }

    public void Save(IEnumerable<GoogleAccount> accounts)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, JsonSerializer.Serialize(accounts, new JsonSerializerOptions { WriteIndented = true }));
    }

    public void AddOrReplace(string email, string refreshToken)
    {
        var all = Load();
        all.RemoveAll(a => a.Email.Equals(email, StringComparison.OrdinalIgnoreCase));
        all.Add(new GoogleAccount { Email = email, ProtectedRefreshToken = Protect(refreshToken) });
        Save(all);
    }

    public void Remove(string email)
    {
        var all = Load();
        all.RemoveAll(a => a.Email.Equals(email, StringComparison.OrdinalIgnoreCase));
        Save(all);
    }

    public static string Protect(string value) =>
        Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser));

    public static string Unprotect(string value) =>
        Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(value), null, DataProtectionScope.CurrentUser));
}
