namespace MBExtreme.Core.Licensing;

/// <summary>
/// Quais páginas do app exigem plano pago, e qual. Planos, do menor para o maior: Grátis, Essencial, Gamer, Creator e Técnico
/// (cada um inclui o anterior). Enquanto <see cref="LicenseConfig.Enforce"/> for falso, nada é bloqueado (beta aberto).
/// </summary>
public static class FeaturePolicy
{
    public const string Essencial = "essencial", Gamer = "gamer", Creator = "creator", Tecnico = "tecnico";

    /// <summary>Ordem dos planos. "pro" é o nome antigo (licenças emitidas antes dos planos) e vale como o maior.</summary>
    public static int Rank(string? plan) => plan switch
    {
        Essencial => 1, Gamer => 2, Creator => 3, Tecnico => 4, "pro" => 4, _ => 0,
    };

    public static string Title(string? plan) => plan switch
    {
        Essencial => "Essencial", Gamer => "Gamer", Creator => "Creator", Tecnico => "Técnico", "pro" => "Pro", _ => "Grátis",
    };

    /// <summary>
    /// Plano mínimo de cada página paga. Tudo que não está aqui é do plano Grátis (com conta): Painel com as correções básicas,
    /// limpeza simples, memória, programas da inicialização, programas (instalar, atualizar, desinstalar), segurança, pontos de
    /// restauração, Central Desfazer, Teste de internet e a Central de Rede só com a Visão geral (o resto dela segue
    /// <see cref="NetCenterAccess"/>).
    /// </summary>
    public static IReadOnlyDictionary<string, string> PagePlans { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        // Essencial: otimização completa e manutenção
        ["bios"] = Essencial, ["bios-tune"] = Essencial, ["boot"] = Essencial, ["cleanextras"] = Essencial, ["debloat"] = Essencial,
        ["devices"] = Essencial, ["diskmap"] = Essencial, ["diskmgr"] = Essencial, ["dns"] = Essencial, ["driver-removal"] = Essencial,
        ["drivers"] = Essencial, ["duplicates"] = Essencial, ["fakecheck"] = Essencial, ["ghosts"] = Essencial, ["hardware"] = Essencial,
        ["largefiles"] = Essencial, ["net-tweaks"] = Essencial, ["periph"] = Essencial, ["periphtest"] = Essencial,
        ["privacy"] = Essencial, ["proc-profiles"] = Essencial, ["processes"] = Essencial, ["recipe"] = Essencial, ["registry"] = Essencial,
        ["removable"] = Essencial, ["repair"] = Essencial, ["services"] = Essencial, ["shredder"] = Essencial,
        ["storage"] = Essencial, ["tweaks-all"] = Essencial, ["tweaks-perf"] = Essencial, ["tweaks-storage"] = Essencial,
        ["uninstall-leftovers"] = Essencial, ["visual"] = Essencial, ["weekly"] = Essencial, ["wifi"] = Essencial,
        // Gamer: jogos, benchmark, controle, personalização e monitor
        ["bench"] = Gamer, ["bottleneck"] = Gamer, ["compare"] = Gamer, ["fans"] = Gamer, ["fps"] = Gamer, ["gameadvisor"] = Gamer,
        ["gameboost"] = Gamer, ["gamefps"] = Gamer, ["gamelibrary"] = Gamer, ["gamesettings"] = Gamer, ["latency"] = Gamer,
        ["mbc-emu"] = Gamer, ["mbc-kbm"] = Gamer, ["mbc-light"] = Gamer, ["mbc-overview"] = Gamer, ["mbc-tune"] = Gamer,
        ["monitor-hz"] = Gamer, ["mouse-aim"] = Gamer, ["overlay-setup"] = Gamer, ["pad-analyzer"] = Gamer, ["pad-competitive"] = Gamer, ["pad-drivers"] = Gamer, ["pad-info"] = Gamer, ["pad-overclock"] = Gamer,
        ["pad-profiles"] = Gamer, ["padcalib"] = Gamer, ["profiles"] = Gamer, ["rgb"] = Gamer, ["stress"] = Gamer,
        ["tweaks-game"] = Gamer, ["usb-ports"] = Gamer, ["wallpaper"] = Gamer,
        // Creator: gravação, lives e editor de vídeos
        ["record"] = Creator, ["shorts"] = Creator, ["studio"] = Creator,
        // Técnico: tudo + ferramentas de assistência
        ["formatbackup"] = Tecnico, ["iso-create"] = Tecnico, ["netanalyzer"] = Tecnico, ["postinstall"] = Tecnico, ["reinstall"] = Tecnico, ["remote-tech"] = Tecnico, ["service"] = Tecnico,
    };

    /// <summary>
    /// O que abre sem conta (modo visitante): Painel (só analisar), Meu PC (ver), Suporte, Nossas Redes e Sobre, além da própria tela
    /// de conta. O resto mostra o cadeado "Crie sua conta grátis e ganhe 3 dias do plano Gamer".
    /// </summary>
    public static IReadOnlySet<string> VisitorPages { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "a11y", "about", "alltools", "cpu-detail", "diagnose", "disks", "gpu-detail", "home", "license", "monitor", "news", "ram-detail",
        "remote", "social", "suggest", "support", "sysinfo", "win11",
    };

    /// <summary>Páginas pagas (qualquer plano).</summary>
    public static IReadOnlyCollection<string> ProPages => (IReadOnlyCollection<string>)PagePlans.Keys;

    public static bool RequiresPro(string pageKey) => PagePlans.ContainsKey(pageKey);

