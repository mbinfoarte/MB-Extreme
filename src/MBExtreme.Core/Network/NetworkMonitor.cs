using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;

namespace MBExtreme.Core.Network;

/// <summary>Velocidade de download e envio (soma dos adaptadores ativos), em bytes por segundo.</summary>
public sealed record NetworkSample(double DownloadBytesPerSecond, double UploadBytesPerSecond);

public sealed class NetworkMonitor
{
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private long _lastReceived;
    private long _lastSent;
    private double _lastSeconds;
    private bool _primed;

    private static (long Received, long Sent) Totals()
    {
        long received = 0, sent = 0;
        foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (adapter.OperationalStatus != OperationalStatus.Up
                || adapter.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                continue;
            try
            {
                var stats = adapter.GetIPv4Statistics();
                received += stats.BytesReceived;
                sent += stats.BytesSent;
            }
            catch (NetworkInformationException)
            {
                // Adaptador sem IPv4: ignorar.
            }
        }

        return (received, sent);
    }

    /// <summary>Chame em intervalos regulares. A primeira chamada devolve zero.</summary>
    public NetworkSample Sample()
    {
        var (received, sent) = Totals();
        double now = _clock.Elapsed.TotalSeconds;
        if (!_primed)
        {
            (_lastReceived, _lastSent, _lastSeconds, _primed) = (received, sent, now, true);
            return new NetworkSample(0, 0);
        }

        double elapsed = Math.Max(0.001, now - _lastSeconds);
        var sample = new NetworkSample(
            Math.Max(0, (received - _lastReceived) / elapsed),
            Math.Max(0, (sent - _lastSent) / elapsed));
        (_lastReceived, _lastSent, _lastSeconds) = (received, sent, now);
        return sample;
    }

    /// <summary>Latência até um servidor, em milissegundos. Nulo se não respondeu.</summary>
    public static async Task<double?> PingAsync(string host = "1.1.1.1", int timeoutMs = 1500)
    {
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(IPAddress.Parse(host), timeoutMs);
            return reply.Status == IPStatus.Success ? Math.Max(1, reply.RoundtripTime) : null;
        }
        catch (Exception ex) when (ex is PingException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }
}
