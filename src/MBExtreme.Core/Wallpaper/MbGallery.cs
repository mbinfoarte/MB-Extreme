using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MBExtreme.Core.Wallpaper;

/// <summary>Um wallpaper da galeria do MB Extreme (feito no Canva, em 4K), guardado no repositório público de downloads.</summary>
public sealed record MbGalleryItem(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("theme")] string Theme,
    [property: JsonPropertyName("width")] int Width,
    [property: JsonPropertyName("height")] int Height,
    [property: JsonPropertyName("file")] string File,
    [property: JsonPropertyName("thumb")] string Thumb,
    [property: JsonPropertyName("bytes")] long Bytes);

/// <summary>
/// Galeria de wallpapers do MB Extreme: a lista (wallpapers.json) e as imagens ficam no repositório público de downloads, então dá para
/// acrescentar wallpapers novos sem lançar versão. O programa mostra as miniaturas e só baixa o 4K do que a pessoa aplicar.
/// </summary>
public static class MbGallery
{
    public const string BaseUrl = "https://raw.githubusercontent.com/mbinfoarte/MB-Extreme-Download/main/";
    public const string ListUrl = BaseUrl + "wallpapers/wallpapers.json";

    private sealed record Manifest([property: JsonPropertyName("items")] List<MbGalleryItem>? Items);

    /// <summary>Lê a lista; só aceita caminhos dentro de wallpapers/ terminados em .jpg/.png (nada de "..", barras invertidas ou endereços de fora).</summary>
    public static IReadOnlyList<MbGalleryItem> Parse(string json)
    {
        try
        {
            var manifest = JsonSerializer.Deserialize<Manifest>(json);
            return (manifest?.Items ?? new List<MbGalleryItem>())
                .Where(i => !string.IsNullOrWhiteSpace(i.Id) && !string.IsNullOrWhiteSpace(i.Title) && IsSafePath(i.File) && IsSafePath(i.Thumb))
                .GroupBy(i => i.Id, StringComparer.OrdinalIgnoreCase).Select(g => g.First())
                .ToList();
        }
        catch (JsonException) { return Array.Empty<MbGalleryItem>(); }
    }

    public static bool IsSafePath(string? path) =>
        path is { Length: > 0 and < 200 } && path.StartsWith("wallpapers/", StringComparison.Ordinal) && !path.Contains("..") && !path.Contains('\\')
        && !path.Contains(':') && (path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
        && path.All(c => char.IsLetterOrDigit(c) || c is '/' or '-' or '_' or '.');

    public static string UrlOf(string path) => BaseUrl + path;

    /// <summary>Baixa a lista (com cópia local para quando estiver sem internet).</summary>
    public static async Task<IReadOnlyList<MbGalleryItem>> LoadAsync(HttpClient http, string cacheFolder, CancellationToken ct = default)
    {
        string cache = Path.Combine(cacheFolder, "galeria.json");
        try
        {
            string json = await http.GetStringAsync(ListUrl, ct);
            var items = Parse(json);
            if (items.Count > 0)
            {
                Directory.CreateDirectory(cacheFolder);
                await System.IO.File.WriteAllTextAsync(cache, json, ct);
                return items;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException) { }
        try { return System.IO.File.Exists(cache) ? Parse(await System.IO.File.ReadAllTextAsync(cache, ct)) : Array.Empty<MbGalleryItem>(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return Array.Empty<MbGalleryItem>(); }
    }

    /// <summary>Baixa o wallpaper em 4K para a pasta (reaproveita se já estiver lá) e devolve o caminho do arquivo.</summary>
    public static async Task<string> DownloadAsync(HttpClient http, MbGalleryItem item, string folder, CancellationToken ct = default)
    {
        if (!IsSafePath(item.File)) throw new InvalidOperationException("Caminho inválido na galeria.");
        Directory.CreateDirectory(folder);
        string target = Path.Combine(folder, "mb-" + Path.GetFileName(item.File));
        if (System.IO.File.Exists(target) && (item.Bytes <= 0 || new FileInfo(target).Length == item.Bytes)) return target;
        string partial = target + ".baixando";
        using (var response = await http.GetAsync(UrlOf(item.File), HttpCompletionOption.ResponseHeadersRead, ct))
        {
            response.EnsureSuccessStatusCode();
            await using var input = await response.Content.ReadAsStreamAsync(ct);
            await using var output = System.IO.File.Create(partial);
            await input.CopyToAsync(output, ct);
        }
        System.IO.File.Move(partial, target, true);
        return target;
    }
}
