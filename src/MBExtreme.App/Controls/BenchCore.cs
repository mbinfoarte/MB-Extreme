using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace MBExtreme.UI.Controls;

/// <summary>
/// Núcleo animado do benchmark: anéis em degradê girando em velocidades diferentes, pontos em órbita, o ícone da etapa
/// no centro (pulsando de leve), "ETAPA X DE N" e barrinhas de equalizador. Fica parado quando as animações estão desligadas.
/// </summary>
internal sealed class BenchCore : FrameworkElement
{
    private static readonly Color Yellow = Color.FromRgb(0xFF, 0xC4, 0x00);
    private static readonly Color Orange = Color.FromRgb(0xFF, 0x7A, 0x00);
    private readonly DateTime _start = DateTime.Now;
    private bool _hooked;

    public string Glyph { get; set; } = "";
    public string Caption { get; set; } = "";
    public double StepPercent { get; set; }
    public bool Running { get; set; }

    public BenchCore()
    {
        Width = 320;
        Height = 300;
        Loaded += (_, _) => Hook(true);
        Unloaded += (_, _) => Hook(false);
        IsVisibleChanged += (_, _) => Hook(IsVisible);
    }

    private void Hook(bool on)
    {
        bool animate = on && AppServices.Settings.Animations && SystemParameters.ClientAreaAnimation;
        if (animate == _hooked) return;
        _hooked = animate;
        if (animate) CompositionTarget.Rendering += Tick;
        else CompositionTarget.Rendering -= Tick;
        InvalidateVisual();
    }

    private void Tick(object? sender, EventArgs e) => InvalidateVisual();

    protected override void OnRender(DrawingContext dc)
    {
        double t = _hooked ? (DateTime.Now - _start).TotalSeconds : 0;
        double speed = Running ? 1.0 : 0.25;
        var c = new Point(ActualWidth / 2, 118);
        double r = 92;

        // brilho de fundo
        var glow = new RadialGradientBrush(Color.FromArgb(Running ? (byte)0x40 : (byte)0x20, 0xFF, 0xB0, 0x00), Color.FromArgb(0, 0xFF, 0xB0, 0x00));
        dc.DrawEllipse(glow, null, c, r * 1.25, r * 1.25);

        // anel de fundo e anel tracejado
        dc.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)), 1), c, r, r);
        var dashed = new Pen(new SolidColorBrush(Color.FromArgb(0x50, 0xFF, 0xC4, 0x00)), 1.2) { DashStyle = new DashStyle(new double[] { 2, 6 }, t * 8 * speed) };
        dc.DrawEllipse(null, dashed, c, r * 0.8, r * 0.8);

        // arcos girando (degradê amarelo → laranja)
        var gradient = new LinearGradientBrush(Yellow, Orange, 45);
        Arc(dc, c, r, t * 70 * speed, 110, new Pen(gradient, 5) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round });
        Arc(dc, c, r, t * 70 * speed + 180, 60, new Pen(gradient, 3) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round });
        Arc(dc, c, r * 1.12, -t * 40 * speed, 40, new Pen(new SolidColorBrush(Color.FromArgb(0x90, 0xFF, 0x9A, 0x1F)), 2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round });
        // progresso da etapa num anel interno
        if (StepPercent > 0)
            Arc(dc, c, r * 0.66, -90, Math.Clamp(StepPercent, 0, 100) * 3.6, new Pen(new SolidColorBrush(Color.FromArgb(0xC0, 0xFF, 0xC4, 0x00)), 2.5) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, fromTop: true);

        // pontos em órbita
        for (int i = 0; i < 3; i++)
        {
            double a = (t * (35 + i * 17) * speed + i * 120) * Math.PI / 180;
            var p = new Point(c.X + Math.Cos(a) * r * 0.8, c.Y + Math.Sin(a) * r * 0.8);
            dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(0xD0, 0xFF, 0xD8, 0x60)), null, p, 2.6, 2.6);
        }

        // centro: círculo de vidro com o ícone pulsando
        double pulse = 1 + (Running ? 0.04 * Math.Sin(t * 3) : 0);
        var core = new RadialGradientBrush(Color.FromRgb(0x2A, 0x24, 0x10), Color.FromRgb(0x10, 0x10, 0x12)) { GradientOrigin = new Point(0.4, 0.3) };
        dc.DrawEllipse(core, new Pen(new SolidColorBrush(Color.FromArgb(0x90, 0xFF, 0xC4, 0x00)), 1.5), c, 30 * pulse, 30 * pulse);
        var glyph = new FormattedText(Glyph, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface("Segoe MDL2 Assets"), 24 * pulse,
            new SolidColorBrush(Yellow), VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(glyph, new Point(c.X - glyph.Width / 2, c.Y - glyph.Height / 2));

        // "ETAPA X DE N"
        var caption = new FormattedText(Caption, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal),
            10.5, new SolidColorBrush(Color.FromRgb(0xB4, 0xB4, 0xBC)), VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(caption, new Point(c.X - caption.Width / 2, c.Y + r + 26));

        // equalizador
        int bars = 13;
        double barW = 4, gap = 4, baseY = c.Y + r + 66;
        double startX = c.X - (bars * barW + (bars - 1) * gap) / 2;
        for (int i = 0; i < bars; i++)
        {
            double h = Running ? 6 + 12 * (0.5 + 0.5 * Math.Sin(t * (5 + i % 4) + i * 0.9)) * (0.6 + 0.4 * Math.Sin(t * 1.3 + i)) : 4;
            var brush = new LinearGradientBrush(Yellow, Orange, 90);
            dc.DrawRoundedRectangle(brush, null, new Rect(startX + i * (barW + gap), baseY - h, barW, h * 2), 2, 2);
        }
    }

    private static void Arc(DrawingContext dc, Point c, double r, double startDeg, double sweepDeg, Pen pen, bool fromTop = false)
    {
        if (sweepDeg <= 0.5) return;
        sweepDeg = Math.Min(sweepDeg, 359.9);
        double a0 = startDeg * Math.PI / 180, a1 = (startDeg + sweepDeg) * Math.PI / 180;
        var p0 = new Point(c.X + Math.Cos(a0) * r, c.Y + Math.Sin(a0) * r);
        var p1 = new Point(c.X + Math.Cos(a1) * r, c.Y + Math.Sin(a1) * r);
        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            g.BeginFigure(p0, false, false);
            g.ArcTo(p1, new Size(r, r), 0, sweepDeg > 180, SweepDirection.Clockwise, true, false);
        }
        geo.Freeze();
        dc.DrawGeometry(null, pen, geo);
    }
}
