using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MBExtreme.UI.Recording;

namespace MBExtreme.UI;

/// <summary>Um modelo de papel de parede do MB Extreme, desenhado na resolução pedida.</summary>
public sealed record MbWallpaper(string Id, string Name, Action<DrawingContext, double, double> Draw);

/// <summary>
/// Papéis de parede próprios do MB Extreme (preto e amarelo), desenhados na hora na resolução de cada monitor — sem perda de qualidade.
/// Não usa imagem de terceiros: tudo é desenhado pelo programa.
/// </summary>
public static class MbWallpaperArt
{
    private static readonly Color Black = Color.FromRgb(0x08, 0x08, 0x0A);
    private static readonly Color Panel = Color.FromRgb(0x14, 0x14, 0x18);
    private static readonly Color Accent = Color.FromRgb(0xFF, 0xC4, 0x00);
    private static readonly Color Accent2 = Color.FromRgb(0xFF, 0x96, 0x00);
    private static readonly Color Soft = Color.FromRgb(0x9A, 0x9A, 0xA3);

    public static IReadOnlyList<MbWallpaper> All { get; } = new MbWallpaper[]
    {
        new("mb-classico", "Clássico", DrawClassic),
        new("mb-degrade", "Degradê", DrawGradient),
        new("mb-grade", "Grade", DrawGrid),
        new("mb-raio", "Raio", DrawBolt),
        new("mb-minimal", "Minimalista", DrawMinimal),
        new("mb-amarelo", "Faixa amarela", DrawYellowBand),
    };

