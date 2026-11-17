using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MBExtreme.Core.Recording;

namespace MBExtreme.UI.Recording;

/// <summary>Desenha o cartão do Pix (QR em fundo branco com uma legenda) e salva em PNG para o FFmpeg colocar na transmissão.</summary>
internal static class PixImage
{
    private const int Cell = 8;
    private const int Quiet = 4;

    public static BitmapSource Render(string payload, string caption, string receiver)
    {
        var modules = PixQr.Modules(payload);
        int n = modules.GetLength(0);
        int qr = (n + Quiet * 2) * Cell;
        int width = Math.Max(qr, 260);
        int captionHeight = 64;
        int height = qr + captionHeight;

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));
            double left = (width - qr) / 2.0 + Quiet * Cell;
            double top = Quiet * Cell;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                    if (modules[y, x]) dc.DrawRectangle(Brushes.Black, null, new Rect(left + x * Cell, top + y * Cell, Cell, Cell));

            var typeface = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
            var title = new FormattedText(caption, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface, 22, Brushes.Black, 1.0)
            { MaxTextWidth = width - 16, MaxLineCount = 1, Trimming = TextTrimming.CharacterEllipsis, TextAlignment = TextAlignment.Center };
            dc.DrawText(title, new Point(8, qr - 4));
            var sub = new FormattedText(receiver, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), 14, Brushes.DimGray, 1.0)
            { MaxTextWidth = width - 16, MaxLineCount = 1, Trimming = TextTrimming.CharacterEllipsis, TextAlignment = TextAlignment.Center };
            dc.DrawText(sub, new Point(8, qr + 28));
        }

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    public static void SavePng(BitmapSource image, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
