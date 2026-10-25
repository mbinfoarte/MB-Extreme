using System.Globalization;
using System.Text.RegularExpressions;

namespace MBExtreme.Core.Recording;

/// <summary>Um trecho de um vídeo longo que vira um vídeo curto (Short, Reel, TikTok).</summary>
public sealed record ShortSegment(double Start, double End, string Name)
{
    public const double MinSeconds = 3;
    public const double MaxSeconds = 180;

    public double Duration => End - Start;

    public bool IsValid => Start >= 0 && Duration >= MinSeconds && Duration <= MaxSeconds;
}

/// <summary>Tempo escrito pela pessoa ("83", "1:23", "1:02:03,5") e de volta para texto.</summary>
public static class TimeText
{
    public static double? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var parts = text.Trim().Replace(',', '.').Split(':');
        if (parts.Length > 3) return null;
        double total = 0;
        foreach (var part in parts)
        {
            if (!double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) || v < 0 || double.IsNaN(v) || double.IsInfinity(v)) return null;
            total = total * 60 + v;
        }
        return total;
    }

    public static string Format(double seconds)
    {
        seconds = Math.Max(0, seconds);
        int whole = (int)Math.Floor(seconds);
        int h = whole / 3600, m = whole % 3600 / 60, s = whole % 60;
        return h > 0 ? $"{h}:{m:00}:{s:00}" : $"{m}:{s:00}";
    }
}

/// <summary>Lê o que o FFmpeg escreve ao abrir um arquivo ou analisar o áudio (não existe ffprobe junto do app).</summary>
public static class MediaProbe
{
    private static readonly Regex DurationRx = new(@"Duration:\s*(\d+):(\d{2}):(\d{2}(?:\.\d+)?)", RegexOptions.Compiled);
    private static readonly Regex SizeRx = new(@"Video:.*?,\s*(\d{2,5})x(\d{2,5})", RegexOptions.Compiled);
    private static readonly Regex PtsRx = new(@"pts_time:\s*(-?[0-9.]+)", RegexOptions.Compiled);
    private static readonly Regex RmsRx = new(@"RMS_level=\s*(-?[0-9.]+|-?inf)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static double? ParseDuration(string ffmpegText)
    {
        var m = DurationRx.Match(ffmpegText);
        if (!m.Success) return null;
        double seconds = int.Parse(m.Groups[1].Value) * 3600 + int.Parse(m.Groups[2].Value) * 60 + double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
        return seconds > 0 ? seconds : null;
    }

    public static (int Width, int Height)? ParseVideoSize(string ffmpegText)
    {
        var m = SizeRx.Match(ffmpegText);
        return m.Success ? (int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value)) : null;
    }

    public static bool HasAudio(string ffmpegText) => ffmpegText.Contains(" Audio:", StringComparison.Ordinal);

    /// <summary>Pares (segundo, volume em dB) da análise <see cref="ShortsCommand.LoudnessArgs"/>. Silêncio total vira -100 dB.</summary>
    public static List<(double Time, double Db)> ParseLoudness(string text)
    {
        var result = new List<(double, double)>();
        double time = 0;
        bool haveTime = false;
        foreach (var line in text.Split('\n'))
        {
            var p = PtsRx.Match(line);
            if (p.Success && double.TryParse(p.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double t)) { time = t; haveTime = true; continue; }
            var r = RmsRx.Match(line);
            if (!r.Success || !haveTime) continue;
            string v = r.Groups[1].Value;
            double db = v.Contains("inf", StringComparison.OrdinalIgnoreCase) ? -100 : double.Parse(v, CultureInfo.InvariantCulture);
            result.Add((time, Math.Max(-100, db)));
            haveTime = false;
        }
        return result;
    }
}

/// <summary>Sugere cortes em um vídeo longo: por tempo fixo ou pelos momentos em que o som sobe (gritos, comemorações, lances).</summary>
public static class ShortsPlanner
{
    public static List<ShortSegment> FixedSplit(double duration, int seconds)
    {
        seconds = Math.Clamp(seconds, (int)ShortSegment.MinSeconds, (int)ShortSegment.MaxSeconds);
        var list = new List<ShortSegment>();
        int n = 1;
        for (double start = 0; start < duration - 0.5; start += seconds, n++)
        {
            double end = Math.Min(duration, start + seconds);
            if (end - start < ShortSegment.MinSeconds) break;
            list.Add(new ShortSegment(start, end, $"Parte {n}"));
        }
        return list;
    }

