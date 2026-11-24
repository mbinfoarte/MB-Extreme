using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using MBExtreme.Core.Windows;

namespace MBExtreme.Core.Repair;

/// <summary>O que o "Escanear o Windows" achou e qual reparo resolve.</summary>
public sealed record RepairFinding(string Title, string Detail, bool Problem, string? TaskId);

/// <summary>
/// Exame rápido do Windows (1 a 2 minutos, só lendo): imagem do Windows (DISM CheckHealth), repositório do WMI, serviços do
/// Windows Update, reinício pendente e rede. Para cada problema, indica o reparo certo da lista.
/// </summary>
public static class RepairScan
{
    /// <summary>Ordem recomendada de execução (a mesma da Microsoft): imagem → arquivos → disco → limpeza → serviços → rede.</summary>
    public static IReadOnlyList<string> Sequence { get; } = new[]
    {
        "dism-scan", "dism-restore", "sfc", "chkdsk-scan", "dism-cleanup", "wu-reset", "store", "wmi", "dns-renew", "net-reset",
    };

    public static int Order(string taskId) => Sequence.ToList().IndexOf(taskId) is int i and >= 0 ? i + 1 : 0;

    public enum ImageState { Healthy, Repairable, NotRepairable, Unknown }

    /// <summary>Saída do "DISM /Online /Cleanup-Image /CheckHealth" em português ou inglês.</summary>
    public static ImageState ParseDism(string output)
    {
        string t = output.ToLowerInvariant();
        if (Regex.IsMatch(t, "not repairable|não é reparável|não pode ser reparad")) return ImageState.NotRepairable;
        if (Regex.IsMatch(t, "is repairable|é reparável|pode ser reparad")) return ImageState.Repairable;
        if (Regex.IsMatch(t, "no component store corruption|nenhuma corrupção|nenhum dano")) return ImageState.Healthy;
        return ImageState.Unknown;
    }

    /// <summary>Saída do "winmgmt /verifyrepository": consistente ou não.</summary>
    public static bool? ParseWmi(string output)
    {
        string t = output.ToLowerInvariant();
        if (Regex.IsMatch(t, "inconsistent|inconsistente")) return false;
        if (Regex.IsMatch(t, "consistent|consistente")) return true;
        return null;
    }

    [SupportedOSPlatform("windows")]
    public static async Task<IReadOnlyList<RepairFinding>> RunAsync(IProgress<string>? progress, CancellationToken token)
    {
        var list = new List<RepairFinding>();

        progress?.Report("Conferindo a imagem do Windows (DISM)...");
        var dism = await Task.Run(() => Shell.RunOem("DISM.exe", "/Online", "/Cleanup-Image", "/CheckHealth"), token).ConfigureAwait(false);
        list.Add(ParseDism(dism.Output + dism.Error) switch
        {
            ImageState.Healthy => new RepairFinding("Imagem do Windows", "Sem corrupção.", false, null),
            ImageState.Repairable => new RepairFinding("Imagem do Windows", "Tem corrupção que dá para reparar. Rode o DISM e depois o SFC (ou o Reparo completo).", true, "full"),
            ImageState.NotRepairable => new RepairFinding("Imagem do Windows", "Corrupção que o DISM não repara sozinho: use Reinstalar sem pendrive (mantém seus arquivos).", true, null),
            _ => new RepairFinding("Imagem do Windows", "O DISM não respondeu (rode como administrador).", false, "dism-scan"),
        });

        token.ThrowIfCancellationRequested();
        progress?.Report("Conferindo o WMI...");
        var wmi = await Task.Run(() => Shell.RunOem("winmgmt.exe", "/verifyrepository"), token).ConfigureAwait(false);
        list.Add(ParseWmi(wmi.Output + wmi.Error) switch
        {
            false => new RepairFinding("WMI (informações do sistema)", "Repositório inconsistente: programas não conseguem ler o hardware.", true, "wmi"),
            true => new RepairFinding("WMI (informações do sistema)", "Consistente.", false, null),
            _ => new RepairFinding("WMI (informações do sistema)", "Não deu para conferir.", false, null),
        });

        progress?.Report("Conferindo o Windows Update...");
        var disabled = UpdateCheck.Services.Where(s =>
        {
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\" + s.Name);
                return key?.GetValue("Start") is int start && start == 4;   // 4 = desativado
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { return false; }
        }).Select(s => s.Title).ToList();
        list.Add(disabled.Count > 0
            ? new RepairFinding("Windows Update", "Serviço desativado: " + string.Join(", ", disabled) + ". As atualizações não instalam.", true, "wu-reset")
            : new RepairFinding("Windows Update", "Serviços ligados.", false, null));

        progress?.Report("Conferindo a rede...");
        var gateway = NetworkStatusGateway();
        bool router = gateway is not null && await Ping(gateway).ConfigureAwait(false);
        bool internet = await Ping("1.1.1.1").ConfigureAwait(false);
        bool names = internet && await Resolves("www.microsoft.com").ConfigureAwait(false);
        list.Add(!router && gateway is not null ? new RepairFinding("Rede", "O roteador não responde: confira o cabo ou o Wi-Fi.", true, "net-reset")
            : !internet ? new RepairFinding("Rede", "Sem internet, mas o roteador responde: problema no provedor ou na pilha de rede.", true, "net-reset")
            : !names ? new RepairFinding("Rede", "A internet funciona mas os nomes não abrem (DNS).", true, "dns-renew")
            : new RepairFinding("Rede", "Internet e DNS funcionando.", false, null));

        bool reboot = RebootState.IsPending();
        list.Add(reboot ? new RepairFinding("Reinício pendente", "O Windows está esperando reiniciar para terminar uma atualização. Reinicie antes de reparar.", true, null)
                        : new RepairFinding("Reinício pendente", "Nada esperando reiniciar.", false, null));
        return list;
    }

    private static string? NetworkStatusGateway() => MBExtreme.Core.Network.NetworkStatus.Active()?.Gateway;

    private static async Task<bool> Ping(string host)
    {
        try
        {
            using var p = new System.Net.NetworkInformation.Ping();
            return (await p.SendPingAsync(host, 1500).ConfigureAwait(false)).Status == System.Net.NetworkInformation.IPStatus.Success;
        }
        catch (System.Net.NetworkInformation.PingException) { return false; }
    }

    private static async Task<bool> Resolves(string host)
    {
        try { return (await System.Net.Dns.GetHostAddressesAsync(host).ConfigureAwait(false)).Length > 0; }
        catch (System.Net.Sockets.SocketException) { return false; }
    }
}
