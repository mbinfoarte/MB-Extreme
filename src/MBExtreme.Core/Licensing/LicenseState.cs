using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace MBExtreme.Core.Licensing;

public enum LicenseState
{
    /// <summary>Sem licença guardada.</summary>
    None,
    Valid,
    Expired,
    /// <summary>Assinatura inválida, texto adulterado ou licença que não é deste app.</summary>
    Invalid,
    /// <summary>A licença é de outro computador.</summary>
    WrongDevice,
    /// <summary>O relógio do PC foi voltado para trás.</summary>
    ClockTampered,
}

public sealed record LicenseStatus(LicenseState State, LicensePayload? Payload, string Message, TimeSpan? Remaining)
{
    public bool IsValid => State == LicenseState.Valid;

    /// <summary>Plano pago sem data de fim (administrador ou chave sem vencimento): não há contagem de dias.</summary>
    public bool NoEndDate => IsValid && Payload is { PeriodEnd: null } && FeaturePolicy.Rank(Payload.Plan) > 0;

    /// <summary>
    /// Dias até o fim do período pago (arredondado para cima). Sem data de assinatura, usa o vencimento da licença, menos nos
    /// planos sem vencimento: ali o vencimento é só o da cópia guardada no PC (7 dias, renovada sozinha), não o do plano.
    /// </summary>
    public int? DaysLeft(DateTimeOffset now)
    {
        if (NoEndDate) return null;
        var end = Payload?.PeriodEnd ?? Payload?.ExpiresAt;
        if (end is null) return null;
        return Math.Max(0, (int)Math.Ceiling((end.Value - now).TotalDays));
    }

    public string Plan => IsValid && Payload is not null ? Payload.Plan : "free";

    /// <summary>Vale a pena pedir uma licença nova ao servidor (falta pouco para vencer ou já venceu).</summary>
    public bool ShouldRefresh => State is LicenseState.Valid or LicenseState.Expired && Remaining is { } r && r < TimeSpan.FromDays(3);
}

/// <summary>Decide o que uma licença guardada vale agora.</summary>
public static class LicenseVerifier
{
    public static readonly TimeSpan ClockTolerance = TimeSpan.FromHours(24);

    public static LicenseStatus Evaluate(string? token, string publicKey, string deviceId, DateTimeOffset now, DateTimeOffset? lastSeen = null)
    {
        if (string.IsNullOrWhiteSpace(token)) return new LicenseStatus(LicenseState.None, null, "Nenhuma licença neste PC.", null);
        if (string.IsNullOrWhiteSpace(publicKey)) return new LicenseStatus(LicenseState.Invalid, null, "O app não tem a chave pública de licenças.", null);

        var payload = LicenseToken.Verify(token, publicKey);
        if (payload is null) return new LicenseStatus(LicenseState.Invalid, null, "A licença guardada não é autêntica.", null);
        if (!string.Equals(payload.Device, deviceId, StringComparison.Ordinal))
            return new LicenseStatus(LicenseState.WrongDevice, payload, "Esta licença foi emitida para outro computador.", null);
        if (lastSeen is { } seen && now + ClockTolerance < seen)
            return new LicenseStatus(LicenseState.ClockTampered, payload, "O relógio do computador está atrasado em relação ao último uso. Corrija a data e a hora.", null);

        var remaining = payload.ExpiresAt - now;
        return remaining > TimeSpan.Zero
            ? new LicenseStatus(LicenseState.Valid, payload, $"Plano {FeaturePolicy.Title(payload.Plan)} ativo.", remaining)
            : new LicenseStatus(LicenseState.Expired, payload, "A licença venceu. Conecte-se à internet para renová-la.", remaining);
    }
}

/// <summary>Guarda só as últimas licenças (a mais recente e a anterior), nunca todas.</summary>
public static class LicenseHistory
{
    public const int Keep = 2;

    /// <summary>Acrescenta a nova licença na frente e descarta as mais antigas. Licenças repetidas não duplicam.</summary>
    public static IReadOnlyList<string> Push(IEnumerable<string> existing, string newToken, int keep = Keep)
    {
        var list = new List<string> { newToken };
        foreach (string token in existing)
            if (!string.IsNullOrWhiteSpace(token) && token != newToken) list.Add(token);
        return list.Take(Math.Max(1, keep)).ToList();
    }
}

/// <summary>Identificador anônimo do PC: hash de um GUID do Windows com um sal. Não contém nome, usuário nem componentes.</summary>
public static class DeviceIdentity
{
    public const string Salt = "MBExtreme.device.v1";

    public static string Compute(string machineGuid, string salt = Salt)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(salt + "|" + machineGuid.Trim().ToLowerInvariant()));
        return Convert.ToHexString(hash)[..32].ToLowerInvariant();
    }

    [SupportedOSPlatform("windows")]
    public static string Current()
    {
        string? guid = null;
        try { guid = Microsoft.Win32.Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Cryptography", "MachineGuid", null) as string; }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException) { }
        guid ??= Environment.MachineName;
        return Compute(guid);
    }

    /// <summary>Nome mostrado ao usuário na lista de dispositivos do site.</summary>
    public static string FriendlyName() => Environment.MachineName;
}
