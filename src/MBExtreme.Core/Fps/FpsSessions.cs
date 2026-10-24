using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using MBExtreme.Core.Sensors;

namespace MBExtreme.Core.Fps;

/// <summary>Uma curva de sensor ao longo da sessão (um valor por segundo): uso, temperatura, clock, potência...</summary>
public sealed record SensorTrack(string Name, string Unit, double[] Values)
{
    public double Max => Values.Length == 0 ? 0 : Values.Max();

    public double Average => Values.Length == 0 ? 0 : Values.Average();
}

/// <summary>Sessão de análise de um jogo: medidas, série de tempos de quadro (reduzida), FPS por segundo e sensores.</summary>
public sealed record FpsSession(
    DateTime When, string Game, string? Label, FrameMetrics Metrics, double[] FrameMs, double[] FpsSeries, IReadOnlyList<SensorTrack> Sensors, double WarmupSeconds, string? Gpu = null);

/// <summary>Vai juntando as leituras dos sensores (uma por segundo) e entrega uma curva por sensor que tenha leitura.</summary>
public sealed class SensorRecorder
{
    private readonly List<SensorSnapshot> _readings = new();

    public int Count => _readings.Count;

    public void Add(SensorSnapshot snapshot)
    {
        if (snapshot.Available) _readings.Add(snapshot);
    }

    public IReadOnlyList<SensorTrack> Tracks()
    {
        var tracks = new List<SensorTrack>();
        void Add(string name, string unit, Func<SensorSnapshot, double?> pick)
        {
            var values = _readings.Select(pick).Where(v => v is not null).Select(v => v!.Value).ToArray();
            if (values.Length >= 2 && values.Any(v => v > 0)) tracks.Add(new SensorTrack(name, unit, values));
        }

        Add("Uso da GPU", "%", r => r.GpuLoad);
        Add("Temperatura da GPU", "°C", r => r.GpuTemperature);
        Add("Clock da GPU", "MHz", r => r.GpuClockMhz);
        Add("Potência da GPU", "W", r => r.GpuPowerWatts);
        Add("Uso da CPU", "%", r => r.CpuLoad);
        Add("Temperatura da CPU", "°C", r => r.CpuTemperature);
        Add("Clock da CPU", "MHz", r => r.CpuClockMhz);
        Add("Potência da CPU", "W", r => r.CpuPowerWatts);
        return tracks;
    }
}

public static class FpsSessionBuilder
{
    public const int GraphPoints = 600;

    /// <summary>Monta a sessão a partir dos instantes de quadros, cortando o aquecimento.</summary>
    public static FpsSession? Build(string game, string? label, IReadOnlyList<double> presentSeconds, IReadOnlyList<SensorTrack> sensors, double warmupSeconds, DateTime when, string? gpu = null)
    {
        var (frameMs, pauses) = FrameAnalysis.FrameTimes(presentSeconds, warmupSeconds);
        if (frameMs.Count < 30) return null;
        var metrics = FrameAnalysis.Compute(frameMs, pauses);
        return new FpsSession(when, game, label, metrics, FrameAnalysis.DownsampleMax(frameMs, GraphPoints),
            FrameAnalysis.FpsPerSecond(presentSeconds, warmupSeconds), sensors, warmupSeconds, gpu);
    }
}

public static class FpsSessionHistory
{
    private static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MBExtreme", "fps-sessions.json");

    public static IReadOnlyList<FpsSession> Load(string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            if (!File.Exists(path)) return Array.Empty<FpsSession>();
            return JsonSerializer.Deserialize<List<FpsSession>>(File.ReadAllText(path)) ?? new List<FpsSession>();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return Array.Empty<FpsSession>();
        }
    }

    public static void Add(FpsSession session, string? path = null, int keep = 30)
    {
        path ??= DefaultPath;
        var all = Load(path).Append(session).TakeLast(keep).ToList();
        Save(all, path);
    }

    public static void Remove(DateTime when, string? path = null)
    {
        path ??= DefaultPath;
        Save(Load(path).Where(s => s.When != when).ToList(), path);
    }

    private static void Save(List<FpsSession> all, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(all, new JsonSerializerOptions { WriteIndented = false }));
    }
}

