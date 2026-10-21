using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using MBExtreme.Core.Progress;

namespace MBExtreme.Core.Dns;

public sealed record DnsResult(DnsProvider Provider, double? AverageMs, int Successes, int Attempts);

/// <summary>Mede o tempo de resposta de servidores DNS e os ordena do mais rápido ao mais lento.</summary>
public static class DnsBenchmark
{
    public static IReadOnlyList<string> DefaultDomains { get; } =
        new[] { "google.com", "youtube.com", "facebook.com", "amazon.com", "wikipedia.org" };

    public static async Task<IReadOnlyList<DnsResult>> RunAsync(
        IReadOnlyList<DnsProvider> providers,
        IReadOnlyList<string> domains,
        int rounds,
        TimeSpan timeout,
        IProgress<ProgressEvent>? progress = null,
        CancellationToken ct = default)
    {
        var results = new List<DnsResult>();
        int total = Math.Max(1, providers.Count * domains.Count * rounds);
        int done = 0;

        foreach (var provider in providers)
        {
            var times = new List<double>();
            for (int round = 0; round < rounds; round++)
            {
                foreach (var domain in domains)
                {
                    ct.ThrowIfCancellationRequested();
                    if (await QueryAsync(provider.Primary, domain, timeout, ct) is double ms)
                        times.Add(ms);
                    done++;
                    progress?.Report(new ProgressEvent("Servidores DNS", 100.0 * done / total, $"Testando {provider.Name}"));
                }
            }

            results.Add(new DnsResult(
                provider,
                times.Count > 0 ? times.Average() : null,
                times.Count,
                rounds * domains.Count));
        }

        return results.OrderBy(r => r.AverageMs ?? double.MaxValue).ToList();
    }

    private static async Task<double?> QueryAsync(string server, string host, TimeSpan timeout, CancellationToken ct)
    {
        try
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            await socket.ConnectAsync(new IPEndPoint(IPAddress.Parse(server), 53), ct);

            ushort id = (ushort)Random.Shared.Next(0, 65536);
            var query = DnsPacket.BuildQuery(id, host);

            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutSource.CancelAfter(timeout);

            var watch = Stopwatch.StartNew();
            await socket.SendAsync(query, SocketFlags.None, timeoutSource.Token);
            var buffer = new byte[512];
            int length = await socket.ReceiveAsync(buffer, SocketFlags.None, timeoutSource.Token);
            watch.Stop();

            return DnsPacket.IsSuccessResponse(buffer, length, id) ? watch.Elapsed.TotalMilliseconds : null;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return null; // tempo esgotado
        }
        catch (SocketException)
        {
            return null;
        }
    }
}