    /// <summary>Pega os picos de volume acima do normal do vídeo, com um respiro antes e depois, sem se sobrepor.</summary>
    public static List<ShortSegment> FromLoudness(IReadOnlyList<(double Time, double Db)> series, double duration, int count, double before = 10, double after = 15, double minOverDb = 5)
    {
        var voiced = series.Where(s => s.Db > -80).Select(s => s.Db).OrderBy(x => x).ToList();
        if (voiced.Count < 5 || count <= 0) return new List<ShortSegment>();
        double baseline = voiced[voiced.Count / 2];

        // suaviza em 3 s para um grito de um segundo não valer menos que uma conversa alta
        var smooth = new List<(double Time, double Score)>();
        for (int i = 0; i < series.Count; i++)
        {
            double sum = 0;
            int n = 0;
            for (int j = Math.Max(0, i - 1); j <= Math.Min(series.Count - 1, i + 1); j++) { sum += Math.Max(-80, series[j].Db); n++; }
            smooth.Add((series[i].Time, sum / n - baseline));
        }

        var chosen = new List<ShortSegment>();
        foreach (var peak in smooth.Where(s => s.Score >= minOverDb).OrderByDescending(s => s.Score))
        {
            double start = Math.Max(0, peak.Time - before);
            double end = Math.Min(duration, peak.Time + after);
            if (end - start < ShortSegment.MinSeconds) continue;
            if (chosen.Any(c => start < c.End && end > c.Start)) continue;
            chosen.Add(new ShortSegment(start, end, "Momento"));
            if (chosen.Count >= count) break;
        }

        return chosen.OrderBy(c => c.Start).Select((c, i) => c with { Name = $"Momento {i + 1} ({TimeText.Format(c.Start)})" }).ToList();
    }
}

public sealed record ShortsRequest(string InputFile, ShortSegment Segment, VerticalLayout Layout, EncoderOption Encoder, string OutputFile, bool HasAudio, int Fps = 30, int VideoMbps = 12, EncoderQuality Quality = EncoderQuality.Balanced);

public static class ShortsCommand
{
    private static readonly char[] Bad = { '"', '|', '\'', ';', '[', ']', '\r', '\n' };

    public static string? Validate(ShortsRequest r)
    {
        if (string.IsNullOrWhiteSpace(r.InputFile) || r.InputFile.IndexOfAny(Bad) >= 0) return "O vídeo escolhido tem caracteres que o FFmpeg não aceita no nome.";
        if (string.IsNullOrWhiteSpace(r.OutputFile) || r.OutputFile.IndexOfAny(Bad) >= 0) return "O nome do arquivo de saída não é válido.";
        if (!r.Segment.IsValid) return $"O trecho \"{r.Segment.Name}\" precisa ter entre {ShortSegment.MinSeconds:0} e {ShortSegment.MaxSeconds:0} segundos.";
        if (r.Layout.Validate() is { } layoutProblem) return layoutProblem;
        if (r.Layout.Panes.Any(p => p.Kind == PaneKind.Camera)) return "Num vídeo já gravado não há câmera ao vivo: use a tela (o próprio vídeo) ou uma imagem/vídeo de fundo.";
        return null;
    }

