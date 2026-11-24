namespace MBExtreme.Core.Bottleneck;

public enum BottleneckKind { Balanced, Gpu, Cpu, Memory, Disk, Thermal }

/// <summary>Uma medida por segundo durante o jogo. Valores nulos = sensor indisponível.</summary>
public sealed record BottleneckSample(
    double Fps, double FrameTimeMs, double Low1PercentFps,
    double? CpuTotal, double? CpuMaxCore, double? CpuTemperature,
    double? GpuLoad, double? GpuTemperature,
    double RamPercent, double? DiskQueue);

public sealed record BottleneckReport(
    string Game, TimeSpan Duration, int Samples,
    double AverageFps, double WorstFps, double Low1PercentFps, int Stutters,
    IReadOnlyDictionary<BottleneckKind, double> Percent, BottleneckKind Primary,
    double TargetFps, IReadOnlyList<string> Advice);

public static class BottleneckAnalyzer
{
    public const double GpuLimit = 95, CoreLimit = 90, GpuIdle = 85, RamLimit = 90, DiskQueueLimit = 2;
    public const double CpuHot = 92, GpuHot = 86;

    public static string Label(BottleneckKind kind) => kind switch
    {
        BottleneckKind.Gpu => "Limite da placa de vídeo",
        BottleneckKind.Cpu => "Limite do processador",
        BottleneckKind.Memory => "Limite da memória RAM",
        BottleneckKind.Disk => "Limite do armazenamento",
        BottleneckKind.Thermal => "Limite térmico",
        _ => "Equilibrado",
    };

    /// <summary>Decide o gargalo de uma medida. Ordem: calor, memória, placa de vídeo, processador, disco.</summary>
    public static BottleneckKind Classify(BottleneckSample s)
    {
        if (s.CpuTemperature >= CpuHot || s.GpuTemperature >= GpuHot) return BottleneckKind.Thermal;
        if (s.RamPercent >= RamLimit) return BottleneckKind.Memory;
        double maxCore = s.CpuMaxCore ?? s.CpuTotal ?? 0;
        if (s.GpuLoad >= GpuLimit && maxCore < CoreLimit + 5) return BottleneckKind.Gpu;
        if (maxCore >= CoreLimit && (s.GpuLoad ?? 0) < GpuIdle) return BottleneckKind.Cpu;
        if (s.GpuLoad >= GpuLimit) return BottleneckKind.Gpu;
        bool stutter = IsStutter(s);
        if (stutter && s.DiskQueue >= DiskQueueLimit) return BottleneckKind.Disk;
        return BottleneckKind.Balanced;
    }

    /// <summary>Travada: o "1% pior" cai bem abaixo do FPS atual.</summary>
    public static bool IsStutter(BottleneckSample s) => s.Fps >= 10 && s.Low1PercentFps > 0 && s.Low1PercentFps < s.Fps * 0.6;

    public static BottleneckReport Analyze(string game, IReadOnlyList<BottleneckSample> samples, double targetFps)
    {
        var valid = samples.Where(s => s.Fps >= 1).ToList();
        if (valid.Count == 0)
            return new BottleneckReport(game, TimeSpan.FromSeconds(samples.Count), samples.Count, 0, 0, 0, 0,
                new Dictionary<BottleneckKind, double>(), BottleneckKind.Balanced, targetFps,
                new[] { "Não houve quadros medidos. Abra o jogo com o monitor ligado (precisa de administrador) e jogue por alguns minutos." });

        var counts = Enum.GetValues<BottleneckKind>().ToDictionary(k => k, _ => 0);
        foreach (var s in valid) counts[Classify(s)]++;
        var percent = counts.ToDictionary(kv => kv.Key, kv => 100.0 * kv.Value / valid.Count);

        var primary = percent.Where(kv => kv.Key != BottleneckKind.Balanced).OrderByDescending(kv => kv.Value).First();
        var main = primary.Value >= 20 ? primary.Key : BottleneckKind.Balanced;

        double avg = valid.Average(s => s.Fps);
        double low = valid.Average(s => s.Low1PercentFps > 0 ? s.Low1PercentFps : s.Fps);
        int stutters = valid.Count(IsStutter);
        var report = new BottleneckReport(game, TimeSpan.FromSeconds(samples.Count), valid.Count, avg, valid.Min(s => s.Fps), low, stutters,
            percent, main, targetFps, Array.Empty<string>());
        return report with { Advice = Advise(report, valid) };
    }

    public static IReadOnlyList<string> Advise(BottleneckReport r, IReadOnlyList<BottleneckSample> samples)
    {
        var advice = new List<string>();
        double gap = r.TargetFps > 0 && r.AverageFps > 0 ? r.TargetFps / r.AverageFps - 1 : 0;
        string meta = gap > 0.05 ? $" Para chegar a {r.TargetFps:0} fps seria preciso cerca de {gap:P0} a mais de desempenho nesse ponto." : "";

        switch (r.Primary)
        {
            case BottleneckKind.Gpu:
                advice.Add("A placa de vídeo é o limite (uso perto de 100% enquanto o processador sobra). Reduza resolução, sombras, reflexos e anti-aliasing, ou use DLSS/FSR se o jogo tiver." + meta);
                break;
            case BottleneckKind.Cpu:
                advice.Add("O processador é o limite (um núcleo no máximo enquanto a placa de vídeo sobra). Reduza distância de visão, quantidade de objetos/NPCs e feche programas em segundo plano; o Modo Jogo ajuda." + meta);
                break;
            case BottleneckKind.Memory:
                advice.Add("A memória RAM passou de 90%. Feche navegadores e programas, ligue a limpeza automática da memória e, se continuar, considere mais RAM.");
                break;
            case BottleneckKind.Disk:
                advice.Add("As travadas coincidem com a fila do disco. Se o jogo está num HD, mova-o para um SSD; confira também o antivírus escaneando durante o jogo.");
                break;
            case BottleneckKind.Thermal:
                advice.Add("Temperatura alta: o hardware reduz o clock para se proteger. Limpe poeira, melhore a ventilação, troque a pasta térmica e use o plano de energia Equilibrado se for notebook.");
                break;
            default:
                advice.Add(r.AverageFps >= r.TargetFps && r.TargetFps > 0
                    ? "Sem gargalo claro e a meta de FPS foi atingida. Está bom."
                    : "Sem gargalo claro. Se o FPS está preso em um valor fixo, confira V-Sync, limite de FPS do jogo e a taxa de atualização do monitor.");
                break;
        }

        if (r.Stutters > 0 && r.Samples > 0)
        {
            double pct = 100.0 * r.Stutters / r.Samples;
            if (pct >= 5) advice.Add($"{pct:0}% do tempo teve travadas (o 1% pior caiu muito abaixo da média). Rode também o Diagnóstico de latência para achar driver problemático.");
        }
        if (samples.Count > 0 && samples.All(s => s.GpuLoad is null))
            advice.Add("Os sensores da placa de vídeo não foram lidos, então o gargalo da GPU pode estar subestimado. Ative os sensores no Painel.");
        advice.Add("Isto é uma estimativa por regras. Quanto mais sessões do mesmo jogo, mais confiável fica a leitura.");
        return advice;
    }
}
