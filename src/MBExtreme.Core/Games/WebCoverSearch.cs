using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MBExtreme.Core.Games;

/// <summary>
/// Busca de capas na internet inteira (imagens do DuckDuckGo, com o Bing de reserva), sem chave nem conta, e o salvamento seguro de uma
/// imagem escolhida por link ou arquivo: só https, nunca endereço da rede local, no máximo 15 MB e só JPEG, PNG ou WebP de verdade.
/// </summary>
public static class WebCoverSearch
{
    public const int MaxBytes = 15 * 1024 * 1024;
    public const string WebAppId = "web";
    private const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Safari/537.36";

    /// <summary>Texto da busca: o nome do jogo e "capa" para vir a arte do jogo, não fotos aleatórias.</summary>
    public static string Query(string gameName) => gameName.Trim() + " game cover art";

    /// <summary>Endereço do Google Imagens para o usuário procurar no navegador e colar o link.</summary>
    public static string GoogleImagesUrl(string gameName) =>
        "https://www.google.com/search?tbm=isch&q=" + Uri.EscapeDataString(gameName.Trim() + " capa jogo");

    /// <summary>Endereço que pode ser baixado: https com nome de servidor público (sem IP de rede local, localhost ou porta estranha).</summary>
    public static bool IsSafeUrl(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u) || u.Scheme != Uri.UriSchemeHttps || !u.IsDefaultPort) return false;
        string host = u.IdnHost.ToLowerInvariant();
        if (host is "localhost" || host.EndsWith(".local") || host.EndsWith(".localhost") || host.EndsWith(".internal") || !host.Contains('.')) return false;
        if (IPAddress.TryParse(host.Trim('[', ']'), out var ip)) return IsPublic(ip);
        return true;
    }

    public static bool IsPublic(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip)) return false;
        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6Multicast || ip.Equals(IPAddress.IPv6Any)) return false;
            if (ip.IsIPv4MappedToIPv6) return IsPublic(ip.MapToIPv4());
            byte first = ip.GetAddressBytes()[0];
            return (first & 0xFE) != 0xFC;   // fc00::/7 (rede local)
        }
        byte[] b = ip.GetAddressBytes();
        return !(b[0] == 10 || b[0] == 127 || b[0] == 0 || (b[0] == 169 && b[1] == 254) || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
            || (b[0] == 192 && b[1] == 168) || (b[0] == 100 && b[1] >= 64 && b[1] <= 127) || b[0] >= 224);
    }

    /// <summary>Confere pelo começo do arquivo que é JPEG, PNG ou WebP (o nome ou o link não provam nada).</summary>
    public static bool LooksLikeImage(ReadOnlySpan<byte> data) =>
        data.Length >= 12 && (
            (data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF) ||
            (data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47) ||
            (data[0] == (byte)'R' && data[1] == (byte)'I' && data[2] == (byte)'F' && data[3] == (byte)'F' && data[8] == (byte)'W' && data[9] == (byte)'E' && data[10] == (byte)'B' && data[11] == (byte)'P'));

    // ------------------------------------------------------------ leitura das respostas

    /// <summary>O código de sessão (vqd) que o DuckDuckGo põe na página e exige na busca de imagens.</summary>
    public static string? ParseDuckToken(string html)
    {
        var m = Regex.Match(html, @"vqd\s*[=:]\s*[""']?(\d-[\d-]+)[""']?");
        return m.Success ? m.Groups[1].Value : null;
    }

    public static IReadOnlyList<CoverCandidate> ParseDuckResults(string json, int max = 24)
    {
        var list = new List<CoverCandidate>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array) return list;
            foreach (var r in results.EnumerateArray())
            {
                string? image = Str(r, "image"), thumb = Str(r, "thumbnail") ?? image, title = Str(r, "title") ?? "Imagem";
                if (!IsSafeUrl(image) || !IsSafeUrl(thumb)) continue;
                int w = Int(r, "width"), h = Int(r, "height");
                list.Add(new CoverCandidate(WebAppId, Shorten(title), thumb!, image, w, h));
                if (list.Count >= max) break;
            }
        }
        catch (JsonException) { }
        return Portrait(list);
    }

    /// <summary>Página de resultados do Bing Imagens: cada resultado traz um JSON no atributo m (murl = imagem, turl = miniatura, t = título).</summary>
    public static IReadOnlyList<CoverCandidate> ParseBing(string html, int max = 24)
    {
        var list = new List<CoverCandidate>();
        foreach (Match tag in Regex.Matches(html, @"<a\b[^>]*\bclass=""iusc""[^>]*>", RegexOptions.IgnoreCase))
        {
            var m = Regex.Match(tag.Value, @"\bm=""([^""]+)""");
            if (!m.Success) continue;
            try
            {
                using var doc = JsonDocument.Parse(WebUtility.HtmlDecode(m.Groups[1].Value));
                var r = doc.RootElement;
                string? image = Str(r, "murl"), thumb = Str(r, "turl") ?? image, title = Str(r, "t") ?? "Imagem";
                if (!IsSafeUrl(image) || !IsSafeUrl(thumb)) continue;
                list.Add(new CoverCandidate(WebAppId, Shorten(WebUtility.HtmlDecode(title)), thumb!, image));
                if (list.Count >= max) break;
            }
            catch (JsonException) { }
        }
        return list;
    }

    // capa de jogo é vertical: as imagens em pé vêm primeiro
    private static IReadOnlyList<CoverCandidate> Portrait(List<CoverCandidate> list) =>
        list.OrderByDescending(c => c.Height > c.Width && c.Width > 0).ThenByDescending(c => c.Height).ToList();

    private static string? Str(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    private static int Int(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int i) ? i : 0;
    private static string Shorten(string s) { s = Regex.Replace(s, @"\s+", " ").Trim(); return s.Length > 60 ? s[..60] + "…" : s; }

    // ------------------------------------------------------------ busca

    public static async Task<IReadOnlyList<CoverCandidate>> SearchAsync(HttpClient http, string gameName, CancellationToken stop = default)
    {
        if (string.IsNullOrWhiteSpace(gameName)) return Array.Empty<CoverCandidate>();
        string q = Uri.EscapeDataString(Query(gameName));
        try
        {
            using var page = new HttpRequestMessage(HttpMethod.Get, $"https://duckduckgo.com/?q={q}&iax=images&ia=images");
            page.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
            using var pageResponse = await http.SendAsync(page, stop);
            string? token = ParseDuckToken(await pageResponse.Content.ReadAsStringAsync(stop));
            if (token is not null)
            {
                using var api = new HttpRequestMessage(HttpMethod.Get, $"https://duckduckgo.com/i.js?l=br-pt&o=json&q={q}&vqd={Uri.EscapeDataString(token)}&f=,,,,,&p=1");
                api.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
                api.Headers.TryAddWithoutValidation("Referer", "https://duckduckgo.com/");
                using var apiResponse = await http.SendAsync(api, stop);
                if (apiResponse.IsSuccessStatusCode)
                {
                    var found = ParseDuckResults(await apiResponse.Content.ReadAsStringAsync(stop));
                    if (found.Count > 0) return found;
                }
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException) { }
        try
        {
            using var bing = new HttpRequestMessage(HttpMethod.Get, $"https://www.bing.com/images/search?q={q}&form=HDRSC2&first=1");
            bing.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
            using var response = await http.SendAsync(bing, stop);
            return response.IsSuccessStatusCode ? ParseBing(await response.Content.ReadAsStringAsync(stop)) : Array.Empty<CoverCandidate>();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException) { return Array.Empty<CoverCandidate>(); }
    }

    // ------------------------------------------------------------ salvar

    /// <summary>Baixa a imagem do link (com as travas de segurança) e guarda como capa. Devolve o arquivo ou o motivo da recusa.</summary>
    public static async Task<(string? File, string? Error)> SaveFromUrlAsync(HttpClient http, string url, string folder, string gameId, CancellationToken stop = default)
    {
        if (!IsSafeUrl(url)) return (null, "o link precisa começar com https:// e ser de um site público");
        try
        {
            // confere também o IP do servidor (um nome público não pode apontar para a rede local)
            var host = new Uri(url).IdnHost;
            if (!IPAddress.TryParse(host, out _))
            {
                var addresses = await System.Net.Dns.GetHostAddressesAsync(host, stop);
                if (addresses.Length == 0 || addresses.Any(a => !IsPublic(a))) return (null, "o site do link não é público");
            }
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, stop);
            if (!response.IsSuccessStatusCode) return (null, $"o site respondeu {(int)response.StatusCode}");
            if (response.RequestMessage?.RequestUri is { } final && !IsSafeUrl(final.ToString())) return (null, "o link redirecionou para um endereço não permitido");
            if (response.Content.Headers.ContentLength is > MaxBytes) return (null, "a imagem passa de 15 MB");
            await using var source = await response.Content.ReadAsStreamAsync(stop);
            using var ms = new MemoryStream();
            var buffer = new byte[81920];
            int read;
            while ((read = await source.ReadAsync(buffer, stop)) > 0)
            {
                ms.Write(buffer, 0, read);
                if (ms.Length > MaxBytes) return (null, "a imagem passa de 15 MB");
            }
            return SaveBytes(ms.ToArray(), folder, gameId);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or SocketException or IOException or UnauthorizedAccessException or UriFormatException)
        {
            return (null, ex.Message);
        }
    }

    /// <summary>Usa uma imagem do próprio PC como capa.</summary>
    public static (string? File, string? Error) SaveFromFile(string path, string folder, string gameId)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists) return (null, "arquivo não encontrado");
            if (info.Length > MaxBytes) return (null, "a imagem passa de 15 MB");
            return SaveBytes(File.ReadAllBytes(path), folder, gameId);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return (null, ex.Message); }
    }

    private static (string? File, string? Error) SaveBytes(byte[] data, string folder, string gameId)
    {
        if (!LooksLikeImage(data)) return (null, "o arquivo não é uma imagem JPG, PNG ou WebP");
        Directory.CreateDirectory(Path.Combine(folder, "custom"));
        string file = Path.Combine(folder, "custom", CoverSearch.FileKey(gameId) + ".jpg");   // o leitor de imagens reconhece pelo conteúdo
        File.WriteAllBytes(file, data);
        return (file, null);
    }
}
