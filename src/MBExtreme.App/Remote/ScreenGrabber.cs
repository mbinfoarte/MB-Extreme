using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using MBExtreme.Core.Remote;

namespace MBExtreme.UI.Remote;

/// <summary>Captura a tela principal e reduz para até 1280 px de largura, em JPEG.</summary>
internal sealed class ScreenGrabber : IScreenSource
{
    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    private const int MaxWidth = 1280;
    private static readonly ImageCodecInfo Jpeg = ImageCodecInfo.GetImageEncoders().First(e => e.FormatID == ImageFormat.Jpeg.Guid);
    private readonly EncoderParameters _quality = new(1) { Param = { [0] = new EncoderParameter(Encoder.Quality, 55L) } };

    public FrameMessage? Capture()
    {
        try
        {
            int width = GetSystemMetrics(0), height = GetSystemMetrics(1);
            if (width <= 0 || height <= 0) return null;
            using var full = new Bitmap(width, height, PixelFormat.Format24bppRgb);
            using (var g = Graphics.FromImage(full)) g.CopyFromScreen(0, 0, 0, 0, full.Size, CopyPixelOperation.SourceCopy);

            double scale = Math.Min(1.0, (double)MaxWidth / width);
            int w = Math.Max(16, (int)(width * scale)), h = Math.Max(16, (int)(height * scale));
            using var small = new Bitmap(w, h, PixelFormat.Format24bppRgb);
            using (var g = Graphics.FromImage(small))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.Bilinear;
                g.DrawImage(full, 0, 0, w, h);
            }

            using var stream = new MemoryStream();
            small.Save(stream, Jpeg, _quality);
            return new FrameMessage(width, height, stream.ToArray());
        }
        catch (Exception ex) when (ex is ExternalException or ArgumentException or OutOfMemoryException)
        {
            return null;   // tela bloqueada ou trocando de resolução: tenta de novo no próximo quadro
        }
    }
}
