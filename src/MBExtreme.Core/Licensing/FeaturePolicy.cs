namespace MBExtreme.Core.Licensing;

/// <summary>Quais páginas do app exigem o plano pago. Enquanto <see cref="LicenseConfig.Enforce"/> for falso, nada é bloqueado (beta aberto).</summary>
public static class FeaturePolicy
{
    /// <summary>Páginas (chaves da navegação) do plano Pro. Tudo que não está aqui é gratuito.</summary>
    public static IReadOnlySet<string> ProPages { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "record", "shorts", "bench", "compare", "stress", "latency", "gameboost", "gameadvisor", "fps",
        "hardware", "bios", "recipe", "netanalyzer", "wifi", "drivers",
        "tweaks-perf", "tweaks-game", "net-tweaks", "duplicates", "largefiles", "shredder",
        "gamelibrary", "profiles",
    };

    public static bool RequiresPro(string pageKey) => ProPages.Contains(pageKey);

    /// <summary>A página pode abrir? Sem aplicação de licença ou com licença válida de plano pago, sim.</summary>
    public static bool IsAllowed(string pageKey, LicenseStatus status, bool enforce)
    {
        if (!enforce || !RequiresPro(pageKey)) return true;
        if (!status.IsValid || status.Payload is null) return false;
        return status.Plan is "pro" || status.Payload.Features.Contains(pageKey);
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

    public static bool IsPro(LicenseStatus status) => status.IsValid && status.Plan == "pro";

    public static bool ShouldRevert(bool enforce, bool configured, LicenseStatus status) => enforce && configured && !IsPro(status);
}

/// <summary>Configuração do licenciamento. O endereço do servidor e a chave pública vêm das configurações do usuário (ou de variáveis de ambiente, para testes) até serem fixados na versão de venda.</summary>
public sealed record LicenseConfig(bool Enforce, string ServerUrl, string PublicKey)
{
    public bool Configured => ServerUrl.Length > 0 && PublicKey.Length > 0;

    public static LicenseConfig From(Settings.AppSettings settings)
    {
        string url = Environment.GetEnvironmentVariable("MBX_LICENSE_URL") ?? (settings.LicenseServerUrl.Length > 0 ? settings.LicenseServerUrl : AppInfo.DefaultServerUrl);
        string key = Environment.GetEnvironmentVariable("MBX_LICENSE_PUBLIC_KEY") ?? (settings.LicensePublicKey.Length > 0 ? settings.LicensePublicKey : AppInfo.DefaultPublicKey);
        return new LicenseConfig(settings.LicenseEnforce, url.Trim().TrimEnd('/'), key.Trim());
    }
}
