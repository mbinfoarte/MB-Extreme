using System.Globalization;

namespace MBExtreme.Core.Recording;

/// <summary>Tamanho do vídeo vertical (Shorts, Reels, TikTok...). Os números gravados nas configurações não podem mudar: formatos novos entram no fim.</summary>
public sealed record VerticalFormat(string Name, int Width, int Height, string Note)
{
    public static IReadOnlyList<VerticalFormat> All { get; } = new[]
    {
        new VerticalFormat("Shorts / Reels / TikTok 1080×1920", 1080, 1920, "9:16, o padrão das redes. Boa qualidade com pouca banda."),
        new VerticalFormat("Alta 1440×2560", 1440, 2560, "9:16 em 2K. O YouTube mantém a nitidez; Instagram e TikTok reduzem para 1080."),
        new VerticalFormat("Ultra 2160×3840", 2160, 3840, "9:16 em 4K. Pesa muito na placa de vídeo; só para gravar."),
        new VerticalFormat("Econômico 720×1280", 720, 1280, "9:16 leve, para PC fraco ou internet lenta."),
        new VerticalFormat("Instagram feed 1080×1350", 1080, 1350, "4:5, ocupa mais espaço no feed do Instagram."),
        new VerticalFormat("Quadrado 1080×1080", 1080, 1080, "1:1, funciona em qualquer rede."),
    };

    public static VerticalFormat ByIndex(int index) => All[Math.Clamp(index, 0, All.Count - 1)];

    /// <summary>Mesmo desenho em outra largura (a transmissão é limitada pela plataforma), com medidas pares.</summary>
    public (int Width, int Height) ScaledToWidth(int width)
    {
        width = Math.Clamp(width, 2, Width) & ~1;
        int height = (int)Math.Round(Height * (width / (double)Width)) & ~1;
        return (width, Math.Max(2, height));
    }
}

public enum PaneKind { Screen, Camera, File }

/// <summary>Como o conteúdo entra no quadro: preenchendo (corta o que sobra) ou inteiro, com o fundo desfocado atrás.</summary>
public enum PaneFit { Fill, Fit }

/// <summary>
/// Uma metade do vídeo vertical. <paramref name="FocusX"/> e <paramref name="FocusY"/> (0 a 100) dizem qual parte da imagem fica quando o corte é necessário;
/// <paramref name="ZoomPercent"/> aproxima (100 = sem zoom). <paramref name="Source"/> é a câmera ou o arquivo (imagem, GIF ou vídeo que se repete).
/// </summary>
public sealed record VerticalPane(PaneKind Kind, PaneFit Fit = PaneFit.Fill, int FocusX = 50, int FocusY = 50, int ZoomPercent = 100, string? Source = null)
{
    public static readonly string[] FileExtensions = { ".png", ".jpg", ".jpeg", ".gif", ".mp4", ".mov", ".mkv", ".webm" };

    public int Fx => Math.Clamp(FocusX, 0, 100);

    public int Fy => Math.Clamp(FocusY, 0, 100);

    public int Zoom => Math.Clamp(ZoomPercent, 100, 300);

    public bool IsVideo => Kind == PaneKind.File && Source is { } s && !IsImage(s);

    public static bool IsImage(string path) =>
        path.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase);

    public static bool IsValidFile(string? path) =>
        !string.IsNullOrWhiteSpace(path) && FileExtensions.Any(e => path.EndsWith(e, StringComparison.OrdinalIgnoreCase))
        && path.IndexOfAny(new[] { '"', '|', '\'', '\r', '\n', ';', '[', ']' }) < 0;
}

/// <summary>Montagem do vídeo vertical: uma imagem só, ou tela dividida em duas partes (em cima e embaixo).</summary>
public sealed record VerticalLayout(int Width, int Height, bool Split, int TopPercent, VerticalPane Top, VerticalPane Bottom, bool Divider)
{
    public int ClampedTopPercent => Math.Clamp(TopPercent, 25, 75);

    /// <summary>Altura da parte de cima (par). Sem divisão, a de cima ocupa tudo.</summary>
    public int TopHeight => Split ? Math.Clamp((int)Math.Round(Height * ClampedTopPercent / 100.0) & ~1, 2, Height - 2) : Height;

    public int BottomHeight => Split ? Height - TopHeight : 0;

    /// <summary>Mesmo quadro numa largura menor (a transmissão é limitada pela plataforma), com medidas pares.</summary>
    public VerticalLayout ScaledToWidth(int width)
    {
        width = Math.Clamp(width, 2, Width) & ~1;
        int height = Math.Max(2, (int)Math.Round(Height * (width / (double)Width)) & ~1);
        return this with { Width = width, Height = height };
    }

    public IEnumerable<VerticalPane> Panes => Split ? new[] { Top, Bottom } : new[] { Top };

    public string? Validate()
    {
        if (Width < 2 || Height < 2) return "Tamanho do vídeo vertical inválido.";
        if (!Panes.Any(p => p.Kind == PaneKind.Screen)) return "Pelo menos uma das partes precisa mostrar a tela ou a janela capturada.";
        foreach (var p in Panes)
        {
            if (p.Kind == PaneKind.Camera && !CameraOverlay.IsValidName(p.Source)) return "Escolha uma câmera válida para a parte do vídeo vertical.";
            if (p.Kind == PaneKind.File && !VerticalPane.IsValidFile(p.Source)) return "O arquivo da parte do vídeo vertical não é válido (use PNG, JPG, GIF, MP4, MOV, MKV ou WEBM).";
        }
        return null;
    }
}

