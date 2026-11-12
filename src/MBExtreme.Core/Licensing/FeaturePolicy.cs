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

    /// <summary>Plano mínimo de cada página paga. Tudo que não está aqui é gratuito.</summary>
    public static IReadOnlyDictionary<string, string> PagePlans { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        // Essencial: otimização e manutenção
        ["hardware"] = Essencial, ["bios"] = Essencial, ["recipe"] = Essencial, ["netanalyzer"] = Essencial, ["wifi"] = Essencial,
        ["drivers"] = Essencial, ["tweaks-perf"] = Essencial, ["net-tweaks"] = Essencial, ["duplicates"] = Essencial,
        ["largefiles"] = Essencial, ["shredder"] = Essencial,
        // Gamer: jogos, desempenho e benchmark
        ["gamefps"] = Gamer, ["bench"] = Gamer, ["compare"] = Gamer, ["stress"] = Gamer, ["latency"] = Gamer, ["gameboost"] = Gamer,
        ["gameadvisor"] = Gamer, ["fps"] = Gamer, ["tweaks-game"] = Gamer, ["gamelibrary"] = Gamer, ["profiles"] = Gamer,
        ["padcalib"] = Gamer, ["wallpaper"] = Gamer,
        // Creator: estúdio de live e gravação
        ["studio"] = Creator, ["record"] = Creator, ["shorts"] = Creator,
        // Técnico: ferramentas de assistência
        ["postinstall"] = Tecnico, ["formatbackup"] = Tecnico,
    };

    /// <summary>Páginas pagas (qualquer plano).</summary>
    public static IReadOnlyCollection<string> ProPages => (IReadOnlyCollection<string>)PagePlans.Keys;

    public static bool RequiresPro(string pageKey) => PagePlans.ContainsKey(pageKey);

    /// <summary>Plano mínimo da página, ou null se for gratuita.</summary>
    public static string? RequiredPlan(string pageKey) => PagePlans.TryGetValue(pageKey, out var plan) ? plan : null;

    /// <summary>O plano dá acesso à página?</summary>
    public static bool PlanAllows(string? plan, string pageKey) => RequiredPlan(pageKey) is not { } need || Rank(plan) >= Rank(need);

    /// <summary>A página pode abrir? Sem aplicação de licença, no teste grátis ou com o plano certo, sim.</summary>
    public static bool IsAllowed(string pageKey, LicenseStatus status, bool enforce, bool trialActive = false)
    {
        if (!enforce || !RequiresPro(pageKey) || trialActive) return true;   // no teste grátis, tudo fica liberado
        if (!status.IsValid || status.Payload is null) return false;
        return PlanAllows(status.Plan, pageKey) || status.Payload.Features.Contains(pageKey);
    }
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
        enforce && !trialActive && !IsPro(status) && (configured || AppInfo.EnforceLicense);
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
