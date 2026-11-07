using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MBExtreme.Core.Update;

/// <summary>
/// Atualização pelas Releases públicas do GitHub (sem servidor): a compilação vem do número no fim da tag (beta-49, v1.0.49),
/// o instalador é o arquivo MBExtreme-Beta-Setup.exe da Release e a impressão digital (SHA-256) vem do "digest" que o GitHub informa
/// (ou de um arquivo .sha256 publicado junto). O texto da Release vira o "O que há de novo".
/// Para obrigar a atualização, escreva [obrigatória] no texto da Release (ou "Compilação mínima: N").
/// </summary>
public static class GitHubReleases
{
    public const string SetupAsset = "MBExtreme-Beta-Setup.exe";

    private static readonly Regex TrailingNumber = new(@"(\d+)\s*$");
    private static readonly Regex Minimum = new(@"compila[cç][aã]o\s+m[ií]nima\s*:\s*(\d+)", RegexOptions.IgnoreCase);
    private static readonly Regex Required = new(@"\[\s*obrigat[oó]ria\s*\]", RegexOptions.IgnoreCase);

    /// <summary>Número da compilação pela tag (o último número dela).</summary>
    public static int BuildOf(string tag) => TrailingNumber.Match(tag.Trim()) is { Success: true } m && int.TryParse(m.Groups[1].Value, out int n) ? n : 0;

    /// <summary>Texto da Release sem as marcas técnicas (para mostrar ao cliente).</summary>
    public static string CleanNotes(string body)
    {
        string text = Minimum.Replace(Required.Replace(body.Replace("\r", ""), ""), "");
        int footer = text.IndexOf("\n---", StringComparison.Ordinal);   // rodapé técnico da Release (compilação, commit, como instalar)
        if (footer >= 0) text = text[..footer];
        var lines = text.Split('\n').Where(l => !l.TrimStart().StartsWith('#')).Select(l => l.TrimEnd());
        return string.Join("\n", lines).Trim();
    }

    /// <summary>Lê uma Release (objeto do /releases/latest ou um item do /releases). Nulo se faltar o instalador.</summary>
    public static UpdateManifest? ParseRelease(JsonElement release, bool includePrerelease)
    {
        if (release.ValueKind != JsonValueKind.Object) return null;
        if (release.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True) return null;
        bool pre = release.TryGetProperty("prerelease", out var p) && p.ValueKind == JsonValueKind.True;
        if (pre && !includePrerelease) return null;
        string tag = Str(release, "tag_name");
        int build = BuildOf(tag);
        if (build <= 0 || !release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array) return null;
        string body = Str(release, "body");
        string url = "", sha = "", shaUrl = "";
        long size = 0;
        foreach (var asset in assets.EnumerateArray())
        {
            string name = Str(asset, "name");
            if (name.Equals(SetupAsset, StringComparison.OrdinalIgnoreCase))
            {
                url = Str(asset, "browser_download_url");
                size = asset.TryGetProperty("size", out var s) && s.TryGetInt64(out long n) ? n : 0;
                string digest = Str(asset, "digest");
                if (digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)) sha = digest[7..].ToLowerInvariant();
            }
            else if (name.Equals(SetupAsset + ".sha256", StringComparison.OrdinalIgnoreCase)) shaUrl = Str(asset, "browser_download_url");
        }
        if (!AppUpdates.IsSafeUrl(url)) return null;
        int minimum = Minimum.Match(body) is { Success: true } m ? int.Parse(m.Groups[1].Value) : Required.IsMatch(body) ? build : 0;
        string version = Str(release, "name") is { Length: > 0 } n2 ? n2 : tag;
        // sem digest: o SHA-256 vem do arquivo .sha256 (o endereço fica guardado no lugar dele até ser lido)
        return new UpdateManifest(build, version, url, AppUpdates.IsSha256(sha) ? sha : shaUrl, size, CleanNotes(body), minimum);
    }

    /// <summary>A Release mais nova (maior compilação) de uma lista do /releases.</summary>
    public static UpdateManifest? Newest(string json, bool includePrerelease)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var items = root.ValueKind == JsonValueKind.Array ? root.EnumerateArray().ToList() : new List<JsonElement> { root };
            return items.Select(r => ParseRelease(r, includePrerelease)).OfType<UpdateManifest>().OrderByDescending(m => m.Build).FirstOrDefault();
        }
        catch (JsonException) { return null; }
    }

    /// <summary>A primeira linha com 64 letras/números hexadecimais de um arquivo .sha256 ("abc123...  MBExtreme-Beta-Setup.exe").</summary>
    public static string? ParseShaFile(string text)
    {
        var m = Regex.Match(text, @"\b([0-9a-fA-F]{64})\b");
        return m.Success ? m.Groups[1].Value.ToLowerInvariant() : null;
    }

    /// <summary>Procura a compilação mais nova no GitHub. Nulo sem internet, com o repositório privado ou sem instalador conferível.</summary>
    public static async Task<UpdateManifest?> CheckAsync(HttpClient http, string repository, bool includePrerelease, CancellationToken stop = default)
    {
        if (!Regex.IsMatch(repository, @"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$")) return null;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{repository}/releases?per_page=15");
            request.Headers.UserAgent.ParseAdd("MBExtreme-Updater");
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            using var response = await http.SendAsync(request, stop);
            if (!response.IsSuccessStatusCode) return null;   // 404 enquanto o repositório for privado
            var manifest = Newest(await response.Content.ReadAsStringAsync(stop), includePrerelease);
            if (manifest is null) return null;
            if (!AppUpdates.IsSha256(manifest.Sha256))
            {
                if (!AppUpdates.IsSafeUrl(manifest.Sha256)) return null;   // sem como conferir o arquivo: não atualiza
                string? sha = ParseShaFile(await http.GetStringAsync(manifest.Sha256, stop));
                if (sha is null) return null;
                manifest = manifest with { Sha256 = sha };
            }
            return manifest;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { return null; }
    }

    private static string Str(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
}
