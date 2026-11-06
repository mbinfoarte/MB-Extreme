using System.Diagnostics;
using System.Text.Json;

namespace MBExtreme.Core.Network;

/// <summary>Resultado de um teste de internet.</summary>
public sealed record SpeedResult(DateTimeOffset At, double? DownloadMbps, double? UploadMbps, double? PingMs, double? JitterMs, string? ServerCode, string? City, int Connections,
    double? LoadedPingMs = null, double? RouterMs = null, double? DnsMs = null, string? Name = null);

/// <summary>Contas do teste de internet (separadas para testar sem rede).</summary>
public static class SpeedMath
{
    public static double Mbps(long bytes, double seconds) => seconds <= 0 ? 0 : bytes * 8.0 / seconds / 1_000_000.0;

    public static double Median(IReadOnlyList<double> values)
    {
        if (values.Count == 0) return 0;
        var sorted = values.OrderBy(v => v).ToList();
        int mid = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
    }

    /// <summary>Variação (jitter): média da diferença entre medições seguidas, como nos testes de internet comuns.</summary>
    public static double Jitter(IReadOnlyList<double> values) =>
        values.Count < 2 ? 0 : values.Zip(values.Skip(1), (a, b) => Math.Abs(b - a)).Average();

    /// <summary>
    /// Ping sob carga (bufferbloat): quanto a latência sobe enquanto a internet está cheia (download rodando). É o que faz o jogo
    /// travar quando alguém da casa está baixando ou vendo vídeo. Faixas parecidas com as dos testes de bufferbloat conhecidos.
    /// </summary>
    public static (string Grade, string Text) Bufferbloat(double? idleMs, double? loadedMs)
    {
        if (idleMs is not double idle || loadedMs is not double loaded) return ("?", "Não deu para medir o ping sob carga.");
        double rise = Math.Max(0, loaded - idle);
        return rise switch
        {
            < 5 => ("A+", $"Excelente: o ping quase não sobe com a internet cheia (+{rise:0} ms)."),
            < 30 => ("A", $"Muito bom: +{rise:0} ms com a internet cheia."),
            < 60 => ("B", $"Bom: +{rise:0} ms com a internet cheia; jogo e chamada seguem bem."),
            < 200 => ("C", $"Atenção: +{rise:0} ms com a internet cheia. Jogos e chamadas podem travar quando alguém baixa algo. O QoS/SQM do roteador ajuda."),
            _ => ("F", $"Ruim: +{rise:0} ms com a internet cheia. O roteador enche a fila: ligue o QoS/SQM ou troque o roteador."),
        };
    }

    /// <summary>Lê a resposta "chave=valor" do /cdn-cgi/trace da Cloudflare.</summary>
    public static IReadOnlyDictionary<string, string> ParseTrace(string text) =>
        text.Split('\n').Select(l => l.Trim()).Where(l => l.Contains('='))
            .Select(l => (Key: l[..l.IndexOf('=')], Value: l[(l.IndexOf('=') + 1)..]))
            .GroupBy(p => p.Key).ToDictionary(g => g.Key, g => g.Last().Value);

    /// <summary>Cidade do servidor pelo código do aeroporto que a Cloudflare usa (os mais comuns para quem está no Brasil).</summary>
    public static string? CityOf(string? code) => code?.ToUpperInvariant() switch
    {
        "GRU" => "São Paulo", "GIG" => "Rio de Janeiro", "FOR" => "Fortaleza", "POA" => "Porto Alegre", "CWB" => "Curitiba",
        "BSB" => "Brasília", "SSA" => "Salvador", "REC" => "Recife", "BEL" => "Belém", "BNU" => "Blumenau", "CNF" => "Belo Horizonte",
        "FLN" => "Florianópolis", "GYN" => "Goiânia", "MAO" => "Manaus", "CGB" => "Cuiabá", "VCP" => "Campinas", "SJP" => "São José do Rio Preto",
        "ITJ" => "Itajaí", "JOI" => "Joinville", "QWJ" => "Americana", "UDI" => "Uberlândia", "SJK" => "São José dos Campos", "NVT" => "Navegantes",
        "EZE" => "Buenos Aires", "SCL" => "Santiago", "LIM" => "Lima", "BOG" => "Bogotá", "MIA" => "Miami", "LIS" => "Lisboa", "MAD" => "Madri",
        _ => null,
    };
}

