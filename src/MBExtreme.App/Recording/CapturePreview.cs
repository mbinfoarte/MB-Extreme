using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;
using MBExtreme.Core.Recording;

namespace MBExtreme.UI.Recording;

/// <summary>Quadro reduzido do que será capturado, para a pré-visualização do estúdio.</summary>
internal sealed record PreviewFrame(BitmapSource Image, bool MostlyBlack);

/// <summary>
/// Pré-visualização leve: copia a área (tela ou recorte da janela) e reduz para poucos pixels.
/// Janelas pelo modo "conteúdo" usam PrintWindow, como a captura real.
/// </summary>
internal static class CapturePreview
{
    private const int MaxWidth = 480;

    [DllImport("user32.dll")]
    private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }

    /// <param name="screenArea">Área em coordenadas absolutas da tela (tela inteira ou janela).</param>
    /// <param name="gdiWindow">Janela a capturar pelo conteúdo (modo GDI); nulo = copia a área da tela.</param>
    public static PreviewFrame? Grab(PixelRect screenArea, IntPtr? gdiWindow)
    {
        try
        {
            using Bitmap full = gdiWindow is { } handle && handle != IntPtr.Zero
                ? GrabWindow(handle)
                : GrabArea(screenArea);
            if (full.Width <= 0 || full.Height <= 0) return null;

            var (w, h) = PreviewRules.Fit(full.Width, full.Height, MaxWidth);
            using var small = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(small))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.Bilinear;
                g.DrawImage(full, 0, 0, w, h);
            }

            var data = small.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
            try
            {
                var bytes = new byte[data.Stride * h];
                Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
                bool black = PreviewRules.IsMostlyBlack(bytes);
                var bitmap = BitmapSource.Create(w, h, 96, 96, System.Windows.Media.PixelFormats.Pbgra32, null, bytes, data.Stride);
                bitmap.Freeze();
                return new PreviewFrame(bitmap, black);
            }
            finally
            {
                small.UnlockBits(data);
            }
        }
        catch (Exception ex) when (ex is ExternalException or ArgumentException or InvalidOperationException or OutOfMemoryException)
        {
            return null;
        }
    }

    private static Bitmap GrabArea(PixelRect area)
    {
        var bitmap = new Bitmap(Math.Max(1, area.Width), Math.Max(1, area.Height), PixelFormat.Format32bppPArgb);
        using var g = Graphics.FromImage(bitmap);
        g.CopyFromScreen(area.X, area.Y, 0, 0, bitmap.Size, CopyPixelOperation.SourceCopy);
        return bitmap;
    }

    private static Bitmap GrabWindow(IntPtr handle)
    {
        if (!GetWindowRect(handle, out var r)) throw new ExternalException("janela inválida");
        var bitmap = new Bitmap(Math.Max(1, r.Right - r.Left), Math.Max(1, r.Bottom - r.Top), PixelFormat.Format32bppPArgb);
        using var g = Graphics.FromImage(bitmap);
        IntPtr hdc = g.GetHdc();
        try { PrintWindow(handle, hdc, 2); }   // 2 = PW_RENDERFULLCONTENT
        finally { g.ReleaseHdc(hdc); }
        return bitmap;
    }
}
