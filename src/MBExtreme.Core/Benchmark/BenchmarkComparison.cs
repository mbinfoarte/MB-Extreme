namespace MBExtreme.Core.Benchmark;

public enum DeltaVerdict { Same, Better, Worse }

public sealed record MetricDelta(string Name, string Unit, double Before, double After, bool HigherIsBetter, double Percent, DeltaVerdict Verdict);

/// <summary>Compara duas medições. Diferenças dentro da variação normal entre rodadas (padrão 3%) contam como "igual".</summary>
public static class BenchmarkComparison
{
    public static IReadOnlyList<MetricDelta> Compare(BenchmarkResult before, BenchmarkResult after, double noisePercent = 3)
    {
        var list = new List<MetricDelta>();

        void Add(string name, string unit, double a, double b, bool higherBetter = true)
        {
            if (a <= 0 || b <= 0) return;
            double percent = (b - a) / a * 100;
            double effect = higherBetter ? percent : -percent;
            var verdict = Math.Abs(percent) <= noisePercent ? DeltaVerdict.Same : effect > 0 ? DeltaVerdict.Better : DeltaVerdict.Worse;
            list.Add(new MetricDelta(name, unit, a, b, higherBetter, percent, verdict));
        }

        Add("Pontuação geral", "", before.Score, after.Score);
        Add("CPU, 1 núcleo", "Mop/s", before.CpuSingleMops, after.CpuSingleMops);
        Add("CPU, todos os núcleos", "Mop/s", before.CpuMultiMops, after.CpuMultiMops);
        Add("CPU vetorial", "GFLOPS", before.CpuSimdGflops, after.CpuSimdGflops);
        Add("Criptografia (AES)", "MB/s", before.CryptoMBps, after.CryptoMBps);
        Add("Compressão", "MB/s", before.CompressMBps, after.CompressMBps);
        Add("Memória: leitura", "GB/s", before.MemoryReadGBps, after.MemoryReadGBps);
        Add("Memória: escrita", "GB/s", before.MemoryWriteGBps, after.MemoryWriteGBps);
        Add("Memória: cópia", "GB/s", before.MemoryGBps, after.MemoryGBps);
        Add("Memória: latência", "ns", before.MemoryLatencyNs, after.MemoryLatencyNs, higherBetter: false);
        Add("Disco: gravação", "MB/s", before.DiskWriteMBps, after.DiskWriteMBps);
        Add("Disco: leitura", "MB/s", before.DiskReadMBps, after.DiskReadMBps);
        Add("Processador, 1 núcleo", "pts", before.CpuSingleScore, after.CpuSingleScore);
        Add("Processador, todos os núcleos", "pts", before.CpuMultiScore, after.CpuMultiScore);
        Add("Inteligência artificial", "pts", before.AiScore, after.AiScore);
        Add("Uso do dia a dia", "pts", before.DailyScore, after.DailyScore);
        Add("GPU 3D", "pontos", before.Gpu3DScore, after.Gpu3DScore);
        Add("Cena 1 · Reflexos", "FPS", before.ReflectionsFps, after.ReflectionsFps);
        Add("Cena 2 · Geometria", "FPS", before.GeometryFps, after.GeometryFps);
        Add("Cena 3 · Estúdio", "M amostras/s", before.StudioMsps, after.StudioMsps);
        Add("Simulação de jogo", "FPS", before.GameFps, after.GameFps);
        return list;
    }

    public static string Summary(IReadOnlyList<MetricDelta> deltas)
    {
        if (deltas.Count == 0) return "Não há métricas em comum para comparar.";
        int better = deltas.Count(d => d.Verdict == DeltaVerdict.Better);
        int worse = deltas.Count(d => d.Verdict == DeltaVerdict.Worse);
        if (better == 0 && worse == 0) return "Sem diferença além da variação normal entre rodadas. Esse ajuste não mudou o desempenho medido.";
        if (worse == 0) return $"Melhorou em {better} métrica(s), sem piora.";
        if (better == 0) return $"Piorou em {worse} métrica(s), sem melhora. Considere desfazer o ajuste.";
        return $"Melhorou em {better} e piorou em {worse} métrica(s). Veja qual importa mais para você.";
    }
}
