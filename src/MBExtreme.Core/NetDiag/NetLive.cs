using MBExtreme.Core.Network;

namespace MBExtreme.Core.NetDiag;

// Painel ao vivo da Central de Rede: ping contínuo, eventos, mapa da conexão e conexões por programa.
// Tudo é montado a partir de medições reais (ping de verdade, diagnóstico salvo, varredura da rede); nada é inventado.

/// <summary>Série de pings contínuos (os mais recentes). null = sem resposta.</summary>
public sealed class PingSeries
{
    private readonly List<double?> _samples = new();

    public PingSeries(int capacity = 120) => Capacity = Math.Max(2, capacity);

    public int Capacity { get; }

    public IReadOnlyList<double?> Samples => _samples;

    public void Add(double? ms)
    {
        _samples.Add(ms);
        if (_samples.Count > Capacity) _samples.RemoveAt(0);
    }

    public void Clear() => _samples.Clear();

    private List<double> Ok => _samples.Where(s => s.HasValue).Select(s => s!.Value).ToList();

    public double? Median
    {
        get
        {
            var ok = Ok.OrderBy(x => x).ToList();
            if (ok.Count == 0) return null;
            return ok.Count % 2 == 1 ? ok[ok.Count / 2] : (ok[ok.Count / 2 - 1] + ok[ok.Count / 2]) / 2;
        }
    }

    /// <summary>Variação: média da diferença entre respostas seguidas.</summary>
    public double? Jitter
    {
        get
        {
            var ok = Ok;
            return ok.Count < 2 ? null : Math.Round(ok.Zip(ok.Skip(1), (a, b) => Math.Abs(b - a)).Average(), 1);
        }
    }

    public double LossPct => _samples.Count == 0 ? 0 : Math.Round(100.0 * _samples.Count(s => !s.HasValue) / _samples.Count, 1);

    /// <summary>Limite de pico: 2,5 vezes a mediana e pelo menos 25 ms acima dela.</summary>
    public static bool IsSpike(double ms, double? median) => median is { } m && ms > Math.Max(m * 2.5, m + 25);

    /// <summary>Índices dos picos na série.</summary>
    public IReadOnlyList<int> Spikes()
    {
        double? m = Median;
        var list = new List<int>();
        for (int i = 0; i < _samples.Count; i++)
            if (_samples[i] is { } v && IsSpike(v, m)) list.Add(i);
        return list;
    }
}

/// <summary>Um acontecimento do registro de eventos da Central de Rede.</summary>
public sealed record NetEvent(DateTimeOffset At, string Text, FindingSeverity Level);

/// <summary>Registro de eventos (o mais novo primeiro), com limite.</summary>
public sealed class NetEventLog
{
    private readonly List<NetEvent> _items = new();

    public NetEventLog(int max = 200) => Max = Math.Max(1, max);

    public int Max { get; }

    public IReadOnlyList<NetEvent> Items => _items;

    public void Add(NetEvent e)
    {
        _items.Insert(0, e);
        if (_items.Count > Max) _items.RemoveAt(_items.Count - 1);
    }

    public void Add(DateTimeOffset at, string text, FindingSeverity level = FindingSeverity.Info) => Add(new NetEvent(at, text, level));
}

/// <summary>
/// Regras do ping contínuo: pico de latência, queda (3 pings seguidos sem resposta) e volta. Cada mudança vira um evento, sem repetir
/// o mesmo aviso a cada segundo.
/// </summary>
public sealed class PingWatch
{
    public const int LostInARow = 3;
    private int _lost;
    private bool _down;

    public PingWatch(string target) => Target = target;

    public string Target { get; }

    public NetEvent? Feed(DateTimeOffset at, double? ms, double? median)
    {
        if (ms is null)
        {
            _lost++;
            if (_lost == LostInARow && !_down) { _down = true; return new NetEvent(at, $"Sem resposta de {Target} ({LostInARow} seguidas)", FindingSeverity.Problema); }
            return null;
        }
        _lost = 0;
        if (_down) { _down = false; return new NetEvent(at, $"{Target} voltou a responder · {ms:0} ms", FindingSeverity.Info); }
        if (PingSeries.IsSpike(ms.Value, median)) return new NetEvent(at, $"Pico de {ms:0} ms em {Target} (normal {median:0} ms)", FindingSeverity.Atencao);
        return null;
    }
}

/// <summary>Um ponto do mapa da conexão.</summary>
public sealed record MapNode(string Title, string Value, FindingSeverity Level, bool Tested = true);

/// <summary>Um destino da internet no mapa (lado direito).</summary>
public sealed record MapTarget(string Name, double? Ms, FindingSeverity Level);

/// <summary>Um aparelho da rede local no mapa (lado esquerdo).</summary>
public sealed record MapDevice(string Name, string Ip, bool Conflict);

public sealed record ConnectionMap(MapNode Pc, MapNode Router, MapNode Isp, MapNode Internet, int? IspHop, IReadOnlyList<MapTarget> Targets, IReadOnlyList<MapDevice> Devices);

/// <summary>Monta o mapa PC → roteador → provedor → internet a partir do diagnóstico, da rota e da varredura da rede.</summary>
public static class ConnectionMapRules
{
    private static FindingSeverity Of(RttStats? s, double warnMs, double badMs) =>
        s is null ? FindingSeverity.Info : s.AllLost || s.LossPct >= 5 || (s.Median ?? 0) >= badMs ? FindingSeverity.Problema
        : s.LossPct > 0 || (s.Median ?? 0) >= warnMs ? FindingSeverity.Atencao : FindingSeverity.Info;

    private static string Ms(double? v) => v is null ? "—" : v < 1 ? $"{v:0.0} ms".Replace('.', ',') : $"{v:0} ms";

