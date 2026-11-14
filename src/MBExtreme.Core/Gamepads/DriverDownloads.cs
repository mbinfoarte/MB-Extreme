using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

namespace MBExtreme.Core.Gamepads;

/// <summary>Como baixar e instalar um componente da emulação pela versão mais nova do site oficial (GitHub Releases).</summary>
public sealed record DriverPackage(string Id, string Repo, Func<string, bool> Asset, string Arguments, bool Silent, bool Msi = false);

/// <summary>Arquivo escolhido na versão mais nova: nome, endereço e o SHA-256 que o GitHub informa (quando informa).</summary>
public sealed record ReleaseAsset(string Tag, string Name, string Url, string? Sha256, long Size);

/// <summary>
/// Instala os drivers da emulação de dentro do MB Extreme, um clique por componente: baixa o instalador oficial da versão mais nova
/// (nada é embutido no MB), confere o SHA-256 publicado pelo GitHub e roda. VIIPER (com o USBIP) e FakerInput instalam sem perguntas;
/// ViGEmBus e HidHide abrem o instalador deles (a pessoa só clica em Avançar).
/// </summary>
public static class DriverDownloads
{
    public static bool IsArm => System.Runtime.InteropServices.RuntimeInformation.OSArchitecture == System.Runtime.InteropServices.Architecture.Arm64;

    public static IReadOnlyList<DriverPackage> All { get; } = new[]
    {
        new DriverPackage("viiper", "Alia5/VIIPER",
            n => n.Equals(IsArm ? "viiper-windows-arm64.exe" : "viiper-windows-amd64.exe", StringComparison.OrdinalIgnoreCase),
            "install --quiet", Silent: true),
        new DriverPackage("fakerinput", "Ryochan7/FakerInput",
            n => n.EndsWith(".msi", StringComparison.OrdinalIgnoreCase) && !n.Contains("arm", StringComparison.OrdinalIgnoreCase)
                 && (n.Contains("x64", StringComparison.OrdinalIgnoreCase) || !n.Contains("x86", StringComparison.OrdinalIgnoreCase)),
            "/qn /norestart", Silent: true, Msi: true),
        new DriverPackage("vigem", "nefarius/ViGEmBus", n => n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase), "", Silent: false),
        new DriverPackage("hidhide", "nefarius/HidHide",
            n => n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && (n.Contains("x64", StringComparison.OrdinalIgnoreCase) || !n.Contains("x86", StringComparison.OrdinalIgnoreCase)),
            "", Silent: false),
    };

    public static DriverPackage? For(string id) => All.FirstOrDefault(p => p.Id == id);

    public static string LatestUrl(string repo) => $"https://api.github.com/repos/{repo}/releases/latest";

    /// <summary>Escolhe o arquivo da versão mais nova (resposta da API do GitHub). Null quando nenhum serve.</summary>
    public static ReleaseAsset? Pick(string json, Func<string, bool> asset)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            string tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
            if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array) return null;
            foreach (var a in assets.EnumerateArray())
            {
                string name = a.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                if (name.Length == 0 || !asset(name)) continue;
                string url = a.TryGetProperty("browser_download_url", out var u) ? u.GetString() ?? "" : "";
                if (!url.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase)) continue;
                string? sha = a.TryGetProperty("digest", out var d) && d.GetString() is { } digest && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
                    ? digest[7..].ToLowerInvariant() : null;
                long size = a.TryGetProperty("size", out var s) && s.TryGetInt64(out long sz) ? sz : 0;
                return new ReleaseAsset(tag, name, url, sha, size);
            }
        }
        catch (JsonException) { }
        return null;
    }

    /// <summary>Código de saída → resultado. 3010/1641 = pronto, mas o Windows precisa reiniciar.</summary>
    public static (bool Ok, bool Reboot) Result(int exitCode) => exitCode switch
    {
        0 => (true, false),
        3010 or 1641 => (true, true),
        1602 or 1223 => (false, false),   // cancelado pela pessoa
        _ => (false, false),
    };

    /// <summary>Baixa a versão mais nova para a pasta temporária e confere o SHA-256. Devolve o caminho do instalador.</summary>
    public static async Task<(string Path, ReleaseAsset Asset)> DownloadAsync(HttpClient http, DriverPackage package, IProgress<string>? progress, CancellationToken ct)
    {
        progress?.Report("Procurando a versão mais nova...");
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestUrl(package.Repo));
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("MBExtreme", "1.0"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        using var response = await http.SendAsync(request, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"O site oficial não respondeu ({(int)response.StatusCode}). Tente de novo mais tarde.");
        string json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        var asset = Pick(json, package.Asset) ?? throw new InvalidOperationException("Não achei o instalador para Windows na versão mais nova.");

        string folder = Path.Combine(Path.GetTempPath(), "MBExtreme", "drivers");
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, Path.GetFileName(asset.Name));
        progress?.Report($"Baixando {asset.Name} ({asset.Tag})...");
        using (var download = await http.GetAsync(asset.Url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
        {
            download.EnsureSuccessStatusCode();
            await using var source = await download.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            await using var file = File.Create(path);
            await source.CopyToAsync(file, ct).ConfigureAwait(false);
        }
        if (asset.Sha256 is { } expected)
        {
            await using var check = File.OpenRead(path);
            string got = Convert.ToHexString(await SHA256.HashDataAsync(check, ct).ConfigureAwait(false)).ToLowerInvariant();
            if (got != expected)
            {
                File.Delete(path);
                throw new InvalidOperationException("O arquivo baixado não confere com o publicado no site oficial (SHA-256). Nada foi instalado.");
            }
        }
        return (path, asset);
    }
}
