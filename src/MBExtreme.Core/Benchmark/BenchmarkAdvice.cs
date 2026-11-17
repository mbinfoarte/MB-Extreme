using System.Text.RegularExpressions;

namespace MBExtreme.Core.Benchmark;

/// <summary>Uma dica do painel lateral do benchmark. <see cref="Important"/> = afeta a nota (aparece em destaque).</summary>
public sealed record BenchmarkTip(string Title, string Detail, bool Important = false);

/// <summary>Dicas para um resultado confiável e a comparação automática com as suas medições.</summary>
public static partial class BenchmarkAdvice
{
    [GeneratedRegex(@"\(([^()]+)\)\s*\*?\s*$", RegexOptions.Multiline)]
    private static partial Regex PlanNamePattern();

    /// <summary>Nome do plano na saída de "powercfg /getactivescheme" ("... (Equilibrado)").</summary>
    public static string? PowerPlanName(string? powercfgOutput)
    {
        if (string.IsNullOrWhiteSpace(powercfgOutput)) return null;
        var match = PlanNamePattern().Match(powercfgOutput.Trim());
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    /// <summary>Nome do plano de energia ativo (Windows), ou null se não deu para ler.</summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public static string? ActivePowerPlan()
    {
        try { return PowerPlanName(MBExtreme.Core.Windows.Shell.RunOem("powercfg.exe", "/getactivescheme").Output); }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or IOException) { return null; }
    }

    private static bool IsPerformancePlan(string plan) =>
        plan.Contains("desempenho", StringComparison.OrdinalIgnoreCase) || plan.Contains("performance", StringComparison.OrdinalIgnoreCase)
        || plan.Contains("MB Extreme", StringComparison.OrdinalIgnoreCase);

    public static IReadOnlyList<BenchmarkTip> Tips(bool laptop, bool? onBattery, string? powerPlan, BenchmarkMode mode, int openWindows = 0)
    {
        var tips = new List<BenchmarkTip>();
        if (onBattery == true)
            tips.Add(new BenchmarkTip("Ligue o carregador", "Na bateria o Windows segura o processador e a placa de vídeo: a nota sai bem menor.", true));
        if (powerPlan is { Length: > 0 } plan && !IsPerformancePlan(plan))
            tips.Add(new BenchmarkTip("Plano de energia: " + plan, "Para a nota máxima use \"Alto desempenho\" (ou o perfil Jogo do MB Extreme). Para comparar, use sempre o mesmo plano.", true));
        if (openWindows > 6)
            tips.Add(new BenchmarkTip($"{openWindows} janelas abertas", "Feche o navegador, jogos e programas pesados: eles dividem o PC com o teste.", true));
        else
            tips.Add(new BenchmarkTip("Feche outros programas", "Navegador, jogos e downloads dividem o PC com o teste e baixam a nota."));
        if (laptop)
            tips.Add(new BenchmarkTip("Notebook em superfície dura", "Em cima de cama ou almofada as entradas de ar fecham, ele esquenta e perde velocidade."));
        tips.Add(new BenchmarkTip("Espere esfriar entre os testes", "Rodar um atrás do outro esquenta o PC e a segunda nota tende a sair menor."));
        tips.Add(mode == BenchmarkMode.Preciso
            ? new BenchmarkTip("Compare no mesmo modo", "O Preciso é o mais estável: use-o para medir o antes e o depois de uma mudança.")
            : new BenchmarkTip("Para comparar, use o Preciso", "Rápido e Padrão variam mais de uma rodada para outra. Compare sempre no mesmo modo e resolução."));
        return tips;
    }

    /// <summary>Uma frase que compara a medição com as anteriores do mesmo PC (melhor, média e a última).</summary>
    public static string Compare(BenchmarkResult current, IReadOnlyList<BenchmarkResult> history)
    {
        var others = history.Where(h => h.Score > 0 && h.When != current.When).ToList();
        if (current.Score <= 0) return "Medição parcial: rode o teste completo para ter a nota geral.";
        if (others.Count == 0) return "Primeira medição: ela vira a referência. Rode de novo depois de uma mudança para comparar.";
        var sameMode = others.Where(h => h.Mode == current.Mode).ToList();
        var pool = sameMode.Count > 0 ? sameMode : others;
        int best = pool.Max(h => h.Score);
        double average = pool.Average(h => h.Score);
        double vsAverage = (current.Score - average) / average * 100;
        string scope = sameMode.Count > 0 && current.Mode is { Length: > 0 } m ? $" no modo {m}" : "";
        if (current.Score >= best) return $"Seu melhor resultado{scope} até agora ({vsAverage:+0.#;-0.#;0}% sobre a sua média).";
        double belowBest = (best - current.Score) * 100.0 / best;
        if (Math.Abs(vsAverage) <= 3) return $"Dentro do normal deste PC{scope}: {belowBest:0.#}% abaixo do seu melhor.";
        return vsAverage > 0
            ? $"{vsAverage:0.#}% acima da sua média{scope} e {belowBest:0.#}% abaixo do seu melhor."
            : $"{-vsAverage:0.#}% abaixo da sua média{scope}. Veja as dicas: plano de energia, bateria e programas abertos.";
    }
}
