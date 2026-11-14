using System.Text.Json;

namespace MBExtreme.Core.Games;

/// <summary>Uma sessão de jogo medida. Optimized = o jogo estava otimizado pelo MB Extreme quando foi medido.</summary>
public sealed class PerfSession
{
    public DateTime StartedAt { get; set; }
    public bool Optimized { get; set; }
    public int Seconds { get; set; }
    public double AvgFps { get; set; }
    public double Low1Fps { get; set; }
    public double MinFps { get; set; }
    public double MaxFps { get; set; }
    public double AvgFrameMs { get; set; }
    /// <summary>0,1% pior (pelos quadros capturados; 0 = sem captura de quadros).</summary>
    public double Low01Fps { get; set; }
    /// <summary>Travadas: quadros muito mais lentos que os vizinhos.</summary>
    public int Stutters { get; set; }
    public double? CpuAvg { get; set; }
    public double? GpuAvg { get; set; }
    /// <summary>Quem segurou o FPS (rótulo) e a primeira dica.</summary>
    public string Bottleneck { get; set; } = "";
    public string Advice { get; set; } = "";
    /// <summary>Tempo de quadro (ms) resumido para o gráfico (até 240 pontos).</summary>
    public List<double> FrameMs { get; set; } = new();

    /// <summary>Resume uma série longa em até <paramref name="points"/> pontos, guardando o pior de cada trecho (os picos aparecem).</summary>
    public static List<double> Downsample(IReadOnlyList<double> values, int points = 240)
    {
        if (values.Count <= points) return values.Select(v => Math.Round(v, 2)).ToList();
        var result = new List<double>(points);
        double step = values.Count / (double)points;
        for (int i = 0; i < points; i++)
        {
            int a = (int)(i * step), b = Math.Min(values.Count, (int)((i + 1) * step));
            double max = 0;
            for (int j = a; j < b; j++) max = Math.Max(max, values[j]);
            result.Add(Math.Round(max, 2));
        }
        return result;
    }
}

/// <summary>Acumula as leituras de FPS de uma sessão (uma por segundo) e resume.</summary>
public sealed class SessionRecorder
{
    public const int MinimumSeconds = 20;
    private readonly List<double> _fps = new();
    private readonly List<double> _low = new();
    private readonly List<double> _frameMs = new();

    public int Count => _fps.Count;

    public void Add(double fps, double low1Fps, double frameMs)
    {
        if (fps < 1 || double.IsNaN(fps) || double.IsInfinity(fps)) return;
        _fps.Add(fps);
        _low.Add(double.IsFinite(low1Fps) && low1Fps > 0 ? low1Fps : fps);
        _frameMs.Add(double.IsFinite(frameMs) ? frameMs : 0);
    }

    /// <summary>Resumo da sessão, ou null se foi curta demais para valer como medida.</summary>
    public PerfSession? Finish(bool optimized, DateTime startedAt)
    {
        if (_fps.Count < MinimumSeconds) return null;
        // "1% pior": média dos 1% menores FPS medidos na sessão (pelo menos 1 leitura)
        var worst = _fps.OrderBy(x => x).Take(Math.Max(1, _fps.Count / 100)).Average();
        return new PerfSession
        {
            StartedAt = startedAt, Optimized = optimized, Seconds = _fps.Count,
            AvgFps = Math.Round(_fps.Average(), 1), Low1Fps = Math.Round(Math.Min(worst, _low.Average()), 1),
            MinFps = Math.Round(_fps.Min(), 1), MaxFps = Math.Round(_fps.Max(), 1), AvgFrameMs = Math.Round(_frameMs.Average(), 2),
        };
    }
}

public enum Verdict { NotEnoughData, Better, Same, Worse }

public sealed record PerfComparison(PerfSession? Before, PerfSession? After, Verdict Verdict, double AvgChangePercent, double LowChangePercent, string Message, IReadOnlyList<string> Warnings);