    private static string N(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>Análise de volume por segundo do vídeo inteiro (saída de texto no stdout).</summary>
    public static IReadOnlyList<string> LoudnessArgs(string file) => new[]
    {
        "-hide_banner", "-nostats", "-loglevel", "error", "-i", file, "-vn",
        "-af", "aresample=16000,asetnsamples=n=16000,astats=metadata=1:reset=1,ametadata=print:key=lavfi.astats.Overall.RMS_level:file=-",
        "-f", "null", "-",
    };

    /// <summary>Só abre o arquivo e mostra duração e tamanho (o FFmpeg termina com erro por falta de saída; o texto é o que importa).</summary>
    public static IReadOnlyList<string> ProbeArgs(string file) => new[] { "-hide_banner", "-i", file };

    public static IReadOnlyList<string> Build(ShortsRequest r)
    {
        if (Validate(r) is { } problem) throw new InvalidOperationException(problem);
        var args = new List<string> { "-hide_banner", "-loglevel", "warning", "-nostats", "-progress", "pipe:1", "-y", "-ss", N(r.Segment.Start), "-t", N(r.Segment.Duration), "-i", r.InputFile };

        foreach (var input in VerticalGraph.ExtraInputs(r.Layout, r.Fps)) args.AddRange(input);
        var parts = VerticalGraph.Build(r.Layout, "[0:v]format=bgra[scr]", 1);
        parts.Add($"[b0]fps={Math.Clamp(r.Fps, 15, 60)},format=yuv420p[v]");
        args.AddRange(new[] { "-filter_complex", string.Join(";", parts), "-map", "[v]" });

        int mbps = Math.Clamp(r.VideoMbps, 2, 40);
        args.AddRange(new[] { "-c:v", r.Encoder.FfmpegName });
        args.AddRange(StudioCommand.QualityArgs(r.Encoder, r.Quality));
        args.AddRange(new[] { "-b:v", mbps + "M", "-maxrate", (int)Math.Ceiling(mbps * 1.5) + "M", "-bufsize", mbps * 2 + "M", "-g", (Math.Clamp(r.Fps, 15, 60) * 2).ToString() });
        if (r.HasAudio) args.AddRange(new[] { "-map", "0:a:0", "-c:a", "aac", "-b:a", "192k", "-ar", "48000", "-ac", "2" });
        args.AddRange(new[] { "-movflags", "+faststart", "-shortest", r.OutputFile });
        return args;
    }

    /// <summary>
    /// Uma imagem só (PNG) do vídeo vertical num ponto do vídeo, em tamanho reduzido, montada pelo mesmo desenho do arquivo final.
    /// Serve para mostrar a pré-visualização antes de gerar os vídeos.
    /// </summary>
    public static IReadOnlyList<string> PreviewArgs(ShortsRequest r, double atSeconds, string outputPng, int previewWidth = 360)
    {
        if (r.InputFile.IndexOfAny(Bad) >= 0 || string.IsNullOrWhiteSpace(r.InputFile)) throw new InvalidOperationException("O vídeo escolhido tem caracteres que o FFmpeg não aceita no nome.");
        if (string.IsNullOrWhiteSpace(outputPng) || outputPng.IndexOfAny(Bad) >= 0) throw new InvalidOperationException("O arquivo da prévia não é válido.");
        if (r.Layout.Validate() is { } layoutProblem) throw new InvalidOperationException(layoutProblem);
        if (r.Layout.Panes.Any(p => p.Kind == PaneKind.Camera)) throw new InvalidOperationException("Num vídeo já gravado não há câmera ao vivo: use a tela (o próprio vídeo) ou uma imagem/vídeo de fundo.");

        var layout = r.Layout.ScaledToWidth(Math.Clamp(previewWidth, 120, 1080));
        var args = new List<string> { "-hide_banner", "-loglevel", "error", "-nostats", "-y", "-ss", N(Math.Max(0, atSeconds)), "-i", r.InputFile };
        foreach (var input in VerticalGraph.ExtraInputs(layout, 1, realtime: false)) args.AddRange(input);
        var parts = VerticalGraph.Build(layout, "[0:v]format=bgra[scr]", 1);
        parts.Add("[b0]format=rgb24[v]");
        args.AddRange(new[] { "-filter_complex", string.Join(";", parts), "-map", "[v]", "-frames:v", "1", "-f", "image2", outputPng });
        return args;
    }

    /// <summary>Nome de arquivo seguro: sem acentos problemáticos nem caracteres que o Windows recusa.</summary>
    public static string SafeName(string text)
    {
        var chars = text.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : ' ').ToArray();
        string clean = string.Join("-", new string(chars).Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return clean.Length == 0 ? "corte" : clean.Length > 40 ? clean[..40] : clean;
    }
}