    /// <summary>Desenha o modelo numa imagem na resolução pedida.</summary>
    public static BitmapSource Render(MbWallpaper model, int width, int height)
    {
        width = Math.Clamp(width, 320, 7680);
        height = Math.Clamp(height, 240, 4320);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Black), null, new Rect(0, 0, width, height));
            model.Draw(dc, width, height);
        }
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>Prévia pequena para a grade (sem gravar em disco).</summary>
    public static BitmapSource Preview(MbWallpaper model, double aspect) => Render(model, 480, (int)Math.Round(480 / Math.Max(0.5, aspect)));

    /// <summary>Grava o modelo em PNG na resolução pedida e devolve o caminho do arquivo.</summary>
    public static string Save(MbWallpaper model, int width, int height, string folder)
    {
        var path = Path.Combine(folder, $"{model.Id}-{Math.Clamp(width, 320, 7680)}x{Math.Clamp(height, 240, 4320)}.png");
        OverlayArt.SavePng(Render(model, width, height), path);
        return path;
    }

    // ---------------------------------------------------------------- desenhos

    private static void Wordmark(DrawingContext dc, double cx, double cy, double unit, bool withSubtitle = true)
    {
        // quadrado "MB" + "EXTREME"
        double box = unit * 1.6;
        var rect = new Rect(cx - box / 2, cy - box / 2, box, box);
        dc.DrawRoundedRectangle(new SolidColorBrush(Accent), null, rect, unit * 0.28, unit * 0.28);
        Text(dc, "MB", rect.Left + box / 2, rect.Top + box / 2, unit * 0.82, Colors.Black, FontWeights.Bold, center: true);
        Text(dc, "EXTREME", cx, cy + box * 0.75, unit * 0.42, Colors.White, FontWeights.SemiBold, center: true, spacing: 6);
        if (withSubtitle) Text(dc, "Tecnologia sem complicação", cx, cy + box * 0.75 + unit * 0.5, unit * 0.2, Soft, FontWeights.Normal, center: true, spacing: 2);
    }

    private static void DrawClassic(DrawingContext dc, double w, double h)
    {
        double unit = Math.Min(w, h) * 0.10;
        // brilho suave atrás
        var glow = new RadialGradientBrush { GradientOrigin = new Point(0.5, 0.42), Center = new Point(0.5, 0.42), RadiusX = 0.65, RadiusY = 0.65 };
        glow.GradientStops.Add(new GradientStop(Color.FromArgb(0x40, 0xFF, 0xC4, 0x00), 0));
        glow.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0xFF, 0xC4, 0x00), 1));
        dc.DrawRectangle(glow, null, new Rect(0, 0, w, h));
        // faixa diagonal fininha
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xC4, 0x00)), Math.Max(1, unit * 0.06));
        for (double x = -h; x < w; x += unit * 1.4) dc.DrawLine(pen, new Point(x, h), new Point(x + h, 0));
        Wordmark(dc, w / 2, h * 0.42, unit);
    }

    private static void DrawGradient(DrawingContext dc, double w, double h)
    {
        var bg = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
        bg.GradientStops.Add(new GradientStop(Color.FromRgb(0x16, 0x12, 0x06), 0));
        bg.GradientStops.Add(new GradientStop(Black, 0.55));
        bg.GradientStops.Add(new GradientStop(Color.FromRgb(0x0C, 0x0C, 0x10), 1));
        dc.DrawRectangle(bg, null, new Rect(0, 0, w, h));
        double unit = Math.Min(w, h) * 0.11;
        var arc = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0x96, 0x00));
        dc.DrawEllipse(arc, null, new Point(w * 0.82, h * 0.2), w * 0.28, w * 0.28);
        Wordmark(dc, w / 2, h / 2, unit);
    }

    private static void DrawGrid(DrawingContext dc, double w, double h)
    {
        double step = Math.Min(w, h) * 0.055;
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(0x16, 0xFF, 0xC4, 0x00)), 1);
        for (double x = 0; x <= w; x += step) dc.DrawLine(pen, new Point(x, 0), new Point(x, h));
        for (double y = 0; y <= h; y += step) dc.DrawLine(pen, new Point(0, y), new Point(w, y));
        double unit = Math.Min(w, h) * 0.085;
        Wordmark(dc, w * 0.5, h * 0.5, unit);
    }

    private static void DrawBolt(DrawingContext dc, double w, double h)
    {
        double unit = Math.Min(w, h) * 0.095;
        double s = Math.Min(w, h) * 0.32;
        var cx = w * 0.5; var cy = h * 0.4;
        var bolt = new StreamGeometry();
        using (var ctx = bolt.Open())
        {
            ctx.BeginFigure(new Point(cx + s * 0.1, cy - s), true, true);
            foreach (var (dx, dy) in new[] { (-0.5, 0.2), (0.0, 0.25), (-0.35, 0.8), (0.45, -0.1), (0.0, -0.15), (0.35, -0.75) })
                ctx.LineTo(new Point(cx + s * dx, cy + s * dy), true, false);
        }
        bolt.Freeze();
        dc.DrawGeometry(new SolidColorBrush(Accent), new Pen(new SolidColorBrush(Accent2), 2), bolt);
        Text(dc, "MB EXTREME", w / 2, h * 0.78, unit * 0.5, Colors.White, FontWeights.SemiBold, center: true, spacing: 8);
    }

    private static void DrawMinimal(DrawingContext dc, double w, double h)
    {
        double unit = Math.Min(w, h) * 0.07;
        Wordmark(dc, w / 2, h / 2, unit, withSubtitle: false);
    }

    private static void DrawYellowBand(DrawingContext dc, double w, double h)
    {
        double bandW = w * 0.44;
        dc.DrawRectangle(new SolidColorBrush(Accent), null, new Rect(0, 0, bandW, h));
        double unit = Math.Min(w, h) * 0.12;
        // "MB" grande em preto sobre o amarelo
        Text(dc, "MB", bandW * 0.5, h * 0.5, unit * 1.7, Colors.Black, FontWeights.Bold, center: true);
        Text(dc, "EXTREME", w * 0.5 + bandW * 0.5, h * 0.5, unit * 0.6, Colors.White, FontWeights.SemiBold, center: true, spacing: 10);
        dc.DrawRectangle(new SolidColorBrush(Panel), null, new Rect(bandW, h * 0.5 - 1, w - bandW, 2));
    }

    private static void Text(DrawingContext dc, string text, double x, double y, double size, Color color, FontWeight weight, bool center = false, double spacing = 0)
    {
        var typeface = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, weight, FontStretches.Normal);
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, size, new SolidColorBrush(color), 1.0);
        double ox = center ? x - ft.Width / 2 : x;
        double oy = center ? y - ft.Height / 2 : y;
        dc.DrawText(ft, new Point(ox, oy));
    }
}
