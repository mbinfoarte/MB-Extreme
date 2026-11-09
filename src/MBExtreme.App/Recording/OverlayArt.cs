using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MBExtreme.UI.Recording;

/// <summary>Identidade do canal que vira imagem: nome, apresentador, frase e logo.</summary>
internal sealed record ChannelProfile(string Name, string Presenter, string Tagline, string? LogoPath, int Theme);

/// <summary>Desenha em PNG (com transparência) a entrada do canal, a faixa com o nome do apresentador e o logo, que o FFmpeg coloca por cima do vídeo.</summary>
internal static class OverlayArt
{
    public static readonly (string Name, Color Color)[] Themes =
    {
        ("Amarelo", Color.FromRgb(0xFF, 0xC4, 0x00)), ("Branco", Color.FromRgb(0xF2, 0xF2, 0xF4)), ("Azul", Color.FromRgb(0x2F, 0x80, 0xED)),
        ("Vermelho", Color.FromRgb(0xE5, 0x48, 0x4D)), ("Verde", Color.FromRgb(0x30, 0xA4, 0x6C)), ("Roxo", Color.FromRgb(0x8E, 0x4E, 0xC6)),
    };

    private static Color Accent(int theme) => Themes[Math.Clamp(theme, 0, Themes.Length - 1)].Color;

    private static readonly Typeface Bold = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
    private static readonly Typeface Regular = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

