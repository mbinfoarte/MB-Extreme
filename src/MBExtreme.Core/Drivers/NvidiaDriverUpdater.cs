using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Xml.Linq;
using MBExtreme.Core.Progress;
using MBExtreme.Core.Windows;

namespace MBExtreme.Core.Drivers;

public sealed record NvidiaLatest(string Version, string DownloadUrl);

public sealed record DriverUpdateResult(bool Ok, bool RebootRecommended, string Message);

/// <summary>
/// Atualização do driver NVIDIA direto do servidor oficial da NVIDIA: consulta a versão mais nova para a placa,
/// baixa o instalador, confere a assinatura digital da NVIDIA e instala em modo silencioso.
/// </summary>
[SupportedOSPlatform("windows")]
public static class NvidiaDriverUpdater
{
    public const string StepSearch = "Procurar versão";
    public const string StepDownload = "Baixar";
    public const string StepVerify = "Verificar assinatura";
    public const string StepRestore = "Ponto de restauração";
    public const string StepInstall = "Instalar";

    public static string[] Steps => new[] { StepSearch, StepDownload, StepVerify, StepRestore, StepInstall };

    private const string LookupBase = "https://www.nvidia.com/Download/API/lookupValueSearch.aspx";
    private const string DriverLookup =
        "https://gfwsl.geforce.com/services_toolkit/services/com/nvidia/services/AjaxDriverService.php";

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("MBExtreme/" + AppInfo.Version);
        return client;
    }

    // ---------- partes puras (testáveis) ----------

    /// <summary>"NVIDIA GeForce RTX 3060 Ti" e "GeForce RTX 3060 Ti" viram a mesma chave de comparação.</summary>
    public static string NormalizeGpuName(string name)
    {
        string text = name.ToLowerInvariant().Replace("nvidia", "").Replace("geforce", "");
        return string.Join(' ', text.Split(new[] { ' ', '\t', '-' }, StringSplitOptions.RemoveEmptyEntries));
    }

    public static IReadOnlyList<(string Name, string Value)> ParseLookupValues(string xml)
    {
        try
        {
            return XDocument.Parse(xml).Descendants("LookupValue")
                .Select(e => (Name: (e.Element("Name")?.Value ?? "").Trim(), Value: (e.Element("Value")?.Value ?? "").Trim()))
                .Where(p => p.Name.Length > 0 && p.Value.Length > 0)
                .ToList();
        }
        catch (System.Xml.XmlException)
        {
            return Array.Empty<(string, string)>();
        }
    }

    public static NvidiaLatest? ParseDriverJson(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("IDS", out var ids) || ids.ValueKind != JsonValueKind.Array || ids.GetArrayLength() == 0)
                return null;
            if (!ids[0].TryGetProperty("downloadInfo", out var info)) return null;

            string version = info.TryGetProperty("Version", out var v) ? v.GetString() ?? "" : "";
            string url = info.TryGetProperty("DownloadURL", out var u) ? u.GetString() ?? "" : "";
            if (version.Length == 0 || url.Length == 0) return null;
            if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) url = "https://" + url[7..];
            return new NvidiaLatest(version, url);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Só aceita HTTPS em servidores da NVIDIA e arquivos .exe.</summary>
    public static bool IsTrustedDownload(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && (uri.Host.EndsWith(".nvidia.com", StringComparison.OrdinalIgnoreCase) || uri.Host.Equals("nvidia.com", StringComparison.OrdinalIgnoreCase))
        && uri.AbsolutePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);

    /// <summary>Positivo se <paramref name="a"/> é mais nova que <paramref name="b"/> (formato 560.94).</summary>
    public static int CompareVersions(string a, string b)
    {
        bool okA = decimal.TryParse(a, NumberStyles.Number, CultureInfo.InvariantCulture, out var x);
        bool okB = decimal.TryParse(b, NumberStyles.Number, CultureInfo.InvariantCulture, out var y);
        return okA && okB ? x.CompareTo(y) : string.CompareOrdinal(a, b);
    }

    // ---------- rede ----------

    /// <summary>Procura a versão mais nova para a placa. Retorna null se a placa não for encontrada no catálogo da NVIDIA.</summary>
    public static async Task<NvidiaLatest?> FindLatestAsync(string gpuName, CancellationToken token)
    {
        string wanted = NormalizeGpuName(gpuName);

        // Tipo 1 = GeForce; séries (psid) e produtos (pfid) vêm do catálogo da página oficial de downloads.
        var series = ParseLookupValues(await Http.GetStringAsync($"{LookupBase}?TypeID=2&ParentID=1", token));
        if (series.Count == 0) return null;

        var lookups = series.Select(async s =>
        {
            try
            {
                string xml = await Http.GetStringAsync($"{LookupBase}?TypeID=3&ParentID={Uri.EscapeDataString(s.Value)}", token);
                return (Series: s.Value, Products: ParseLookupValues(xml));
            }
            catch (HttpRequestException)
            {
                return (Series: s.Value, Products: (IReadOnlyList<(string Name, string Value)>)Array.Empty<(string, string)>());
            }
        });

        foreach (var result in await Task.WhenAll(lookups))
        {
            foreach (var product in result.Products)
            {
                if (NormalizeGpuName(product.Name) != wanted) continue;

                int osId = Environment.OSVersion.Version.Build >= 22000 ? 135 : 57;
                string url = $"{DriverLookup}?func=DriverManualLookup&psid={Uri.EscapeDataString(result.Series)}&pfid={Uri.EscapeDataString(product.Value)}" +
                             $"&osID={osId}&languageCode=1033&beta=0&isWHQL=0&dltype=-1&dch=1&upCRD=0&qnf=0&sort1=0&numberOfResults=1";
                return ParseDriverJson(await Http.GetStringAsync(url, token));
            }
        }

        return null;
    }

    public static async Task DownloadAsync(string url, string path, IProgress<double> percent, CancellationToken token)
    {
        if (!IsTrustedDownload(url)) throw new InvalidOperationException("Endereço de download não confiável.");

        using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        long total = response.Content.Headers.ContentLength ?? -1;

        await using var source = await response.Content.ReadAsStreamAsync(token);
        await using var target = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true);
        var buffer = new byte[1 << 17];
        long done = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, token)) > 0)
        {
            await target.WriteAsync(buffer.AsMemory(0, read), token);
            done += read;
            if (total > 0) percent.Report(100.0 * done / total);
        }
    }

    // ---------- assinatura e instalação ----------

    public static (bool Valid, string Signer) VerifySignature(string path)
    {
        string escaped = path.Replace("'", "''");
        var result = Shell.PowerShellUtf8(
            $"$s = Get-AuthenticodeSignature -LiteralPath '{escaped}'; $s.Status.ToString() + '|' + $s.SignerCertificate.Subject");
        string[] parts = result.Output.Trim().Split('|', 2);
        string subject = parts.Length > 1 ? parts[1] : "";
        bool valid = parts[0] == "Valid" && subject.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase);
        return (valid, subject);
    }

    /// <summary>Instala em modo silencioso (-s). A tela pode piscar e o Windows pode pedir reinício.</summary>
    public static (int ExitCode, string Output) InstallSilently(string installerPath)
    {
        var info = new ProcessStartInfo(installerPath) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in new[] { "-s", "-noreboot", "-noeula" }) info.ArgumentList.Add(argument);

        using var process = Process.Start(info) ?? throw new InvalidOperationException("Não foi possível iniciar o instalador.");
        if (!process.WaitForExit(TimeSpan.FromMinutes(30)))
        {
            return (-1, "O instalador demorou demais. Verifique se ele ainda está rodando antes de tentar de novo.");
        }

        return (process.ExitCode, "");
    }

    /// <summary>Fluxo completo: procurar, baixar, conferir assinatura, ponto de restauração e instalar.</summary>
    public static async Task<DriverUpdateResult> UpdateAsync(GpuDriverInfo gpu, IProgress<ProgressEvent> progress, CancellationToken token)
    {
        progress.Report(new ProgressEvent(StepSearch, 3, "Consultando a NVIDIA"));
        var latest = await FindLatestAsync(gpu.Name, token);
        if (latest is null)
            return new DriverUpdateResult(false, false, "Não encontrei esta placa no catálogo da NVIDIA. Use a página oficial.");

        if (CompareVersions(latest.Version, gpu.DisplayVersion) <= 0)
            return new DriverUpdateResult(true, false, $"Você já está com a versão mais nova ({gpu.DisplayVersion}).");

        string folder = Path.Combine(Path.GetTempPath(), "MBExtreme", "drivers");
        Directory.CreateDirectory(folder);
        string file = Path.Combine(folder, $"nvidia-{latest.Version}.exe");

        try
        {
            progress.Report(new ProgressEvent(StepDownload, 8, $"Baixando a versão {latest.Version}"));
            var download = new Progress<double>(p =>
                progress.Report(new ProgressEvent(StepDownload, 8 + 62 * p / 100.0, $"Baixando a versão {latest.Version}: {p:0}%")));
            await DownloadAsync(latest.DownloadUrl, file, download, token);

            token.ThrowIfCancellationRequested();
            progress.Report(new ProgressEvent(StepVerify, 72, "Conferindo a assinatura digital da NVIDIA"));
            var (valid, signer) = await Task.Run(() => VerifySignature(file), token);
            if (!valid)
                return new DriverUpdateResult(false, false, "A assinatura do arquivo baixado não é da NVIDIA. Instalação cancelada por segurança.");

            token.ThrowIfCancellationRequested();
            progress.Report(new ProgressEvent(StepRestore, 78, "Criando ponto de restauração"));
            await Task.Run(() => RestorePointService.TryCreate("MB Extreme - driver NVIDIA " + latest.Version), token);

            progress.Report(new ProgressEvent(StepInstall, 85, "Instalando (a tela pode piscar; não desligue o PC)"));
            var (exit, message) = await Task.Run(() => InstallSilently(file));
            progress.Report(new ProgressEvent(StepInstall, 100, "Concluído"));

            return exit == 0
                ? new DriverUpdateResult(true, true, $"Driver {latest.Version} instalado. Reinicie o PC para concluir.")
                : new DriverUpdateResult(false, false, message.Length > 0 ? message : $"O instalador terminou com o código {exit}.");
        }
        finally
        {
            try { File.Delete(file); }
            catch (IOException) { /* o Windows limpa a pasta temporária depois */ }
        }
    }
}
