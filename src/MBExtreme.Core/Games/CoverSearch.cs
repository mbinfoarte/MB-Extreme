using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MBExtreme.Core.Games;

/// <summary>Uma capa encontrada na internet para o usuário escolher.</summary>
public sealed record CoverCandidate(string AppId, string Name, string ImageUrl, string? DownloadUrl = null, int Width = 0, int Height = 0)
{
    /// <summary>Resultado da busca na internet (baixado com as travas de <see cref="WebCoverSearch"/>).</summary>
    public bool IsWeb => AppId == WebCoverSearch.WebAppId;

    /// <summary>Só endereços https do próprio SteamGridDB são baixados (a lista vem da internet, então não se confia no que ela traz).</summary>
    public static bool IsAllowedDownload(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps
        && (u.Host == "steamgriddb.com" || u.Host.EndsWith(".steamgriddb.com", StringComparison.OrdinalIgnoreCase));
}

/// <summary>Busca capas na loja da Steam pelo nome (sem chave, sem conta) e guarda a escolhida como capa do jogo na biblioteca.</summary>
public static class CoverSearch
{
    public static string CoverUrl(string appId) => $"https://cdn.cloudflare.steamstatic.com/steam/apps/{appId}/library_600x900.jpg";

    private static string HeaderUrl(string appId) => $"https://cdn.cloudflare.steamstatic.com/steam/apps/{appId}/header.jpg";

    public static string SearchUrl(string name) =>
        "https://store.steampowered.com/api/storesearch/?l=portuguese&cc=BR&term=" + Uri.EscapeDataString(name.Trim());

    /// <summary>Lê a resposta da busca da loja. Ignora itens sem número de aplicativo.</summary>
    public static IReadOnlyList<CoverCandidate> ParseSearch(string json, int max = 12)
    {
        var list = new List<CoverCandidate>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array) return list;
            foreach (var item in items.EnumerateArray())
            {
                if (!item.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.Number) continue;
                if (item.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String && type.GetString() is { Length: > 0 } kind && kind != "app") continue; // trilhas sonoras e DLC não servem
                string appId = id.GetInt64().ToString();
                string name = item.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() ?? appId : appId;
                list.Add(new CoverCandidate(appId, name, CoverUrl(appId)));
                if (list.Count >= max) break;
            }
        }
        catch (JsonException) { }
        return list;
    }

    public static async Task<IReadOnlyList<CoverCandidate>> SearchAsync(HttpClient http, string name, CancellationToken stop = default)
    {
        if (string.IsNullOrWhiteSpace(name)) return Array.Empty<CoverCandidate>();
        try { return ParseSearch(await http.GetStringAsync(SearchUrl(name), stop)); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { return Array.Empty<CoverCandidate>(); }
    }

    /// <summary>Nome de arquivo seguro e estável para o jogo (o código dele pode ter caracteres que o Windows não aceita).</summary>
    public static string FileKey(string gameId) => Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(gameId))).ToLowerInvariant();

    public static string? FindCustom(string folder, string gameId)
    {
        string file = Path.Combine(folder, "custom", FileKey(gameId) + ".jpg");
        return File.Exists(file) ? file : null;
    }

    /// <summary>Baixa a capa escolhida (se a vertical não existir, usa a imagem larga) e guarda como a capa do jogo.</summary>
    public static async Task<string?> SaveCustomAsync(HttpClient http, CoverCandidate candidate, string folder, string gameId, CancellationToken stop = default)
    {
        if (candidate.IsWeb)
            return candidate.DownloadUrl is { } link ? (await WebCoverSearch.SaveFromUrlAsync(http, link, folder, gameId, stop)).File : null;
        bool grid = candidate.DownloadUrl is not null;
        if (grid ? !CoverCandidate.IsAllowedDownload(candidate.DownloadUrl) : !candidate.AppId.All(char.IsDigit) || candidate.AppId.Length is 0 or > 12) return null;
        try
        {
            Directory.CreateDirectory(Path.Combine(folder, "custom"));
            byte[]? data = null;
            foreach (string url in grid ? new[] { candidate.DownloadUrl! } : new[] { CoverUrl(candidate.AppId), HeaderUrl(candidate.AppId) })
            {
                try { data = await http.GetByteArrayAsync(url, stop); if (data.Length >= 1000) break; data = null; }
                catch (HttpRequestException) { data = null; }
            }
            if (data is null) return null;
            string file = Path.Combine(folder, "custom", FileKey(gameId) + ".jpg");
            await File.WriteAllBytesAsync(file, data, stop);
            return file;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or TaskCanceledException) { return null; }
    }

    public static void RemoveCustom(string folder, string gameId)
    {
        try { string? f = FindCustom(folder, gameId); if (f is not null) File.Delete(f); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
