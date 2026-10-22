using System.Runtime.Versioning;
using System.Text.Json;

namespace MBExtreme.Core.Windows;

public enum SecurityLevel { Ok, Warning, Bad }

public sealed record SecurityItem(string Title, SecurityLevel Level, string Detail);

/// <summary>Situação da proteção do Windows num instante. Nulo = não foi possível ler.</summary>
public sealed record SecurityStatus(
    bool? DefenderEnabled,
    bool? RealTimeProtection,
    int? SignatureAgeDays,
    DateTime? LastQuickScan,
    bool? FirewallDomain,
    bool? FirewallPrivate,
    bool? FirewallPublic,
    bool? UacEnabled,
    IReadOnlyList<string> OtherAntivirus);

/// <summary>Lê o estado do Microsoft Defender, do firewall e do UAC e traduz em avisos. Somente leitura; os botões da página só abrem a Segurança do Windows ou rodam verificações.</summary>
[SupportedOSPlatform("windows")]
public static class SecurityService
{
    public static SecurityStatus Read()
    {
        const string script =
            "$mp = Get-MpComputerStatus -ErrorAction SilentlyContinue; " +
            "$fw = @(Get-NetFirewallProfile -ErrorAction SilentlyContinue | ForEach-Object { [pscustomobject]@{ Name=[string]$_.Name; Enabled=([string]$_.Enabled -eq 'True') } }); " +
            "$av = @(Get-CimInstance -Namespace root/SecurityCenter2 -ClassName AntiVirusProduct -ErrorAction SilentlyContinue | ForEach-Object { [pscustomobject]@{ Name=[string]$_.displayName; State=[int]$_.productState } }); " +
            "$uac = (Get-ItemProperty 'HKLM:\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Policies\\System' -ErrorAction SilentlyContinue).EnableLUA; " +
            "[pscustomobject]@{ Defender = if ($mp) { [pscustomobject]@{ Enabled=[bool]$mp.AntivirusEnabled; RealTime=[bool]$mp.RealTimeProtectionEnabled; SignatureAge=[int]$mp.AntivirusSignatureAge; " +
            "QuickScan=if ($mp.QuickScanEndTime) { $mp.QuickScanEndTime.ToString('o') } else { $null } } } else { $null }; Firewall=$fw; Av=$av; Uac=$uac } | ConvertTo-Json -Depth 5 -Compress";

        var result = Shell.PowerShellUtf8(script);
        if (result.ExitCode != 0 && string.IsNullOrWhiteSpace(result.Output))
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(result.Error) ? "Não foi possível ler a proteção do Windows." : result.Error.Trim());
        return Parse(result.Output);
    }

    public static SecurityStatus Parse(string json)
    {
        using var doc = JsonDocument.Parse(json.Trim().Length == 0 ? "{}" : json);
        var root = doc.RootElement;

        bool? defender = null, realTime = null;
        int? age = null;
        DateTime? scan = null;
        if (root.TryGetProperty("Defender", out var d) && d.ValueKind == JsonValueKind.Object)
        {
            defender = Bool(d, "Enabled");
            realTime = Bool(d, "RealTime");
            if (d.TryGetProperty("SignatureAge", out var a) && a.ValueKind == JsonValueKind.Number && a.TryGetInt32(out int days)) age = days;
            if (d.TryGetProperty("QuickScan", out var q) && q.ValueKind == JsonValueKind.String
                && DateTime.TryParse(q.GetString(), null, System.Globalization.DateTimeStyles.RoundtripKind, out var when)) scan = when;
        }

        bool? fwDomain = null, fwPrivate = null, fwPublic = null;
        foreach (var item in Items(root, "Firewall"))
        {
            string name = item.TryGetProperty("Name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() ?? "" : "";
            bool? enabled = Bool(item, "Enabled");
            if (name.Equals("Domain", StringComparison.OrdinalIgnoreCase)) fwDomain = enabled;
            else if (name.Equals("Private", StringComparison.OrdinalIgnoreCase)) fwPrivate = enabled;
            else if (name.Equals("Public", StringComparison.OrdinalIgnoreCase)) fwPublic = enabled;
        }

        var others = new List<string>();
        foreach (var item in Items(root, "Av"))
        {
            string name = item.TryGetProperty("Name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() ?? "" : "";
            int state = item.TryGetProperty("State", out var s) && s.ValueKind == JsonValueKind.Number && s.TryGetInt32(out int v) ? v : 0;
            bool on = (state & 0x1000) != 0;
            if (name.Length > 0 && on && !name.Contains("Windows Defender", StringComparison.OrdinalIgnoreCase)
                && !name.Contains("Microsoft Defender", StringComparison.OrdinalIgnoreCase)) others.Add(name);
        }

        bool? uac = root.TryGetProperty("Uac", out var u) && u.ValueKind == JsonValueKind.Number && u.TryGetInt32(out int lua) ? lua == 1 : null;
        return new SecurityStatus(defender, realTime, age, scan, fwDomain, fwPrivate, fwPublic, uac, others);
    }

    private static IEnumerable<JsonElement> Items(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var e)) yield break;
        if (e.ValueKind == JsonValueKind.Array)
            foreach (var item in e.EnumerateArray()) if (item.ValueKind == JsonValueKind.Object) yield return item;
        else if (e.ValueKind == JsonValueKind.Object) yield return e;
    }

    private static bool? Bool(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : null;

    public static IReadOnlyList<SecurityItem> Evaluate(SecurityStatus s, DateTime now)
    {
        var items = new List<SecurityItem>();

        if (s.OtherAntivirus.Count > 0)
            items.Add(new SecurityItem("Antivírus", SecurityLevel.Ok, $"Protegido por {string.Join(", ", s.OtherAntivirus)}. O Microsoft Defender fica em segundo plano."));
        else if (s.RealTimeProtection == true)
            items.Add(new SecurityItem("Antivírus", SecurityLevel.Ok, "Microsoft Defender com proteção em tempo real ligada."));
        else if (s.RealTimeProtection == false || s.DefenderEnabled == false)
            items.Add(new SecurityItem("Antivírus", SecurityLevel.Bad, "A proteção em tempo real está desligada e não há outro antivírus ativo. Ligue em Segurança do Windows > Proteção contra vírus e ameaças."));
        else
            items.Add(new SecurityItem("Antivírus", SecurityLevel.Warning, "Não foi possível confirmar se há proteção em tempo real."));

        if (s.SignatureAgeDays is int age)
            items.Add(age <= 7
                ? new SecurityItem("Definições de vírus", SecurityLevel.Ok, age == 0 ? "Atualizadas hoje." : $"Atualizadas há {age} dia(s).")
                : new SecurityItem("Definições de vírus", SecurityLevel.Warning, $"Estão com {age} dias. Clique em Atualizar definições."));

        var off = new List<string>();
        if (s.FirewallDomain == false) off.Add("domínio");
        if (s.FirewallPrivate == false) off.Add("rede privada");
        if (s.FirewallPublic == false) off.Add("rede pública");
        if (s.FirewallDomain is null && s.FirewallPrivate is null && s.FirewallPublic is null)
            items.Add(new SecurityItem("Firewall do Windows", SecurityLevel.Warning, "Não foi possível ler o estado do firewall."));
        else if (off.Count == 0)
            items.Add(new SecurityItem("Firewall do Windows", SecurityLevel.Ok, "Ligado em todos os perfis de rede."));
        else
            items.Add(new SecurityItem("Firewall do Windows", SecurityLevel.Bad, $"Desligado em: {string.Join(", ", off)}. Ligue em Segurança do Windows > Firewall e proteção de rede."));

        if (s.UacEnabled is bool uac)
            items.Add(uac
                ? new SecurityItem("Controle de Conta de Usuário", SecurityLevel.Ok, "Ligado.")
                : new SecurityItem("Controle de Conta de Usuário", SecurityLevel.Bad, "Desligado: programas podem alterar o sistema sem pedir permissão."));

        if (s.LastQuickScan is DateTime scan)
        {
            int days = (int)(now - scan).TotalDays;
            items.Add(days <= 30
                ? new SecurityItem("Última verificação rápida", SecurityLevel.Ok, $"Há {days} dia(s).")
                : new SecurityItem("Última verificação rápida", SecurityLevel.Warning, $"Há {days} dias. Rode uma verificação rápida."));
        }

        return items;
    }

    public static (bool Ok, string Message) UpdateSignatures()
    {
        var r = Shell.PowerShellUtf8("Update-MpSignature -ErrorAction Stop");
        return r.ExitCode == 0 ? (true, "Definições atualizadas.") : (false, string.IsNullOrWhiteSpace(r.Error) ? "Não foi possível atualizar." : r.Error.Trim());
    }

    public static (bool Ok, string Message) QuickScan()
    {
        var r = Shell.PowerShellUtf8("Start-MpScan -ScanType QuickScan -ErrorAction Stop");
        return r.ExitCode == 0 ? (true, "Verificação rápida concluída. Veja o resultado em Segurança do Windows.") : (false, string.IsNullOrWhiteSpace(r.Error) ? "Não foi possível verificar." : r.Error.Trim());
    }
}
