using System.Globalization;

namespace MBExtreme.Core.Recording;

/// <summary>
/// Prévia da tela feita pela placa de vídeo (ddagrab, a mesma captura da gravação), em vez de copiar a tela pelo GDI várias vezes por segundo:
/// pesa bem menos no processador e não engasga o jogo. Sai em JPEG pequeno pela saída do FFmpeg.
/// </summary>
public static class ScreenPreview
{
    /// <summary>Recorte (janela de jogo) em coordenadas da própria tela, ajustado para caber nela e com medidas pares; null = a tela inteira.</summary>
    public static PixelRect? CropWithin(PixelRect display, PixelRect area)
    {
        int x = Math.Clamp(area.X - display.X, 0, Math.Max(0, display.Width - 2));
        int y = Math.Clamp(area.Y - display.Y, 0, Math.Max(0, display.Height - 2));
        int w = Math.Min(area.Width, display.Width - x);
        int h = Math.Min(area.Height, display.Height - y);
        w -= w % 2;
        h -= h % 2;
        if (w < 16 || h < 16) return null;
        if (x == 0 && y == 0 && w >= display.Width - 1 && h >= display.Height - 1) return null;
        return new PixelRect(x, y, w, h);
    }

    /// <summary>A tela (pelo índice da saída na placa) cujo centro contém o centro da área; -1 se nenhuma.</summary>
    public static int DisplayFor(IReadOnlyList<DisplayInfo> displays, PixelRect area)
    {
        int cx = area.X + area.Width / 2, cy = area.Y + area.Height / 2;
        foreach (var d in displays)
            if (cx >= d.Bounds.X && cx < d.Bounds.X + d.Bounds.Width && cy >= d.Bounds.Y && cy < d.Bounds.Y + d.Bounds.Height) return d.Index;
        return -1;
    }

    /// <summary>Largura da prévia conforme o espaço na tela (640, 960 ou 1280) e a altura na proporção da imagem (medidas pares).</summary>
    public static (int Width, int Height) Size(double shownWidth, int sourceWidth, int sourceHeight)
    {
        int width = shownWidth > 1000 ? 1280 : shownWidth > 680 ? 960 : 640;
        width = Math.Min(width, Math.Max(160, sourceWidth - sourceWidth % 2));
        int height = (int)Math.Round(width * Math.Max(2, sourceHeight) / (double)Math.Max(2, sourceWidth));
        return (width, Math.Max(2, height - height % 2));
    }

    /// <summary>Versão nítida: imagem crua BGRA do tamanho exato (sem JPEG), desenhada direto na tela.</summary>
    public static IReadOnlyList<string> RawArgs(int output, PixelRect? crop, string? chain, int width, int height, int fps = 24)
    {
        string rate = Math.Clamp(fps, 5, 30).ToString(CultureInfo.InvariantCulture);
        string cut = crop is { } c ? $"crop={c.Width}:{c.Height}:{c.X}:{c.Y}," : "";
        string filters = $"hwdownload,format=bgra,{cut}scale={width}:{height}:flags=bicubic," + (string.IsNullOrEmpty(chain) ? "" : chain + ",") + "format=bgra";
        return new[]
        {
            "-hide_banner", "-loglevel", "error", "-nostats",
            "-f", "lavfi", "-i", $"ddagrab=output_idx={Math.Max(0, output)}:framerate={rate}:draw_mouse=1",
            "-an", "-vf", filters, "-f", "rawvideo", "-pix_fmt", "bgra", "pipe:1",
        };
    }
}
