using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MBExtreme.Core.Licensing;

/// <summary>Conteúdo da licença. É assinado pelo servidor; o app só confere a assinatura com a chave pública.</summary>
public sealed record LicensePayload
{
    [JsonPropertyName("v")] public int Version { get; init; } = 1;

    /// <summary>Identificador da conta.</summary>
    [JsonPropertyName("sub")] public string Subject { get; init; } = "";

    [JsonPropertyName("email")] public string Email { get; init; } = "";

    /// <summary>Nome do cliente, como cadastrado no site (pode ser vazio).</summary>
    [JsonPropertyName("name")] public string Name { get; init; } = "";

    /// <summary>Foto da conta no site (https; normalmente a do Google). Vazio quando a conta não tem foto.</summary>
    [JsonPropertyName("pic")] public string Picture { get; init; } = "";

    /// <summary>Fim do período pago (ou do teste). É o que o cliente vê como "dias restantes"; difere do vencimento da licença, que se renova sozinho.</summary>
    [JsonPropertyName("pe")] public DateTimeOffset? PeriodEnd { get; init; }

    /// <summary>"free" ou "pro" (outros planos podem ser acrescentados no servidor).</summary>
    [JsonPropertyName("plan")] public string Plan { get; init; } = "free";

    /// <summary>Recursos liberados além do plano (venda avulsa, teste de um recurso novo...).</summary>
    [JsonPropertyName("features")] public List<string> Features { get; init; } = new();

    [JsonPropertyName("maxDevices")] public int MaxDevices { get; init; } = 1;

    /// <summary>PC para o qual a licença foi emitida (hash, nunca dados do hardware).</summary>
    [JsonPropertyName("device")] public string Device { get; init; } = "";

    [JsonPropertyName("iat")] public DateTimeOffset IssuedAt { get; init; }

    [JsonPropertyName("exp")] public DateTimeOffset ExpiresAt { get; init; }

    /// <summary>active, trialing, past_due...</summary>
    [JsonPropertyName("status")] public string Status { get; init; } = "active";
}

/// <summary>Licença no formato <c>mbx1.payload.assinatura</c> (base64url), assinada com ECDSA P-256 e SHA-256.</summary>
public static class LicenseToken
{
    private const string Prefix = "mbx1";

    private static readonly JsonSerializerOptions Json = new() { DefaultIgnoreCondition = JsonIgnoreCondition.Never };

    public static string Sign(LicensePayload payload, ECDsa privateKey)
    {
        byte[] body = JsonSerializer.SerializeToUtf8Bytes(payload, Json);
        string head = Prefix + "." + Base64Url(body);
        byte[] signature = privateKey.SignData(Encoding.ASCII.GetBytes(head), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return head + "." + Base64Url(signature);
    }

    /// <summary>Lê o conteúdo sem conferir a assinatura (use só para mostrar dados; a decisão vem de <see cref="Verify"/>).</summary>
    public static LicensePayload? Peek(string token)
    {
        var parts = token.Split('.');
        if (parts.Length != 3 || parts[0] != Prefix) return null;
        try { return JsonSerializer.Deserialize<LicensePayload>(FromBase64Url(parts[1]), Json); }
        catch (Exception ex) when (ex is JsonException or FormatException) { return null; }
    }

    /// <summary>Confere a assinatura com a chave pública (SubjectPublicKeyInfo em base64). Retorna o conteúdo só se for autêntico.</summary>
    public static LicensePayload? Verify(string token, string publicKeyBase64)
    {
        var parts = token.Split('.');
        if (parts.Length != 3 || parts[0] != Prefix) return null;
        try
        {
            using var key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKeyBase64), out _);
            byte[] signature = FromBase64Url(parts[2]);
            bool ok = key.VerifyData(Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]), signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
            return ok ? JsonSerializer.Deserialize<LicensePayload>(FromBase64Url(parts[1]), Json) : null;
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or JsonException or ArgumentException)
        {
            return null;
        }
    }

    public static string ExportPublicKey(ECDsa key) => Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());

    public static string Base64Url(byte[] data) => Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static byte[] FromBase64Url(string text)
    {
        string s = text.Replace('-', '+').Replace('_', '/');
        s = s.PadRight(s.Length + (4 - s.Length % 4) % 4, '=');
        return Convert.FromBase64String(s);
    }
}