    private static FormattedText Text(string text, Typeface face, double size, Brush brush, double maxWidth) =>
        new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, face, size, brush, 1.0)
        { MaxTextWidth = maxWidth, MaxLineCount = 1, Trimming = TextTrimming.CharacterEllipsis };

    public static BitmapSource? LoadLogo(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = 512;
            bitmap.UriSource = new Uri(path);
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception ex) when (ex is NotSupportedException or IOException or InvalidOperationException) { return null; }
    }

    private static void DrawLogo(DrawingContext dc, BitmapSource? logo, string fallbackLetter, Color accent, Rect area)
    {
        var center = new Point(area.X + area.Width / 2, area.Y + area.Height / 2);
        double radius = Math.Min(area.Width, area.Height) / 2;
        dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(0x1B, 0x1B, 0x20)), new Pen(new SolidColorBrush(accent), 3), center, radius, radius);
        if (logo is not null)
        {
            dc.PushClip(new EllipseGeometry(center, radius - 3, radius - 3));
            double scale = Math.Max(area.Width / logo.PixelWidth, area.Height / logo.PixelHeight);
            double w = logo.PixelWidth * scale, h = logo.PixelHeight * scale;
            dc.DrawImage(logo, new Rect(center.X - w / 2, center.Y - h / 2, w, h));
            dc.Pop();
        }
        else
        {
            var letter = Text(fallbackLetter, Bold, radius * 1.1, new SolidColorBrush(accent), radius * 2);
            dc.DrawText(letter, new Point(center.X - letter.Width / 2, center.Y - letter.Height / 2));
        }
    }

    private static BitmapSource Finish(DrawingVisual visual, int width, int height)
    {
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private static string Initial(ChannelProfile p) => string.IsNullOrWhiteSpace(p.Name) ? "?" : p.Name.Trim()[..1].ToUpperInvariant();

    /// <summary>Cartão de abertura: logo, nome do canal e frase.</summary>
    public static BitmapSource RenderIntro(ChannelProfile p)
    {
        const int w = 900, h = 260;
        var accent = Accent(p.Theme);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(0xE6, 0x10, 0x10, 0x14)), null, new Rect(0, 0, w, h), 28, 28);
            dc.DrawRoundedRectangle(new SolidColorBrush(accent), null, new Rect(0, 0, 16, h), 8, 8);
            DrawLogo(dc, LoadLogo(p.LogoPath), Initial(p), accent, new Rect(48, 30, 200, 200));
            var white = Brushes.White;
            dc.DrawText(Text(string.IsNullOrWhiteSpace(p.Name) ? "Seu canal" : p.Name, Bold, 56, white, w - 300), new Point(280, 52));
            if (!string.IsNullOrWhiteSpace(p.Tagline)) dc.DrawText(Text(p.Tagline, Regular, 28, new SolidColorBrush(Color.FromRgb(0xC9, 0xC9, 0xD1)), w - 300), new Point(282, 128));
            if (!string.IsNullOrWhiteSpace(p.Presenter)) dc.DrawText(Text("com " + p.Presenter, Bold, 26, new SolidColorBrush(accent), w - 300), new Point(282, 176));
        }
        return Finish(visual, w, h);
    }

    /// <summary>Faixa com o nome de quem apresenta e o canal.</summary>
    public static BitmapSource RenderLowerThird(ChannelProfile p)
    {
        const int w = 760, h = 140;
        var accent = Accent(p.Theme);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(0xE6, 0x10, 0x10, 0x14)), null, new Rect(0, 0, w, h), 20, 20);
            dc.DrawRoundedRectangle(new SolidColorBrush(accent), null, new Rect(0, 0, 14, h), 7, 7);
            DrawLogo(dc, LoadLogo(p.LogoPath), Initial(p), accent, new Rect(36, 22, 96, 96));
            string name = !string.IsNullOrWhiteSpace(p.Presenter) ? p.Presenter : !string.IsNullOrWhiteSpace(p.Name) ? p.Name : "Apresentador";
            dc.DrawText(Text(name, Bold, 44, Brushes.White, w - 170), new Point(152, 18));
            string line = string.Join("  ·  ", new[] { p.Name, p.Tagline }.Where(t => !string.IsNullOrWhiteSpace(t)));
            if (line.Length > 0) dc.DrawText(Text(line, Regular, 24, new SolidColorBrush(accent), w - 170), new Point(154, 78));
        }
        return Finish(visual, w, h);
    }

    /// <summary>Logo redondo para a marca d'água.</summary>
    public static BitmapSource RenderLogo(ChannelProfile p)
    {
        const int size = 280;
        var accent = Accent(p.Theme);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen()) DrawLogo(dc, LoadLogo(p.LogoPath), Initial(p), accent, new Rect(6, 6, size - 12, size - 12));
        return Finish(visual, size, size);
    }

    /// <summary>Cartão do Pix com o QR do próprio banco: moldura, nome do canal, a imagem do QR e uma legenda.</summary>
    public static BitmapSource RenderPixOwn(string imagePath, string channelName, string caption)
    {
        var qr = LoadLogo(imagePath) ?? throw new IOException("Não foi possível abrir a imagem do QR.");
        const int w = 420;
        int qrSize = 340;
        int h = qrSize + 150;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRoundedRectangle(Brushes.White, new Pen(new SolidColorBrush(Color.FromRgb(0xFF, 0xC4, 0x00)), 8), new Rect(4, 4, w - 8, h - 8), 22, 22);
            var title = Text(string.IsNullOrWhiteSpace(caption) ? "Apoie via Pix" : caption, Bold, 30, Brushes.Black, w - 40);
            title.TextAlignment = TextAlignment.Center;
            dc.DrawText(title, new Point(20, 20));
            double scale = Math.Min((double)qrSize / qr.PixelWidth, (double)qrSize / qr.PixelHeight);
            double dw = qr.PixelWidth * scale, dh = qr.PixelHeight * scale;
            dc.DrawImage(qr, new Rect((w - dw) / 2, 74 + (qrSize - dh) / 2, dw, dh));
            var name = Text(string.IsNullOrWhiteSpace(channelName) ? "Escaneie com o app do seu banco" : channelName, Bold, 24, Brushes.Black, w - 40);
            name.TextAlignment = TextAlignment.Center;
            dc.DrawText(name, new Point(20, 74 + qrSize + 14));
            var hint = Text("Escaneie com o app do seu banco", Regular, 18, Brushes.DimGray, w - 40);
            hint.TextAlignment = TextAlignment.Center;
            dc.DrawText(hint, new Point(20, 74 + qrSize + 52));
        }
        return Finish(visual, w, h);
    }

    public static void SavePng(BitmapSource image, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    /// <summary>Copia (e converte para PNG de até 512 px) uma imagem escolhida pelo usuário para dentro da pasta do programa.</summary>
    public static string Import(string source, string destination)
    {
        var bitmap = LoadLogo(source) ?? throw new IOException("Não foi possível abrir a imagem escolhida.");
        SavePng(bitmap, destination);
        return destination;
    }
}
