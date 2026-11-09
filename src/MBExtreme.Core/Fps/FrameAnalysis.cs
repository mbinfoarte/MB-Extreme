using System.Globalization;
using System.Text;

namespace MBExtreme.Core.Fps;

/// <summary>Medidas de uma sessão de quadros. "Low" é o FPS dos piores quadros: a média dos x% mais lentos (como a NVIDIA) e o valor pelo qual se fica abaixo por x% do tempo (como o CapFrameX).</summary>
public sealed record FrameMetrics(
    int Frames, double DurationSeconds, double AverageFps, double MinFps, double MaxFps,
    double AverageFrameMs, double MedianFrameMs, double P99FrameMs, double MaxFrameMs, double StdDevMs,
    double Low1Fps, double Low01Fps, double Low1TimeFps, double Low01TimeFps,
    int Stutters, double StuttersPerMinute, int Pauses)
{
    /// <summary>O 0,1% pior precisa de muitos quadros para ser confiável (de uma rodada para outra ele muda muito).</summary>
    public bool Low01Reliable => Frames >= 2000;

    public static FrameMetrics Empty { get; } = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
}

/// <summary>Cálculo de FPS, "lows", engasgos e séries para gráfico a partir dos instantes em que o jogo apresentou cada quadro.</summary>
public static class FrameAnalysis
{
    /// <summary>Intervalos maiores que isto (alt+tab, tela de carregamento) saem das contas e contam como pausa.</summary>
    public const double MaxGapSeconds = 1.0;

    /// <summary>Transforma os instantes (segundos) em tempos de quadro (ms), cortando os primeiros <paramref name="warmupSeconds"/> e as pausas.</summary>
    public static (List<double> FrameMs, int Pauses) FrameTimes(IReadOnlyList<double> presentSeconds, double warmupSeconds = 0)
    {
        var result = new List<double>();
        int pauses = 0;
        if (presentSeconds.Count < 2) return (result, 0);
        double start = presentSeconds[0] + Math.Max(0, warmupSeconds);
        for (int i = 1; i < presentSeconds.Count; i++)
        {
            if (presentSeconds[i - 1] < start) continue;
            double delta = presentSeconds[i] - presentSeconds[i - 1];
            if (delta <= 0) continue;
            if (delta > MaxGapSeconds) { pauses++; continue; }
            result.Add(delta * 1000.0);
        }
        return (result, pauses);
    }

    public static FrameMetrics Compute(IReadOnlyList<double> frameMs, int pauses = 0)
    {
        int n = frameMs.Count;
        if (n == 0) return FrameMetrics.Empty;

        double totalMs = frameMs.Sum();
        double avgMs = totalMs / n;
        var ascending = frameMs.OrderBy(x => x).ToArray();
        double median = ascending[n / 2];
        double p99 = ascending[Math.Min(n - 1, (int)Math.Ceiling(0.99 * n) - 1)];
        double variance = frameMs.Sum(x => (x - avgMs) * (x - avgMs)) / n;
        double stdDev = Math.Sqrt(variance);

        int stutters = CountStutters(frameMs);
        double minutes = totalMs / 60000.0;
        return new FrameMetrics(
            n, totalMs / 1000.0, 1000.0 / avgMs, 1000.0 / ascending[^1], 1000.0 / Math.Max(0.001, ascending[0]),
            avgMs, median, p99, ascending[^1], stdDev,
            LowAverage(ascending, 0.01), LowAverage(ascending, 0.001), LowByTime(ascending, 0.01), LowByTime(ascending, 0.001),
            stutters, minutes > 0 ? stutters / minutes : 0, pauses);
    }

    /// <summary>FPS da média dos <paramref name="fraction"/> piores quadros (pelo menos um).</summary>
    public static double LowAverage(double[] ascendingMs, double fraction)
    {
        int count = Math.Max(1, (int)Math.Ceiling(ascendingMs.Length * fraction));
        double avg = ascendingMs.Skip(ascendingMs.Length - count).Average();
        return 1000.0 / avg;
    }

    /// <summary>Soma os quadros do mais lento para o mais rápido até chegar a <paramref name="fraction"/> do tempo total; o FPS do quadro em que isso acontece é o "low" por tempo.</summary>
    public static double LowByTime(double[] ascendingMs, double fraction)
    {
        double limit = ascendingMs.Sum() * fraction;
        double accumulated = 0;
        for (int i = ascendingMs.Length - 1; i >= 0; i--)
        {
            accumulated += ascendingMs[i];
            if (accumulated >= limit) return 1000.0 / ascendingMs[i];
        }
        return 1000.0 / ascendingMs[0];
    }

    /// <summary>
    /// Engasgo: quadro bem mais lento que a média dos 20 anteriores (o dobro e pelo menos 8 ms a mais). Quadros seguidos contam como um engasgo só.
    /// Compara com a média recente, e não com o quadro anterior, para não acusar uma cena que ficou realmente mais pesada.
    /// </summary>
    public static int CountStutters(IReadOnlyList<double> frameMs, int window = 20)
    {
        int count = 0;
        bool inside = false;
        for (int i = 0; i < frameMs.Count; i++)
        {
            int from = Math.Max(0, i - window);
            if (i - from < 5) { inside = false; continue; }
            double running = 0;
            for (int j = from; j < i; j++) running += frameMs[j];
            running /= i - from;
            bool spike = frameMs[i] > running * 2 && frameMs[i] - running > 8;
            if (spike && !inside) count++;
            inside = spike;
        }
        return count;
    }

