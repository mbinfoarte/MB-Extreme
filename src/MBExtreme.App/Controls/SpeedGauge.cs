using System.Globalization;
using System.Windows;
using System.Windows.Media;
using MBExtreme.Core.Network;

namespace MBExtreme.UI.Controls;

/// <summary>
/// Velocímetro do teste de internet: arco de 270°, marcas com o mesmo espaço (0, 5, 10, 50, 100...), faixa colorida até o valor, ponteiro e o número grande.
/// O valor anima (a agulha desliza); a cor muda entre download (amarelo da MB) e upload (roxo).
/// </summary>
internal sealed class SpeedGauge : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(nameof(Value), typeof(double), typeof(SpeedGauge),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Velocidade mostrada (Mbps). Animável.</summary>
    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }

    public bool Upload { get => _upload; set { _upload = value; InvalidateVisual(); } }
    public string Caption { get => _caption; set { _caption = value; InvalidateVisual(); } }
    public string? CenterText { get => _center; set { _center = value; InvalidateVisual(); } }
    private bool _upload;
    private string _caption = "Mbps";
    private string? _center;
    private double[] _ticks = SpeedScale.Normal;

    /// <summary>Anda até a velocidade nova (rápido, para acompanhar a medição ao vivo).</summary>
    public void Show(double mbps)
    {
        if (mbps > _ticks[^1] && _ticks == SpeedScale.Normal) { _ticks = SpeedScale.Fast; InvalidateVisual(); }
        Motion.To(this, ValueProperty, mbps, new Duration(TimeSpan.FromMilliseconds(380)));
    }

    public void Reset()
    {
        BeginAnimation(ValueProperty, null);
        Value = 0;
        _ticks = SpeedScale.Normal;
        _center = null;
        InvalidateVisual();
    }

    private const double Start = 135, Sweep = 270;

    private static Point At(Point c, double r, double deg) => new(c.X + r * Math.Cos(deg * Math.PI / 180), c.Y + r * Math.Sin(deg * Math.PI / 180));

    private static Geometry Arc(Point c, double r, double from, double to)
    {
        var fig = new PathFigure { StartPoint = At(c, r, from), IsClosed = false };
        fig.Segments.Add(new ArcSegment(At(c, r, to), new Size(r, r), 0, to - from > 180, SweepDirection.Clockwise, true));
        return new PathGeometry(new[] { fig });
    }

    protected override void OnRender(DrawingContext dc)
    {
        double size = Math.Min(ActualWidth, ActualHeight);
        if (size < 80) return;
        var c = new Point(ActualWidth / 2, ActualHeight / 2 + size * 0.04);
        double thickness = size * 0.075, r = size / 2 - thickness;
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        // trilho
        dc.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromRgb(0x26, 0x26, 0x30)), thickness) { StartLineCap = PenLineCap.Flat, EndLineCap = PenLineCap.Flat }, Arc(c, r, Start, Start + Sweep));

        // faixa colorida até o valor
        double frac = SpeedScale.Fraction(Value, _ticks);
        var (a, b) = _upload ? (Color.FromRgb(0x7C, 0x5C, 0xFF), Color.FromRgb(0xFF, 0x6C, 0xD8)) : (Color.FromRgb(0xFF, 0x8A, 0x00), Color.FromRgb(0xFF, 0xDD, 0x33));
        var gradient = new LinearGradientBrush(a, b, new Point(0, 1), new Point(1, 0));
        if (frac > 0.001) dc.DrawGeometry(null, new Pen(gradient, thickness), Arc(c, r, Start, Start + Sweep * frac));

        // marcas
        var label = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xEC));
        var dim = new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x8A));
        for (int i = 0; i < _ticks.Length; i++)
        {
            double f = (double)i / (_ticks.Length - 1);
            var text = new FormattedText(SpeedScale.Label(_ticks[i]), CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal), size * 0.052, f <= frac + 0.001 ? label : dim, dpi);
            var p = At(c, r - thickness * 1.35, Start + Sweep * f);
            dc.DrawText(text, new Point(p.X - text.Width / 2, p.Y - text.Height / 2));
        }

        // ponteiro: cunha que afina, com brilho
        double angle = Start + Sweep * frac;
        var tip = At(c, r - thickness * 0.3, angle);
        var left = At(c, size * 0.02, angle - 90);
        var right = At(c, size * 0.02, angle + 90);
        var needle = new StreamGeometry();
        using (var ctx = needle.Open())
        {
            ctx.BeginFigure(left, true, true);
            ctx.LineTo(tip, true, false);
            ctx.LineTo(right, true, false);
        }
        var needleBrush = new LinearGradientBrush(Color.FromArgb(0x10, 0xFF, 0xFF, 0xFF), Color.FromArgb(0xF0, 0xFF, 0xFF, 0xFF), At(new Point(0.5, 0.5), 0.5, angle + 180), At(new Point(0.5, 0.5), 0.5, angle));
        dc.DrawGeometry(needleBrush, null, needle);
        dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(0x2E, 0x2E, 0x36)), null, c, size * 0.028, size * 0.028);

        // número grande e unidade
        string big = _center ?? (Value >= 100 ? Value.ToString("0.0", CultureInfo.GetCultureInfo("pt-BR")) : Value.ToString("0.00", CultureInfo.GetCultureInfo("pt-BR")));
        var number = new FormattedText(big, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI Light"), FontStyles.Normal, FontWeights.Light, FontStretches.Normal), size * 0.1, Brushes.White, dpi);
        dc.DrawText(number, new Point(c.X - number.Width / 2, c.Y + size * 0.17));
        var unit = new FormattedText((_upload ? "↑ " : "↓ ") + _caption, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), size * 0.05, new SolidColorBrush(b), dpi);
        dc.DrawText(unit, new Point(c.X - unit.Width / 2, c.Y + size * 0.17 + number.Height));
    }
}
