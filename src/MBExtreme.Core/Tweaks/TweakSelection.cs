using MBExtreme.Core.Apps;

namespace MBExtreme.Core.Tweaks;

/// <summary>Grupos de seleção das páginas de ajustes.</summary>
public enum TweakGroup { Recommended, Safe, Moderate, Advanced, All }

/// <summary>Filtros por objetivo e busca da tela Otimizações.</summary>
public static class TweakFilter
{
    public static readonly IReadOnlyList<(TweakGoal Goal, string Title)> Goals = new[]
    {
        (TweakGoal.None, "Todos"),
        (TweakGoal.FpsLatency, "FPS e latência"),
        (TweakGoal.Network, "Rede e ping"),
        (TweakGoal.QualityOfLife, "Qualidade de vida"),
        (TweakGoal.Privacy, "Privacidade e segurança"),
    };

    /// <summary>O ajuste passa no objetivo (None = todos) e contém todas as palavras da busca (sem diferenciar acentos e maiúsculas).</summary>
    public static bool Matches(Tweak t, TweakGoal goal, string? search)
    {
        if (goal != TweakGoal.None && (t.Goals & goal) == 0) return false;
        if (string.IsNullOrWhiteSpace(search)) return true;
        string haystack = Fold($"{t.Title} {t.Description} {t.Effect} {t.Details} {t.Badge}");
        return search.Split(' ', StringSplitOptions.RemoveEmptyEntries).All(w => haystack.Contains(Fold(w), StringComparison.Ordinal));
    }

    /// <summary>"x de y ativas".</summary>
    public static string Counter(int active, int total) => $"{active} de {total} ativa{(total == 1 ? "" : "s")}";

    private static string Fold(string text)
    {
        var normalized = text.ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD);
        var sb = new System.Text.StringBuilder(normalized.Length);
        foreach (char c in normalized)
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark) sb.Append(c);
        return sb.ToString();
    }
}

/// <summary>Perfis do assistente de pós-formatação.</summary>
public enum PostInstallProfile { Light, Balanced, Extreme }

public static class TweakSelection
{
    public static string Title(TweakGroup g) => g switch
    {
        TweakGroup.Recommended => "Recomendados",
        TweakGroup.Safe => "Seguros",
        TweakGroup.Moderate => "Moderados",
        TweakGroup.Advanced => "Avançados",
        _ => "Todos",
    };

    /// <summary>Recomendados = a seleção pronta; Seguros = risco baixo; Moderados = risco médio; Avançados = risco alto; Todos = tudo.</summary>
    public static bool InGroup(Tweak t, TweakGroup g) => g switch
    {
        TweakGroup.Recommended => t.Recommended,
        TweakGroup.Safe => t.Risk == TweakRisk.Low,
        TweakGroup.Moderate => t.Risk == TweakRisk.Medium,
        TweakGroup.Advanced => t.Risk == TweakRisk.High,
        _ => true,
    };

    public static IReadOnlyList<Tweak> Select(IEnumerable<Tweak> tweaks, TweakGroup g) => tweaks.Where(t => InGroup(t, g)).ToList();
}

/// <summary>O que cada perfil do pós-formatação marca de saída (você ainda pode marcar e desmarcar cada item).</summary>
public static class PostInstallPlan
{
    public static string Title(PostInstallProfile p) => p switch
    {
        PostInstallProfile.Light => "Leve",
        PostInstallProfile.Balanced => "Equilibrado",
        _ => "Extremo",
    };

    public static string Description(PostInstallProfile p) => p switch
    {
        PostInstallProfile.Light => "Só o recomendado e de risco baixo. Tira propaganda e telemetria sem mexer em nada que você possa sentir falta.",
        PostInstallProfile.Balanced => "Tudo de risco baixo e os aplicativos que quase ninguém usa. Bom para a maioria dos PCs.",
        _ => "Inclui ajustes de risco médio e apps que algumas pessoas usam (ex.: Vincular ao Celular). Leia os avisos: alguns recursos do Windows deixam de funcionar. Risco alto nunca é marcado sozinho.",
    };

    /// <summary>Ajustes marcados pelo perfil. Risco alto nunca entra automaticamente.</summary>
    public static IReadOnlyList<Tweak> Tweaks(PostInstallProfile p, IEnumerable<Tweak> available) => available.Where(t => p switch
    {
        PostInstallProfile.Light => t.Recommended && t.Risk == TweakRisk.Low,
        PostInstallProfile.Balanced => t.Risk == TweakRisk.Low,
        _ => t.Risk is TweakRisk.Low or TweakRisk.Medium,
    }).ToList();

    /// <summary>Aplicativos marcados para remover. "Perigosos" nunca entram automaticamente.</summary>
    public static IReadOnlyList<BloatApp> Apps(PostInstallProfile p, IEnumerable<BloatApp> installed) => installed.Where(a => p switch
    {
        PostInstallProfile.Light => false,
        PostInstallProfile.Balanced => a.Level == AppRemovalLevel.Safe,
        _ => a.Level is AppRemovalLevel.Safe or AppRemovalLevel.Caution,
    }).ToList();
}
