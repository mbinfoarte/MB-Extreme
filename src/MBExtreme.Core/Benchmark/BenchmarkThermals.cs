using MBExtreme.Core.Fps;

namespace MBExtreme.Core.Benchmark;

public enum ThermalLevel { Unknown, Ok, Warm, Hot }

/// <summary>Como o PC se comportou no calor durante o teste: picos de temperatura e se o clock caiu do começo para o fim (throttling).</summary>
public sealed record ThermalReport(
    double? CpuPeak, double? GpuPeak, double? CpuClockDropPercent, double? GpuClockDropPercent, ThermalLevel Level, string Title, IReadOnlyList<string> Notes);

/// <summary>
/// Análise de temperatura do benchmark. Os limites seguem o que os fabricantes usam: processador por volta de 95–100 °C e placa de vídeo
/// por volta de 83–90 °C já começam a cortar o clock para se proteger. Uma queda de clock de 10% ou mais do começo para o fim da carga,
/// com temperatura alta, é o sinal de que o calor está limitando o desempenho.
/// </summary>
public static class BenchmarkThermals
{
    public const double CpuHot = 95, CpuWarm = 88, GpuHot = 87, GpuWarm = 80, DropAlert = 10;

    public static ThermalReport Analyze(IReadOnlyList<SensorTrack>? tracks)
    {
        double? Peak(string name) => Track(tracks, name) is { Values.Length: > 0 } t ? Math.Round(t.Values.Max(), 0) : null;
        double? cpu = Peak("Temperatura da CPU"), gpu = Peak("Temperatura da GPU");
        double? cpuDrop = ClockDrop(Track(tracks, "Clock da CPU"), Track(tracks, "Uso da CPU"));
        double? gpuDrop = ClockDrop(Track(tracks, "Clock da GPU"), Track(tracks, "Uso da GPU"));
        if (cpu is null && gpu is null)
            return new(null, null, cpuDrop, gpuDrop, ThermalLevel.Unknown, "Temperatura não lida",
                new[] { "Os sensores de temperatura não responderam (abra o MB Extreme como administrador para ler todos)." });

        var notes = new List<string>();
        var level = ThermalLevel.Ok;
        void Raise(ThermalLevel l) { if (l > level) level = l; }

        if (cpu is { } c)
        {
            if (c >= CpuHot) { Raise(ThermalLevel.Hot); notes.Add($"Processador chegou a {c:0} °C: está no limite e corta o clock para não queimar."); }
            else if (c >= CpuWarm) { Raise(ThermalLevel.Warm); notes.Add($"Processador chegou a {c:0} °C: quente, mas ainda dentro do limite."); }
            else notes.Add($"Processador chegou a {c:0} °C: temperatura boa.");
        }
        if (gpu is { } g)
        {
            if (g >= GpuHot) { Raise(ThermalLevel.Hot); notes.Add($"Placa de vídeo chegou a {g:0} °C: está no limite e reduz o desempenho para se proteger."); }
            else if (g >= GpuWarm) { Raise(ThermalLevel.Warm); notes.Add($"Placa de vídeo chegou a {g:0} °C: quente, mas ainda dentro do limite."); }
            else notes.Add($"Placa de vídeo chegou a {g:0} °C: temperatura boa.");
        }
        if (cpuDrop is >= DropAlert) { Raise(cpu is >= CpuWarm ? ThermalLevel.Hot : ThermalLevel.Warm); notes.Add($"O clock do processador caiu {cpuDrop:0}% do começo para o fim da carga: o calor (ou o limite de energia) está segurando o desempenho."); }
        if (gpuDrop is >= DropAlert) { Raise(gpu is >= GpuWarm ? ThermalLevel.Hot : ThermalLevel.Warm); notes.Add($"O clock da placa de vídeo caiu {gpuDrop:0}% do começo para o fim da carga: o calor está segurando o desempenho."); }

        if (level == ThermalLevel.Hot)
            notes.Add("O que ajuda: limpar a poeira das ventoinhas, trocar a pasta térmica, usar o notebook em superfície dura (ou com base refrigerada) e conferir a curva das ventoinhas.");
        string title = level switch
        {
            ThermalLevel.Hot => "Esquentou demais: o calor está limitando o desempenho",
            ThermalLevel.Warm => "Esquentou, mas aguentou",
            _ => "Temperatura sob controle durante todo o teste",
        };
        return new(cpu, gpu, cpuDrop, gpuDrop, level, title, notes);
    }

    /// <summary>Queda do clock (%) entre o começo e o fim da parte com carga (uso acima de 60%), pela mediana de cada ponta. Null sem dados.</summary>
    public static double? ClockDrop(SensorTrack? clock, SensorTrack? load)
    {
        if (clock is null || clock.Values.Length < 10) return null;
        var values = clock.Values;
        var busy = Enumerable.Range(0, values.Length)
            .Where(i => values[i] > 0 && (load is null || i >= load.Values.Length || load.Values[i] >= 60))
            .Select(i => values[i]).ToArray();
        if (busy.Length < 10) return null;
        int slice = Math.Max(3, busy.Length / 5);
        double start = Median(busy.Take(slice)), end = Median(busy.Skip(busy.Length - slice));
        if (start <= 0) return null;
        return Math.Round(Math.Max(0, (start - end) / start * 100), 1);
    }

    private static SensorTrack? Track(IReadOnlyList<SensorTrack>? tracks, string name) => tracks?.FirstOrDefault(t => t.Name == name);

    private static double Median(IEnumerable<double> values)
    {
        var sorted = values.OrderBy(v => v).ToArray();
        return sorted.Length == 0 ? 0 : sorted.Length % 2 == 1 ? sorted[sorted.Length / 2] : (sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2]) / 2;
    }
}
