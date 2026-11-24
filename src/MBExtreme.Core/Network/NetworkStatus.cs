using System.Net.NetworkInformation;
using System.Net.Sockets;
using MBExtreme.Core.Dns;

namespace MBExtreme.Core.Network;

public sealed record ActiveAdapter(string Name, string? Gateway, IReadOnlyList<string> Dns, long LinkSpeedBitsPerSecond);

/// <summary>Adaptador de rede em uso, gateway (roteador) e servidores DNS.</summary>
public static class NetworkStatus
{
    /// <summary>Endereço local que o Windows usaria para sair para a internet (não envia pacotes).</summary>
    private static System.Net.IPAddress? RouteAddress()
    {
        try
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.Connect("1.1.1.1", 53);
            return (socket.LocalEndPoint as System.Net.IPEndPoint)?.Address;
        }
        catch (SocketException)
        {
            return null;
        }
    }

    public static ActiveAdapter? Active()
    {
        try
        {
            var route = RouteAddress();
            ActiveAdapter? fallback = null;
            foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (adapter.OperationalStatus != OperationalStatus.Up
                    || adapter.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;

                var props = adapter.GetIPProperties();
                string? gateway = props.GatewayAddresses
                    .Select(g => g.Address)
                    .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && !a.Equals(System.Net.IPAddress.Any))?.ToString();
                var dns = props.DnsAddresses.Where(a => a.AddressFamily == AddressFamily.InterNetwork).Select(a => a.ToString()).ToList();
                var found = new ActiveAdapter(adapter.Name, gateway, dns, adapter.Speed);
                if (route is not null && props.UnicastAddresses.Any(u => u.Address.Equals(route))) return found;
                if (gateway is not null) fallback ??= found;
                continue;
            }

            return fallback;
        }
        catch (NetworkInformationException)
        {
            return null;
        }
    }

    /// <summary>"Cloudflare (1.1.1.1)", "roteador (192.168.0.1)" ou só o endereço.</summary>
    public static string DnsLabel(string address, string? gateway = null)
    {
        foreach (var provider in DnsProviders.Defaults)
            if (address == provider.Primary || address == provider.Secondary) return $"{provider.Name} ({address})";
        if (gateway is not null && address == gateway) return $"roteador ({address})";
        return address;
    }

    public static string DescribeDns(IReadOnlyList<string> dns, string? gateway) =>
        dns.Count == 0 ? "não identificado" : string.Join(", ", dns.Take(2).Select(d => DnsLabel(d, gateway)));
}

/// <summary>Estatísticas das últimas medições de ping: média, oscilação (jitter) e perda.</summary>
public sealed class PingWindow
{
    private readonly Queue<double?> _samples = new();
    private readonly int _size;

    public PingWindow(int size = 20) => _size = Math.Max(2, size);

    public void Add(double? milliseconds)
    {
        _samples.Enqueue(milliseconds);
        while (_samples.Count > _size) _samples.Dequeue();
    }

    public int Count => _samples.Count;

    public double? Last => _samples.Count == 0 ? null : _samples.Last();

    public double? Average
    {
        get
        {
            var ok = _samples.Where(s => s.HasValue).Select(s => s!.Value).ToList();
            return ok.Count == 0 ? null : ok.Average();
        }
    }

    /// <summary>Média da diferença entre medições seguidas.</summary>
    public double? Jitter
    {
        get
        {
            var ok = _samples.Where(s => s.HasValue).Select(s => s!.Value).ToList();
            if (ok.Count < 2) return null;
            double total = 0;
            for (int i = 1; i < ok.Count; i++) total += Math.Abs(ok[i] - ok[i - 1]);
            return total / (ok.Count - 1);
        }
    }

    public double LossPercent => _samples.Count == 0 ? 0 : 100.0 * _samples.Count(s => s is null) / _samples.Count;
}

public sealed record RouteReport(int Hops, int? WorstJumpHop, string? WorstJumpAddress, double WorstJumpMs, double EndLossPercent, bool Healthy, string Text);

/// <summary>Lê o resultado de uma análise de rota (MTR) e diz, em português, se há um ponto problemático.</summary>
public static class RouteSummary
{
    public static RouteReport Summarize(IReadOnlyList<HopStats> hops)
    {
        var answered = hops.Where(h => h.Received > 0).ToList();
        if (answered.Count == 0) return new RouteReport(hops.Count, null, null, 0, 100, false, "Nenhum salto respondeu. Verifique a conexão.");

        // o salto só conta se a latência continua alta nos pontos seguintes (um roteador que responde devagar ao ping não é problema da rota)
        double previous = answered[0].Average, worstJump = 0;
        HopStats? worstHop = null;
        for (int i = 1; i < answered.Count; i++)
        {
            double sustained = answered.Skip(i).Min(h => h.Average);
            double jump = sustained - previous;
            if (jump > worstJump) { worstJump = jump; worstHop = answered[i]; }
            previous = Math.Max(previous, answered[i].Average);
        }

        var last = hops[^1];
        double endLoss = last.LossPercent;
        bool jumpBad = worstJump >= 40;
        bool lossBad = endLoss >= 5 && last.Sent >= 3 && last.Sent - last.Received >= 2;

        string text;
        if (!jumpBad && !lossBad)
            text = $"{hops.Count} saltos · rota estável (destino {last.Average:0} ms)";
        else
        {
            var parts = new List<string>();
            if (jumpBad) parts.Add($"o maior salto de latência é no ponto {worstHop!.Hop} ({worstHop.Address}): +{worstJump:0} ms");
            if (lossBad) parts.Add($"há {endLoss:0}% de perda até o destino");
            text = $"{hops.Count} saltos · atenção: " + string.Join("; ", parts) + ". O MB Extreme não troca a rota da internet; se persistir, teste outro DNS ou fale com o provedor.";
        }

        return new RouteReport(hops.Count, worstHop?.Hop, worstHop?.Address, worstJump, endLoss, !jumpBad && !lossBad, text);
    }
}
