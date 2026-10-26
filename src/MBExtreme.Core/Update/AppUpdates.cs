using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace MBExtreme.Core.Update;

/// <summary>O que o servidor diz sobre a versão mais nova: número da compilação, onde baixar e a impressão digital do arquivo.</summary>
public sealed record UpdateManifest(int Build, string Version, string Url, string Sha256, long Size, string Notes, int MinimumBuild);

public enum UpdateState { UpToDate, Available, Required }

/// <summary>
/// Atualização dentro do programa: o app pergunta ao servidor qual é a compilação mais nova; se for maior que a instalada, avisa
/// (e exige, quando a compilação instalada é menor que a mínima aceita). O arquivo baixado só é executado se a impressão digital (SHA-256) conferir.
/// </summary>
public static class AppUpdates
{
    /// <summary>Número da compilação instalada (o CI grava 0.9.N; N é o número da compilação). Sem número, 0.</summary>
    public static int CurrentBuild()
    {
        var version = typeof(AppInfo).Assembly.GetName().Version;
        return version?.Build ?? 0;
    }

    public static UpdateManifest? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            int build = Int(root, "build");
            string url = Str(root, "url");
            string sha = Str(root, "sha256").ToLowerInvariant();
            if (build <= 0 || !IsSafeUrl(url) || !IsSha256(sha)) return null;
            return new UpdateManifest(build, Str(root, "version"), url, sha, Long(root, "size"), Str(root, "notes"), Math.Max(0, Int(root, "minimumBuild")));
        }
        catch (JsonException) { return null; }
    }

    private static string Str(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
    private static int Int(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int n) ? n : 0;
    private static long Long(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out long n) ? n : 0;

    /// <summary>Só endereços https (nada de http, arquivo local ou outro esquema).</summary>
    public static bool IsSafeUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps && !string.IsNullOrEmpty(u.Host) && string.IsNullOrEmpty(u.UserInfo);

    public static bool IsSha256(string? text) => text is { Length: 64 } && text.All(Uri.IsHexDigit);

    public static UpdateState Evaluate(int currentBuild, UpdateManifest manifest)
    {
        if (manifest.Build <= currentBuild) return UpdateState.UpToDate;
        return currentBuild < manifest.MinimumBuild ? UpdateState.Required : UpdateState.Available;
    }

    public static string Sha256Of(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    /// <summary>Pergunta ao servidor pela compilação mais nova. Nulo sem internet, sem servidor ou resposta inválida.</summary>
    public static async Task<UpdateManifest?> CheckAsync(HttpClient http, string serverUrl, CancellationToken stop = default)
    {
        if (!Uri.TryCreate(serverUrl.TrimEnd('/') + "/v1/app/latest", UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) return null;
        try { return Parse(await http.GetStringAsync(uri, stop)); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { return null; }
    }

    /// <summary>Baixa o instalador para a pasta dada e confere a impressão digital. Apaga o arquivo se não conferir.</summary>
    public static async Task<string> DownloadAsync(HttpClient http, UpdateManifest manifest, string folder, IProgress<double>? progress, CancellationToken stop)
    {
        if (!IsSafeUrl(manifest.Url)) throw new InvalidDataException("Endereço de download inseguro.");
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, $"MBExtreme-Beta-Setup-{manifest.Build}.exe");
        using var response = await http.GetAsync(manifest.Url, HttpCompletionOption.ResponseHeadersRead, stop);
        response.EnsureSuccessStatusCode();
        long total = response.Content.Headers.ContentLength ?? manifest.Size;
        await using (var source = await response.Content.ReadAsStreamAsync(stop))
        await using (var target = File.Create(path))
        {
            var buffer = new byte[128 * 1024];
            long done = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, stop)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read), stop);
                done += read;
                if (total > 0) progress?.Report(Math.Min(1.0, done / (double)total));
            }
        }

        if (Sha256Of(path) != manifest.Sha256)
        {
            try { File.Delete(path); } catch (IOException) { }
            throw new InvalidDataException("O arquivo baixado não confere com a impressão digital esperada (SHA-256). Ele foi apagado.");
        }
        return path;
    }
}
