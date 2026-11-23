using System.Net;
using System.Net.Sockets;

namespace MBExtreme.Core.NetDiag;

/// <summary>
/// Política de segurança dos testes (RNF-01, seção 9): varredura só em rede privada e do tamanho de uma rede de casa ou escritório,
/// destinos externos só os fixos do diagnóstico. Nada aqui altera a rede.
/// </summary>
public static class NetScope
{
    /// <summary>Maior faixa que a varredura da rede local aceita sem pedir outra autorização (/22 = 1022 endereços).</summary>
    public const int MinPrefixForScan = 22;

    /// <summary>Endereços externos usados no teste de internet (públicos, de serviços de DNS conhecidos).</summary>
    public static readonly IReadOnlyList<string> ExternalTargets = new[] { "1.1.1.1", "8.8.8.8" };

    /// <summary>Resolvedor alternativo usado só para comparar com o DNS do PC (não troca nada).</summary>
    public const string AlternateResolver = "1.1.1.1";

    /// <summary>Nomes consultados no teste de DNS.</summary>
    public static readonly IReadOnlyList<string> DnsNames = new[] { "www.google.com", "www.microsoft.com", "www.mbinfoarte.com.br" };

    /// <summary>Página de teste do próprio Windows (NCSI): responde um texto fixo; redirecionar indica portal de login.</summary>
    public const string HttpProbeUrl = "http://www.msftconnecttest.com/connecttest.txt";
    public const string HttpProbeBody = "Microsoft Connect Test";

    public static bool IsPrivate(IPAddress ip)
    {
        if (ip.AddressFamily != AddressFamily.InterNetwork) return ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || IsUniqueLocal(ip);
        var b = ip.GetAddressBytes();
        return b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168) || (b[0] == 169 && b[1] == 254);
    }

    private static bool IsUniqueLocal(IPAddress ip) => (ip.GetAddressBytes()[0] & 0xFE) == 0xFC;

    /// <summary>A faixa pode ser varrida? Só IPv4 privado e no máximo /22. Devolve o motivo quando não pode.</summary>
    public static (bool Allowed, string Reason) CanScan(string network, int prefix)
    {
        if (!IPAddress.TryParse(network, out var ip) || ip.AddressFamily != AddressFamily.InterNetwork) return (false, "Endereço inválido.");
        if (!IsPrivate(ip)) return (false, "Só redes privadas (10.x, 172.16–31.x, 192.168.x) podem ser varridas.");
        if (prefix < MinPrefixForScan || prefix > 32) return (false, $"Faixa grande demais: o limite é /{MinPrefixForScan} (até 1022 endereços).");
        return (true, "");
    }

    public static bool SameNetwork(IPAddress a, IPAddress b, int prefix)
    {
        if (a.AddressFamily != b.AddressFamily || a.AddressFamily != AddressFamily.InterNetwork) return false;
        uint mask = prefix <= 0 ? 0 : prefix >= 32 ? uint.MaxValue : uint.MaxValue << (32 - prefix);
        return (ToUInt(a) & mask) == (ToUInt(b) & mask);
    }

    private static uint ToUInt(IPAddress ip)
    {
        var b = ip.GetAddressBytes();
        return (uint)(b[0] << 24 | b[1] << 16 | b[2] << 8 | b[3]);
    }

    /// <summary>Esconde dados para o relatório do cliente: 192.168.0.15 → 192.168.x.x, MAC → últimos 2 bytes.</summary>
    public static string Mask(string value)
    {
        if (IPAddress.TryParse(value, out var ip))
        {
            if (ip.AddressFamily == AddressFamily.InterNetwork) { var b = ip.GetAddressBytes(); return $"{b[0]}.{b[1]}.x.x"; }
            string s = ip.ToString();
            int cut = s.IndexOf(':', s.IndexOf(':') + 1);
            return (cut > 0 ? s[..cut] : s) + ":…";
        }
        if (value.Length >= 12 && value.Count(char.IsAsciiHexDigit) >= 12) return "xx-xx-xx-xx-" + string.Concat(value.Where(char.IsAsciiHexDigit).TakeLast(4)).Insert(2, "-");
        return value;
    }
}