/// <summary>
/// Teste de internet pelo servidor público de velocidade da Cloudflare: várias conexões ao mesmo tempo, alguns segundos de aquecimento
/// descartados (o começo de uma conexão é sempre mais lento) e só então a medição. Não é o Speedtest da Ookla: o servidor e o método são outros,
/// então os números podem diferir.
/// </summary>
public sealed class SpeedTest(HttpClient http, string baseUrl = "https://speed.cloudflare.com")
{
    private const int ChunkBytes = 25_000_000;

    /// <summary>Latência (mediana) e variação: tempo até a resposta de pedidos vazios.</summary>
    public async Task<(double? PingMs, double? JitterMs)> LatencyAsync(int samples, CancellationToken ct)
    {
        var times = new List<double>();
        for (int i = 0; i < samples; i++)
        {
            ct.ThrowIfCancellationRequested();
            var clock = Stopwatch.StartNew();
            try
            {
                using var response = await http.GetAsync($"{baseUrl}/__down?bytes=0", HttpCompletionOption.ResponseHeadersRead, ct);
                if (!response.IsSuccessStatusCode) continue;
                times.Add(clock.Elapsed.TotalMilliseconds);
            }
            catch (HttpRequestException) { }
        }
        if (times.Count == 0) return (null, null);
        var useful = times.Count > 2 ? times.Skip(1).ToList() : times;   // a primeira inclui abrir a conexão (DNS, TLS)
        return (SpeedMath.Median(useful), SpeedMath.Jitter(useful));
    }

    /// <summary>Velocidade de download com <paramref name="connections"/> conexões ao mesmo tempo.</summary>
    public Task<double?> DownloadAsync(int connections, TimeSpan warmup, TimeSpan measure, IProgress<double>? live, CancellationToken ct) =>
        MeasureAsync(connections, warmup, measure, live, ct, async (counter, token) =>
        {
            using var response = await http.GetAsync($"{baseUrl}/__down?bytes={ChunkBytes}", HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(token);
            var buffer = new byte[64 * 1024];
            int read;
            while ((read = await stream.ReadAsync(buffer, token)) > 0) counter(read);
        });

    /// <summary>Velocidade de envio com várias conexões ao mesmo tempo.</summary>
    public Task<double?> UploadAsync(int connections, TimeSpan warmup, TimeSpan measure, IProgress<double>? live, CancellationToken ct) =>
        MeasureAsync(connections, warmup, measure, live, ct, async (counter, token) =>
        {
            var data = new byte[2 * 1024 * 1024];
            using var content = new ByteArrayContent(data);
            using var response = await http.PostAsync($"{baseUrl}/__up", content, token);
            response.EnsureSuccessStatusCode();
            counter(data.Length);
        });

    private static async Task<double?> MeasureAsync(int connections, TimeSpan warmup, TimeSpan measure, IProgress<double>? live, CancellationToken ct,
        Func<Action<int>, CancellationToken, Task> transfer)
    {
        long total = 0;
        int failures = 0;
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var clock = Stopwatch.StartNew();
        var workers = Enumerable.Range(0, Math.Clamp(connections, 1, 16)).Select(_ => Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                try { await transfer(n => Interlocked.Add(ref total, n), stop.Token); }
                catch (OperationCanceledException) { return; }
                catch (HttpRequestException) { if (Interlocked.Increment(ref failures) > 20) return; }
            }
        })).ToArray();

        // aquecimento: descarta o começo, quando as conexões ainda estão acelerando
        try { await Task.Delay(warmup, ct); } catch (OperationCanceledException) { stop.Cancel(); await Task.WhenAll(workers); throw; }
        long baseBytes = Interlocked.Read(ref total);
        double baseTime = clock.Elapsed.TotalSeconds;
        var end = baseTime + measure.TotalSeconds;
        while (clock.Elapsed.TotalSeconds < end && !ct.IsCancellationRequested)
        {
            await Task.Delay(250, CancellationToken.None);
            live?.Report(SpeedMath.Mbps(Interlocked.Read(ref total) - baseBytes, clock.Elapsed.TotalSeconds - baseTime));
        }
        long bytes = Interlocked.Read(ref total) - baseBytes;
        double seconds = clock.Elapsed.TotalSeconds - baseTime;
        stop.Cancel();
        try { await Task.WhenAll(workers); } catch (OperationCanceledException) { }
        ct.ThrowIfCancellationRequested();
        return bytes <= 0 ? null : SpeedMath.Mbps(bytes, seconds);
    }

    /// <summary>Ping medido enquanto o download roda (a fila do roteador cheia).</summary>
    public async Task<(double? Down, double? LoadedPing)> DownloadWithLoadedPingAsync(int connections, TimeSpan warmup, TimeSpan measure, IProgress<double>? live, CancellationToken ct)
    {
        var download = DownloadAsync(connections, warmup, measure, live, ct);
        await Task.Delay(warmup + TimeSpan.FromSeconds(1), ct);
        var times = new List<double>();
        var end = DateTime.UtcNow + measure - TimeSpan.FromSeconds(2);
        while (DateTime.UtcNow < end && !download.IsCompleted)
        {
            ct.ThrowIfCancellationRequested();
            var clock = Stopwatch.StartNew();
            try
            {
                using var response = await http.GetAsync($"{baseUrl}/__down?bytes=0", HttpCompletionOption.ResponseHeadersRead, ct);
                if (response.IsSuccessStatusCode) times.Add(clock.Elapsed.TotalMilliseconds);
            }
            catch (HttpRequestException) { }
            await Task.Delay(300, ct);
        }
        double? down = await download;
        return (down, times.Count == 0 ? null : SpeedMath.Median(times));
    }

    /// <summary>Tempo para o DNS responder um nome que não está no cache (nome aleatório).</summary>
    public static async Task<double?> DnsTimeAsync(CancellationToken ct)
    {
        var times = new List<double>();
        for (int i = 0; i < 3; i++)
        {
            string name = $"mbx{Guid.NewGuid():N}".Substring(0, 14) + ".cloudflare.com";
            var clock = Stopwatch.StartNew();
            try { await System.Net.Dns.GetHostAddressesAsync(name, ct); }
            catch (System.Net.Sockets.SocketException) { }   // "não existe" também é resposta do DNS
            times.Add(clock.Elapsed.TotalMilliseconds);
        }
        return times.Count == 0 ? null : SpeedMath.Median(times);
    }

    /// <summary>Qual servidor respondeu (código do aeroporto, ex.: GRU) e a cidade, quando conhecida.</summary>
    public async Task<(string? Code, string? City)> ServerAsync(CancellationToken ct)
    {
        try
        {
            string text = await http.GetStringAsync($"{baseUrl}/cdn-cgi/trace", ct);
            var trace = SpeedMath.ParseTrace(text);
            string? code = trace.GetValueOrDefault("colo");
            return (code, SpeedMath.CityOf(code));
        }
        catch (HttpRequestException) { return (null, null); }
    }
}

