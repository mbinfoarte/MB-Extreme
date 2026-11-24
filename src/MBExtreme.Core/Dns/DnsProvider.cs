namespace MBExtreme.Core.Dns;

public sealed record DnsProvider(string Name, string Primary, string Secondary, string Note = "");

public static class DnsProviders
{
    public static IReadOnlyList<DnsProvider> Defaults { get; } = new[]
    {
        new DnsProvider("Cloudflare", "1.1.1.1", "1.0.0.1", "Sem filtro"),
        new DnsProvider("Google", "8.8.8.8", "8.8.4.4", "Sem filtro"),
        new DnsProvider("Quad9", "9.9.9.9", "149.112.112.112", "Bloqueia domínios maliciosos"),
        new DnsProvider("AdGuard", "94.140.14.14", "94.140.15.15", "Bloqueia anúncios e rastreadores"),
        new DnsProvider("NextDNS", "45.90.28.0", "45.90.30.0", "Filtro configurável"),
        new DnsProvider("Control D", "76.76.2.0", "76.76.10.0", "Sem filtro"),
    };
}
