using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MBExtreme.LicenseServer;

public sealed record GoogleIdentity(string Subject, string Email, bool EmailVerified, string Name);

/// <summary>
/// Confere o "ID token" do Login com Google (JWT RS256) no servidor: assinatura com as chaves públicas do Google, emissor, público (o seu
/// Client ID), validade e e-mail verificado. O servidor nunca vê a senha do Google.
/// </summary>
public sealed class GoogleAuth
{
    private static readonly string[] Issuers = { "https://accounts.google.com", "accounts.google.com" };
    private readonly string _clientId;
    private readonly Func<CancellationToken, Task<string>> _fetchKeys;
    private Dictionary<string, RSA> _keys = new();
    private DateTimeOffset _fetchedAt = DateTimeOffset.MinValue;

    public GoogleAuth(string clientId, Func<CancellationToken, Task<string>>? fetchKeys = null, HttpClient? http = null)
    {
        _clientId = clientId;
        var client = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        _fetchKeys = fetchKeys ?? (async ct => await client.GetStringAsync("https://www.googleapis.com/oauth2/v3/certs", ct));
    }

    public bool Configured => _clientId.Length > 0;

    public string ClientId => _clientId;

    private static byte[] B64(string s)
    {
        s = s.Replace('-', '+').Replace('_', '/');
        s = s.PadRight(s.Length + (4 - s.Length % 4) % 4, '=');
        return Convert.FromBase64String(s);
    }

    private async Task<RSA?> KeyAsync(string kid, bool refresh, CancellationToken ct)
    {
        if (refresh || _keys.Count == 0 || DateTimeOffset.UtcNow - _fetchedAt > TimeSpan.FromHours(6))
        {
            var fresh = new Dictionary<string, RSA>();
            using var doc = JsonDocument.Parse(await _fetchKeys(ct));
            foreach (var key in doc.RootElement.GetProperty("keys").EnumerateArray())
            {
                if (key.GetProperty("kty").GetString() != "RSA") continue;
                var rsa = RSA.Create();
                rsa.ImportParameters(new RSAParameters { Modulus = B64(key.GetProperty("n").GetString()!), Exponent = B64(key.GetProperty("e").GetString()!) });
                fresh[key.GetProperty("kid").GetString()!] = rsa;
            }
            _keys = fresh;
            _fetchedAt = DateTimeOffset.UtcNow;
        }
        return _keys.GetValueOrDefault(kid);
    }

    /// <summary>Devolve a identidade se o token for autêntico e vigente; nulo em qualquer dúvida.</summary>
    public async Task<GoogleIdentity?> ValidateAsync(string? idToken, DateTimeOffset now, CancellationToken ct = default)
    {
        if (!Configured || string.IsNullOrWhiteSpace(idToken) || idToken.Length > 4096) return null;
        var parts = idToken.Split('.');
        if (parts.Length != 3) return null;
        try
        {
            using var header = JsonDocument.Parse(B64(parts[0]));
            if (header.RootElement.GetProperty("alg").GetString() != "RS256") return null;
            string kid = header.RootElement.TryGetProperty("kid", out var k) ? k.GetString() ?? "" : "";

            var rsa = await KeyAsync(kid, refresh: false, ct) ?? await KeyAsync(kid, refresh: true, ct); // chave nova: busca de novo uma vez
            if (rsa is null) return null;
            byte[] signed = Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]);
            if (!rsa.VerifyData(signed, B64(parts[2]), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)) return null;

            using var payload = JsonDocument.Parse(B64(parts[1]));
            var p = payload.RootElement;
            if (!Issuers.Contains(p.GetProperty("iss").GetString())) return null;
            string aud = p.GetProperty("aud").GetString() ?? "";
            if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(aud), Encoding.UTF8.GetBytes(_clientId))) return null;
            if (DateTimeOffset.FromUnixTimeSeconds(p.GetProperty("exp").GetInt64()) < now.AddSeconds(-60)) return null;
            if (p.TryGetProperty("iat", out var iat) && DateTimeOffset.FromUnixTimeSeconds(iat.GetInt64()) > now.AddMinutes(5)) return null;

            string sub = p.GetProperty("sub").GetString() ?? "";
            string email = p.TryGetProperty("email", out var e) ? (e.GetString() ?? "").Trim().ToLowerInvariant() : "";
            bool verified = p.TryGetProperty("email_verified", out var v) && (v.ValueKind == JsonValueKind.True || (v.ValueKind == JsonValueKind.String && v.GetString() == "true"));
            string name = p.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
            if (sub.Length == 0 || email.Length == 0) return null;
            return new GoogleIdentity(sub, email, verified, name.Length > 80 ? name[..80] : name);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or FormatException or InvalidOperationException or CryptographicException or HttpRequestException or TaskCanceledException)
        {
            return null;
        }
    }
}