/// <summary>Histórico dos testes de internet (os 50 mais recentes) em %AppData%\MBExtreme\internet.json, para comparar.</summary>
public sealed class SpeedHistory
{
    public SpeedHistory(string? path = null) => FilePath = path ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MBExtreme", "internet.json");

    public string FilePath { get; }

    public IReadOnlyList<SpeedResult> Load()
    {
        try { return File.Exists(FilePath) ? JsonSerializer.Deserialize<List<SpeedResult>>(File.ReadAllText(FilePath)) ?? new() : new List<SpeedResult>(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return new List<SpeedResult>(); }
    }

    public IReadOnlyList<SpeedResult> Add(SpeedResult result)
    {
        var list = Load().Prepend(result).Take(50).ToList();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(list));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return list;
    }

    /// <summary>Comparação com a média dos testes anteriores ("12% mais rápido que a média dos últimos 5").</summary>
    public static string? Compare(IReadOnlyList<SpeedResult> history)
    {
        if (history.Count < 2 || history[0].DownloadMbps is not double now) return null;
        var previous = history.Skip(1).Take(5).Select(r => r.DownloadMbps).OfType<double>().ToList();
        if (previous.Count == 0) return null;
        double average = previous.Average();
        if (average <= 0) return null;
        double change = (now - average) / average * 100;
        return Math.Abs(change) < 5 ? $"Download igual à média dos últimos {previous.Count} testes."
            : $"Download {Math.Abs(change):0}% {(change > 0 ? "mais rápido" : "mais lento")} que a média dos últimos {previous.Count} testes.";
    }
}
