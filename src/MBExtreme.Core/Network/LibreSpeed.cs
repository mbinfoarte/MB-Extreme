using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;

namespace MBExtreme.Core.Network;

/// <summary>Um servidor público do LibreSpeed (teste de velocidade de código aberto).</summary>
public sealed record LibreServer(int Id, string Name, string Server, string DownloadUrl, string UploadUrl, string PingUrl, string Sponsor)
{
    /// <summary>Endereço base (a lista usa "//host/caminho": vira https).</summary>
    public string BaseUrl => (Server.StartsWith("//", StringComparison.Ordinal) ? "https:" + Server : Server).TrimEnd('/');

    public string Url(string file) => BaseUrl + "/" + file.TrimStart('/');
}

/// <summary>
/// Servidores do LibreSpeed: a lista pública (librespeed.org) mantida por voluntários e provedores. O programa mede o ping de cada um e
/// sugere o mais rápido para você. Só servidores https; os de http ficam de fora.
/// </summary>
public static class LibreSpeed
{
    public const string ListUrl = "https://librespeed.org/backend-servers/servers.php";

    public static IReadOnlyList<LibreServer> ParseList(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return Array.Empty<LibreServer>();
            var list = new List<LibreServer>();
            foreach (var e in doc.RootElement.EnumerateArray())
            {
                string S(string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
                int id = e.TryGetProperty("id", out var i) && i.TryGetInt32(out int n) ? n : list.Count + 1;
                var server = new LibreServer(id, S("name"), S("server"), S("dlURL") is { Length: > 0 } d ? d : "garbage.php",
                    S("ulURL") is { Length: > 0 } u ? u : "empty.php", S("pingURL") is { Length: > 0 } p ? p : "empty.php", S("sponsorName"));
                if (server.Name.Length == 0 || !Uri.TryCreate(server.BaseUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) continue;
                list.Add(server);
            }
            return list;
        }
        catch (JsonException) { return Array.Empty<LibreServer>(); }
    }

    /// <summary>Servidores do Brasil e da América do Sul primeiro (pelo nome), depois o resto.</summary>
    public static int RegionRank(LibreServer s)
    {
        string n = s.Name;
        if (n.Contains("Brazil", StringComparison.OrdinalIgnoreCase) || n.Contains("Brasil", StringComparison.OrdinalIgnoreCase)) return 0;
        foreach (var c in new[] { "Argentina", "Chile", "Uruguay", "Paraguay", "Peru", "Colombia", "Bolivia" })
            if (n.Contains(c, StringComparison.OrdinalIgnoreCase)) return 1;
        foreach (var c in new[] { "USA", "United States", "Mexico", "Miami", "Canada" })
            if (n.Contains(c, StringComparison.OrdinalIgnoreCase)) return 2;
        return 3;
    }

    public static async Task<IReadOnlyList<LibreServer>> ListAsync(HttpClient http, CancellationToken ct)
    {
        try { return ParseList(await http.GetStringAsync(ListUrl, ct)); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { return Array.Empty<LibreServer>(); }
    }

    /// <summary>Ping de cada servidor (melhor de 3), vários ao mesmo tempo; os que não respondem ficam com null.</summary>
    public static async Task<IReadOnlyList<(LibreServer Server, double? Ping)>> RankAsync(HttpClient http, IReadOnlyList<LibreServer> servers, CancellationToken ct)
    {
        using var gate = new SemaphoreSlim(8);
        var tasks = servers.Select(async s =>
        {
            await gate.WaitAsync(ct);
            try
            {
                double? best = null;
                for (int i = 0; i < 3; i++)
                {
                    var clock = Stopwatch.StartNew();
                    try
                    {
                        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                        timeout.CancelAfter(2500);
                        using var r = await http.GetAsync(s.Url(s.PingUrl) + "?r=" + i, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                        if (r.IsSuccessStatusCode) best = Math.Min(best ?? double.MaxValue, clock.Elapsed.TotalMilliseconds);
                    }
                    catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { if (ct.IsCancellationRequested) throw; }
                }
                return (s, best);
            }
            finally { gate.Release(); }
        });
        var results = await Task.WhenAll(tasks);
        return results.OrderBy(r => r.best ?? double.MaxValue).ToList();
    }
}
