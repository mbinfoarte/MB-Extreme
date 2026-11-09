namespace MBExtreme.Core.Benchmark;

/// <summary>Nota de uma categoria (1000 = PC de referência) e quanto ela pesa na nota geral.</summary>
public sealed record CategoryScore(string Key, string Name, int Score, int WeightPercent);

/// <summary>
/// Notas por categoria e nota geral. Cada categoria compara as medidas com um PC de referência (um PC gamer
/// intermediário de 2022 = 1000); a nota geral é a média geométrica ponderada das categorias medidas, então
/// melhorar uma parte sobe a nota na proporção do peso dela.
/// </summary>
public static class BenchmarkScoring
{
    public const int Version = 3;

    public static readonly IReadOnlyList<(string Key, string Name, int Weight)> Categories = new[]
    {
        ("cpu1", "Processador · 1 núcleo", 12),
        ("cpuN", "Processador · todos os núcleos", 15),
        ("ram", "Memória RAM", 8),
        ("disk", "Armazenamento", 10),
        ("gpu", "Placa de vídeo (3D)", 20),
        ("game", "Simulação de jogo", 15),
        ("ai", "Inteligência artificial", 8),
        ("daily", "Uso do dia a dia", 12),
    };

    private static int Geo(params double[] ratios)
    {
        var valid = ratios.Where(r => r > 0 && double.IsFinite(r)).ToList();
        return valid.Count == 0 ? 0 : (int)Math.Round(1000 * Math.Exp(valid.Average(Math.Log)));
    }

    public static int Cpu1(IReadOnlyList<CpuTaskResult>? tasks) => tasks is { Count: > 0 } ? CpuTasks.Score(tasks) : 0;

    /// <summary>Todos os núcleos: a referência tem 8 linhas de execução.</summary>
    public static int CpuN(IReadOnlyList<CpuTaskResult>? tasks) => tasks is { Count: > 0 } ? (int)Math.Round(CpuTasks.Score(tasks) / 8.0) : 0;

    public static int Ram(double readGBps, double writeGBps, double copyGBps, double latencyNs) =>
        readGBps <= 0 ? 0 : Geo(readGBps / 30, writeGBps / 20, copyGBps / 15, latencyNs > 0 ? 90 / latencyNs : 0);

    public static int Disk(double seqRead, double seqWrite, double randRead, double iops, double randWrite, double accessUs) =>
        seqRead <= 0 ? 0 : Geo(seqRead / 2000, seqWrite / 1000, randRead / 40, iops / 60000, randWrite / 150, accessUs > 0 ? 100 / accessUs : 0);

    public static int Gpu(double reflectionsFps, double geometryFps, double studioMsps, long pixels)
    {
        double scale = pixels / Gpu3DBenchmark.ReferencePixels;
        return reflectionsFps <= 0 ? 0 : Geo(reflectionsFps * scale / 120, geometryFps / 500, studioMsps / 800);
    }

    public static int Game(double fps, double low1, long pixels)
    {
        double scale = pixels / Gpu3DBenchmark.ReferencePixels;
        return fps <= 0 ? 0 : Geo(fps * scale / 350, low1 > 0 ? low1 * scale / 220 : 0);
    }

    public static int Ai(double generateTps, double promptTps, double fp32Gflops, double int8Gops, double gpuGflops) =>
        generateTps <= 0 && fp32Gflops <= 0 ? 0 : Geo(generateTps / 150, promptTps / 4000, fp32Gflops / 400, int8Gops / 800, gpuGflops > 0 ? gpuGflops / 8000 : 0);

    /// <summary>Nota geral: média geométrica ponderada das categorias que foram medidas.</summary>
    public static int Overall(IReadOnlyList<CategoryScore> categories)
    {
        var valid = categories.Where(c => c.Score > 0).ToList();
        if (valid.Count == 0) return 0;
        double weights = valid.Sum(c => c.WeightPercent);
        return (int)Math.Round(Math.Exp(valid.Sum(c => c.WeightPercent * Math.Log(c.Score)) / weights));
    }

    /// <summary>Monta as categorias a partir de um resultado (só as que têm medida).</summary>
    public static IReadOnlyList<CategoryScore> For(BenchmarkResult r)
    {
        long pixels = 1920L * 1080;
        if (r.Resolution is { } res && res.Split('×') is [var w, var h] && int.TryParse(w, out int iw) && int.TryParse(h, out int ih)) pixels = (long)iw * ih;
        var values = new Dictionary<string, int>
        {
            ["cpu1"] = Cpu1(r.CpuTasksSingle),
            ["cpuN"] = CpuN(r.CpuTasksMulti),
            ["ram"] = Ram(r.MemoryReadGBps, r.MemoryWriteGBps, r.MemoryGBps, r.MemoryLatencyNs),
            ["disk"] = Disk(r.DiskReadMBps, r.DiskWriteMBps, r.DiskRandReadMBps, r.DiskRandQ8Iops, r.DiskRandWriteMBps, r.DiskAccessUs),
            ["gpu"] = Gpu(r.ReflectionsFps, r.GeometryFps, r.StudioMsps, pixels),
            ["game"] = Game(r.GameFps, r.GameLow1Fps, pixels),
            ["ai"] = Ai(r.AiGenerateTps, r.AiPromptTps, r.AiCpuGflops, r.AiInt8Gops, r.AiGpuGflops),
            ["daily"] = r.DailyScore,
        };
        return Categories.Where(c => values[c.Key] > 0).Select(c => new CategoryScore(c.Key, c.Name, values[c.Key], c.Weight)).ToList();
    }

    /// <summary>Diferença em % entre duas notas (0 quando uma delas não existe).</summary>
    public static double Delta(double before, double after) => before > 0 && after > 0 ? (after - before) / before * 100 : 0;

    /// <summary>Uma frase para o ganho total depois de uma otimização.</summary>
    public static string Verdict(double percent) => percent switch
    {
        >= 10 => "Ganho expressivo de desempenho!",
        >= 3 => "Ganho de desempenho.",
        > -3 => "Dentro da variação normal entre testes.",
        > -10 => "Um pouco mais lento: veja o que mudou (temperatura, energia, programas abertos).",
        _ => "Bem mais lento que antes: confira a temperatura, o plano de energia e os drivers.",
    };
}

/// <summary>Consumo durante o teste e a fonte recomendada.</summary>
public static class PowerAdvice
{
    private static readonly int[] Sizes = { 400, 450, 550, 650, 750, 850, 1000, 1200, 1600 };

    /// <summary>Pico do sistema = CPU + placa de vídeo + 60 W do resto (placa-mãe, memória, discos, ventoinhas). Fonte = pico x 1,4, arredondada para cima.</summary>
    public static (int SystemPeakWatts, int RecommendedWatts) Estimate(double cpuPeakWatts, double gpuPeakWatts)
    {
        int peak = (int)Math.Round(Math.Max(0, cpuPeakWatts) + Math.Max(0, gpuPeakWatts) + 60);
        int need = (int)Math.Ceiling(peak * 1.4);
        return (peak, Sizes.FirstOrDefault(s => s >= need, Sizes[^1]));
    }
}
