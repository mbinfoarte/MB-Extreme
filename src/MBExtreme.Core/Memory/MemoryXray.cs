using System.Text.RegularExpressions;
using MBExtreme.Core.Games;
using MBExtreme.Core.Tools;

namespace MBExtreme.Core.Memory;

/// <summary>Grupo de cada linha do Raio-X da memória.</summary>
public enum XrayKind { App, Background, Service, StoreApp, Protected }

/// <summary>O que o botão "Liberar" faz com a linha.</summary>
public enum XrayAction
{
    /// <summary>Nada (protegido ou só informação).</summary>
    None,
    /// <summary>Fecha o programa agora (volta quando abrir de novo).</summary>
    Close,
    /// <summary>Fecha e tira da inicialização do Windows (volta pela Central Desfazer).</summary>
    CloseAndStartup,
    /// <summary>Para o serviço agora e deixa em Manual (volta pela Central Desfazer).</summary>
    StopService,
    /// <summary>Bloqueia o app da Store em segundo plano e fecha (volta pela Central Desfazer).</summary>
    BlockBackground,
}

/// <summary>Um processo como o Windows mostra: memória privada (o que ele ocupa de verdade) e se tem janela.</summary>
public sealed record XrayProcess(int Pid, string Name, long PrivateBytes, bool HasWindow, string? Path = null, string? Title = null, DateTime? StartTime = null);

/// <summary>Um serviço rodando e o processo onde ele roda (vários serviços do Windows dividem o mesmo svchost).</summary>
public sealed record XrayService(string Name, string Display, int Pid, int StartType);

/// <summary>Um app da Microsoft Store com a permissão de segundo plano (nome da família do pacote).</summary>
public sealed record XrayStoreApp(string PackageFamily, bool BackgroundDisabled);

/// <summary>Uma linha do Raio-X: quem é, quanto ocupa, o que dá para fazer e se já vem marcado.</summary>
public sealed record XrayItem(
    string Key, XrayKind Kind, string Name, string Detail, long Bytes, XrayAction Action, bool Preselected, string Reason,
    IReadOnlyList<XrayProcess> Processes, string? ServiceName = null, string? PackageFamily = null, string? StartupName = null)
{
    public bool Protected => Kind == XrayKind.Protected || Action == XrayAction.None;
}

/// <summary>Resultado da leitura: as linhas e o total por grupo.</summary>
public sealed record XrayReport(IReadOnlyList<XrayItem> Items)
{
    public long Bytes(XrayKind kind) => Items.Where(i => i.Kind == kind).Sum(i => i.Bytes);

    /// <summary>Quanto volta se liberar o que já vem marcado.</summary>
    public long PreselectedBytes => Items.Where(i => i.Preselected).Sum(i => i.Bytes);
}

