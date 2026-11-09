using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace MBExtreme.Core.Wallpaper;

/// <summary>Uma foto de um banco online com licença de uso (Pexels).</summary>
public sealed record OnlinePhoto(string Id, int Width, int Height, string ThumbUrl, string FullUrl, string Author, string Description, string PageUrl);

/// <summary>
/// Galeria de papéis de parede de bancos gratuitos com licença de uso (Pexels). Precisa de uma chave de API gratuita
/// (pexels.com/api). Sem a chave, a galeria online fica desligada e o resto do módulo (pacote MB e importar do PC) continua.
/// Os créditos do autor e o link da página são mostrados, como a licença do Pexels pede.
/// </summary>
public static class Pexels
{
    public const string GetKeyUrl = "https://www.pexels.com/api/";
    private const string SearchUrl = "https://api.pexels.com/v1/search";
    private const string CuratedUrl = "https://api.pexels.com/v1/curated";

    public static bool HasKey(string? key) => !string.IsNullOrWhiteSpace(key) && key.Trim().Length >= 20;

    public static IReadOnlyList<OnlinePhoto> Parse(string json)
    {
        var result = new List<OnlinePhoto>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("photos", out var photos) || photos.ValueKind != JsonValueKind.Array) return result;
            foreach (var p in photos.EnumerateArray())
            {
                string id = p.TryGetProperty("id", out var i) ? (i.ValueKind == JsonValueKind.Number ? i.GetInt64().ToString() : i.GetString() ?? "") : "";
                if (id.Length == 0 || !p.TryGetProperty("src", out var src) || src.ValueKind != JsonValueKind.Object) continue;
                string Full() => Str(src, "original").Length > 0 ? Str(src, "original") : Str(src, "large2x");
                string thumb = Str(src, "medium").Length > 0 ? Str(src, "medium") : Str(src, "large");
                string full = Str(src, "large2x").Length > 0 ? Str(src, "large2x") : Full();
                if (!Safe(full) || !Safe(thumb)) continue;
                result.Add(new OnlinePhoto(id,
                    p.TryGetProperty("width", out var w) && w.TryGetInt32(out int wi) ? wi : 0,
                    p.TryGetProperty("height", out var h) && h.TryGetInt32(out int hi) ? hi : 0,
                    thumb, full, Str(p, "photographer"), Str(p, "alt"), Str(p, "url")));
            }
        }
        catch (JsonException) { }
        return result;
    }

    private static string Str(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    /// <summary>Só endereços https do Pexels (as imagens vêm de images.pexels.com).</summary>
    public static bool Safe(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps && u.Host.EndsWith("pexels.com", StringComparison.OrdinalIgnoreCase);

    public static async Task<IReadOnlyList<OnlinePhoto>> SearchAsync(HttpClient http, string key, string query, int page, CancellationToken ct)
    {
        if (!HasKey(key)) return Array.Empty<OnlinePhoto>();
        string q = query.Trim();
        string url = q.Length == 0
            ? $"{CuratedUrl}?per_page=24&page={Math.Max(1, page)}"
            : $"{SearchUrl}?query={Uri.EscapeDataString(q)}&per_page=24&page={Math.Max(1, page)}&orientation=landscape";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue(key.Trim());
        try
        {
            using var response = await http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) return Array.Empty<OnlinePhoto>();
            return Parse(await response.Content.ReadAsStringAsync(ct));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { return Array.Empty<OnlinePhoto>(); }
    }

    /// <summary>Baixa a foto para a pasta do módulo e devolve o caminho do arquivo.</summary>
    public static async Task<string> DownloadAsync(HttpClient http, OnlinePhoto photo, string folder, CancellationToken ct)
    {
        if (!Safe(photo.FullUrl)) throw new InvalidOperationException("Endereço de imagem inseguro.");
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, $"pexels-{photo.Id}.jpg");
        using var response = await http.GetAsync(photo.FullUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        await using (var input = await response.Content.ReadAsStreamAsync(ct))
        await using (var output = File.Create(path))
            await input.CopyToAsync(output, ct);
        return path;
    }
}
