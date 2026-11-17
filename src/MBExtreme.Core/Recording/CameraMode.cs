using System.Globalization;
using System.Text.RegularExpressions;

namespace MBExtreme.Core.Recording;

/// <summary>Um modo da câmera: tamanho, quadros por segundo e formato (MJPEG comprimido ou imagem crua, como YUY2 e NV12).</summary>
public sealed record CameraMode(int Width, int Height, double MaxFps, string? Codec, string? PixelFormat)
{
    public bool IsMjpeg => string.Equals(Codec, "mjpeg", StringComparison.OrdinalIgnoreCase);

    /// <summary>Quadros pedidos à câmera: até 30 (mais que isso só pesa; a câmera fica num canto da tela).</summary>
    public int RequestFps => (int)Math.Clamp(Math.Floor(MaxFps + 0.01), 5, 30);

    public string Title => $"{Width}×{Height} · {RequestFps} fps · {(IsMjpeg ? "MJPEG" : PixelFormat ?? Codec ?? "?")}";

    public string[] InputArgs()
    {
        var args = new List<string> { "-video_size", $"{Width}x{Height}", "-framerate", RequestFps.ToString(CultureInfo.InvariantCulture) };
        if (Codec is { Length: > 0 }) args.AddRange(new[] { "-vcodec", Codec });
        else if (PixelFormat is { Length: > 0 }) args.AddRange(new[] { "-pixel_format", PixelFormat });
        return args.ToArray();
    }
}

/// <summary>Lê os modos que a câmera anuncia ("ffmpeg -f dshow -list_options true") e escolhe o mais leve que ainda fica bonito.</summary>
public static class CameraModes
{
    private static readonly Regex Line = new(
        @"(?:vcodec=(?<codec>\S+)|pixel_format=(?<pix>\S+))\s+min\s+s=(?<w1>\d+)x(?<h1>\d+)\s+fps=(?<f1>[\d.]+)\s+max\s+s=(?<w2>\d+)x(?<h2>\d+)\s+fps=(?<f2>[\d.]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>Só o primeiro pino de vídeo (o "Capture"): alguns aparelhos listam um segundo pino de foto ou prévia com outros modos.</summary>
    public static IReadOnlyList<CameraMode> Parse(string text)
    {
        var modes = new List<CameraMode>();
        int pins = 0;
        foreach (string raw in text.Split('\n'))
        {
            if (raw.Contains("Pin \"", StringComparison.Ordinal) && ++pins > 1) break;
            var m = Line.Match(raw);
            if (!m.Success) continue;
            int w = int.Parse(m.Groups["w2"].Value, CultureInfo.InvariantCulture);
            int h = int.Parse(m.Groups["h2"].Value, CultureInfo.InvariantCulture);
            if (!double.TryParse(m.Groups["f2"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double fps)) continue;
            string? codec = m.Groups["codec"].Success ? m.Groups["codec"].Value : null;
            string? pix = m.Groups["pix"].Success ? m.Groups["pix"].Value : null;
            var mode = new CameraMode(w, h, fps, codec, pix);
            if (w >= 160 && h >= 120 && !modes.Contains(mode)) modes.Add(mode);
        }
        return modes;
    }

    /// <summary>
    /// O melhor modo da câmera: pelo menos 24 quadros, altura perto de <paramref name="targetHeight"/> (sem passar dele; até 4K),
    /// imagem crua quando ela aguenta esse tamanho (não precisa descomprimir) e MJPEG senão. H.264 e outros formatos ficam por último.
    /// Com o alvo em 2160 (qualidade máxima), fica com o maior tamanho que a câmera entrega a pelo menos 24 quadros.
    /// </summary>
    public static CameraMode? Pick(IReadOnlyList<CameraMode> modes, int targetHeight = 720)
    {
        targetHeight = Math.Clamp(targetHeight, 240, CameraRelayPlan.MaxHeight);
        return modes
            .Where(m => m.Height <= targetHeight)
            .OrderByDescending(m => m.MaxFps >= 23.9)                                // movimento suave primeiro
            .ThenBy(m => m.Height >= targetHeight ? m.Height - targetHeight : (targetHeight - m.Height) * 3)   // perto do alvo; menor que o alvo pesa mais
            .ThenBy(m => m.Codec is null ? 0 : m.IsMjpeg ? 1 : 2)                     // cru > MJPEG > outros
            .ThenBy(m => Math.Abs(Math.Min(m.MaxFps, 30) - 30))
            .FirstOrDefault();
    }
}