/// <summary>Uma linha da comparação entre duas sessões.</summary>
public sealed record ComparisonRow(string Label, double A, double B, double DeltaPercent, bool HigherIsBetter, string Unit)
{
    public bool Improved => HigherIsBetter ? DeltaPercent > 1 : DeltaPercent < -1;

    public bool Worsened => HigherIsBetter ? DeltaPercent < -1 : DeltaPercent > 1;
}

public static class SessionComparison
{
    public static List<ComparisonRow> Compare(FrameMetrics a, FrameMetrics b)
    {
        ComparisonRow Row(string label, double x, double y, bool higherBetter, string unit) => new(label, x, y, FrameAnalysis.Delta(x, y), higherBetter, unit);
        return new List<ComparisonRow>
        {
            Row("FPS médio", a.AverageFps, b.AverageFps, true, "FPS"),
            Row("1% pior", a.Low1Fps, b.Low1Fps, true, "FPS"),
            Row("0,1% pior", a.Low01Fps, b.Low01Fps, true, "FPS"),
            Row("Tempo de quadro médio", a.AverageFrameMs, b.AverageFrameMs, false, "ms"),
            Row("Tempo de quadro P99", a.P99FrameMs, b.P99FrameMs, false, "ms"),
            Row("Engasgos por minuto", a.StuttersPerMinute, b.StuttersPerMinute, false, ""),
        };
    }
}

/// <summary>Relatório da sessão em uma página HTML sozinha (gráficos em SVG, sem internet), para guardar ou mandar para alguém.</summary>
public static class SessionReport
{
    private static string N(double v, string format = "0.#") => v.ToString(format, CultureInfo.InvariantCulture);

    public static string ToText(FpsSession s)
    {
        var m = s.Metrics;
        var sb = new StringBuilder();
        sb.AppendLine($"{s.Game} · {s.When:dd/MM/yyyy HH:mm}" + (s.Label is { Length: > 0 } ? $" · {s.Label}" : ""));
        sb.AppendLine($"FPS médio {m.AverageFps:0.#} · 1% pior {m.Low1Fps:0.#} · 0,1% pior {m.Low01Fps:0.#} (mínimo {m.MinFps:0.#}, máximo {m.MaxFps:0.#})");
        sb.AppendLine($"Tempo de quadro médio {m.AverageFrameMs:0.##} ms · P99 {m.P99FrameMs:0.##} ms · pior {m.MaxFrameMs:0.#} ms");
        sb.AppendLine($"Engasgos: {m.Stutters} ({m.StuttersPerMinute:0.#} por minuto) · {m.Frames} quadros em {m.DurationSeconds:0} s");
        sb.AppendLine(FrameAnalysis.Describe(FrameAnalysis.Rate(m)));
        foreach (var t in s.Sensors) sb.AppendLine($"{t.Name}: média {t.Average:0.#} {t.Unit}, máximo {t.Max:0.#} {t.Unit}");
        return sb.ToString();
    }

    private static string Svg(IReadOnlyList<double> values, string color, double? ceiling = null, int width = 760, int height = 140)
    {
        if (values.Count < 2) return "<p class=\"m\">Sem dados.</p>";
        double max = ceiling ?? Math.Max(1, values.Max() * 1.1);
        var points = new StringBuilder();
        for (int i = 0; i < values.Count; i++)
        {
            double x = i * (width - 1.0) / (values.Count - 1);
            double y = height - Math.Clamp(values[i] / max, 0, 1) * (height - 4) - 2;
            points.Append(N(x, "0.#")).Append(',').Append(N(y, "0.#")).Append(' ');
        }
        return $"<svg viewBox=\"0 0 {width} {height}\" width=\"100%\" preserveAspectRatio=\"none\"><polyline fill=\"none\" stroke=\"{color}\" stroke-width=\"1.5\" points=\"{points}\"/></svg><div class=\"m\">máx. {N(max)}</div>";
    }

