using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace MBExtreme.Core.Network;

/// <summary>Estatísticas de um ponto do caminho (um salto no MTR) ou do destino (ping).</summary>
public sealed class HopStats
{
    public int Hop { get; init; }
    public string Address { get; set; } = "???";
    public int Sent { get; private set; }
    public int Received { get; private set; }
    public double? Last { get; private set; }
    public double Best { get; private set; } = double.MaxValue;
    public double Worst { get; private set; }
    private double _total;
    private double? _previousRtt;
    private double _jitterTotal;
    private int _jitterCount;

    public double LossPercent => Sent == 0 ? 0 : 100.0 * (Sent - Received) / Sent;
    public double Average => Received == 0 ? 0 : _total / Received;
    /// <summary>Variação média entre respostas seguidas (ms). Valores altos causam "picos" de lag mesmo com ping médio bom.</summary>
    public double Jitter => _jitterCount == 0 ? 0 : _jitterTotal / _jitterCount;
    public double BestOrZero => Received == 0 ? 0 : Best;

    public void Add(double? milliseconds)
    {
        Sent++;
        Last = milliseconds;
        if (milliseconds is not { } ms) return;
        Received++;
        if (_previousRtt is { } before)
        {
            _jitterTotal += Math.Abs(ms - before);
            _jitterCount++;
        }

        _previousRtt = ms;
        _total += ms;
        Best = Math.Min(Best, ms);
        Worst = Math.Max(Worst, ms);
    }
}

/// <summary>Ping, TCPing e MTR (caminho salto a salto com perda e latência).</summary>
public static class NetAnalyzer
{
    public static bool IsValidHost(string host) =>
        !string.IsNullOrWhiteSpace(host) && host.Length <= 253 && Uri.CheckHostName(host.Trim()) != UriHostNameType.Unknown;

    public static async Task<double?> PingAsync(string host, int timeoutMs = 2000)
    {
        using var ping = new Ping();
        try
        {
            var reply = await ping.SendPingAsync(host, timeoutMs);
            return reply.Status == IPStatus.Success ? reply.RoundtripTime : null;
        }
        catch (PingException)
        {
            return null;
        }
    }

    public static async Task<double?> TcpPingAsync(string host, int port, int timeoutMs, CancellationToken token)
    {
        using var client = new TcpClient();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(timeoutMs);
        var watch = Stopwatch.StartNew();
        try
        {
            await client.ConnectAsync(host, port, timeout.Token);
            return watch.Elapsed.TotalMilliseconds;
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException)
        {
            token.ThrowIfCancellationRequested();
            return null;
        }
    }

    /// <summary>
    /// Repete rodadas até ser cancelado. Cada rodada envia um pacote por salto (TTL crescente) e chama
    /// <paramref name="onRound"/> com as estatísticas acumuladas.
    /// </summary>
    public static async Task TraceAsync(string host, int maxHops, Action<IReadOnlyList<HopStats>> onRound, CancellationToken token)
    {
        var hops = new List<HopStats>();
        int limit = maxHops;
        using var ping = new Ping();
        var data = new byte[32];

        while (!token.IsCancellationRequested)
        {
            for (int ttl = 1; ttl <= limit; ttl++)
            {
                token.ThrowIfCancellationRequested();
                while (hops.Count < ttl) hops.Add(new HopStats { Hop = hops.Count + 1 });
                var stats = hops[ttl - 1];

                PingReply reply;
                try
                {
                    reply = await ping.SendPingAsync(host, 1000, data, new PingOptions(ttl, true));
                }
                catch (PingException)
                {
                    stats.Add(null);
                    continue;
                }

                if (reply.Status is IPStatus.Success or IPStatus.TtlExpired)
                {
                    stats.Address = reply.Address?.ToString() ?? "???";
                    stats.Add(reply.RoundtripTime);
                }
                else
                {
                    stats.Add(null);
                }

                if (reply.Status == IPStatus.Success)
                {
                    limit = ttl;
                    break;
                }
            }

            onRound(hops.Take(limit).ToList());
            try { await Task.Delay(1000, token); }
            catch (OperationCanceledException) { break; }
        }
    }
}
