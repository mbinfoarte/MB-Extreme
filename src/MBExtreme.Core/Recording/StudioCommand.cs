using System.Text.RegularExpressions;

namespace MBExtreme.Core.Recording;

/// <summary>Monta a linha de comando do FFmpeg para o estúdio: uma captura, uma codificação e várias saídas (arquivo, transmissão e replay).</summary>
public static class StudioCommand
{
    public const int SegmentSeconds = 4;
    public const int MaxStreamMbps = 8;
    public const int MaxCameras = 3;
    public const int MaxLayers = 4;
    public const int MaxLiveScenes = 6;

    /// <summary>Tamanho e ritmo da imagem pequena "No ar" (leve: só para conferir na tela do programa).</summary>
    private const string ProgramChain = "fps=15,scale=640:-2:flags=bilinear,format=yuvj420p";

    /// <summary>Cópia de cada câmera para o quadro dela na tela do programa (pequena e leve).</summary>
    private const string CameraPreviewChain = "fps=12,scale=320:-2:flags=bilinear,format=yuvj420p";

    private static string? CameraPipe(StudioRequest r, int index) => index < r.CameraPreviewPipes.Count ? r.CameraPreviewPipes[index] : null;

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
        if (r.VerticalCopy is { } copy)
        {
            if (r.Vertical is not null) return "A cópia vertical não vale quando o vídeo principal já é vertical.";
            if (r.VerticalFile is null && r.VerticalStream is null) return "O vídeo vertical precisa ir para algum lugar: ligue Gravar ou a live vertical.";
            if (r.VerticalFile is not null && r.RecordFile is null) return "A cópia vertical é gravada junto com a gravação: ligue a opção Gravar.";
            if (r.VerticalFile is not null && (string.IsNullOrWhiteSpace(r.VerticalFile) || r.VerticalFile.IndexOfAny(new[] { '"', '|', '\'', ';', '[', ']' }) >= 0)) return "O arquivo da cópia vertical não é válido.";
            if (r.VerticalStream is { } vs)
            {
                if (r.Encoder.Codec != VideoCodec.H264) return "A live vertical exige H.264. Escolha um codificador H.264.";
                if (ValidateTarget(vs, "live vertical") is { } vsProblem) return vsProblem;
            }
            if (copy.Validate() is { } cp) return cp;
            var names = copy.Panes.Where(p => p.Kind == PaneKind.Camera).Select(p => p.Source ?? "").ToList();
            if (names.Count == 2 && string.Equals(names[0], names[1], StringComparison.OrdinalIgnoreCase)) return "As duas partes da cópia vertical usam a mesma câmera; escolha câmeras diferentes.";
        }
        if (r.Captions is { } c)
        {
            if (!CaptionOverlay.IsValidPath(c.Line1File) || !CaptionOverlay.IsValidPath(c.Line2File) || !CaptionOverlay.IsValidPath(c.FontFile))
                return "O caminho da legenda ou da fonte tem caracteres que não são aceitos.";
        }
        foreach (var camPipe in r.CameraPreviewPipes)
            if (camPipe is not null && (!camPipe.StartsWith(@"\\.\pipe\", StringComparison.Ordinal) || camPipe.IndexOfAny(new[] { '"', '|', '\'', ';', '[', ']', ' ' }) >= 0))
                return "O canal da imagem de uma câmera não é válido.";
        if (r.ProgramPipe is { } pipe && (!pipe.StartsWith(@"\\.\pipe\", StringComparison.Ordinal) || pipe.IndexOfAny(new[] { '"', '|', '\'', ';', '[', ']', ' ' }) >= 0))
            return "O canal da imagem \"No ar\" não é válido.";
        if (r.LiveScenes && ValidateScenes(r) is { } sceneProblem) return sceneProblem;
        return null;
    }

    private static string? ValidateScenes(StudioRequest r)
    {
        if (r.Scenes.Count > MaxLiveScenes) return $"A troca de cena sem reiniciar funciona com até {MaxLiveScenes} cenas.";
        if (r.SceneCaptures.Count == 0) return "As cenas não têm tela nem janela para capturar.";
        if (r.ActiveScene < 0 || r.ActiveScene >= r.Scenes.Count) return "A cena inicial não existe.";
        foreach (var capture in r.SceneCaptures)
            if (capture.Kind == SourceKind.WindowGdi && string.IsNullOrWhiteSpace(capture.WindowTitle)) return "Uma janela usada numa cena não tem título.";
        foreach (var scene in r.Scenes)
        {
            if (scene.Capture < 0 || scene.Capture >= r.SceneCaptures.Count) return $"A cena \"{scene.Name}\" não tem tela ou janela válida.";
            if (scene.Cameras.Any(i => i < 0 || i >= r.Cameras.Count) || scene.Cameras.Distinct().Count() != scene.Cameras.Count) return $"A cena \"{scene.Name}\" tem uma câmera inválida.";
            if (scene.Layers.Any(i => i < 0 || i >= r.Layers.Count) || scene.Layers.Distinct().Count() != scene.Layers.Count) return $"A cena \"{scene.Name}\" tem uma camada inválida.";
            if (scene.Pix && r.Pix is null) return $"A cena \"{scene.Name}\" usa o Pix, mas ele não está configurado.";
        }
        return null;
    }

    /// <summary>Troca a chave de transmissão por pontos em qualquer texto (mensagens de erro do FFmpeg, por exemplo).</summary>
    public static string MaskSecrets(string text, params StreamTarget?[] targets)
    {
        foreach (var target in targets)
        {
            if (target is null) continue;
            if (target.FullUrl.Length > 0) text = text.Replace(target.FullUrl, "••••");
            if (target.Service == StreamService.Custom && target.PathSecret is { Length: > 0 } secret) text = text.Replace(secret, "••••");
            if (target.Key.Length >= 4) text = text.Replace(target.Key, "••••");
        }
        return text;
    }

    /// <summary>Problema com um destino de transmissão (chave e endereço), ou null.</summary>
    private static string? ValidateTarget(StreamTarget s, string what)
    {
        if (s.Key.Length == 0 && s.Service != StreamService.Custom) return $"Informe a chave da {what}.";
        if (s.Key.Length > 0 && !SafeKey.IsMatch(s.Key)) return $"A chave da {what} tem caracteres que não são aceitos. Copie de novo, sem espaços.";
        string url = s.BaseUrl;
        if (!url.StartsWith("rtmp://", StringComparison.OrdinalIgnoreCase) && !url.StartsWith("rtmps://", StringComparison.OrdinalIgnoreCase))
            return $"O endereço do servidor da {what} precisa começar com rtmp:// ou rtmps://.";
        if (url.IndexOfAny(new[] { '|', '[', ']', '"', ' ', '\'' }) >= 0) return $"O endereço do servidor da {what} tem caracteres inválidos.";
        return null;
    }

    public static IReadOnlyList<string> Build(StudioRequest r)
    {
        string? problem = Validate(r);
        if (problem is not null) throw new InvalidOperationException(problem);

        var args = new List<string> { "-hide_banner", "-loglevel", "warning", "-nostats", "-progress", "pipe:1", "-y" };
        int fps = Math.Clamp(r.Fps, 5, 240);
        bool audio = r.Audio is not null;
        string? filter = null;
        string? complex;
        if (r.LiveScenes) complex = LiveScenesGraph(args, r, fps);
        else (filter, complex) = SingleSceneInputs(args, r, fps);

        // ---- imagem "No ar": uma cópia pequena do vídeo final, em JPEG, para a tela do programa
        string videoMap = complex is null ? "0:v" : "[v]";
        if (r.ProgramPipe is not null)
        {
            if (complex is not null) complex += $";[v]split=2[vo][pvsrc];[pvsrc]{ProgramChain}[pv]";
            else if (filter is not null) complex = $"[0:v]{filter}[vf];[vf]split=2[vo][pvsrc];[pvsrc]{ProgramChain}[pv]";
            else complex = $"[0:v]split=2[vo][pvsrc];[pvsrc]fps=15,hwdownload,format=bgra,scale=640:-2:flags=bilinear,format=yuvj420p[pv]";   // quadros na placa: só a cópia pequena desce
            filter = null;
            videoMap = "[vo]";
        }
        if (filter is not null) args.AddRange(new[] { "-vf", filter });
        if (complex is not null) args.AddRange(new[] { "-filter_complex", complex });
        AddEncodingAndOutputs(args, r, fps, audio, videoMap);
        return args;
    }

    /// <summary>Entradas e filtros do modo de uma cena só (o caminho mais leve: sem câmeras nem camadas, os quadros nem saem da placa de vídeo).</summary>
    private static (string? Filter, string? Complex) SingleSceneInputs(List<string> args, StudioRequest r, int fps)
    {
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
        if (audio) AddAudioInput(args, r);

        // ---- câmeras e QR do Pix (entradas extras, depois do vídeo e do áudio)
        int firstExtra = audio ? 2 : 1;
        int verticalFirst = firstExtra;
        int copyFirst = 0;
        if (r.Vertical is { } vertical)
        {
            foreach (var input in VerticalGraph.ExtraInputs(vertical, fps)) args.AddRange(input);
            firstExtra += vertical.Panes.Count(p => p.Kind != PaneKind.Screen);
        }
        if (r.VerticalCopy is { } copyLayout)
        {
            copyFirst = firstExtra;
            var shared = SharedCameraTest(r);
            foreach (var input in VerticalGraph.ExtraInputs(copyLayout, fps, shared)) args.AddRange(input);
            firstExtra += copyLayout.Panes.Count(p => p.Kind == PaneKind.File || (p.Kind == PaneKind.Camera && !shared(p)));
        }
        foreach (var camera in r.Cameras)
            args.AddRange(CameraSource.InputArgs(camera.DeviceName));
        if (r.Pix is { } pixInput)
            args.AddRange(new[] { "-loop", "1", "-framerate", "5", "-i", pixInput.ImagePath });
        foreach (var layer in r.Layers)
            args.AddRange(new[] { "-loop", "1", "-framerate", "25", "-i", layer.ImagePath });   // 25 quadros por segundo para o esmaecer ficar suave

        // ---- filtros
        string? filter = null;
        string? complex = null;
        if (r.Cameras.Count > 0 || r.Pix is not null || r.Layers.Count > 0 || r.Filter is { IsNeutral: false } || r.Vertical is not null || r.Captions is not null || r.VerticalCopy is not null)
        {
            complex = OverlayGraph(r, firstExtra, verticalFirst, copyFirst, gdi, sameSize);
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
        return (filter, complex);
    }

    private static void AddAudioInput(List<string> args, StudioRequest r) => args.AddRange(new[]
    {
        "-f", "s16le", "-ar", "48000", "-ac", "2", "-thread_queue_size", "4096",
        "-use_wallclock_as_timestamps", "1", "-i", r.Audio!.PipePath,
    });

    /// <summary>Codificação (uma vez só) e as saídas: arquivo, transmissão e replay pelo tee; cópia vertical e imagem "No ar" à parte.</summary>
    private static void AddEncodingAndOutputs(List<string> args, StudioRequest r, int fps, bool audio, string videoMap)
    {
        // ---- codificação
        double maxFactor = r.Stream is null ? 1.5 : 1.0;
        string bitrate, maxrate, bufsize;
        if (r.VideoKbps > 0)
        {
            int kbps = Math.Max(1000, r.Stream is null ? r.VideoKbps : Math.Min(r.VideoKbps, r.StreamMaxMbps * 1000));
            bitrate = kbps + "k";
            maxrate = (int)Math.Ceiling(kbps * maxFactor) + "k";
            bufsize = (r.Stream is null ? kbps * 2 : kbps) + "k";   // ao vivo, o reservatório é de 1 s: menos rajadas de envio, menos travadas na internet
        }
        else
        {
            int rate = Math.Max(2, r.Stream is null ? r.BitrateMbps : Math.Min(r.BitrateMbps, r.StreamMaxMbps));
            bitrate = rate + "M";
            maxrate = (int)Math.Ceiling(rate * maxFactor) + "M";
            bufsize = (r.Stream is null ? rate * 2 : rate) + "M";
        }

        args.AddRange(new[] { "-map", videoMap, "-c:v", r.Encoder.FfmpegName });
        args.AddRange(QualityArgs(r.Encoder, r.Quality));
        if (r.QualityLevel > 0 && r.Stream is null)
            args.AddRange(ConstantQualityArgs(r.Encoder, r.QualityLevel));   // só gravando: a imagem manda, o tamanho do arquivo varia
        else
            args.AddRange(new[] { "-b:v", bitrate, "-maxrate", maxrate, "-bufsize", bufsize });
        if (r.Vertical is not null) args.AddRange(SocialArgs(r.Encoder));
        args.AddRange(new[] { "-g", (fps * 2).ToString(), "-flags", "+global_header" });
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

        // ---- cópia vertical: segunda saída com a mesma captura e o mesmo áudio: arquivo próprio e/ou a live vertical (Instagram, TikTok...)
        if (r.VerticalCopy is { } verticalCopy && (r.VerticalFile is not null || r.VerticalStream is not null))
        {
            int copyFps = r.VerticalFps > 0 ? Math.Clamp(r.VerticalFps, 10, 60) : fps;
            // a live vertical exige H.264: com ela ligada, o codificador próprio só vale se for H.264
            var copyEncoder = r.VerticalEncoder is { } own && (r.VerticalStream is null || own.Codec == VideoCodec.H264) ? own : r.Encoder;
            args.AddRange(new[] { "-map", "[vv]", "-c:v", copyEncoder.FfmpegName });
            args.AddRange(QualityArgs(copyEncoder, r.VerticalEffort ?? r.Quality));
            if (r.VerticalStream is not null)
            {
                // ao vivo: taxa constante e reservatório de 1 s, como a live principal
                int kbps = Math.Clamp(r.VerticalStreamKbps, 1000, 9000);
                args.AddRange(new[] { "-b:v", kbps + "k", "-maxrate", kbps + "k", "-bufsize", kbps + "k" });
            }
            else if (r.VerticalQualityLevel > 0)
            {
                args.AddRange(ConstantQualityArgs(copyEncoder, r.VerticalQualityLevel));
            }
            else
            {
                int copyMbps = r.VerticalBitrateMbps > 0 ? Math.Clamp(r.VerticalBitrateMbps, 2, 120)
                    : Math.Clamp(FfmpegCommand.DefaultBitrateMbps(verticalCopy.Width, verticalCopy.Height, copyFps), 4, 30);
                args.AddRange(new[] { "-b:v", copyMbps + "M", "-maxrate", (int)Math.Ceiling(copyMbps * 1.5) + "M", "-bufsize", copyMbps * 2 + "M" });
            }
            args.AddRange(SocialArgs(copyEncoder));
            args.AddRange(new[] { "-g", (copyFps * 2).ToString(), "-flags", "+global_header" });
            if (audio) args.AddRange(new[] { "-map", "1:a", "-c:a", "aac", "-b:a", Math.Clamp(r.VerticalStream is null ? r.AudioKbps : Math.Min(r.AudioKbps, 128), 64, 320) + "k", "-ar", "48000", "-ac", "2", "-af", "aresample=async=1:first_pts=0" });

            if (r.VerticalStream is null)
            {
                // só o arquivo: saída direta, como sempre foi
                args.AddRange(r.RecordMp4
                    ? new[] { "-f", "mp4", "-movflags", "frag_keyframe+empty_moov+default_base_moof", r.VerticalFile! }
                    : new[] { "-f", "matroska", r.VerticalFile! });
            }
            else
            {
                // com a live vertical: arquivo (se gravando) e servidor pelo tee; se a live cair, o arquivo continua
                var verticalOutputs = new List<string>();
                if (r.VerticalFile is { } verticalFile)
                    verticalOutputs.Add((r.RecordMp4 ? "[f=mp4:movflags=frag_keyframe+empty_moov+default_base_moof]" : "[f=matroska]") + verticalFile);
                verticalOutputs.Add("[f=flv:flvflags=no_duration_filesize:onfail=ignore]" + r.VerticalStream.FullUrl);
                args.AddRange(new[] { "-f", "tee", string.Join("|", verticalOutputs) });
            }
        }

        if (r.ProgramPipe is { } programPipe)
            args.AddRange(new[] { "-map", "[pv]", "-c:v", "mjpeg", "-q:v", "8", "-f", "mjpeg", "-flush_packets", "1", programPipe });
        for (int i = 0; i < r.Cameras.Count; i++)
            if (CameraPipe(r, i) is { } cameraPipe)
                args.AddRange(new[] { "-map", $"[cpv{i}]", "-c:v", "mjpeg", "-q:v", "8", "-f", "mjpeg", "-flush_packets", "1", cameraPipe });
    }

    // ---------------------------------------------------------------- cenas ao vivo

    /// <summary>Telas pela placa (ddagrab) abrem uma vez por monitor; uma janela "recortada" é um corte da tela dela. Janelas pelo GDI abrem cada uma.</summary>
    public static string CaptureGroupKey(CaptureSource c) => c.Kind == SourceKind.WindowGdi ? "gdi:" + c.WindowTitle : "tela:" + c.DisplayIndex;

    private static string Fan(IReadOnlyList<string> labels) => labels.Count == 1 ? labels[0] : $",split={labels.Count}{string.Concat(labels)}";

    /// <summary>
    /// Todas as cenas montadas ao mesmo tempo e um seletor no fim (streamselect): trocar de cena é só mudar o seletor, sem reiniciar.
    /// Com esmaecer, dois seletores (a cena que sai e a que entra) passam por uma mistura (mix) cujos pesos mudam aos poucos.
    /// Cada câmera, o Pix e cada camada abrem uma vez e são copiados (split) para as cenas que os usam.
    /// </summary>
    private static string LiveScenesGraph(List<string> args, StudioRequest r, int fps)
    {
        int w = r.OutputWidth, h = r.OutputHeight;
        var groupKeys = new List<string>();
        var captureGroup = new int[r.SceneCaptures.Count];
        for (int i = 0; i < r.SceneCaptures.Count; i++)
        {
            string key = CaptureGroupKey(r.SceneCaptures[i]);
            int index = groupKeys.IndexOf(key);
            if (index < 0) { groupKeys.Add(key); index = groupKeys.Count - 1; }
            captureGroup[i] = index;
        }

        // ---- entradas: a primeira captura, o áudio (sempre a entrada 1), as outras capturas, câmeras, Pix e camadas
        int next = 0;
        var groupInput = new int[groupKeys.Count];
        for (int g = 0; g < groupKeys.Count; g++)
        {
            var first = r.SceneCaptures[Array.IndexOf(captureGroup, g)];
            if (first.Kind == SourceKind.WindowGdi)
                args.AddRange(new[] { "-f", "gdigrab", "-framerate", fps.ToString(), "-draw_mouse", "1", "-i", "title=" + first.WindowTitle });
            else
                args.AddRange(new[] { "-f", "lavfi", "-i", $"ddagrab=output_idx={first.DisplayIndex}:framerate={fps}:draw_mouse=1" });
            groupInput[g] = next++;
            if (g == 0 && r.Audio is not null) { AddAudioInput(args, r); next++; }
        }
        int camBase = next;
        foreach (var camera in r.Cameras) args.AddRange(CameraSource.InputArgs(camera.DeviceName));
        next += r.Cameras.Count;
        int pixInput = -1;
        if (r.Pix is { } pixImage)
        {
            args.AddRange(new[] { "-loop", "1", "-framerate", "5", "-i", pixImage.ImagePath });
            pixInput = next++;
        }
        int layerBase = next;
        foreach (var layer in r.Layers) args.AddRange(new[] { "-loop", "1", "-framerate", "25", "-i", layer.ImagePath });

        var parts = new List<string>();
        int margin = Math.Max(8, w / 80);
        string mainFilter = r.Filter?.ToChain() is { } chain ? "," + chain : "";
        List<string> Users(Func<SceneLayout, bool> uses, string suffix) =>
            r.Scenes.Select((scene, index) => (scene, index)).Where(x => uses(x.scene)).Select(x => $"[s{x.index}{suffix}]").ToList();

        // ---- capturas: desce da placa uma vez por monitor e copia para cada recorte usado
        var used = Enumerable.Range(0, r.SceneCaptures.Count).Where(ci => r.Scenes.Any(s => s.Capture == ci)).ToList();
        for (int g = 0; g < groupKeys.Count; g++)
        {
            var captures = used.Where(ci => captureGroup[ci] == g).ToList();
            if (captures.Count == 0) { parts.Add($"[{groupInput[g]}:v]nullsink"); continue; }
            bool gdi = r.SceneCaptures[captures[0]].Kind == SourceKind.WindowGdi;
            string download = $"[{groupInput[g]}:v]" + (gdi ? "format=bgra" : "hwdownload,format=bgra");
            parts.Add(download + Fan(captures.Select(c => $"[cap{c}]").ToList()));
        }
        foreach (int ci in used)
        {
            var capture = r.SceneCaptures[ci];
            string crop = capture.Kind == SourceKind.WindowRegion ? $",crop={capture.Area.Width}:{capture.Area.Height}:{capture.Area.X}:{capture.Area.Y}" : "";
            parts.Add($"[cap{ci}]null{crop}{mainFilter},scale={w}:{h}:force_original_aspect_ratio=decrease,pad={w}:{h}:(ow-iw)/2:(oh-ih)/2:black,setsar=1"
                      + Fan(Users(s => s.Capture == ci, "b0")));
        }

        // ---- câmeras, Pix e camadas: uma vez cada, copiadas para as cenas que usam
        for (int i = 0; i < r.Cameras.Count; i++)
        {
            int index = i;
            var camera = r.Cameras[i];
            var users = Users(s => s.Cameras.Contains(index), "c" + i);
            if (CameraPipe(r, i) is not null)
            {
                users.Add($"[cpp{i}]");
                parts.Add($"[cpp{i}]{CameraPreviewChain}[cpv{i}]");
            }
            if (users.Count == 0) { parts.Add($"[{camBase + i}:v]nullsink"); continue; }
            int width = Math.Max(2, w * camera.ClampedPercent / 100);
            width -= width % 2;
            string camFilter = camera.Filter?.ToChain() is { } camChain ? "," + camChain : "";
            parts.Add($"[{camBase + i}:v]scale={width}:-2:flags=bilinear,format=bgra{camFilter}" + (camera.Frame ? ",drawbox=x=0:y=0:w=iw:h=ih:color=0xFFC400:t=4" : "") + Fan(users));
        }
        if (r.Pix is { } pix)
        {
            var users = Users(s => s.Pix, "pix");
            int width = Math.Max(2, w * pix.ClampedPercent / 100);
            width -= width % 2;
            parts.Add(users.Count == 0 ? $"[{pixInput}:v]nullsink" : $"[{pixInput}:v]scale={width}:-2,format=bgra" + Fan(users));
        }
        for (int i = 0; i < r.Layers.Count; i++)
        {
            int index = i;
            var users = Users(s => s.Layers.Contains(index), "l" + i);
            if (users.Count == 0) { parts.Add($"[{layerBase + i}:v]nullsink"); continue; }
            int width = Math.Max(2, w * r.Layers[i].ClampedPercent / 100);
            width -= width % 2;
            parts.Add(LayerBody(r.Layers[i], layerBase + i, width) + Fan(users));
        }

        // ---- cada cena: a captura e, por cima, as câmeras, o Pix e as camadas dela
        for (int sn = 0; sn < r.Scenes.Count; sn++)
        {
            var scene = r.Scenes[sn];
            int k = 0;
            foreach (int ci in scene.Cameras)
                parts.Add($"[s{sn}b{k}][s{sn}c{ci}]overlay={CameraPosition(r.Cameras[ci].Corner, margin)}:shortest=0[s{sn}b{++k}]");
            if (scene.Pix && r.Pix is { } p)
            {
                string enable = p.EveryMinutes > 0 ? $":enable='lt(mod(t,{p.EveryMinutes * 60}),{Math.Clamp(p.ShowSeconds, 3, p.EveryMinutes * 60)})'" : "";
                parts.Add($"[s{sn}b{k}][s{sn}pix]overlay={CameraPosition(p.Corner, margin)}{enable}:shortest=0[s{sn}b{++k}]");
            }
            foreach (int li in scene.Layers)
                parts.Add($"[s{sn}b{k}][s{sn}l{li}]overlay={LayerPlacement(r.Layers[li], margin)}:shortest=0[s{sn}b{++k}]");
            parts.Add($"[s{sn}b{k}]null[s{sn}]");
        }

        // ---- seletor de cena (e a mistura do esmaecer)
        int n = r.Scenes.Count;
        int active = Math.Clamp(r.ActiveScene, 0, n - 1);
        if (r.Transition == SceneTransition.Fade)
        {
            // dois seletores (a cena que sai e a que entra) e a mistura das duas; fora da troca, os dois mostram a mesma cena
            for (int sn = 0; sn < n; sn++) parts.Add($"[s{sn}]split=2[s{sn}x][s{sn}y]");
            parts.Add(string.Concat(Enumerable.Range(0, n).Select(sn => $"[s{sn}x]")) + $"streamselect@{SceneSwitch.SelectOut}=inputs={n}:map={active}[ska]");
            parts.Add(string.Concat(Enumerable.Range(0, n).Select(sn => $"[s{sn}y]")) + $"streamselect@{SceneSwitch.SelectIn}=inputs={n}:map={active}[skb]");
            parts.Add($"[ska][skb]mix@{SceneSwitch.Mix}=inputs=2:weights='1 0':scale=1[sel]");
        }
        else
        {
            parts.Add(string.Concat(Enumerable.Range(0, n).Select(sn => $"[s{sn}]")) + $"streamselect@{SceneSwitch.Select}=inputs={n}:map={active}[sel]");
        }

        string last = "[sel]";
        if (r.Captions is { } captions)
        {
            parts.Add(CaptionFilter.Build(captions, "[sel]", "[selc]", w, h, 0.5));
            last = "[selc]";
        }
        parts.Add($"{last}format=nv12[v]");
        return string.Join(";", parts);
    }

    /// <summary>Valor de qualidade constante: 1 alta, 2 muito alta, 3 máxima (quanto menor o número, melhor a imagem). O AV1 usa uma escala mais alta.</summary>
    public static int QualityValue(int level, VideoCodec codec) =>
        Math.Clamp(level, 1, 3) switch { 1 => 23, 2 => 20, _ => 17 } + (codec == VideoCodec.Av1 ? 8 : 0);

    /// <summary>Qualidade constante conforme a marca: NVENC (cq), AMD (cqp), Intel (global_quality) e processador (crf).</summary>
    public static IReadOnlyList<string> ConstantQualityArgs(EncoderOption encoder, int level)
    {
        string q = QualityValue(level, encoder.Codec).ToString();
        return encoder.Kind switch
        {
            EncoderKind.Nvenc => new[] { "-rc", "vbr", "-cq", q, "-b:v", "0" },
            EncoderKind.Amf => new[] { "-rc", "cqp", "-qp_i", q, "-qp_p", q },
            EncoderKind.Qsv => new[] { "-global_quality", q },
            _ => new[] { "-crf", q },
        };
    }

    /// <summary>O que as redes sociais esperam: perfil High no H.264 (Main no HEVC) e cores BT.709 marcadas no arquivo.</summary>
    public static IReadOnlyList<string> SocialArgs(EncoderOption encoder)
    {
        var args = new List<string>();
        if (encoder.Codec == VideoCodec.H264) args.AddRange(new[] { "-profile:v", "high" });
        else if (encoder.Codec == VideoCodec.Hevc) args.AddRange(new[] { "-profile:v", "main" });
        args.AddRange(new[] { "-color_primaries", "bt709", "-color_trc", "bt709", "-colorspace", "bt709" });
        return args;
    }

    /// <summary>Esforço do codificador conforme a marca da placa (NVENC p2/p4/p6, AMF speed/balanced/quality, Quick Sync veryfast/medium/slower).</summary>
    public static IReadOnlyList<string> QualityArgs(EncoderOption encoder, EncoderQuality quality) => encoder.Kind switch
    {
        EncoderKind.Nvenc => new[] { "-preset", quality switch { EncoderQuality.Fast => "p2", EncoderQuality.Quality => "p6", _ => "p4" } },
        EncoderKind.Amf => new[] { "-quality", quality switch { EncoderQuality.Fast => "speed", EncoderQuality.Quality => "quality", _ => "balanced" } },
        EncoderKind.Software => new[] { "-preset", quality switch { EncoderQuality.Fast => "superfast", EncoderQuality.Quality => "faster", _ => "veryfast" } },   // o processador também roda o jogo
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
    private static string OverlayGraph(StudioRequest r, int firstExtra, int verticalFirst, int copyFirst, bool gdi, bool sameSize)
    {
        // sem vertical, a tela já desce reduzida ao tamanho final: câmeras, Pix, camadas e cores trabalham em menos pixels (bem mais leve em 1440p e 4K)
        bool scaledFirst = r.Vertical is null && r.VerticalCopy is null;
        int w = r.OutputWidth, h = r.OutputHeight;
        int baseWidth = r.Vertical is { } vl ? vl.Width : gdi || scaledFirst ? w : r.Source.Area.Width;
        int baseHeight = r.Vertical is { } vh ? vh.Height : gdi || scaledFirst ? h : r.Source.Area.Height;
        int margin = Math.Max(8, baseWidth / 80);
        string mainFilter = r.Filter?.ToChain() is { } chain ? "," + chain : "";
        string grab = gdi ? "[0:v]format=bgra" : "[0:v]hwdownload,format=bgra";
        if (scaledFirst && gdi) grab += $",scale={w}:{h}:force_original_aspect_ratio=decrease,pad={w}:{h}:(ow-iw)/2:(oh-ih)/2:black,setsar=1";
        else if (scaledFirst && !sameSize) grab += $",scale={w}:{h}:flags=bilinear";
        string screen = grab + mainFilter;
        List<string> parts;
        if (r.Vertical is { } vertical) parts = VerticalGraph.Build(vertical, screen + "[scr]", verticalFirst);
        else if (r.VerticalCopy is { } copy)
        {
            // a imagem tratada é dividida em duas: uma segue para o vídeo normal e a outra vira o quadro vertical
            parts = new List<string> { screen + "[scr0]", "[scr0]split=2[b0][vsrc]" };
            string? SharedLabel(VerticalPane pane)
            {
                int index = r.Cameras.ToList().FindIndex(c => string.Equals(c.DeviceName, pane.Source, StringComparison.OrdinalIgnoreCase));
                return index >= 0 ? $"[cv{index}]" : null;
            }
            parts.AddRange(VerticalGraph.Build(copy, "[vsrc]null[v_scr]", copyFirst, "v_", SharedLabel));
            parts.Add(r.VerticalFps > 0 ? $"[v_b0]fps={Math.Clamp(r.VerticalFps, 10, 60)},format=nv12[vv]" : "[v_b0]format=nv12[vv]");
        }
        else parts = new List<string> { screen + "[b0]" };

        int layer = 0;
        for (int i = 0; i < r.Cameras.Count; i++)
        {
            var camera = r.Cameras[i];
            int width = Math.Max(2, baseWidth * camera.ClampedPercent / 100);
            width -= width % 2;
            string camFilter = camera.Filter?.ToChain() is { } camChain ? "," + camChain : "";
            string camIn = $"[{firstExtra + i}:v]";
            if (r.VerticalCopy is { } usedCopy && usedCopy.Panes.Any(p => p.Kind == PaneKind.Camera && string.Equals(p.Source, camera.DeviceName, StringComparison.OrdinalIgnoreCase)))
            {
                parts.Add($"{camIn}split=2[cm{i}][cv{i}]");   // a mesma câmera alimenta o canto do vídeo normal e a parte do vertical
                camIn = $"[cm{i}]";
            }
            string cam = $"{camIn}scale={width}:-2:flags=bilinear,format=bgra{camFilter}" + (camera.Frame ? ",drawbox=x=0:y=0:w=iw:h=ih:color=0xFFC400:t=4" : "");
            if (CameraPipe(r, i) is not null)
            {
                parts.Add($"{cam},split=2[c{i}][cpp{i}]");
                parts.Add($"[cpp{i}]{CameraPreviewChain}[cpv{i}]");
            }
            else parts.Add($"{cam}[c{i}]");
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
            parts.Add(CaptionFilter.Build(captions, $"[b{layer}]", $"[b{layer + 1}]", baseWidth, baseHeight, divider));
            layer++;
        }

        string finish = r.Vertical is not null || scaledFirst ? "format=nv12" : gdi
            ? $"scale={r.OutputWidth}:{r.OutputHeight}:force_original_aspect_ratio=decrease,pad={r.OutputWidth}:{r.OutputHeight}:(ow-iw)/2:(oh-ih)/2:black,format=nv12"
            : sameSize ? "format=nv12" : $"scale={r.OutputWidth}:{r.OutputHeight}:flags=bilinear,format=nv12";
        parts.Add($"[b{layer}]{finish}[v]");
        return string.Join(";", parts);
    }

    /// <summary>Câmera de uma parte da cópia vertical que já está aberta como câmera do canto (o DirectShow não abre a mesma duas vezes).</summary>
    private static Func<VerticalPane, bool> SharedCameraTest(StudioRequest r) =>
        pane => pane.Kind == PaneKind.Camera && r.Cameras.Any(c => string.Equals(c.DeviceName, pane.Source, StringComparison.OrdinalIgnoreCase));

    private static string N(double v) => v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>A entrada do canal esmaece ao aparecer e ao sair; a faixa e o logo só são redimensionados (a faixa entra deslizando pela posição).</summary>
    private static string LayerChain(OverlayLayer layer, int input, int width, int index) => LayerBody(layer, input, width) + $"[l{index}]";

    private static string LayerBody(OverlayLayer layer, int input, int width)
    {
        string chain = $"[{input}:v]scale={width}:-2,format=bgra";
        if (layer.Kind == OverlayKind.Intro)
        {
            double fade = Math.Min(0.6, layer.Show / 3);
            chain += $",fade=t=in:st={N(layer.StartSeconds)}:d={N(fade)}:alpha=1,fade=t=out:st={N(layer.StartSeconds + layer.Show - fade)}:d={N(fade)}:alpha=1";
        }

        return chain;
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
        return MaskSecrets(text, r.Stream, r.VerticalStream);
    }
}