/// <summary>
/// Regras do Raio-X da memória (sem acessar o Windows, para testar). Mostra quem ocupa a RAM, separando programas abertos,
/// ajudantes de segundo plano, serviços (cada um, mesmo dentro do svchost) e apps da Store. Só vem marcado o que é seguro:
/// ajudantes de fundo, serviços que ninguém usa e apps da Store de fundo conhecidos. O que tem janela nunca vem marcado e o
/// que é do Windows, segurança, drivers, rede, áudio, antitrapaça, lojas de jogos e o MB Extreme nunca aparece como opção.
/// </summary>
public static class XrayRules
{
    /// <summary>Apps da Store que só servem quando abertos: podem ficar bloqueados em segundo plano.</summary>
    private static readonly Regex StoreBackgroundSafe = new(
        "^(MSTeams|MicrosoftTeams|MicrosoftWindows\\.Client\\.WebExperience|Microsoft\\.YourPhone|Microsoft\\.549981C3F5F10|Microsoft\\.BingNews|" +
        "Microsoft\\.BingWeather|Microsoft\\.People|Microsoft\\.ZuneMusic|Microsoft\\.ZuneVideo|Microsoft\\.GetHelp|Microsoft\\.Getstarted|Microsoft\\.Todos|" +
        "Microsoft\\.WindowsMaps|Microsoft\\.MicrosoftOfficeHub|Microsoft\\.WindowsFeedbackHub|Microsoft\\.MicrosoftSolitaireCollection|Microsoft\\.OutlookForWindows|" +
        "microsoft\\.windowscommunicationsapps|Microsoft\\.Copilot|Microsoft\\.Windows\\.DevHome|Clipchamp\\.Clipchamp|Microsoft\\.PowerAutomateDesktop|" +
        "SpotifyAB\\.SpotifyMusic|Disney\\.|5319275A\\.WhatsAppDesktop|FACEBOOK\\.|BytedancePte\\.)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Apps da Store que nunca são bloqueados (jogos, segurança, loja, componentes do Windows).</summary>
    private static readonly Regex StoreKeep = new(
        "^(Microsoft\\.GamingServices|Microsoft\\.Xbox|Microsoft\\.WindowsStore|Microsoft\\.StorePurchaseApp|Microsoft\\.SecHealthUI|Microsoft\\.Windows\\.|" +
        "Microsoft\\.UI\\.|Microsoft\\.VCLibs|Microsoft\\.NET|Microsoft\\.DesktopAppInstaller|Microsoft\\.WindowsTerminal|Microsoft\\.AAD|Microsoft\\.LockApp|" +
        "Microsoft\\.ShellExperienceHost|Microsoft\\.StartMenuExperienceHost|RealtekSemiconductor|NVIDIACorp|AdvancedMicroDevicesInc|AppUp\\.Intel|Dolby|DTS|" +
        "WavesAudio|Microsoft\\.HEVCVideoExtension|Microsoft\\.WebpImageExtension|Microsoft\\.HEIFImageExtension|Microsoft\\.VP9VideoExtensions)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Ajudantes de hardware e de jogos que ficam rodando de propósito: nunca entram como "pode fechar".</summary>
    private static readonly Regex KeepBackground = new(
        "nvidia|nvcontainer|nvdisplay|amd|radeon|igfx|intel|realtek|rtk|nahimic|dolby|waves|audio|sound|bluetooth|wlan|wifi|vpn|logi|lghub|razer|corsair|" +
        "steelseries|asus|armoury|msi|gigabyte|aorus|steam|epic|battle|riot|vanguard|vgc|easyanticheat|battleye|faceit|discord|obs|rustdesk|anydesk|teamviewer|" +
        "defender|antivir|kaspersky|avast|avg|norton|mcafee|eset|bitdefender|malwarebytes|mbextreme",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Família do pacote (Nome_Editor) a partir do caminho do programa em WindowsApps, ou null.</summary>
    public static string? PackageFamilyFromPath(string? path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        var m = Regex.Match(path, @"\\WindowsApps\\([^\\]+)\\", RegexOptions.IgnoreCase);
        if (!m.Success) return null;
        string[] parts = m.Groups[1].Value.Split('_');   // Nome_Versão_Arquitetura_Recurso_Editor
        return parts.Length >= 5 ? parts[0] + "_" + parts[^1] : null;
    }

    /// <summary>Nome amigável de um app da Store (o nome do pacote sem o editor).</summary>
    public static string StoreAppName(string family)
    {
        string name = family.Split('_')[0];
        return name switch
        {
            "MSTeams" or "MicrosoftTeams" => "Microsoft Teams",
            "MicrosoftWindows.Client.WebExperience" => "Widgets do Windows",
            "Microsoft.YourPhone" => "Phone Link (Vincular ao Celular)",
            "Microsoft.549981C3F5F10" => "Cortana",
            "Microsoft.BingNews" => "Notícias",
            "Microsoft.BingWeather" => "Clima",
            "Microsoft.ZuneMusic" => "Media Player",
            "Microsoft.ZuneVideo" => "Filmes e TV",
            "Microsoft.OutlookForWindows" or "microsoft.windowscommunicationsapps" => "Outlook / Email",
            "Microsoft.Copilot" => "Copilot",
            "5319275A.WhatsAppDesktop" => "WhatsApp",
            "SpotifyAB.SpotifyMusic" => "Spotify",
            _ => name.Replace("Microsoft.", "").Replace("MicrosoftWindows.", ""),
        };
    }

    /// <summary>Nome do executável (sem .exe) de um comando da inicialização: "C:\\x\\OneDrive.exe" /background → OneDrive.</summary>
    public static string? StartupExe(string command)
    {
        if (string.IsNullOrWhiteSpace(command)) return null;
        string c = command.Trim();
        string path = c.StartsWith('"') ? c[1..].Split('"')[0] : Regex.Match(c, @"^(.+?\.(exe|lnk|bat|cmd))\b", RegexOptions.IgnoreCase) is { Success: true } m ? m.Groups[1].Value : c.Split(' ')[0];
        string file = path.Replace('/', '\\').Split('\\')[^1];
        int dot = file.LastIndexOf('.');
        return dot > 0 ? file[..dot] : file;
    }

    public static bool StoreAppCanBlock(string family) => !StoreKeep.IsMatch(family);

    public static bool StoreAppPreselect(string family) => StoreBackgroundSafe.IsMatch(family) && !StoreKeep.IsMatch(family);

    /// <summary>
    /// Monta o Raio-X. <paramref name="startupNames"/>: nomes dos executáveis que abrem com o Windows (sem .exe).
    /// <paramref name="allowlist"/>: o que a pessoa pediu para nunca mexer. <paramref name="foreground"/>: programa em uso agora.
    /// </summary>
    public static XrayReport Build(IReadOnlyList<XrayProcess> processes, IReadOnlyList<XrayService> services, IReadOnlyList<XrayStoreApp> storeApps,
        IReadOnlyCollection<string> startupNames, IReadOnlyCollection<string> allowlist, string? foreground = null, int selfPid = -1)
    {
        var items = new List<XrayItem>();
        var allow = new HashSet<string>(allowlist, StringComparer.OrdinalIgnoreCase);
        var startup = new HashSet<string>(startupNames, StringComparer.OrdinalIgnoreCase);
        var byPid = processes.GroupBy(p => p.Pid).ToDictionary(g => g.Key, g => g.First());
        var used = new HashSet<int>();

        // 1) serviços: a memória de cada processo de serviço é dividida entre os serviços que rodam nele
        foreach (var group in services.Where(s => s.Pid > 0).GroupBy(s => s.Pid))
        {
            if (!byPid.TryGetValue(group.Key, out var host)) continue;
            used.Add(group.Key);
            var list = group.ToList();
            long share = host.PrivateBytes / list.Count;
            foreach (var s in list)
            {
                var advice = OptimizeAdvisor.ServiceAdvice(s.Name, s.Display);
                var known = OptimizeAdvisor.Services.FirstOrDefault(k => k.Name.Equals(s.Name, StringComparison.OrdinalIgnoreCase));
                bool sharedHost = list.Count > 1;
                string where = sharedHost ? $"divide o processo {host.Name} com mais {list.Count - 1}" : host.Name + ".exe";
                bool canStop = advice is OptimizeAdvice.Safe or OptimizeAdvice.IfUnused && !allow.Contains(s.Name);
                items.Add(new XrayItem("svc:" + s.Name, canStop ? XrayKind.Service : XrayKind.Protected, known?.Title ?? s.Display,
                    (known?.WhatIs ?? "Serviço") + " · " + where, share, canStop ? XrayAction.StopService : XrayAction.None,
                    canStop && advice == OptimizeAdvice.Safe, canStop ? OptimizeAdvisor.AdviceText(advice) : "Do Windows ou importante",
                    sharedHost ? Array.Empty<XrayProcess>() : new[] { host }, ServiceName: s.Name));
            }
        }

        // 2) apps da Store: processos que rodam de dentro da pasta WindowsApps
        var store = storeApps.ToDictionary(a => a.PackageFamily, StringComparer.OrdinalIgnoreCase);
        foreach (var group in processes.Where(p => !used.Contains(p.Pid) && PackageFamilyFromPath(p.Path) is not null)
                     .GroupBy(p => PackageFamilyFromPath(p.Path)!, StringComparer.OrdinalIgnoreCase))
        {
            var procs = group.ToList();
            foreach (var p in procs) used.Add(p.Pid);
            string family = group.Key;
            bool window = procs.Any(p => p.HasWindow);
            bool inUse = window && procs.Any(p => p.Name.Equals(foreground, StringComparison.OrdinalIgnoreCase));
            bool block = StoreAppCanBlock(family) && !allow.Contains(family) && procs.All(p => !allow.Contains(p.Name));
            bool already = store.TryGetValue(family, out var app) && app.BackgroundDisabled;
            items.Add(new XrayItem("store:" + family, block ? XrayKind.StoreApp : XrayKind.Protected, StoreAppName(family),
                window ? "App da Store aberto" : "App da Store rodando em segundo plano", procs.Sum(p => p.PrivateBytes),
                block ? (already ? XrayAction.Close : XrayAction.BlockBackground) : XrayAction.None,
                block && !window && StoreAppPreselect(family),
                !block ? "Do Windows ou importante" : inUse ? "Você está usando" : window ? "Aberto: feche se não estiver usando" : "Pode bloquear",
                procs, PackageFamily: family));
        }
        // apps da Store com segundo plano liberado mas fechados agora: bloquear evita que abram sozinhos depois
        foreach (var app in storeApps.Where(a => !a.BackgroundDisabled && !items.Any(i => i.PackageFamily == a.PackageFamily) && StoreAppPreselect(a.PackageFamily)))
            items.Add(new XrayItem("store:" + app.PackageFamily, XrayKind.StoreApp, StoreAppName(app.PackageFamily),
                "Pode abrir sozinho em segundo plano", 0, XrayAction.BlockBackground, true, "Pode bloquear", Array.Empty<XrayProcess>(), PackageFamily: app.PackageFamily));

        // 3) o resto, agrupado por programa
        foreach (var group in processes.Where(p => !used.Contains(p.Pid) && p.Pid != selfPid).GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
        {
            var procs = group.ToList();
            string name = group.Key;
            long bytes = procs.Sum(p => p.PrivateBytes);
            bool window = procs.Any(p => p.HasWindow);
            string? title = procs.Select(p => p.Title).FirstOrDefault(t => !string.IsNullOrWhiteSpace(t));
            bool locked = OptimizeAdvisor.ProcessProtected(name) || allow.Contains(name) || name.StartsWith("MBExtreme", StringComparison.OrdinalIgnoreCase);
            if (locked)
            {
                items.Add(new XrayItem("proc:" + name, XrayKind.Protected, name, ProtectedDetail(name), bytes, XrayAction.None, false, "Protegido", procs));
                continue;
            }
            bool helper = OptimizeAdvisor.ProcessOneClick(name);
            bool keep = !helper && KeepBackground.IsMatch(name);
            bool onStartup = startup.Contains(name);
            bool sync = GameModeRules.ReopenAfter.Contains(name, StringComparer.OrdinalIgnoreCase);   // nuvem: fecha agora, mas continua abrindo com o Windows
            string count = procs.Count > 1 ? $"{procs.Count} processos" : "1 processo";
            if (window)
            {
                bool inUse = name.Equals(foreground, StringComparison.OrdinalIgnoreCase);
                items.Add(new XrayItem("proc:" + name, XrayKind.App, name, (title is null ? "" : title + " · ") + count, bytes, XrayAction.Close, false,
                    inUse ? "Você está usando" : "Aberto: feche se não estiver usando", procs, StartupName: onStartup ? name : null));
            }
            else if (keep)
                items.Add(new XrayItem("proc:" + name, XrayKind.Protected, name, "Driver ou programa de hardware, jogo ou segurança · " + count, bytes, XrayAction.None, false, "Deixe rodando", procs));
            else
                items.Add(new XrayItem("proc:" + name, XrayKind.Background, name, (onStartup ? "Abre com o Windows · " : "") + count, bytes,
                    onStartup && helper && !sync ? XrayAction.CloseAndStartup : XrayAction.Close, helper,
                    sync ? "Para a sincronização até abrir de novo" : helper ? (onStartup ? "Fechar e não abrir com o Windows" : "Pode fechar") : "Confira antes",
                    procs, StartupName: onStartup && !sync ? name : null));
        }

        return new XrayReport(items.OrderByDescending(i => i.Preselected).ThenByDescending(i => i.Bytes).ToList());
    }

    private static string ProtectedDetail(string name) => name.ToLowerInvariant() switch
    {
        "memory compression" => "Memória comprimida do Windows (é RAM que ele já economizou)",
        "explorer" => "Área de trabalho e pastas",
        "dwm" => "Desenha as janelas na tela",
        "msmpeng" => "Antivírus do Windows",
        _ => "Do Windows, segurança ou do MB Extreme",
    };

    /// <summary>Para a Limpeza e o 1 clique: só fecha ajudantes de fundo e para serviços seguros, sem mudar nada que fique depois.</summary>
    public static IReadOnlyList<XrayItem> QuickPick(XrayReport report) =>
        report.Items.Where(i => i.Preselected && i.Kind is XrayKind.Background or XrayKind.Service).ToList();

    /// <summary>Aviso de memória: passa do limite e a limpeza comum não resolveu (o que ocupa são programas e serviços rodando).</summary>
    public static bool ShouldWarn(double usedPercent, double usedAfterClean, int threshold = 90) => usedPercent >= threshold && usedAfterClean >= threshold;
}
