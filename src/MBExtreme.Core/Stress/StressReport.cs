using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using MBExtreme.Core.Fps;

namespace MBExtreme.Core.Stress;

/// <summary>Uma rodada do teste de estresse (CPU, memória e/ou GPU) com as curvas dos sensores, para o relatório e o histórico.</summary>
public sealed record StressRun(
    DateTime When, TimeSpan Elapsed, bool Cpu, bool Memory, bool Gpu, string? GpuScene, bool Completed, string Reason,
    long CpuMemoryErrors, long GpuErrors, double? MaxCpuTemperature, double? MaxGpuTemperature, double GpuAverageFps, double GpuMinFps,
    string? CpuName, string? GpuName, IReadOnlyList<SensorTrack> Tracks, string? Label = null)
{
    public long Errors => CpuMemoryErrors + GpuErrors;

    /// <summary>Estável: terminou o tempo sem nenhum erro de cálculo e sem a placa de vídeo ser reiniciada.</summary>
    public bool Stable => Completed && Errors == 0;

    public string Verdict => Stable
        ? $"Estável: {Elapsed:mm\\:ss} de carga sem nenhum erro."
        : Errors > 0
            ? $"Instável: {Errors} erro(s) de cálculo ou de imagem durante o teste ({Reason})"
            : $"Interrompido: {Reason}";

    public string Title => string.Join(" + ", new[] { Cpu ? "CPU" : null, Memory ? "Memória" : null, Gpu ? "GPU 3D" : null }.Where(x => x is not null));
}

public static class StressHistory
{
    private static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MBExtreme", "stress-history.json");

    public static IReadOnlyList<StressRun> Load(string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            if (!File.Exists(path)) return Array.Empty<StressRun>();
            return JsonSerializer.Deserialize<List<StressRun>>(File.ReadAllText(path)) ?? new List<StressRun>();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return Array.Empty<StressRun>();
        }
    }

    public static void Add(StressRun run, string? path = null, int keep = 20)
    {
        path ??= DefaultPath;
        var all = Load(path).Append(run).TakeLast(keep).ToList();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(all));
    }
}

public static class StressReportBuilder
{
    public static string ToText(StressRun r)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Teste de estresse ({r.Title}) · {r.When:dd/MM/yyyy HH:mm}" + (r.Label is { Length: > 0 } ? $" · {r.Label}" : ""));
        sb.AppendLine(r.Verdict);
        sb.AppendLine($"Duração {r.Elapsed:mm\\:ss} · erros {r.Errors}" + (r.MaxCpuTemperature is { } c ? $" · CPU máx. {c:0} °C" : "") + (r.MaxGpuTemperature is { } g ? $" · GPU máx. {g:0} °C" : ""));
        if (r.Gpu) sb.AppendLine($"GPU 3D: {r.GpuAverageFps:0.#} FPS em média (mínimo {r.GpuMinFps:0.#}) · {r.GpuScene}");
        foreach (var t in r.Tracks) sb.AppendLine($"{t.Name}: média {t.Average:0.#} {t.Unit}, máximo {t.Max:0.#} {t.Unit}");
        return sb.ToString();
    }

    public static string ToHtml(StressRun r)
    {
        string E(string text) => EncodeHtml(text);
        var sb = new StringBuilder();
        sb.Append("<!doctype html><html lang=\"pt-BR\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><title>Relatório do teste de estresse</title><style>")
          .Append("body{background:#0d0d0f;color:#eee;font:14px Segoe UI,Arial,sans-serif;margin:0;padding:24px}h1{font-size:22px;margin:0 0 4px}.m{color:#8e8e96;font-size:12px}")
          .Append(".grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(170px,1fr));gap:10px;margin:16px 0}.c{background:#17171a;border:1px solid #2a2a2e;border-radius:8px;padding:10px 12px}")
          .Append(".c b{display:block;font-size:22px;color:#ffc400}.p{background:#17171a;border:1px solid #2a2a2e;border-radius:8px;padding:12px}h2{font-size:14px;margin:0 0 6px;color:#ffc400}")
          .Append(".charts{display:grid;grid-template-columns:repeat(auto-fit,minmax(340px,1fr));gap:10px}.ok{color:#3ddc97}.bad{color:#ff5a5f}.warn{color:#ffa94d}</style></head><body>");
        sb.Append($"<h1>Teste de estresse: {E(r.Title)}</h1><div class=\"m\">{r.When:dd/MM/yyyy HH:mm}{(r.Label is { Length: > 0 } ? " · " + E(r.Label) : "")}{(r.CpuName is { Length: > 0 } ? " · " + E(r.CpuName) : "")}{(r.GpuName is { Length: > 0 } && r.Gpu ? " · " + E(r.GpuName) : "")}</div>");
        string cls = r.Stable ? "ok" : r.Errors > 0 ? "bad" : "warn";
        sb.Append($"<div class=\"p\" style=\"margin-top:12px\"><b class=\"{cls}\">{E(r.Verdict)}</b></div>");
        sb.Append("<div class=\"grid\">");
        void Card(string title, string value, string detail = "") => sb.Append($"<div class=\"c\"><span class=\"m\">{E(title)}</span><b>{E(value)}</b><span class=\"m\">{E(detail)}</span></div>");
        Card("Duração", r.Elapsed.ToString("mm\\:ss", CultureInfo.InvariantCulture), r.Completed ? "completo" : "interrompido");
        Card("Erros", r.Errors.ToString(CultureInfo.InvariantCulture), $"cálculo/memória {r.CpuMemoryErrors} · imagem {r.GpuErrors}");
        if (r.MaxCpuTemperature is { } cpu) Card("CPU, temperatura máxima", SessionReport.N(cpu) + " °C");
        if (r.MaxGpuTemperature is { } gpu) Card("GPU, temperatura máxima", SessionReport.N(gpu) + " °C");
        if (r.Gpu) Card("GPU 3D", SessionReport.N(r.GpuAverageFps) + " FPS", $"mínimo {SessionReport.N(r.GpuMinFps)}");
        sb.Append("</div><div class=\"charts\">");
        foreach (var t in r.Tracks)
            sb.Append($"<div class=\"p\"><h2>{E(t.Name)} ({E(t.Unit)}) · média {SessionReport.N(t.Average)} · máx. {SessionReport.N(t.Max)}</h2>{SessionReport.Svg(t.Values, "#ff8a4d", t.Unit == "%" ? 100 : null, 560, 100)}</div>");
        sb.Append("</div><p class=\"m\">Gerado pelo MB Extreme. Erros são diferenças nas contas conferidas ou no quadro de referência da GPU; qualquer erro indica instabilidade.</p></body></html>");
        return sb.ToString();
    }

    private static string EncodeHtml(string text) => HtmlText.Escape(text);
}
