using System.Diagnostics;
using System.Net.NetworkInformation;

namespace MBExtreme.Core.Network;

/// <summary>Quanto a internet está enviando e recebendo agora, e um teste curto de velocidade de envio (usado para escolher a taxa da transmissão).</summary>
public static class NetThroughput
{
    public static double Mbps(long previousBytes, long currentBytes, double seconds) =>
        seconds <= 0 || currentBytes < previousBytes ? 0 : (currentBytes - previousBytes) * 8.0 / seconds / 1_000_000.0;

    /// <summary>Lê os contadores das placas de rede ativas (sem as virtuais e a local).</summary>
    public sealed class Sampler
    {
        private long _sent, _received;
        private long _stamp;
        private bool _has;

        public (double UpMbps, double DownMbps) Sample()
        {
            long sent = 0, received = 0;
            try
            {
                foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != OperationalStatus.Up) continue;
                    if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
                    var stats = nic.GetIPv4Statistics();
                    sent += stats.BytesSent;
                    received += stats.BytesReceived;
                }
            }
            catch (Exception ex) when (ex is NetworkInformationException or InvalidOperationException) { return (0, 0); }

            long now = Stopwatch.GetTimestamp();
            double up = 0, down = 0;
            if (_has)
            {
                double seconds = (now - _stamp) / (double)Stopwatch.Frequency;
                up = Mbps(_sent, sent, seconds);
                down = Mbps(_received, received, seconds);
            }

            _sent = sent; _received = received; _stamp = now; _has = true;
            return (up, down);
        }
    }
}

public static class UploadTest
{
    /// <summary>Envia dados para o teste público da Cloudflare por alguns segundos e devolve a velocidade de envio em Mbps (null sem internet).</summary>
    public static async Task<double?> RunAsync(HttpClient http, int seconds, CancellationToken token)
    {
        var data = new byte[1024 * 1024];
        long total = 0;
        var clock = Stopwatch.StartNew();
        try
        {
            while (clock.Elapsed.TotalSeconds < seconds && !token.IsCancellationRequested)
            {
                using var content = new ByteArrayContent(data);
                using var response = await http.PostAsync("https://speed.cloudflare.com/__up", content, token);
                if (!response.IsSuccessStatusCode) return null;
                total += data.Length;
            }
        }
        catch (HttpRequestException) { return null; }
        catch (TaskCanceledException) { return null; }

        return clock.Elapsed.TotalSeconds <= 0 || total == 0 ? null : total * 8.0 / clock.Elapsed.TotalSeconds / 1_000_000.0;
    }
}
