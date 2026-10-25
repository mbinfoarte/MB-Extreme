using System.Net.Http.Headers;
using System.Text.Json;

namespace MBExtreme.Core.Games;

/// <summary>
/// SteamGridDB (steamgriddb.com): banco de capas feitas pela comunidade, inclusive de jogos que não são da Steam (EA, Epic, Ubisoft, Battle.net...).
/// Precisa de uma chave gratuita. Só leitura, só para escolher a capa.
/// </summary>
public static class SteamGridDb
{
    private const string Api = "https://www.steamgriddb.com/api/v2";

    public static string SearchUrl(string name) => $"{Api}/search/autocomplete/{Uri.EscapeDataString(name.Trim())}";

    public static string GridsUrl(long gameId) => $"{Api}/grids/game/{gameId}?dimensions=600x900&types=static&nsfw=false&humor=false&limit=8";

    public static IReadOnlyList<(long Id, string Name)> ParseSearch(string json, int max = 4)
    {
        var list = new List<(long, string)>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array) return list;
            foreach (var item in data.EnumerateArray())
            {
                if (!item.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.Number) continue;
                string name = item.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() ?? "" : "";
                list.Add((id.GetInt64(), name));
                if (list.Count >= max) break;
            }
        }
        catch (JsonException) { }
        return list;
    }

    /// <summary>Capas (miniatura para mostrar, imagem inteira para baixar). Descarta endereços que não sejam do SteamGridDB.</summary>
    public static IReadOnlyList<(string Thumb, string Url)> ParseGrids(string json)
    {
        var list = new List<(string, string)>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array) return list;
            foreach (var item in data.EnumerateArray())
            {
                string? url = item.TryGetProperty("url", out var u) && u.ValueKind == JsonValueKind.String ? u.GetString() : null;
                string? thumb = item.TryGetProperty("thumb", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : url;
                if (!CoverCandidate.IsAllowedDownload(url) || !CoverCandidate.IsAllowedDownload(thumb)) continue;
                list.Add((thumb!, url!));
            }
        }
        catch (JsonException) { }
        return list;
    }

    public static async Task<IReadOnlyList<CoverCandidate>> SearchAsync(HttpClient http, string apiKey, string name, CancellationToken stop = default)
    {
        var result = new List<CoverCandidate>();
        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(name)) return result;
        try
        {
            foreach (var (id, gameName) in ParseSearch(await GetAsync(http, apiKey, SearchUrl(name), stop), 3))
            {
                var grids = ParseGrids(await GetAsync(http, apiKey, GridsUrl(id), stop));
                int n = 0;
                foreach (var (thumb, url) in grids.Take(4))
                    result.Add(new CoverCandidate("sgdb" + id + "-" + n++, gameName, thumb, url));
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { }
        return result;
    }

    private static async Task<string> GetAsync(HttpClient http, string apiKey, string url, CancellationToken stop)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
        using var response = await http.SendAsync(request, stop);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(stop);
    }
}