/// <summary>Monta o trecho do filtro do FFmpeg que transforma a captura no quadro vertical.</summary>
public static class VerticalGraph
{
    /// <summary>Entradas extras do FFmpeg (câmera ou arquivo de cada parte), na ordem em que as partes aparecem.</summary>
    public static IReadOnlyList<string[]> ExtraInputs(VerticalLayout layout, int fps, Func<VerticalPane, bool>? shared = null, bool realtime = true, IReadOnlyDictionary<string, RelayInput>? relays = null)
    {
        var list = new List<string[]>();
        foreach (var pane in layout.Panes)
        {
            if (pane.Kind == PaneKind.Camera && shared?.Invoke(pane) == true) continue;   // a câmera já está aberta por outra parte do vídeo
            if (pane.Kind == PaneKind.Camera)
                list.Add(CameraSource.InputArgs(pane.Source!, relays));
            else if (pane.Kind == PaneKind.File)
                list.Add(VerticalPane.IsImage(pane.Source!)
                    ? new[] { "-loop", "1", "-framerate", Math.Clamp(fps, 5, 30).ToString(CultureInfo.InvariantCulture), "-i", pane.Source! }
                    : realtime ? new[] { "-stream_loop", "-1", "-re", "-i", pane.Source! } : new[] { "-stream_loop", "-1", "-i", pane.Source! });
        }
        return list;
    }

    /// <summary>
    /// Partes do filtro, terminando em <c>[b0]</c>. <paramref name="screen"/> é o trecho que entrega a captura já em BGRA (com os filtros de cor), terminado em <c>[scr]</c>.
    /// <paramref name="firstExtra"/> é o número da primeira entrada extra no FFmpeg.
    /// </summary>
    public static List<string> Build(VerticalLayout layout, string screen, int firstExtra, string prefix = "", Func<VerticalPane, string?>? sharedCamera = null)
    {
        string p = prefix;
        var parts = new List<string> { screen };
        var panes = layout.Panes.ToList();
        int screens = panes.Count(p => p.Kind == PaneKind.Screen);
        var screenLabels = new Queue<string>();
        if (screens == 2)
        {
            parts.Add($"[{p}scr]split=2[{p}sa][{p}sb]");
            screenLabels.Enqueue($"[{p}sa]");
            screenLabels.Enqueue($"[{p}sb]");
        }
        else screenLabels.Enqueue($"[{p}scr]");

        int extra = firstExtra;
        var outputs = new List<string>();
        for (int i = 0; i < panes.Count; i++)
        {
            var pane = panes[i];
            int h = i == 0 ? layout.TopHeight : layout.BottomHeight;
            string? shared = pane.Kind == PaneKind.Camera ? sharedCamera?.Invoke(pane) : null;
            string input = pane.Kind == PaneKind.Screen ? screenLabels.Dequeue() : shared is not null ? $"{shared}format=bgra," : $"[{extra++}:v]format=bgra,";
            string label = i == 0 ? $"[{p}pt]" : $"[{p}pb]";
            AddPane(parts, input, pane, layout.Width, h, label, p + "p" + i);
            outputs.Add(label);
        }

        if (!layout.Split)
        {
            parts.Add($"{outputs[0]}null[{p}b0]");
            return parts;
        }

        string joined = layout.Divider ? $"[{p}bv]" : $"[{p}b0]";
        if (panes[0].Kind == PaneKind.Screen)
        {
            // a tela manda no ritmo do vídeo: ela é o fundo e a outra parte entra por cima
            parts.Add($"[{p}pt]pad={layout.Width}:{layout.Height}:0:0:black[{p}bp]");
            parts.Add($"[{p}bp][{p}pb]overlay=0:{layout.TopHeight}:shortest=0{joined}");
        }
        else
        {
            parts.Add($"[{p}pb]pad={layout.Width}:{layout.Height}:0:{layout.TopHeight}:black[{p}bp]");
            parts.Add($"[{p}bp][{p}pt]overlay=0:0:shortest=0{joined}");
        }

        if (layout.Divider)
        {
            int thickness = Math.Max(2, layout.Width / 270) & ~1;
            parts.Add($"[{p}bv]drawbox=x=0:y={layout.TopHeight - thickness / 2}:w=iw:h={thickness}:color=0xFFC400:t=fill[{p}b0]");
        }
        return parts;
    }

    private static void AddPane(List<string> parts, string input, VerticalPane pane, int w, int h, string output, string tag)
    {
        // "input" é um rótulo de filtro ([scr]) ou uma entrada nova já com "format=bgra," na frente
        string head = input;
        if (pane.Fit == PaneFit.Fill)
        {
            int zw = Even(w * pane.Zoom / 100), zh = Even(h * pane.Zoom / 100);
            string chain = $"scale={zw}:{zh}:force_original_aspect_ratio=increase,crop={w}:{h}:(iw-ow)*{pane.Fx}/100:(ih-oh)*{pane.Fy}/100,setsar=1";
            parts.Add($"{head}{chain}{output}");
            return;
        }

        int sw = Math.Max(2, Even(w / 6)), sh = Math.Max(2, Even(h / 6));
        parts.Add($"{head}split=2[{tag}a][{tag}b]");
        parts.Add($"[{tag}a]scale={sw}:{sh}:force_original_aspect_ratio=increase,crop={sw}:{sh},boxblur=8:2,scale={w}:{h}:flags=bilinear[{tag}bg]");
        parts.Add($"[{tag}b]scale={w}:{h}:force_original_aspect_ratio=decrease,setsar=1[{tag}fg]");
        parts.Add($"[{tag}bg][{tag}fg]overlay=(main_w-overlay_w)/2:(main_h-overlay_h)/2:shortest=0{output}");
    }

    private static int Even(int v) => Math.Max(2, v & ~1);
}
