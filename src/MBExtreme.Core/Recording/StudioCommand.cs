using System.Text.RegularExpressions;

namespace MBExtreme.Core.Recording;

/// <summary>Monta a linha de comando do FFmpeg para o estúdio: uma captura, uma codificação e várias saídas (arquivo, transmissão e replay).</summary>
public static class StudioCommand
{
    public const int SegmentSeconds = 4;
    public const int MaxStreamMbps = 8;
    public const int MaxCameras = 3;
    public const int MaxLayers = 4;

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
        if (r.Cameras.Count > MaxCameras) return $"Use no máximo {MaxCameras} câmeras ao mesmo tempo.";
        foreach (var cam in r.Cameras)
            if (!CameraOverlay.IsValidName(cam.DeviceName)) return "O nome da câmera tem caracteres que não são aceitos. Escolha outra câmera da lista.";
        if (r.Cameras.Select(c => c.DeviceName).Distinct(StringComparer.OrdinalIgnoreCase).Count() != r.Cameras.Count)
            return "A mesma câmera foi escolhida duas vezes. Cada câmera só pode aparecer uma vez.";
        if (r.Pix is { } pix && !PixOverlay.IsValidPath(pix.ImagePath)) return "A imagem do QR do Pix não é válida.";
        if (r.Layers.Count > MaxLayers) return $"Use no máximo {MaxLayers} camadas de identidade (entrada, faixa e logo).";
        foreach (var layer in r.Layers)
            if (!PixOverlay.IsValidPath(layer.ImagePath)) return "A imagem de uma camada do canal não é válida.";
        if (r.Vertical is { } v)
        {
            if (v.Validate() is { } vp) return vp;
            if (v.Width != r.OutputWidth || v.Height != r.OutputHeight) return "O tamanho do vídeo vertical não bate com o tamanho de saída.";
            foreach (var pane in v.Panes.Where(p => p.Kind == PaneKind.Camera))
                if (r.Cameras.Any(c => string.Equals(c.DeviceName, pane.Source, StringComparison.OrdinalIgnoreCase)))
                    return "A câmera usada numa parte do vídeo vertical não pode aparecer também como câmera pequena no canto.";
            if (v.Panes.Count(p => p.Kind == PaneKind.Camera) == 2 && string.Equals(v.Top.Source, v.Bottom.Source, StringComparison.OrdinalIgnoreCase))
                return "As duas partes usam a mesma câmera; escolha câmeras diferentes.";
        }
        if (r.Captions is { } c)
        {
            if (!CaptionOverlay.IsValidPath(c.Line1File) || !CaptionOverlay.IsValidPath(c.Line2File) || !CaptionOverlay.IsValidPath(c.FontFile))
                return "O caminho da legenda ou da fonte tem caracteres que não são aceitos.";
        }
        return null;
    }

    /// <summary>Troca a chave de transmissão por pontos em qualquer texto (mensagens de erro do FFmpeg, por exemplo).</summary>
    public static string MaskSecrets(string text, StreamTarget? target)
    {
        if (target is null) return text;
        if (target.FullUrl.Length > 0) text = text.Replace(target.FullUrl, "••••");
        if (target.Service == StreamService.Custom && target.PathSecret is { Length: > 0 } secret) text = text.Replace(secret, "••••");
        if (target.Key.Length >= 4) text = text.Replace(target.Key, "••••");
        return text;
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

        // ---- câmeras e QR do Pix (entradas extras, depois do vídeo e do áudio)
        int firstExtra = audio ? 2 : 1;
        int verticalFirst = firstExtra;
        if (r.Vertical is { } vertical)
        {
            foreach (var input in VerticalGraph.ExtraInputs(vertical, fps)) args.AddRange(input);
            firstExtra += vertical.Panes.Count(p => p.Kind != PaneKind.Screen);
        }
        foreach (var camera in r.Cameras)
            args.AddRange(new[] { "-f", "dshow", "-rtbufsize", "128M", "-use_wallclock_as_timestamps", "1", "-i", "video=" + camera.DeviceName });
        if (r.Pix is { } pixInput)
            args.AddRange(new[] { "-loop", "1", "-framerate", "5", "-i", pixInput.ImagePath });
        foreach (var layer in r.Layers)
            args.AddRange(new[] { "-loop", "1", "-framerate", "25", "-i", layer.ImagePath });   // 25 quadros por segundo para o esmaecer ficar suave

        // ---- filtros
        string? filter = null;
        string? complex = null;
        if (r.Cameras.Count > 0 || r.Pix is not null || r.Layers.Count > 0 || r.Filter is { IsNeutral: false } || r.Vertical is not null || r.Captions is not null)
        {
            complex = OverlayGraph(r, firstExtra, verticalFirst, gdi, sameSize);
        }
        else if (gdi)
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
        if (complex is not null) args.AddRange(new[] { "-filter_complex", complex });

        // ---- codificação
        double maxFactor = r.Stream is null ? 1.5 : 1.0;
        string bitrate, maxrate, bufsize;
        if (r.VideoKbps > 0)
        {
            int kbps = Math.Max(1000, r.Stream is null ? r.VideoKbps : Math.Min(r.VideoKbps, r.StreamMaxMbps * 1000));
            bitrate = kbps + "k";
            maxrate = (int)Math.Ceiling(kbps * maxFactor) + "k";
            bufsize = kbps * 2 + "k";
        }
        else
        {
            int rate = Math.Max(2, r.Stream is null ? r.BitrateMbps : Math.Min(r.BitrateMbps, r.StreamMaxMbps));
            bitrate = rate + "M";
            maxrate = (int)Math.Ceiling(rate * maxFactor) + "M";
            bufsize = rate * 2 + "M";
        }

        args.AddRange(new[] { "-map", complex is null ? "0:v" : "[v]", "-c:v", r.Encoder.FfmpegName });
        args.AddRange(QualityArgs(r.Encoder, r.Quality));
        args.AddRange(new[]
        {
            "-b:v", bitrate, "-maxrate", maxrate, "-bufsize", bufsize,
            "-g", (fps * 2).ToString(),
            "-flags", "+global_header",
        });
        if (audio) args.AddRange(new[] { "-map", "1:a", "-c:a", "aac", "-b:a", Math.Clamp(r.AudioKbps, 64, 320) + "k", "-ar", "48000", "-ac", "2", "-af", "aresample=async=1:first_pts=0" });

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

    /// <summary>Esforço do codificador conforme a marca da placa (NVENC p2/p4/p6, AMF speed/balanced/quality, Quick Sync veryfast/medium/slower).</summary>
    public static IReadOnlyList<string> QualityArgs(EncoderOption encoder, EncoderQuality quality) => encoder.Kind switch
    {
        EncoderKind.Nvenc => new[] { "-preset", quality switch { EncoderQuality.Fast => "p2", EncoderQuality.Quality => "p6", _ => "p4" } },
        EncoderKind.Amf => new[] { "-quality", quality switch { EncoderQuality.Fast => "speed", EncoderQuality.Quality => "quality", _ => "balanced" } },
        _ => new[] { "-preset", quality switch { EncoderQuality.Fast => "veryfast", EncoderQuality.Quality => "slower", _ => "medium" } },
    };

    /// <summary>Posição da câmera: o filtro "overlay" usa main_w/overlay_w para encostar no canto com uma margem.</summary>
    public static string CameraPosition(CameraCorner corner, int margin) => corner switch
    {
        CameraCorner.TopLeft => $"x={margin}:y={margin}",
        CameraCorner.TopRight => $"x=main_w-overlay_w-{margin}:y={margin}",
        CameraCorner.BottomLeft => $"x={margin}:y=main_h-overlay_h-{margin}",
        _ => $"x=main_w-overlay_w-{margin}:y=main_h-overlay_h-{margin}",
    };

    /// <summary>
    /// Filtro com a imagem da tela e, por cima, as câmeras e o QR do Pix. As câmeras são medidas em % da largura da tela capturada, antes de reduzir.
    /// Cada camada vira o fundo da próxima: [b0] + câmera 1 = [b1], [b1] + câmera 2 = [b2]...
    /// </summary>
    private static string OverlayGraph(StudioRequest r, int firstExtra, int verticalFirst, bool gdi, bool sameSize)
    {
        int baseWidth = r.Vertical is { } vl ? vl.Width : gdi ? r.OutputWidth : r.Source.Area.Width;
        int margin = Math.Max(8, baseWidth / 80);
        string mainFilter = r.Filter?.ToChain() is { } chain ? "," + chain : "";
        string screen = (gdi ? "[0:v]format=bgra" : "[0:v]hwdownload,format=bgra") + mainFilter;
        var parts = r.Vertical is { } vertical
            ? VerticalGraph.Build(vertical, screen + "[scr]", verticalFirst)
            : new List<string> { screen + "[b0]" };

        int layer = 0;
        for (int i = 0; i < r.Cameras.Count; i++)
        {
            var camera = r.Cameras[i];
            int width = Math.Max(2, baseWidth * camera.ClampedPercent / 100);
            width -= width % 2;
            string camFilter = camera.Filter?.ToChain() is { } camChain ? "," + camChain : "";
            string cam = $"[{firstExtra + i}:v]scale={width}:-2,format=bgra{camFilter}" + (camera.Frame ? ",drawbox=x=0:y=0:w=iw:h=ih:color=0xFFC400:t=4" : "");
            parts.Add($"{cam}[c{i}]");
            parts.Add($"[b{layer}][c{i}]overlay={CameraPosition(camera.Corner, margin)}:shortest=0[b{layer + 1}]");
            layer++;
        }

        if (r.Pix is { } pix)
        {
            int width = Math.Max(2, baseWidth * pix.ClampedPercent / 100);
            width -= width % 2;
            string enable = pix.EveryMinutes > 0
                ? $":enable='lt(mod(t,{pix.EveryMinutes * 60}),{Math.Clamp(pix.ShowSeconds, 3, pix.EveryMinutes * 60)})'"
                : "";
            parts.Add($"[{firstExtra + r.Cameras.Count}:v]scale={width}:-2,format=bgra[pix]");
            parts.Add($"[b{layer}][pix]overlay={CameraPosition(pix.Corner, margin)}{enable}:shortest=0[b{layer + 1}]");
            layer++;
        }

        int layerInput = firstExtra + r.Cameras.Count + (r.Pix is null ? 0 : 1);
        for (int i = 0; i < r.Layers.Count; i++)
        {
            var item = r.Layers[i];
            int width = Math.Max(2, baseWidth * item.ClampedPercent / 100);
            width -= width % 2;
            parts.Add(LayerChain(item, layerInput + i, width, i));
            parts.Add($"[b{layer}][l{i}]overlay={LayerPlacement(item, margin)}:shortest=0[b{layer + 1}]");
            layer++;
        }

        if (r.Captions is { } captions)
        {
            double divider = r.Vertical is { Split: true } split ? split.TopHeight / (double)split.Height : 0.5;
            parts.Add(CaptionFilter.Build(captions, $"[b{layer}]", $"[b{layer + 1}]", baseWidth, r.Vertical is { } vh ? vh.Height : gdi ? r.OutputHeight : r.Source.Area.Height, divider));
            layer++;
        }

        string finish = r.Vertical is not null ? "format=nv12" : gdi
            ? $"scale={r.OutputWidth}:{r.OutputHeight}:force_original_aspect_ratio=decrease,pad={r.OutputWidth}:{r.OutputHeight}:(ow-iw)/2:(oh-ih)/2:black,format=nv12"
            : sameSize ? "format=nv12" : $"scale={r.OutputWidth}:{r.OutputHeight}:flags=bilinear,format=nv12";
        parts.Add($"[b{layer}]{finish}[v]");
        return string.Join(";", parts);
    }

    private static string N(double v) => v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>A entrada do canal esmaece ao aparecer e ao sair; a faixa e o logo só são redimensionados (a faixa entra deslizando pela posição).</summary>
    private static string LayerChain(OverlayLayer layer, int input, int width, int index)
    {
        string chain = $"[{input}:v]scale={width}:-2,format=bgra";
        if (layer.Kind == OverlayKind.Intro)
        {
            double fade = Math.Min(0.6, layer.Show / 3);
            chain += $",fade=t=in:st={N(layer.StartSeconds)}:d={N(fade)}:alpha=1,fade=t=out:st={N(layer.StartSeconds + layer.Show - fade)}:d={N(fade)}:alpha=1";
        }

        return chain + $"[l{index}]";
    }

    /// <summary>Posição e janela de tempo da camada. As expressões usam o relógio do vídeo (t), então não é preciso reiniciar nada para a faixa repetir.</summary>
    private static string LayerPlacement(OverlayLayer layer, int margin)
    {
        string start = N(layer.StartSeconds);
        string show = N(layer.Show);
        switch (layer.Kind)
        {
            case OverlayKind.Logo:
                return CameraPosition(layer.Corner, margin);
            case OverlayKind.Intro:
                return $"x=(main_w-overlay_w)/2:y=(main_h-overlay_h)/2:enable='between(t,{start},{N(layer.StartSeconds + layer.Show)})'";
            default:
            {
                // faixa: aparece em StartSeconds, fica Show segundos e, se EveryMinutes > 0, volta a cada tantos minutos
                string phase = layer.EveryMinutes > 0 ? $"mod(t-{start},{N(layer.EveryMinutes * 60)})" : $"(t-{start})";
                string enable = layer.EveryMinutes > 0 ? $"gte(t,{start})*lt({phase},{show})" : $"between(t,{start},{N(layer.StartSeconds + layer.Show)})";
                bool left = layer.Corner is CameraCorner.TopLeft or CameraCorner.BottomLeft;
                string slideIn = $"{phase}/0.6";
                string slideOut = $"({phase}-({show}-0.6))/0.6";
                string x = left
                    ? $"if(lt({phase},0.6),-overlay_w+(overlay_w+{margin})*{slideIn},if(gt({phase},{show}-0.6),{margin}-(overlay_w+{margin})*{slideOut},{margin}))"
                    : $"if(lt({phase},0.6),main_w-(overlay_w+{margin})*{slideIn},if(gt({phase},{show}-0.6),main_w-overlay_w-{margin}+(overlay_w+{margin})*{slideOut},main_w-overlay_w-{margin}))";
                string y = layer.Corner is CameraCorner.TopLeft or CameraCorner.TopRight ? margin.ToString() : $"main_h-overlay_h-{margin}";
                return $"x='{x}':y={y}:enable='{enable}'";
            }
        }
    }

    /// <summary>Linha de comando para mostrar na tela, com a chave escondida.</summary>
    public static string Describe(StudioRequest r, string ffmpeg = "ffmpeg")
    {
        string text = ffmpeg + " " + string.Join(" ", Build(r).Select(a => a.Contains(' ') ? "\"" + a + "\"" : a));
        return MaskSecrets(text, r.Stream);
    }
}
