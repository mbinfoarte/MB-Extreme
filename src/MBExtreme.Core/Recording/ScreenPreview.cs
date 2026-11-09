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

    /// <summary>Taxa da prévia: 60 quadros por segundo, para ficar tão fluida quanto a tela de verdade.</summary>
    public const int Fps = 60;

    /// <summary>
    /// Largura da prévia conforme o espaço na tela, em pixels de verdade (640, 960, 1280 ou 1920 = Full HD) e a altura na proporção
    /// da imagem (medidas pares). Nunca passa do tamanho da própria origem.
    /// </summary>
    public static (int Width, int Height) Size(double shownWidth, int sourceWidth, int sourceHeight)
    {
        int width = shownWidth > 1280 ? 1920 : shownWidth > 960 ? 1280 : shownWidth > 680 ? 960 : 640;
        width = Math.Min(width, Math.Max(160, sourceWidth - sourceWidth % 2));
        int height = (int)Math.Round(width * Math.Max(2, sourceHeight) / (double)Math.Max(2, sourceWidth));
        return (width, Math.Max(2, height - height % 2));
    }

    /// <summary>Versão nítida: imagem crua BGRA do tamanho exato (sem JPEG), desenhada direto na tela.</summary>
    public static IReadOnlyList<string> RawArgs(int output, PixelRect? crop, string? chain, int width, int height, int fps = Fps)
    {
        string rate = Math.Clamp(fps, 5, 60).ToString(CultureInfo.InvariantCulture);
        string cut = crop is { } c ? $"crop={c.Width}:{c.Height}:{c.X}:{c.Y}," : "";
        string filters = $"hwdownload,format=bgra,{cut}scale={width}:{height}:flags=bilinear," + (string.IsNullOrEmpty(chain) ? "" : chain + ",") + "format=bgra";
        return new[]
        {
            "-hide_banner", "-loglevel", "error", "-nostats", "-filter_threads", "4",
            "-f", "lavfi", "-i", $"ddagrab=output_idx={Math.Max(0, output)}:framerate={rate}:draw_mouse=1",
            "-an", "-vf", filters, "-f", "rawvideo", "-pix_fmt", "bgra", "pipe:1",
        };
    }

    /// <summary>
    /// Prévia de uma janela pelo Windows.Graphics.Capture (filtro gfxcapture do FFmpeg 8.1+): pega o conteúdo da janela mesmo atrás de outras,
    /// já reduzido na placa de vídeo para o tamanho da prévia. Bem mais nítida e fluida que copiar a janela pelo GDI (PrintWindow).
    /// </summary>
    public static IReadOnlyList<string> WindowArgs(long hwnd, string? chain, int width, int height, int fps = Fps)
    {
        string rate = Math.Clamp(fps, 5, 60).ToString(CultureInfo.InvariantCulture);
        string grab = $"gfxcapture=hwnd={hwnd}:max_framerate={rate}:capture_cursor=1:capture_border=0:display_border=0"
                      + $":width={width}:height={height}:resize_mode=scale_aspect:scale_mode=bicubic";
        string filters = "hwdownload,format=bgra," + (string.IsNullOrEmpty(chain) ? "" : chain + ",") + "format=bgra";
        return new[]
        {
            "-hide_banner", "-loglevel", "error", "-nostats",
            "-f", "lavfi", "-i", grab,
            "-an", "-vf", filters, "-f", "rawvideo", "-pix_fmt", "bgra", "pipe:1",
        };
    }
}