    public static string ToHtml(FpsSession s)
    {
        var m = s.Metrics;
        string E(string text) => WebUtility.HtmlEncode(text);
        var sb = new StringBuilder();
        sb.Append("<!doctype html><html lang=\"pt-BR\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><title>Relatório de FPS</title><style>")
          .Append("body{background:#0d0d0f;color:#eee;font:14px Segoe UI,Arial,sans-serif;margin:0;padding:24px}h1{font-size:22px;margin:0 0 4px}.m{color:#8e8e96;font-size:12px}")
          .Append(".grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(160px,1fr));gap:10px;margin:16px 0}.c{background:#17171a;border:1px solid #2a2a2e;border-radius:8px;padding:10px 12px}")
          .Append(".c b{display:block;font-size:22px;color:#ffc400}.p{background:#17171a;border:1px solid #2a2a2e;border-radius:8px;padding:12px;margin:10px 0}h2{font-size:14px;margin:0 0 6px;color:#ffc400}</style></head><body>");
        sb.Append($"<h1>{E(s.Game)}</h1><div class=\"m\">{s.When:dd/MM/yyyy HH:mm}{(s.Label is { Length: > 0 } ? " · " + E(s.Label) : "")}{(s.Gpu is { Length: > 0 } ? " · " + E(s.Gpu) : "")} · aquecimento ignorado: {N(s.WarmupSeconds)} s</div>");
        sb.Append("<div class=\"grid\">");
        void Card(string title, string value, string detail = "") => sb.Append($"<div class=\"c\"><span class=\"m\">{E(title)}</span><b>{E(value)}</b><span class=\"m\">{E(detail)}</span></div>");
        Card("FPS médio", N(m.AverageFps), $"mín. {N(m.MinFps)} · máx. {N(m.MaxFps)}");
        Card("1% pior", N(m.Low1Fps), $"por tempo: {N(m.Low1TimeFps)}");
        Card("0,1% pior", N(m.Low01Fps), m.Low01Reliable ? $"por tempo: {N(m.Low01TimeFps)}" : "amostra pequena");
        Card("Tempo de quadro", N(m.AverageFrameMs, "0.##") + " ms", $"P99 {N(m.P99FrameMs, "0.##")} ms · pior {N(m.MaxFrameMs)} ms");
        Card("Engasgos", m.Stutters.ToString(CultureInfo.InvariantCulture), $"{N(m.StuttersPerMinute)} por minuto");
        Card("Duração", N(m.DurationSeconds, "0") + " s", $"{m.Frames} quadros");
        sb.Append("</div>");
        sb.Append($"<div class=\"p\"><h2>Fluidez</h2>{E(FrameAnalysis.Describe(FrameAnalysis.Rate(m)))}</div>");
        sb.Append($"<div class=\"p\"><h2>Tempo de cada quadro (ms), os picos são engasgos</h2>{Svg(s.FrameMs, "#ffc400")}</div>");
        sb.Append($"<div class=\"p\"><h2>FPS por segundo</h2>{Svg(s.FpsSeries, "#3ddc97")}</div>");
        foreach (var t in s.Sensors)
            sb.Append($"<div class=\"p\"><h2>{E(t.Name)} ({E(t.Unit)}) · média {N(t.Average)} · máx. {N(t.Max)}</h2>{Svg(t.Values, "#ff8a4d", t.Unit == "%" ? 100 : null, 760, 90)}</div>");
        sb.Append("<div class=\"m\">Gerado pelo MB Extreme. 1% pior = média dos 1% quadros mais lentos.</div></body></html>");
        return sb.ToString();
    }
}
