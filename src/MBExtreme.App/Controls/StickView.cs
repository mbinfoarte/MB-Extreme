using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace MBExtreme.UI.Controls;

/// <summary>
/// Desenho de um analógico: quadro tracejado, círculo ideal, cruz, a borda medida em 48 direções (circularidade,
/// azul onde está boa e vermelho onde passa ou falta) e o ponto atual com um rastro curto.
/// </summary>
internal sealed class StickView : FrameworkElement
{
    private readonly Queue<Point> _trail = new();
    private double _x, _y;

    public IReadOnlyList<double>? Map { get; set; }
    public bool ShowMap { get; set; } = true;
    public string Caption { get; set; } = "";

    public void SetPosition(double x, double y)
    {
        _x = Math.Clamp(x, -1.2, 1.2);
        _y = Math.Clamp(y, -1.2, 1.2);
        _trail.Enqueue(new Point(_x, _y));
        while (_trail.Count > 24) _trail.Dequeue();
        InvalidateVisual();
    }

    public void ClearTrail() { _trail.Clear(); InvalidateVisual(); }

    protected override void OnRender(DrawingContext dc)
    {
        double size = Math.Min(ActualWidth, ActualHeight);
        if (size < 20) return;
        var c = new Point(ActualWidth / 2, ActualHeight / 2);
        double r = size / 2 - 12;

        // fundo e quadro tracejado
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(0x55, 0x10, 0x10, 0x14)),
            new Pen(new SolidColorBrush(Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF)), 1) { DashStyle = new DashStyle(new double[] { 4, 4 }, 0) },
            new Rect(c.X - r - 8, c.Y - r - 8, 2 * r + 16, 2 * r + 16), 10, 10);

        // borda medida (circularidade)
        if (ShowMap && Map is { Count: > 0 } map && map.Any(v => v > 0.2))
        {
            int n = map.Count;
            for (int i = 0; i < n; i++)
            {
                double v0 = map[i], v1 = map[(i + 1) % n];
                if (v0 <= 0.2 || v1 <= 0.2) continue;
                double a0 = i * 2 * Math.PI / n, a1 = (i + 1) * 2 * Math.PI / n;
                var p0 = new Point(c.X + Math.Cos(a0) * v0 * r, c.Y + Math.Sin(a0) * v0 * r);
                var p1 = new Point(c.X + Math.Cos(a1) * v1 * r, c.Y + Math.Sin(a1) * v1 * r);
                double dev = Math.Abs((v0 + v1) / 2 - 1);
                // perto de 1 = azul; longe = vermelho
                byte red = (byte)Math.Clamp(60 + dev * 2000, 60, 255), blue = (byte)Math.Clamp(255 - dev * 2000, 60, 255);
                var fill = new SolidColorBrush(Color.FromArgb(0x70, red, 0x60, blue));
                var geo = new StreamGeometry();
                using (var g = geo.Open())
                {
                    g.BeginFigure(c, true, true);
                    g.LineTo(p0, false, false);
                    g.LineTo(p1, false, false);
                }
                geo.Freeze();
                dc.DrawGeometry(fill, new Pen(new SolidColorBrush(Color.FromArgb(0xB0, red, 0x80, blue)), 1), geo);
            }
        }

        // círculo ideal e cruz
        var line = new Pen(new SolidColorBrush(Color.FromArgb(0x70, 0xB4, 0xB4, 0xBC)), 1);
        dc.DrawEllipse(null, line, c, r, r);
        dc.DrawLine(line, new Point(c.X - r, c.Y), new Point(c.X + r, c.Y));
        dc.DrawLine(line, new Point(c.X, c.Y - r), new Point(c.X, c.Y + r));
        dc.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xC4, 0x00)), 1) { DashStyle = new DashStyle(new double[] { 2, 3 }, 0) }, c, r * 0.1, r * 0.1);

        // rastro e ponto
        int k = 0;
        foreach (var p in _trail)
        {
            byte alpha = (byte)(20 + 140 * ++k / Math.Max(1, _trail.Count));
            dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(alpha, 0xFF, 0xC4, 0x00)), null, new Point(c.X + p.X * r, c.Y + p.Y * r), 2, 2);
        }
        var dot = new Point(c.X + _x * r, c.Y + _y * r);
        dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(0x50, 0xFF, 0xC4, 0x00)), null, dot, 10, 10);
        dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(0xFF, 0xC4, 0x00)), new Pen(Brushes.Black, 1), dot, 5, 5);

        if (Caption.Length > 0)
        {
            var text = new FormattedText(Caption, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 11,
                new SolidColorBrush(Color.FromRgb(0xB4, 0xB4, 0xBC)), VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(text, new Point(c.X - text.Width / 2, c.Y + r + 10 - text.Height));
        }
    }
}