    public static string TargetName(string host) => host switch
    {
        "1.1.1.1" or "1.0.0.1" or "one.one.one.one" => "Cloudflare",
        "8.8.8.8" or "8.8.4.4" or "dns.google" or "www.google.com" => "Google",
        "9.9.9.9" => "Quad9",
        "208.67.222.222" => "OpenDNS",
        _ => host,
    };

    public static ConnectionMap Build(NetDiagReport? report, IReadOnlyList<LanDevice>? lan = null, IReadOnlyList<IpConflict>? conflicts = null, double? livePingMs = null)
    {
        var e = report?.Evidence;
        var p = e?.Primary;
        var pc = p is null ? new MapNode("Este PC", "não testado", FindingSeverity.Info, false)
            : new MapNode("Este PC", p.Up ? Ms(e!.Loopback?.Median ?? 0.1) : "placa desligada", p.Up ? FindingSeverity.Info : FindingSeverity.Problema);
        var router = e?.Gateway is { } g ? new MapNode("Roteador", g.AllLost ? "sem resposta" : Ms(g.Median), Of(g, 10, 50))
            : new MapNode("Roteador", "não testado", FindingSeverity.Info, false);

        // provedor: o salto do provedor com mais perda (ou o mais lento)
        MapNode isp = new("Provedor", "não testado", FindingSeverity.Info, false);
        int? ispHop = null;
        if (e is { Hops.Count: > 0 })
        {
            var labels = RouteLabels.Label(e.Hops.Select(h => h.Address).ToList());
            var hops = e.Hops.Select((h, i) => (h, label: labels[i])).Where(x => x.label == "Provedor" && !x.h.Rtt.AllLost).ToList();
            if (hops.Count > 0)
            {
                var worst = hops.OrderByDescending(x => x.h.Rtt.LossPct).ThenByDescending(x => x.h.Rtt.Median ?? 0).First();
                var last = e.Hops[^1].Rtt;
                bool finalOk = !last.AllLost && last.LossPct == 0;
                var lvl = Of(worst.h.Rtt, 30, 80);
                // perda só num salto do meio, com o destino respondendo 100%, é aviso (pode ser limite do equipamento), não problema
                if (lvl == FindingSeverity.Problema && finalOk) lvl = FindingSeverity.Atencao;
                string loss = worst.h.Rtt.LossPct > 0 ? $" · perda {worst.h.Rtt.LossPct:0.#}%".Replace('.', ',') : "";
                isp = new MapNode("Provedor", Ms(worst.h.Rtt.Median) + loss, lvl);
                if (worst.h.Rtt.LossPct > 0 || lvl != FindingSeverity.Info) ispHop = worst.h.Ttl;
            }
        }

        var ext = e?.External.Where(x => !x.Value.AllLost).ToList() ?? new();
        double? best = livePingMs ?? (ext.Count > 0 ? ext.Min(x => x.Value.Median) : null);
        var internet = e is null ? new MapNode("Internet", "não testado", FindingSeverity.Info, false)
            : ext.Count == 0 && livePingMs is null ? new MapNode("Internet", "sem resposta", FindingSeverity.Problema)
            : new MapNode("Internet", Ms(best), best >= 120 ? FindingSeverity.Problema : best >= 60 ? FindingSeverity.Atencao : FindingSeverity.Info);

        var targets = (e?.External ?? new Dictionary<string, RttStats>())
            .Select(x => new MapTarget(TargetName(x.Key), x.Value.AllLost ? null : x.Value.Median, Of(x.Value, 60, 120)))
            .GroupBy(t => t.Name).Select(gr => gr.First()).Take(4).ToList();

        var conflictIps = (conflicts ?? Array.Empty<IpConflict>()).Select(c => c.Ip).ToHashSet();
        var devices = (lan ?? Array.Empty<LanDevice>())
            .Where(d => !d.IsRouter && !d.IsThisPc)
            .OrderByDescending(d => conflictIps.Contains(d.Ip))
            .ThenBy(d => d.Ip.Length).ThenBy(d => d.Ip, StringComparer.Ordinal)
            .Take(4)
            .Select(d => new MapDevice(d.Name.Length > 0 ? d.Name : d.Vendor.Length > 0 ? d.Vendor : "Aparelho", d.Ip, conflictIps.Contains(d.Ip)))
            .ToList();

        return new ConnectionMap(pc, router, isp, internet, ispHop, targets, devices);
    }
}

/// <summary>Conexões abertas por programa (TCP), a partir da tabela do Windows. Não mede bytes por programa: mostra quantas conexões cada um tem.</summary>
public static class TrafficRules
{
    public const uint Established = 5;

    public static IReadOnlyList<(string Program, int Connections)> ByProgram(IEnumerable<(uint State, int Pid)> rows, Func<int, string?> nameOf, int top = 5) =>
        rows.Where(r => r.State == Established && r.Pid > 4)
            .GroupBy(r => nameOf(r.Pid) ?? "")
            .Where(g => g.Key.Length > 0)
            .Select(g => (Program: g.Key, Connections: g.Count()))
            .OrderByDescending(x => x.Connections).ThenBy(x => x.Program, StringComparer.CurrentCulture)
            .Take(top)
            .ToList();

    /// <summary>Velocidade em texto curto (bits por segundo, como o provedor vende).</summary>
    public static string Rate(double bytesPerSecond)
    {
        double bits = Math.Max(0, bytesPerSecond) * 8;
        return bits >= 1_000_000 ? $"{bits / 1_000_000:0.#} Mb/s".Replace('.', ',') : bits >= 1_000 ? $"{bits / 1_000:0} kb/s" : $"{bits:0} b/s";
    }
}
