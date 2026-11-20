using System.Diagnostics;
using System.Management;
using System.Net.Http;
using System.Runtime.Versioning;

namespace MBExtreme.Core.Remote;

/// <summary>
/// Comanda o RustDesk instalado neste PC (Windows, administrador). Instala o oficial na primeira vez (baixa do GitHub do RustDesk e
/// confere o SHA-256 antes de executar), lê o ID, troca a senha, aplica as opções e abre conexões.
/// </summary>
[SupportedOSPlatform("windows")]
public static class RustDeskHost
{
    public static string Exe => RustDesk.InstalledExe(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));

    public static bool IsInstalled => File.Exists(Exe);

    /// <summary>Baixa, confere e instala o RustDesk oficial (com o serviço do Windows). Não faz nada se já estiver instalado.</summary>
    public static async Task InstallAsync(IProgress<string> progress, CancellationToken ct)
    {
        if (IsInstalled) return;
        string dir = Path.Combine(Path.GetTempPath(), "MB-RustDesk");
        Directory.CreateDirectory(dir);
        string file = Path.Combine(dir, $"rustdesk-{RustDesk.Version}-x86_64.exe");
        if (!File.Exists(file) || !RustDesk.IsOfficial(file))
        {
            progress.Report("Baixando o módulo de acesso remoto (RustDesk, 25 MB)...");
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("MBExtreme/" + AppInfo.Version);
            await using (var stream = await http.GetStreamAsync(RustDesk.DownloadUrl, ct).ConfigureAwait(false))
            await using (var output = File.Create(file))
                await stream.CopyToAsync(output, ct).ConfigureAwait(false);
            if (!RustDesk.IsOfficial(file))
            {
                File.Delete(file);
                throw new InvalidOperationException("O arquivo baixado não é o RustDesk oficial (o código de verificação não confere). Tente de novo mais tarde.");
            }
        }
        progress.Report("Instalando o acesso remoto...");
        var (code, _) = await RunAsync(file, RustDesk.InstallArgs(), TimeSpan.FromMinutes(3), ct).ConfigureAwait(false);
        for (int i = 0; i < 30 && !IsInstalled; i++) await Task.Delay(1000, ct).ConfigureAwait(false);
        if (!IsInstalled) throw new InvalidOperationException($"O RustDesk não foi instalado (código {code}).");
        await Task.Delay(3000, ct).ConfigureAwait(false);   // o serviço gera o ID nos primeiros segundos
    }

    public static async Task<string?> GetIdAsync(CancellationToken ct)
    {
        for (int i = 0; i < 10; i++)
        {
            var (_, output) = await RunAsync(Exe, RustDesk.GetIdArgs(), TimeSpan.FromSeconds(15), ct).ConfigureAwait(false);
            if (RustDesk.ParseId(output) is { } id) return id;
            await Task.Delay(1500, ct).ConfigureAwait(false);
        }
        return null;
    }

    public static async Task SetPasswordAsync(string password, CancellationToken ct)
    {
        var (_, output) = await RunAsync(Exe, RustDesk.PasswordArgs(password), TimeSpan.FromSeconds(20), ct).ConfigureAwait(false);
        if (!output.Contains("Done", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("O RustDesk não aceitou a senha. " + output.Trim());
    }

    public static async Task ApplyOptionsAsync(bool unattended, CancellationToken ct)
    {
        foreach (var (key, value) in RustDesk.Options(unattended))
            await RunAsync(Exe, RustDesk.OptionArgs(key, value), TimeSpan.FromSeconds(15), ct).ConfigureAwait(false);
    }

    /// <summary>Abre a janela de conexão (tela ou arquivos) no ID do cliente. Não espera a janela fechar.</summary>
    public static void Connect(string id, string? password, bool files)
    {
        var info = new ProcessStartInfo(Exe) { UseShellExecute = false };
        foreach (var a in RustDesk.ConnectArgs(id, password, files)) info.ArgumentList.Add(a);
        Process.Start(info);
    }

    /// <summary>Tem alguém conectado neste PC agora? (o RustDesk abre o gerenciador de conexão "--cm" durante a sessão)</summary>
    public static bool SessionOpen()
    {
        try
        {
            using var search = new ManagementObjectSearcher("SELECT CommandLine FROM Win32_Process WHERE Name = 'rustdesk.exe'");
            return search.Get().Cast<ManagementObject>().Any(p => RustDesk.IsSessionProcess(p["CommandLine"] as string));
        }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException) { return false; }
    }

    /// <summary>Encerra o acesso de quem estiver conectado (fecha o gerenciador de conexão do RustDesk).</summary>
    public static void EndSessions()
    {
        try
        {
            using var search = new ManagementObjectSearcher("SELECT ProcessId, CommandLine FROM Win32_Process WHERE Name = 'rustdesk.exe'");
            foreach (ManagementObject p in search.Get())
            {
                if (!RustDesk.IsSessionProcess(p["CommandLine"] as string)) continue;
                try { Process.GetProcessById(Convert.ToInt32(p["ProcessId"])).Kill(); }
                catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { }
            }
        }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException) { }
    }

    private static async Task<(int Code, string Output)> RunAsync(string exe, IEnumerable<string> args, TimeSpan timeout, CancellationToken ct)
    {
        var info = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in args) info.ArgumentList.Add(a);
        using var p = Process.Start(info) ?? throw new InvalidOperationException("Não foi possível abrir o RustDesk.");
        var read = p.StandardOutput.ReadToEndAsync(ct);
        var err = p.StandardError.ReadToEndAsync(ct);
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(timeout);
        try { await p.WaitForExitAsync(limit.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            try { p.Kill(true); } catch (InvalidOperationException) { }
            return (-1, "");
        }
        return (p.ExitCode, (await read.ConfigureAwait(false)) + (await err.ConfigureAwait(false)));
    }
}
