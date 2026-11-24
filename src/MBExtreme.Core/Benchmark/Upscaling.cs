using System.IO.Compression;
using System.Security.Cryptography;

namespace MBExtreme.Core.Benchmark;

/// <summary>Um modo do FSR no teste: quanto a resolução interna é menor que a da tela e se tem geração de quadros.</summary>
public sealed record UpscaleMode(string Key, string Title, double Ratio, bool FrameGeneration);

public static class UpscaleModes
{
    public static readonly UpscaleMode Quality = new("quality", "FSR Qualidade", 1.5, false);
    public static readonly UpscaleMode Performance = new("performance", "FSR Desempenho", 2.0, false);
    public static readonly UpscaleMode PerformanceFrameGen = new("performance-fg", "FSR Desempenho + geração de quadros", 2.0, true);
    public static IReadOnlyList<UpscaleMode> All { get; } = new[] { Quality, Performance, PerformanceFrameGen };
}

/// <summary>
/// Resultado de um modo do FSR. <paramref name="DisplayedFps"/> é o que aparece na tela (com geração de quadros, o dobro do
/// renderizado); <paramref name="RenderedFps"/> é o que a placa realmente renderizou.
/// </summary>
public sealed record UpscaleRun(string Key, string Title, bool Ok, string? Error, double DisplayedFps, double RenderedFps,
    double Low1Fps, double Low01Fps, IReadOnlyList<double> FrameMs, int RenderWidth, int RenderHeight, bool FrameGeneration,
    string? UpscalerVersion, string? FrameGenVersion)
{
    public static UpscaleRun Failed(UpscaleMode mode, string error) =>
        new(mode.Key, mode.Title, false, error, 0, 0, 0, 0, Array.Empty<double>(), 0, 0, mode.FrameGeneration, null, null);

    /// <summary>Ganho em relação ao ray tracing nativo (mesma cena, RT ligado, sem upscaling): 1,8 = 80% mais FPS.</summary>
    public double GainOver(double nativeFps) => Ok && nativeFps > 0 ? DisplayedFps / nativeFps : 0;
}

/// <summary>
/// As DLLs do AMD FidelityFX SDK (FSR upscaling e geração de quadros, DirectX 12), baixadas só na primeira vez que o teste
/// roda (55 MB) e conferidas pelo SHA-256. Ficam em %LocalAppData%\MBExtreme\addons.
/// </summary>
public static class FsrAddon
{
    public const string SdkVersion = "2.3.0";
    public const string ZipUrl = "https://raw.githubusercontent.com/mbinfoarte/MB-Extreme-Download/main/addons/fidelityfx/fidelityfx-dx12-2.3.0.zip";
    public const string ZipSha256 = "032077d5c7e9d2ea67c33a8c92e314a369a270891d058bda6618b5143a12bbaf";
    public const long ZipBytes = 54_727_194;
    public static readonly string[] Files = { "amd_fidelityfx_loader_dx12.dll", "amd_fidelityfx_upscaler_dx12.dll", "amd_fidelityfx_framegeneration_dx12.dll" };

    public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MBExtreme", "addons", "fidelityfx-" + SdkVersion);

    public static bool IsReady(string folder) => Files.All(f => File.Exists(Path.Combine(folder, f)));

    public static bool HashMatches(Stream stream) =>
        Convert.ToHexString(SHA256.HashData(stream)).Equals(ZipSha256, StringComparison.OrdinalIgnoreCase);

    /// <summary>Extrai só os arquivos esperados (nada de caminhos com pasta: proteção contra zip malicioso).</summary>
    public static void Extract(Stream zip, string folder)
    {
        Directory.CreateDirectory(folder);
        using var archive = new ZipArchive(zip, ZipArchiveMode.Read, leaveOpen: true);
        foreach (var entry in archive.Entries)
        {
            if (entry.FullName != entry.Name || entry.Name.Length == 0) continue;
            if (!Files.Contains(entry.Name) && !entry.Name.StartsWith("LICENSE", StringComparison.OrdinalIgnoreCase)) continue;
            entry.ExtractToFile(Path.Combine(folder, entry.Name), overwrite: true);
        }
    }

    /// <summary>Garante as DLLs na pasta (baixa e confere quando faltam). <paramref name="progress"/>: 0 a 1.</summary>
    public static async Task<string> EnsureAsync(HttpClient http, Action<double>? progress, CancellationToken ct)
    {
        string folder = Folder;
        if (IsReady(folder)) return folder;
        string temp = Path.Combine(Path.GetTempPath(), "mbx-fidelityfx-" + Guid.NewGuid().ToString("N") + ".zip");
        try
        {
            using (var response = await http.GetAsync(ZipUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                long total = response.Content.Headers.ContentLength ?? ZipBytes;
                await using var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                await using var target = File.Create(temp);
                var buffer = new byte[1 << 16];
                long done = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                    done += read;
                    progress?.Invoke(Math.Min(1, (double)done / Math.Max(1, total)));
                }
            }
            await using (var check = File.OpenRead(temp))
            {
                if (!HashMatches(check)) throw new InvalidDataException("o arquivo do FSR baixado não confere (SHA-256)");
                check.Position = 0;
                Extract(check, folder);
            }
            return folder;
        }
        finally
        {
            try { File.Delete(temp); } catch (IOException) { }
        }
    }
}
