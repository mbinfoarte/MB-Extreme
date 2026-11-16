using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MBExtreme.Core.Wallpaper;

/// <summary>Uma versão do wallpaper num formato de tela: "4k" (16:9), "ultrawide" (21:9) ou "vertical" (9:16).</summary>
public sealed record MbGalleryVariant(
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("width")] int Width,
    [property: JsonPropertyName("height")] int Height,
    [property: JsonPropertyName("file")] string File,
    [property: JsonPropertyName("bytes")] long Bytes);

/// <summary>
/// Um wallpaper da galeria do MB Extreme, guardado no repositório público de downloads. <see cref="File"/> é a versão principal
/// (versões antigas do programa só leem ela); <see cref="Variants"/> traz a mesma arte em outros formatos de tela.
/// </summary>
public sealed record MbGalleryItem(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("theme")] string Theme,
    [property: JsonPropertyName("width")] int Width,
    [property: JsonPropertyName("height")] int Height,
    [property: JsonPropertyName("file")] string File,
    [property: JsonPropertyName("thumb")] string Thumb,
    [property: JsonPropertyName("bytes")] long Bytes,
    [property: JsonPropertyName("variants")] IReadOnlyList<MbGalleryVariant>? Variants = null)
{
    /// <summary>Arte feita para monitor em pé (todas as versões são mais altas que largas).</summary>
    public bool IsVertical => AllVariants().All(v => v.Height > v.Width);

    /// <summary>A versão principal e as outras, sem repetir arquivo.</summary>
    public IEnumerable<MbGalleryVariant> AllVariants()
    {
        var main = new MbGalleryVariant(Height > Width ? "vertical" : "4k", Width, Height, File, Bytes);
        return new[] { main }.Concat(Variants ?? Array.Empty<MbGalleryVariant>())
            .Where(v => v.Width > 0 && v.Height > 0)
            .GroupBy(v => v.File, StringComparer.OrdinalIgnoreCase).Select(g => g.First());
    }
}

/// <summary>
/// Galeria de wallpapers do MB Extreme: a lista (wallpapers.json) e as imagens ficam no repositório público de downloads, então dá para
/// acrescentar wallpapers novos sem lançar versão. O programa mostra as miniaturas e só baixa, do que a pessoa aplicar, a versão
/// que combina com cada monitor (4K 16:9, ultrawide 21:9 ou vertical).
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
                .Select(i => i with { Variants = i.Variants?.Where(v => v is not null && IsSafePath(v.File) && v.Width > 0 && v.Height > 0).ToList() })
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

    /// <summary>
    /// Versão que melhor cobre uma tela de <paramref name="width"/> × <paramref name="height"/>: o formato mais parecido
    /// (assim "Preencher" corta o mínimo e nada deforma); no empate, a de maior resolução.
    /// </summary>
    public static MbGalleryVariant Pick(MbGalleryItem item, int width, int height)
    {
        double screen = width > 0 && height > 0 ? (double)width / height : 16.0 / 9.0;
        return item.AllVariants()
            .OrderBy(v => Math.Abs(Math.Log((double)v.Width / v.Height / screen)))
            .ThenByDescending(v => (long)v.Width * v.Height)
            .First();
    }

    /// <summary>Baixa a versão principal do wallpaper (reaproveita se já estiver na pasta) e devolve o caminho do arquivo.</summary>
    public static Task<string> DownloadAsync(HttpClient http, MbGalleryItem item, string folder, CancellationToken ct = default) =>
        DownloadAsync(http, new MbGalleryVariant("", item.Width, item.Height, item.File, item.Bytes), folder, ct);

    /// <summary>Baixa uma versão do wallpaper para a pasta (reaproveita se já estiver lá) e devolve o caminho do arquivo.</summary>
    public static async Task<string> DownloadAsync(HttpClient http, MbGalleryVariant item, string folder, CancellationToken ct = default)
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
