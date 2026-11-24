namespace MBExtreme.Core.NetDiag;

// Diagnóstico de rede (especificação docs/NETWORK-DIAGNOSTICS.md, RF-01/04/05/06/14/16).
// Regra de ouro: medir antes de recomendar; nada aqui muda a rede. Os valores são sempre medidos de verdade (nada inventado).

/// <summary>Como a conclusão foi tirada: confirmado pelos testes, provável, indeterminado ou não testado.</summary>
public enum FindingStatus { Confirmado, Provavel, Indeterminado, NaoTestado }

public enum FindingSeverity { Info, Atencao, Problema }

/// <summary>Uma placa de rede como o Windows informa (API do .NET, sem ler texto de comando).</summary>
public sealed record NetIface(
    string Name, string Description, string Kind, bool Up, string Mac, long SpeedBps,
    IReadOnlyList<string> Ipv4, IReadOnlyList<int> Ipv4Prefix, IReadOnlyList<string> Ipv6,
    IReadOnlyList<string> Gateways, IReadOnlyList<string> Dns, bool Dhcp, string? DhcpServer, int Mtu, bool IsVirtual)
{
    public bool IsWifi => Kind == "Wi-Fi";
    public bool IsEthernet => Kind == "Ethernet";
    public bool IsVpn => Kind == "VPN";
}

/// <summary>Estatística de latência de uma série de pings (ms). Variação = média da diferença entre respostas seguidas.</summary>
public sealed record RttStats(int Sent, int Received, double? Min, double? Median, double? P95, double? Max, double? Jitter)
{
    public double LossPct => Sent == 0 ? 0 : Math.Round(100.0 * (Sent - Received) / Sent, 1);
    public bool AllLost => Sent > 0 && Received == 0;

    public static RttStats From(int sent, IReadOnlyList<double> rtts)
    {
        if (rtts.Count == 0) return new RttStats(sent, 0, null, null, null, null, null);
        var sorted = rtts.OrderBy(x => x).ToList();
        double Pct(double p) => sorted[Math.Clamp((int)Math.Ceiling(p * sorted.Count) - 1, 0, sorted.Count - 1)];
        double? jitter = rtts.Count < 2 ? null : Math.Round(rtts.Zip(rtts.Skip(1), (a, b) => Math.Abs(b - a)).Average(), 2);
        return new RttStats(sent, rtts.Count, sorted[0], Math.Round(Pct(0.5), 2), Math.Round(Pct(0.95), 2), sorted[^1], jitter);
    }

    public string Text => AllLost ? $"sem resposta ({Sent} tentativas)"
        : $"mediana {Median:0.#} ms · p95 {P95:0.#} ms · variação {Jitter ?? 0:0.#} ms · perda {LossPct:0.#}%";
}

/// <summary>Resposta de um teste de DNS.</summary>
public sealed record DnsCheck(string Resolver, string Name, bool Ok, double? Ms, string Detail);

/// <summary>Teste de HTTP(S) para saber se a internet abre página (e se há portal de login no caminho).</summary>
public sealed record HttpCheck(string Url, bool Ok, int? Status, bool Redirected, string Detail);

/// <summary>Um salto do traceroute.</summary>
public sealed record HopResult(int Ttl, string? Address, RttStats Rtt);

/// <summary>Uma hipótese do assistente: evidências a favor, contra, o quão provável e o próximo teste que separa as causas.</summary>
public sealed record Finding(string Code, FindingSeverity Severity, FindingStatus Status, double Confidence, string Title,
    IReadOnlyList<string> Evidence, IReadOnlyList<string> Counter, string Next);

/// <summary>Tudo o que foi medido numa rodada. Campo nulo = não testado (e o relatório diz isso).</summary>
public sealed class NetDiagEvidence
{
    public List<NetIface> Interfaces { get; set; } = new();
    public NetIface? Primary { get; set; }
    public RttStats? Loopback { get; set; }
    public string? GatewayAddress { get; set; }
    public RttStats? Gateway { get; set; }
    public Dictionary<string, RttStats> External { get; set; } = new();
    public List<DnsCheck> DnsSystem { get; set; } = new();
    public List<DnsCheck> DnsAlternate { get; set; } = new();
    public HttpCheck? Http { get; set; }
    public List<HopResult> Hops { get; set; } = new();
    public string? TraceTarget { get; set; }
}

public sealed record NetDiagReport(int SchemaVersion, DateTimeOffset StartedUtc, DateTimeOffset FinishedUtc, string Machine,
    NetDiagEvidence Evidence, IReadOnlyList<Finding> Findings, IReadOnlyList<string> NotTested)
{
    public const int CurrentSchema = 1;
}