    /// <summary>Índices (e tempos) dos engasgos para marcar no gráfico.</summary>
    public static List<int> StutterIndexes(IReadOnlyList<double> frameMs, int window = 20)
    {
        var list = new List<int>();
        bool inside = false;
        for (int i = 0; i < frameMs.Count; i++)
        {
            int from = Math.Max(0, i - window);
            if (i - from < 5) { inside = false; continue; }
            double running = 0;
            for (int j = from; j < i; j++) running += frameMs[j];
            running /= i - from;
            bool spike = frameMs[i] > running * 2 && frameMs[i] - running > 8;
            if (spike && !inside) list.Add(i);
            inside = spike;
        }
        return list;
    }

    /// <summary>Reduz a série para <paramref name="points"/> pontos guardando o maior valor de cada trecho (picos nunca somem).</summary>
    public static double[] DownsampleMax(IReadOnlyList<double> values, int points)
    {
        points = Math.Max(2, points);
        if (values.Count <= points) return values.ToArray();
        var result = new double[points];
        for (int p = 0; p < points; p++)
        {
            int from = (int)((long)p * values.Count / points);
            int to = Math.Max(from + 1, (int)((long)(p + 1) * values.Count / points));
            double max = double.MinValue;
            for (int i = from; i < Math.Min(to, values.Count); i++) max = Math.Max(max, values[i]);
            result[p] = max;
        }
        return result;
    }

    /// <summary>FPS de cada segundo da sessão (contagem de quadros apresentados).</summary>
    public static double[] FpsPerSecond(IReadOnlyList<double> presentSeconds, double warmupSeconds = 0)
    {
        if (presentSeconds.Count < 2) return Array.Empty<double>();
        double start = presentSeconds[0] + Math.Max(0, warmupSeconds);
        double end = presentSeconds[^1];
        int seconds = (int)Math.Floor(end - start);
        if (seconds <= 0) return Array.Empty<double>();
        var counts = new double[seconds];
        foreach (double t in presentSeconds)
        {
            int bucket = (int)Math.Floor(t - start);
            if (bucket >= 0 && bucket < seconds) counts[bucket]++;
        }
        return counts;
    }

    /// <summary>Curva de percentis: tempo de quadro (ms) em cada percentil. Uma curva reta é jogo fluido; um "taco de hóquei" no fim é engasgo.</summary>
    public static List<(double Percent, double Ms)> PercentileCurve(IReadOnlyList<double> frameMs, params double[] percents)
    {
        var result = new List<(double, double)>();
        if (frameMs.Count == 0) return result;
        var sorted = frameMs.OrderBy(x => x).ToArray();
        foreach (double p in percents.Length > 0 ? percents : new[] { 50, 75, 90, 95, 99, 99.9 })
            result.Add((p, sorted[Math.Clamp((int)Math.Ceiling(p / 100.0 * sorted.Length) - 1, 0, sorted.Length - 1)]));
        return result;
    }

    /// <summary>Quantos quadros caíram em cada faixa de tempo (ms). A última faixa junta tudo que passa do limite.</summary>
    public static int[] Histogram(IReadOnlyList<double> frameMs, double binMs, double maxMs)
    {
        int bins = Math.Max(1, (int)Math.Ceiling(maxMs / binMs));
        var counts = new int[bins];
        foreach (double ms in frameMs) counts[Math.Clamp((int)(ms / binMs), 0, bins - 1)]++;
        return counts;
    }

    public enum Smoothness { Excellent, Good, Irregular, Bad }

    /// <summary>Nota de fluidez: compara o "1% pior" com a média e conta os engasgos.</summary>
    public static Smoothness Rate(FrameMetrics m)
    {
        if (m.Frames < 50 || m.AverageFps <= 0) return Smoothness.Good;
        double ratio = m.Low1Fps / m.AverageFps;
        if (ratio >= 0.8 && m.StuttersPerMinute < 3) return Smoothness.Excellent;
        if (ratio >= 0.6 && m.StuttersPerMinute < 10) return Smoothness.Good;
        if (ratio >= 0.4) return Smoothness.Irregular;
        return Smoothness.Bad;
    }

    public static string Describe(Smoothness s) => s switch
    {
        Smoothness.Excellent => "Muito fluido: quadros chegam num ritmo constante.",
        Smoothness.Good => "Fluido, com poucas quedas.",
        Smoothness.Irregular => "Irregular: há quedas e engasgos que dá para perceber.",
        _ => "Engasgando bastante: a média de FPS esconde quedas fortes.",
    };

    /// <summary>Diferença percentual de B em relação a A (positivo = B maior).</summary>
    public static double Delta(double a, double b) => a <= 0 ? 0 : (b - a) / a * 100.0;

    public static string ToCsv(IReadOnlyList<double> frameMs)
    {
        var sb = new StringBuilder("frame,tempo_ms,fps_instantaneo\n");
        for (int i = 0; i < frameMs.Count; i++)
            sb.Append((i + 1).ToString(CultureInfo.InvariantCulture)).Append(',')
              .Append(frameMs[i].ToString("0.###", CultureInfo.InvariantCulture)).Append(',')
              .Append((1000.0 / frameMs[i]).ToString("0.#", CultureInfo.InvariantCulture)).Append('\n');
        return sb.ToString();
    }
}
