using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.RegularExpressions;
using MBExtreme.Core.Windows;

namespace MBExtreme.Core.Support;

/// <summary>
/// Suporte remoto com o AnyDesk (programa de terceiros, não incluído). O MB Extreme só o baixa do site oficial quando o cliente pede,
/// confere a assinatura digital do fabricante, abre o programa e mostra o ID. Quem atende só entra se o cliente aceitar a conexão no AnyDesk.
/// </summary>
public static class AnyDeskRules
{
    public const string DownloadUrl = "https://download.anydesk.com/AnyDesk.exe";

    private static readonly Regex IdPattern = new(@"\b(\d{9,10})\b", RegexOptions.Compiled);

    /// <summary>Só endereços https do domínio oficial do fabricante.</summary>
    public static bool IsOfficialUrl(Uri uri) =>
        uri.Scheme == Uri.UriSchemeHttps && (uri.Host == "anydesk.com" || uri.Host.EndsWith(".anydesk.com", StringComparison.OrdinalIgnoreCase));

    /// <summary>Assinatura válida e emitida para a empresa do AnyDesk (a empresa já se chamou philandro Software GmbH).</summary>
    public static bool IsTrustedSigner(string status, string subject) =>
        status.Equals("Valid", StringComparison.OrdinalIgnoreCase)
        && (subject.Contains("AnyDesk Software GmbH", StringComparison.OrdinalIgnoreCase) || subject.Contains("philandro Software GmbH", StringComparison.OrdinalIgnoreCase));

    /// <summary>Extrai o ID (9 ou 10 dígitos) da saída de "AnyDesk.exe --get-id".</summary>
    public static string? ParseId(string output)
    {
        var match = IdPattern.Match(output ?? "");
        return match.Success ? match.Groups[1].Value : null;
    }

    /// <summary>"123456789" vira "123 456 789".</summary>
    public static string FormatId(string id)
    {
        string digits = new string(id.Where(char.IsDigit).ToArray());
        if (digits.Length == 9) return $"{digits[..3]} {digits.Substring(3, 3)} {digits[6..]}";
        if (digits.Length == 10) return $"{digits[..1]} {digits.Substring(1, 3)} {digits.Substring(4, 3)} {digits[7..]}";
        return digits;
    }

    /// <summary>Endereço que abre o AnyDesk do técnico já conectando àquele ID. Só aceita números (nada é repassado ao sistema além do ID).</summary>
    public static string? ConnectUri(string idText)
    {
        string digits = new string((idText ?? "").Where(char.IsDigit).ToArray());
        return digits.Length is 9 or 10 && (idText ?? "").All(c => char.IsDigit(c) || char.IsWhiteSpace(c)) ? "anydesk:" + digits : null;
    }

    /// <summary>Mensagem pronta para o cliente enviar ao suporte.</summary>
    public static string SupportMessage(string name, string? id, string problem, string appVersion)
    {
        var lines = new List<string>
        {
            $"Pedido de suporte · MB Extreme {appVersion}",
            $"Cliente: {(string.IsNullOrWhiteSpace(name) ? "(sem nome)" : name.Trim())}",
            id is null ? "ID do AnyDesk: (ainda não gerado)" : $"ID do AnyDesk: {FormatId(id)}",
        };
        if (!string.IsNullOrWhiteSpace(problem)) lines.Add("O que preciso: " + problem.Trim());
        lines.Add("Vou aceitar a conexão quando o técnico pedir.");
        return string.Join(Environment.NewLine, lines);
    }
}

[SupportedOSPlatform("windows")]
public sealed class AnyDeskService
{
    private Process? _started;

    public static string ToolsFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MBExtreme", "tools");

    public static string PortablePath => Path.Combine(ToolsFolder, "AnyDesk.exe");

    public static string? Locate()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "AnyDesk", "AnyDesk.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "AnyDesk", "AnyDesk.exe"),
            PortablePath,
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    /// <summary>Baixa a versão portátil do site oficial, confere a assinatura do fabricante e só então a deixa disponível.</summary>
    public static async Task<(bool Ok, string Message)> DownloadAsync(HttpClient http, IProgress<double>? progress, CancellationToken token)
    {
        Directory.CreateDirectory(ToolsFolder);
        string temp = Path.Combine(ToolsFolder, "AnyDesk.download");
        try
        {
            using var response = await http.GetAsync(AnyDeskRules.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, token);
            if (!response.IsSuccessStatusCode) return (false, $"O site do AnyDesk respondeu {(int)response.StatusCode}.");
            if (response.RequestMessage?.RequestUri is { } final && !AnyDeskRules.IsOfficialUrl(final))
                return (false, "O download foi redirecionado para fora do site oficial e foi bloqueado.");

            long total = response.Content.Headers.ContentLength ?? 0;
            await using (var input = await response.Content.ReadAsStreamAsync(token))
            await using (var output = File.Create(temp))
            {
                var buffer = new byte[81920];
                long done = 0;
                int read;
                while ((read = await input.ReadAsync(buffer, token)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read), token);
                    done += read;
                    if (total > 0) progress?.Report(100.0 * done / total);
                }
            }

            var (status, subject) = await Task.Run(() => ReadSignature(temp), token);
            if (!AnyDeskRules.IsTrustedSigner(status, subject))
                return (false, "A assinatura digital do arquivo baixado não é a do AnyDesk. Por segurança ele foi descartado.");

            File.Move(temp, PortablePath, overwrite: true);
            return (true, "AnyDesk baixado e verificado.");
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException or TaskCanceledException)
        {
            return (false, "Não foi possível baixar o AnyDesk: " + ex.Message);
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch (IOException) { }
        }
    }

    private static (string Status, string Subject) ReadSignature(string path)
    {
        string escaped = path.Replace("'", "''");
        var result = Shell.PowerShellUtf8($"$s = Get-AuthenticodeSignature -LiteralPath '{escaped}'; [pscustomobject]@{{ Status=[string]$s.Status; Subject=[string]$s.SignerCertificate.Subject }} | ConvertTo-Json -Compress");
        try
        {
            using var doc = JsonDocument.Parse(result.Output);
            return (doc.RootElement.GetProperty("Status").GetString() ?? "", doc.RootElement.GetProperty("Subject").GetString() ?? "");
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return ("Unknown", "");
        }
    }

    /// <summary>Abre o AnyDesk (nenhuma conexão é aceita sozinha).</summary>
    public bool Start(string exePath)
    {
        try
        {
            _started = Process.Start(new ProcessStartInfo(exePath) { UseShellExecute = true });
            return true;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>Lê o ID do computador. O AnyDesk pode levar alguns segundos para gerar o ID na primeira vez.</summary>
    public static async Task<string?> GetIdAsync(string exePath, CancellationToken token, int attempts = 15)
    {
        for (int i = 0; i < attempts && !token.IsCancellationRequested; i++)
        {
            try
            {
                var info = new ProcessStartInfo(exePath, "--get-id") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
                using var process = Process.Start(info);
                if (process is not null)
                {
                    string text = await process.StandardOutput.ReadToEndAsync(token);
                    await process.WaitForExitAsync(token);
                    if (AnyDeskRules.ParseId(text) is { } id) return id;
                }
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { }

            await Task.Delay(1000, token);
        }

        return null;
    }

    /// <summary>Fecha só o AnyDesk que o MB Extreme abriu (nunca um instalado como serviço).</summary>
    public bool StopStarted()
    {
        try
        {
            if (_started is { HasExited: false }) { _started.Kill(true); _started = null; return true; }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
        return false;
    }

    public bool IsRunning => _started is { HasExited: false };
}
