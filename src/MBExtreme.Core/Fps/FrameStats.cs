namespace MBExtreme.Core.Fps;

/// <summary>Medida de quadros de um programa: FPS atual, tempo médio por quadro e o "1% pior".</summary>
public sealed record FpsReading(int ProcessId, double Fps, double FrameTimeMs, double Low1PercentFps);

/// <summary>Guarda os instantes das apresentações de quadros por processo e calcula as medidas.</summary>
public sealed class PresentTracker
{
    private const double WindowSeconds = 5.0;
    private readonly object _lock = new();
    private readonly Dictionary<int, List<double>> _times = new();

    public void Record(int processId, double seconds)
    {
        lock (_lock)
        {
            if (!_times.TryGetValue(processId, out var list)) _times[processId] = list = new List<double>();
            list.Add(seconds);
            int stale = list.FindIndex(t => t >= seconds - WindowSeconds);
            if (stale > 0) list.RemoveRange(0, stale);
        }
    }

    public IReadOnlyList<int> Processes()
    {
        lock (_lock) return _times.Keys.ToList();
    }

    public FpsReading? Read(int processId, double nowSeconds)
    {
        List<double> window;
        lock (_lock)
        {
            if (!_times.TryGetValue(processId, out var list)) return null;
            window = list.Where(t => t >= nowSeconds - WindowSeconds).ToList();
        }

        return Compute(processId, window, nowSeconds);
    }

    public static FpsReading? Compute(int processId, IReadOnlyList<double> times, double nowSeconds)
    {
        if (times.Count < 3) return null;

        var lastSecond = times.Where(t => t >= nowSeconds - 1.0).ToList();
        double fps = lastSecond.Count;

        var deltas = new List<double>();
        for (int i = 1; i < times.Count; i++) deltas.Add((times[i] - times[i - 1]) * 1000.0);
        double average = deltas.Average();

        // 1% pior: a média dos 1% maiores tempos de quadro (pelo menos um quadro).
        var worst = deltas.OrderByDescending(d => d).Take(Math.Max(1, deltas.Count / 100)).Average();
        return new FpsReading(processId, fps, average, worst > 0 ? 1000.0 / worst : 0);
    }
}
