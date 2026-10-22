using System.Security.Cryptography;
using System.Text;
using MBExtreme.Core.Licensing;

namespace MBExtreme.LicenseServer;

/// <summary>Senhas, tokens aleatórios, chave de assinatura das licenças e limite de tentativas.</summary>
public static class Security
{
    private const int Iterations = 210_000;

    public static string HashPassword(string password)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(16);
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32);
        return $"pbkdf2-sha256${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public static bool VerifyPassword(string password, string stored)
    {
        var parts = stored.Split('$');
        if (parts.Length != 4 || parts[0] != "pbkdf2-sha256" || !int.TryParse(parts[1], out int iterations)) return false;
        try
        {
            byte[] expected = Convert.FromBase64String(parts[3]);
            byte[] actual = Rfc2898DeriveBytes.Pbkdf2(password, Convert.FromBase64String(parts[2]), iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(expected, actual);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public static string RandomToken(int bytes = 32) => LicenseToken.Base64Url(RandomNumberGenerator.GetBytes(bytes));

    private const string UserCodeAlphabet = "BCDFGHJKLMNPQRSTVWXZ23456789"; // sem vogais nem caracteres parecidos

    public static string NewUserCode()
    {
        var chars = new char[9];
        for (int i = 0; i < 9; i++)
            chars[i] = i == 4 ? '-' : UserCodeAlphabet[RandomNumberGenerator.GetInt32(UserCodeAlphabet.Length)];
        return new string(chars);
    }

    public static string NormalizeUserCode(string input)
    {
        string letters = new string(input.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        return letters.Length == 8 ? letters[..4] + "-" + letters[4..] : input.Trim().ToUpperInvariant();
    }

    public static bool ValidEmail(string email) =>
        email.Length is > 5 and <= 200 && email.Count(c => c == '@') == 1 && !email.Any(char.IsWhiteSpace) && email.IndexOf('.', email.IndexOf('@')) > 0;

    /// <summary>Token de formulário ligado à sessão (impede que outro site envie formulários em nome do usuário).</summary>
    public static string CsrfToken(string sessionToken, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return LicenseToken.Base64Url(hmac.ComputeHash(Encoding.UTF8.GetBytes("csrf|" + sessionToken)));
    }

    public static bool SignatureMatches(string body, string header, string secret)
    {
        if (!header.StartsWith("sha256=", StringComparison.OrdinalIgnoreCase)) return false;
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        byte[] expected = hmac.ComputeHash(Encoding.UTF8.GetBytes(body));
        try { return CryptographicOperations.FixedTimeEquals(expected, Convert.FromHexString(header[7..])); }
        catch (FormatException) { return false; }
    }
}

/// <summary>Limite simples de tentativas por chave (IP + ação) numa janela de tempo.</summary>
public sealed class RateLimiter
{
    private readonly Dictionary<string, Queue<DateTime>> _hits = new();
    private readonly object _gate = new();
    private readonly Func<DateTime> _clock;

    public RateLimiter(Func<DateTime>? clock = null) => _clock = clock ?? (() => DateTime.UtcNow);

    public bool Allow(string key, int max, TimeSpan window)
    {
        lock (_gate)
        {
            var now = _clock();
            if (!_hits.TryGetValue(key, out var queue)) _hits[key] = queue = new Queue<DateTime>();
            while (queue.Count > 0 && now - queue.Peek() > window) queue.Dequeue();
            if (queue.Count >= max) return false;
            queue.Enqueue(now);
            return true;
        }
    }
}

/// <summary>Guarda a chave de assinatura das licenças (ECDSA P-256). Criada na primeira execução; a chave privada nunca sai do servidor.</summary>
public sealed class SigningKey
{
    public ECDsa Key { get; }

    public string PublicKey => LicenseToken.ExportPublicKey(Key);

    public SigningKey(string path)
    {
        Key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        if (File.Exists(path))
        {
            Key.ImportFromPem(File.ReadAllText(path));
        }
        else
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.WriteAllText(path, Key.ExportPkcs8PrivateKeyPem());
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }
}

/// <summary>Decide o plano de uma conta e emite a licença assinada para um PC.</summary>
public sealed class LicenseIssuer
{
    public static readonly TimeSpan TokenLife = TimeSpan.FromDays(7);
    public static readonly TimeSpan GracePeriod = TimeSpan.FromDays(3);

    private readonly SigningKey _key;
    private readonly Func<DateTimeOffset> _clock;

    public LicenseIssuer(SigningKey key, Func<DateTimeOffset>? clock = null)
    {
        _key = key;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public (string Plan, string Status, int MaxDevices, DateTimeOffset Expires) Resolve(Subscription? sub)
    {
        var now = _clock();
        if (sub is null || sub.Plan == "free") return ("free", "active", 1, now + TokenLife);

        bool active = sub.Status is "active" or "trialing" && sub.PeriodEnd > now;
        bool inGrace = sub.Status is "active" or "trialing" or "past_due" && sub.PeriodEnd + GracePeriod > now;
        if (!active && !inGrace) return ("free", "expired", 1, now + TokenLife);

        var limit = sub.PeriodEnd + GracePeriod;
        var expires = now + TokenLife < limit ? now + TokenLife : limit;
        return (sub.Plan, active ? sub.Status : "past_due", Math.Max(1, sub.MaxDevices), expires);
    }

    public string Issue(User user, Subscription? sub, string deviceId)
    {
        var (plan, status, maxDevices, expires) = Resolve(sub);
        var payload = new LicensePayload
        {
            Subject = user.Id.ToString(), Email = user.Email, Plan = plan, Status = status, MaxDevices = maxDevices,
            Device = deviceId, IssuedAt = _clock(), ExpiresAt = expires,
        };
        return LicenseToken.Sign(payload, _key.Key);
    }
}
