using System.Text.RegularExpressions;

namespace MBExtreme.Core.Recording;

/// <summary>Monta a linha de comando do FFmpeg para o estúdio: uma captura, uma codificação e várias saídas (arquivo, transmissão e replay).</summary>
public static class StudioCommand
{
    public const int SegmentSeconds = 4;
    public const int MaxStreamMbps = 8;

    private static readonly Regex SafeKey = new(@"^[A-Za-z0-9_\-.~%=&?]+$", RegexOptions.Compiled);

    public static int ReplaySegmentCount(int replaySeconds) => (int)Math.Ceiling(replaySeconds / (double)SegmentSeconds) + 3;

    /// <summary>Tamanho final mantendo a proporção da origem, com altura-alvo (0 = original) e medidas pares.</summary>
    public static (int Width, int Height) FitOutput(int sourceWidth, int sourceHeight, int targetHeight)
    {
        sourceWidth = Math.Max(2, sourceWidth);
        sourceHeight = Math.Max(2, sourceHeight);
        if (targetHeight <= 0 || targetHeight >= sourceHeight) return (Even(sourceWidth), Even(sourceHeight));
        double width = sourceWidth * (targetHeight / (double)sourceHeight);
        return (Even((int)Math.Round(width)), Even(targetHeight));
    }

    private static int Even(int value) => Math.Max(2, value - value % 2);

    /// <summary>Mensagem de problema, ou null quando o pedido pode ser executado.</summary>
    public static string? Validate(StudioRequest r)
    {
        if (!r.AnyOutput) return "Ligue pelo menos uma saída: gravar, transmitir ou replay.";
        if (r.OutputWidth < 2 || r.OutputHeight < 2) return "Tamanho de vídeo inválido.";
        if (r.Stream is { } s)
        {
            if (r.Encoder.Codec != VideoCodec.H264) return "A transmissão exige H.264. Escolha um codificador H.264.";
            if (s.Key.Length == 0 && s.Service != StreamService.Custom) return "Informe a chave de transmissão.";
            if (s.Key.Length > 0 && !SafeKey.IsMatch(s.Key)) return "A chave de transmissão tem caracteres que não são aceitos. Copie de novo, sem espaços.";
            string url = s.BaseUrl;
            if (!url.StartsWith("rtmp://", StringComparison.OrdinalIgnoreCase) && !url.StartsWith("rtmps://", StringComparison.OrdinalIgnoreCase))
                return "O endereço do servidor precisa começar com rtmp:// ou rtmps://.";
            if (url.IndexOfAny(new[] { '|', '[', ']', '"', ' ', '\'' }) >= 0) return "O endereço do servidor tem caracteres inválidos.";
        }
        if (r.Source.Kind == SourceKind.WindowGdi && string.IsNullOrWhiteSpace(r.Source.WindowTitle)) return "A janela escolhida não tem título.";
        return null;
    }

    /// <summary>Troca a chave de transmissão por pontos em qualquer texto (mensagens de erro do FFmpeg, por exemplo).</summary>
    public static string MaskSecrets(string text, StreamTarget? target)
    {
        if (target is null || target.Key.Length < 4) return text;
        return text.Replace(target.Key, "••••");
    }

    public static IReadOnlyList<string> Build(StudioRequest r)
    {
        string? problem = Validate(r);
        if (problem is not null) throw new InvalidOperationException(problem);

        var args = new List<string> { "-hide_banner", "-loglevel", "warning", "-nostats", "-progress", "pipe:1", "-y" };
        int fps = Math.Clamp(r.Fps, 5, 240);
        var src = r.Source;
        bool gdi = src.Kind == SourceKind.WindowGdi;
        bool sameSize = !gdi && r.OutputWidth == src.Area.Width && r.OutputHeight == src.Area.Height;
        bool hwFrames = !gdi && sameSize && r.Encoder.Kind != EncoderKind.Qsv;

        // ---- vídeo
        if (gdi)
        {
            args.AddRange(new[] { "-f", "gdigrab", "-framerate", fps.ToString(), "-draw_mouse", "1", "-i", "title=" + src.WindowTitle });
        }
        else
        {
            string grab = $"ddagrab=output_idx={src.DisplayIndex}:framerate={fps}:draw_mouse=1";
            if (src.Kind == SourceKind.WindowRegion)
                grab += $":video_size={src.Area.Width}x{src.Area.Height}:offset_x={src.Area.X}:offset_y={src.Area.Y}";
            args.AddRange(new[] { "-f", "lavfi", "-i", grab });
        }

        // ---- áudio
        bool audio = r.Audio is not null;
        if (audio)
        {
            args.AddRange(new[]
            {
                "-f", "s16le", "-ar", "48000", "-ac", "2", "-thread_queue_size", "4096",
                "-use_wallclock_as_timestamps", "1", "-i", r.Audio!.PipePath,
            });
        }

        // ---- filtros
        string? filter = null;
        if (gdi)
        {
            filter = $"scale={r.OutputWidth}:{r.OutputHeight}:force_original_aspect_ratio=decrease,pad={r.OutputWidth}:{r.OutputHeight}:(ow-iw)/2:(oh-ih)/2:black,format=nv12";
        }
        else if (!hwFrames)
        {
            filter = sameSize
                ? "hwdownload,format=bgra,format=nv12"
                : $"hwdownload,format=bgra,scale={r.OutputWidth}:{r.OutputHeight}:flags=bilinear,format=nv12";
        }
        if (filter is not null) args.AddRange(new[] { "-vf", filter });

        // ---- codificação
        int rate = Math.Max(2, r.Stream is null ? r.BitrateMbps : Math.Min(r.BitrateMbps, MaxStreamMbps));
        double maxFactor = r.Stream is null ? 1.5 : 1.0;
        args.AddRange(new[]
        {
            "-map", "0:v",
            "-c:v", r.Encoder.FfmpegName,
            "-b:v", rate + "M", "-maxrate", (int)Math.Ceiling(rate * maxFactor) + "M", "-bufsize", rate * 2 + "M",
            "-g", (fps * 2).ToString(),
            "-flags", "+global_header",
        });
        if (audio) args.AddRange(new[] { "-map", "1:a", "-c:a", "aac", "-b:a", "160k", "-ar", "48000", "-ac", "2", "-af", "aresample=async=1:first_pts=0" });

        // ---- saídas
        var outputs = new List<string>();
        if (r.RecordFile is { } file)
        {
            string container = r.RecordMp4 ? "[f=mp4:movflags=frag_keyframe+empty_moov+default_base_moof]" : "[f=matroska]";
            outputs.Add(container + file);
        }
        if (r.Stream is { } stream)
            outputs.Add("[f=flv:flvflags=no_duration_filesize:onfail=ignore]" + stream.FullUrl);
        if (r.ReplayFolder is { } folder)
        {
            outputs.Add($"[f=segment:segment_time={SegmentSeconds}:segment_wrap={ReplaySegmentCount(r.ReplaySeconds)}:reset_timestamps=1:segment_format=matroska:onfail=ignore]{folder}/seg%03d.mkv");
        }
        args.AddRange(new[] { "-f", "tee", string.Join("|", outputs) });
        return args;
    }

    /// <summary>Linha de comando para mostrar na tela, com a chave escondida.</summary>
    public static string Describe(StudioRequest r, string ffmpeg = "ffmpeg")
    {
        string text = ffmpeg + " " + string.Join(" ", Build(r).Select(a => a.Contains(' ') ? "\"" + a + "\"" : a));
        return MaskSecrets(text, r.Stream);
    }
}