public static class PerfCompare
{
    /// <summary>Compara a última sessão sem otimização (antes) com a última otimizada (depois), que veio depois dela.</summary>
    public static PerfComparison Build(IReadOnlyList<PerfSession> sessions)
    {
        var ordered = sessions.OrderBy(s => s.StartedAt).ToList();
        var after = ordered.LastOrDefault(s => s.Optimized);
        var before = after is null ? ordered.LastOrDefault(s => !s.Optimized) : ordered.LastOrDefault(s => !s.Optimized && s.StartedAt < after.StartedAt);
        if (before is null && after is not null) before = null;

        if (before is null || after is null)
            return new PerfComparison(before, after, Verdict.NotEnoughData, 0, 0,
                before is null && after is null ? "Jogue uma vez para medir o desempenho atual (antes)." :
                after is null ? "Medida de antes guardada. Otimize o jogo e jogue de novo para ver o depois." :
                "Falta uma medida de antes: desfaça a otimização, jogue uns minutos e depois otimize e jogue de novo.", Array.Empty<string>());

        double avg = Percent(before.AvgFps, after.AvgFps);
        double low = Percent(before.Low1Fps, after.Low1Fps);
        var warnings = new List<string>();
        if (Math.Abs(before.Seconds - after.Seconds) > Math.Max(before.Seconds, after.Seconds) * 0.5)
            warnings.Add("As duas sessões têm durações bem diferentes; a comparação é menos confiável.");
        if (Math.Min(before.Seconds, after.Seconds) < 60)
            warnings.Add("Alguma sessão durou menos de 1 minuto. Jogue o mesmo trecho por 2 a 3 minutos.");
        if (Math.Abs(avg) < 3 && Math.Abs(low) < 3) warnings.Add("A diferença está dentro da variação normal entre partidas.");

        var verdict = avg >= 3 || (avg > -3 && low >= 5) ? Verdict.Better : avg <= -3 || low <= -5 ? Verdict.Worse : Verdict.Same;
        string message = verdict switch
        {
            Verdict.Better => $"Ganho: FPS médio {before.AvgFps:0} → {after.AvgFps:0} ({avg:+0.#;-0.#}%), 1% pior {before.Low1Fps:0} → {after.Low1Fps:0} ({low:+0.#;-0.#}%).",
            Verdict.Worse => $"Piorou: FPS médio {before.AvgFps:0} → {after.AvgFps:0} ({avg:+0.#;-0.#}%). Desfaça a otimização e compare de novo; a cena pode ter sido diferente.",
            _ => $"Sem diferença relevante: FPS médio {before.AvgFps:0} → {after.AvgFps:0} ({avg:+0.#;-0.#}%).",
        };
        return new PerfComparison(before, after, verdict, avg, low, message, warnings);
    }

    private static double Percent(double before, double after) => before <= 0 ? 0 : Math.Round((after - before) / before * 100, 1);
}

/// <summary>Guarda as sessões medidas por jogo em %AppData%\MBExtreme\performance.json (últimas 20 de cada jogo).</summary>
public sealed class PerfStore
{
    private const int Keep = 20;
    private readonly string _path;
    private readonly object _lock = new();

    public PerfStore(string? path = null) =>
        _path = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MBExtreme", "performance.json");

    public IReadOnlyList<PerfSession> Get(string gameId)
    {
        lock (_lock) return Load().GetValueOrDefault(gameId) ?? new List<PerfSession>();
    }

    public void Add(string gameId, PerfSession session)
    {
        lock (_lock)
        {
            var all = Load();
            var list = all.GetValueOrDefault(gameId) ?? new List<PerfSession>();
            list.Add(session);
            all[gameId] = list.OrderBy(s => s.StartedAt).TakeLast(Keep).ToList();
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
            File.WriteAllText(_path, JsonSerializer.Serialize(all));
        }
    }

    public void Clear(string gameId)
    {
        lock (_lock)
        {
            var all = Load();
            if (all.Remove(gameId)) File.WriteAllText(_path, JsonSerializer.Serialize(all));
        }
    }

    private Dictionary<string, List<PerfSession>> Load()
    {
        try { return File.Exists(_path) ? JsonSerializer.Deserialize<Dictionary<string, List<PerfSession>>>(File.ReadAllText(_path)) ?? new() : new(); }
        catch (Exception ex) when (ex is IOException or JsonException) { return new(); }
    }
}
