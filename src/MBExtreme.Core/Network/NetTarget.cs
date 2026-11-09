using System.Net;

namespace MBExtreme.Core.Network;

/// <summary>Destino digitado pelo usuário: aceita IP, nome do site ou o endereço copiado do navegador (https://site.com/pagina).</summary>
public sealed record NetTarget(string Host, int? Port)
{
    /// <summary>Extrai o nome (ou IP) e a porta; nulo quando não dá para entender.</summary>
    public static NetTarget? Parse(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        string text = input.Trim().Trim('"', '\'', '<', '>');
        int scheme = text.IndexOf("://", StringComparison.Ordinal);
        int? port = null;
        if (scheme >= 0)
        {
            port = text[..scheme].ToLowerInvariant() switch { "https" or "wss" => 443, "http" or "ws" => 80, _ => null };
            text = text[(scheme + 3)..];
        }

        if (text.Any(char.IsWhiteSpace)) return null;
        int end = text.IndexOfAny(new[] { '/', '?', '#' });
        if (end >= 0) text = text[..end];
        int at = text.LastIndexOf('@');
        if (at >= 0) text = text[(at + 1)..];

        string host;
        if (text.StartsWith('['))
        {
            int close = text.IndexOf(']');
            if (close < 0) return null;
            host = text[1..close];
            if (close + 1 < text.Length && text[close + 1] == ':' && int.TryParse(text[(close + 2)..], out int p6)) port = p6;
        }
        else
        {
            int colon = text.LastIndexOf(':');
            if (colon >= 0 && text.IndexOf(':') == colon) // um só ":" = porta (mais de um é IPv6 sem colchetes)
            {
                if (int.TryParse(text[(colon + 1)..], out int p)) port = p;
                else return null;
                text = text[..colon];
            }
            host = text;
        }

        host = host.TrimEnd('.');
        if (port is < 1 or > 65535) return null;
        if (host.Length == 0 || host.Length > 253 || Uri.CheckHostName(host) == UriHostNameType.Unknown) return null;
        return new NetTarget(host, port);
    }

    /// <summary>Endereços do site (IPv4 primeiro). Vazio se o nome não existe.</summary>
    public static async Task<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken token = default)
    {
        if (IPAddress.TryParse(host, out var literal)) return new[] { literal };
        try
        {
            var all = await System.Net.Dns.GetHostAddressesAsync(host, token);
            return all.OrderBy(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? 0 : 1).Distinct().ToList();
        }
        catch (System.Net.Sockets.SocketException) { return Array.Empty<IPAddress>(); }
    }
}
