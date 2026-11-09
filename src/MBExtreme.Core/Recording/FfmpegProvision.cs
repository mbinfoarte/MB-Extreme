using System.IO.Compression;
using System.Runtime.Versioning;
using System.Security.Cryptography;

namespace MBExtreme.Core.Recording;

/// <summary>
/// O motor de vídeo do estúdio já vem junto com o programa (pasta "ffmpeg" ao lado do MBExtreme.exe).
/// Se faltar (versão antiga, antivírus que apagou...), o programa baixa sozinho a compilação oficial para Windows
/// (gyan.dev, a indicada pelo site do FFmpeg), confere o SHA-256 publicado e guarda na pasta do MB Extreme. Nada é instalado no Windows.
/// </summary>
[SupportedOSPlatform("windows")]
public static class FfmpegProvision
{
    public const string DownloadUrl = "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip";
    public const string HashUrl = DownloadUrl + ".sha256";

    /// <summary>Pasta própria do MB Extreme (sem precisar de administrador).</summary>
    public static string LocalFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MBExtreme", "ffmpeg");

    /// <summary>Onde o FFmpeg que vem com o programa fica, em ordem de preferência.</summary>
    public static IEnumerable<string> BundledCandidates()
    {
        string app = AppContext.BaseDirectory;
        yield return Path.Combine(app, "ffmpeg", "ffmpeg.exe");
        yield return Path.Combine(app, "ffmpeg.exe");
        yield return Path.Combine(LocalFolder, "ffmpeg.exe");
    }

    public static string? LocateBundled() => BundledCandidates().FirstOrDefault(File.Exists);

    /// <summary>Lê o SHA-256 do arquivo .sha256 publicado (só o primeiro campo, 64 dígitos hexadecimais).</summary>
    public static string? ParseHash(string text)
    {
        string first = text.Trim().Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        return first.Length == 64 && first.All(Uri.IsHexDigit) ? first.ToLowerInvariant() : null;
    }

    /// <summary>A entrada do ffmpeg.exe dentro do zip (fica em ffmpeg-x.y-essentials_build/bin/ffmpeg.exe).</summary>
    public static bool IsFfmpegEntry(string fullName)
    {
        string name = fullName.Replace('\\', '/');
        return name.EndsWith("/bin/ffmpeg.exe", StringComparison.OrdinalIgnoreCase) || string.Equals(name, "bin/ffmpeg.exe", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Baixa, confere e extrai só o ffmpeg.exe. Devolve o caminho dele, ou a mensagem do problema.</summary>
    public static async Task<(string? Path, string Message)> DownloadAsync(HttpClient http, IProgress<double>? progress, CancellationToken token)
    {
        Directory.CreateDirectory(LocalFolder);
        string temp = Path.Combine(LocalFolder, "ffmpeg.download");
        try
        {
            string? expected = null;
            try { expected = ParseHash(await http.GetStringAsync(HashUrl, token)); }
            catch (HttpRequestException) { }
            if (expected is null) return (null, "Não consegui confirmar o arquivo oficial do motor de vídeo (sem o código de verificação). Tente de novo mais tarde.");

            using (var response = await http.GetAsync(DownloadUrl, HttpCompletionOption.ResponseHeadersRead, token))
            {
                if (!response.IsSuccessStatusCode) return (null, $"O site do motor de vídeo respondeu {(int)response.StatusCode}.");
                long total = response.Content.Headers.ContentLength ?? 0;
                await using var input = await response.Content.ReadAsStreamAsync(token);
                await using var output = File.Create(temp);
                var buffer = new byte[128 * 1024];
                long done = 0;
                int read;
                while ((read = await input.ReadAsync(buffer, token)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read), token);
                    done += read;
                    if (total > 0) progress?.Report(100.0 * done / total);
                }
            }

            string actual;
            await using (var check = File.OpenRead(temp))
                actual = Convert.ToHexString(await SHA256.HashDataAsync(check, token)).ToLowerInvariant();
            if (actual != expected) return (null, "O arquivo baixado não confere com o oficial e foi descartado.");

            string target = Path.Combine(LocalFolder, "ffmpeg.exe");
            using (var zip = ZipFile.OpenRead(temp))
            {
                var entry = zip.Entries.FirstOrDefault(e => IsFfmpegEntry(e.FullName));
                if (entry is null) return (null, "O pacote baixado não tem o ffmpeg.exe.");
                string partial = target + ".part";
                entry.ExtractToFile(partial, overwrite: true);
                File.Move(partial, target, overwrite: true);
            }
            return (target, "Motor de vídeo pronto.");
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException or InvalidDataException or TaskCanceledException)
        {
            return (null, "Não foi possível baixar o motor de vídeo: " + ex.Message);
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch (IOException) { }
        }
    }
}