    /// <summary>Plano mínimo da página, ou null se for gratuita.</summary>
    public static string? RequiredPlan(string pageKey) => PagePlans.TryGetValue(pageKey, out var plan) ? plan : null;

    /// <summary>
    /// Plano que vale agora para os recursos dentro das telas: sem aplicação de licença ou no teste grátis, o maior (Técnico);
    /// com licença válida, o plano dela; senão, Grátis (null).
    /// </summary>
    public static string? EffectivePlan(LicenseStatus status, bool enforce, bool trialActive = false) =>
        !enforce || trialActive ? Tecnico : status.IsValid && status.Payload is not null ? status.Plan : null;

    /// <summary>O plano dá acesso à página?</summary>
    public static bool PlanAllows(string? plan, string pageKey) => RequiredPlan(pageKey) is not { } need || Rank(plan) >= Rank(need);

    /// <summary>
    /// A página pode abrir? Sem aplicação de licença, no teste grátis ou com o plano certo, sim. Visitante (sem conta) só abre
    /// <see cref="VisitorPages"/>.
    /// </summary>
    public static bool IsAllowed(string pageKey, LicenseStatus status, bool enforce, bool trialActive = false, bool visitor = false)
    {
        if (!enforce) return true;
        if (visitor && !trialActive && status.Payload is null) return VisitorPages.Contains(pageKey);
        if (!RequiresPro(pageKey) || trialActive) return true;   // no teste grátis, tudo fica liberado
        if (!status.IsValid || status.Payload is null) return false;
        return PlanAllows(status.Plan, pageKey) || status.Payload.Features.Contains(pageKey);
    }
}

/// <summary>
/// O que cada plano libera dentro da Central de Rede. Grátis: Visão geral (painel ao vivo e diagnóstico) e Teste de internet.
/// Essencial, Gamer e Creator: também DNS, Otimizações de rede, Wi-Fi (sinal e canal da sua rede) e a reparação básica (limpar cache
/// DNS e renovar IP); Ping dos jogos a partir do Gamer. Técnico: tudo (Analisador, rotas, rede local e conflitos, Wi-Fi completo com
/// as redes vizinhas, Winsock, TCP/IP, relatórios e histórico).
/// </summary>
public static class NetCenterAccess
{
    /// <summary>Plano mínimo de cada seção, ou null se for do Grátis.</summary>
    public static string? SectionPlan(string section) => section switch
    {
        "dns" or "net-tweaks" or "repair" or "wifi" => FeaturePolicy.Essencial,
        "games" => FeaturePolicy.Gamer,
        "lan" or "netanalyzer" or "reports" or "route" => FeaturePolicy.Tecnico,
        _ => null,
    };

    /// <summary>Plano mínimo de cada correção.</summary>
    public static string RepairPlan(NetDiag.RepairKind kind) =>
        kind is NetDiag.RepairKind.FlushDns or NetDiag.RepairKind.RenewDhcp ? FeaturePolicy.Essencial : FeaturePolicy.Tecnico;

    /// <summary>Wi-Fi completo (redes vizinhas, gráfico de canais e melhor canal).</summary>
    public const string WifiFullPlan = FeaturePolicy.Tecnico;

    /// <summary>O plano libera o recurso que pede <paramref name="need"/>?</summary>
    public static bool Allows(string? plan, string? need) => need is null || FeaturePolicy.Rank(plan) >= FeaturePolicy.Rank(need);
}

/// <summary>
/// Quando o teste grátis ou o plano Pro acaba, as otimizações feitas com recursos Pro são desfeitas sozinhas (cada ajuste volta ao valor original
/// guardado) e só voltam a valer se a pessoa ativar o Pro. Só acontece com a aplicação de licença ligada e o servidor configurado.
/// </summary>
public static class LicenseExpiry
{
    /// <summary>Categorias de ajustes que pertencem às páginas Pro (desempenho, jogos e rede).</summary>
    public static IReadOnlySet<Tweaks.TweakCategory> ProTweakCategories { get; } = new HashSet<Tweaks.TweakCategory>
    {
        Tweaks.TweakCategory.Performance, Tweaks.TweakCategory.Gaming, Tweaks.TweakCategory.Network,
    };

    /// <summary>Tem algum plano pago (Essencial ou acima)?</summary>
    public static bool IsPro(LicenseStatus status) => status.IsValid && FeaturePolicy.Rank(status.Plan) > 0;

    public static bool ShouldRevert(bool enforce, bool configured, LicenseStatus status, bool trialActive = false) =>
        enforce && !trialActive && !IsPro(status) && configured;   // sem servidor configurado nunca desfaz
}

/// <summary>Configuração do licenciamento. O endereço do servidor e a chave pública vêm das configurações do usuário (ou de variáveis de ambiente, para testes) até serem fixados na versão de venda.</summary>
public sealed record LicenseConfig(bool Enforce, string ServerUrl, string PublicKey)
{
    public bool Configured => ServerUrl.Length > 0 && PublicKey.Length > 0;

    public static LicenseConfig From(Settings.AppSettings settings)
    {
        string url = Environment.GetEnvironmentVariable("MBX_LICENSE_URL") ?? (settings.LicenseServerUrl.Length > 0 ? settings.LicenseServerUrl : AppInfo.DefaultServerUrl);
        string key = Environment.GetEnvironmentVariable("MBX_LICENSE_PUBLIC_KEY") ?? (settings.LicensePublicKey.Length > 0 ? settings.LicensePublicKey : AppInfo.DefaultPublicKey);
        return new LicenseConfig(settings.LicenseEnforce || AppInfo.EnforceLicense, url.Trim().TrimEnd('/'), key.Trim());
    }
}
